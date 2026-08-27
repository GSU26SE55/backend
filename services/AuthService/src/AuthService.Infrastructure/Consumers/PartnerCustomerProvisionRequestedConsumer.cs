using System.Security.Cryptography;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;

namespace AuthService.Infrastructure.Consumers;

/// <summary>
/// Cấp tài khoản khách hàng cho dữ liệu nhập từ bên thứ ba (pha 2A của luồng import).
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao tạo thẳng ở trạng thái hoạt động thay vì gửi lời mời:</b> handler tạo Site và
/// BatteryAsset của BatteryService đòi bản sao khách hàng phải đang hoạt động, mà bản sao đó chỉ
/// sinh ra khi AuthService phát <see cref="AccountActivatedEvent"/>. Đường mời đặt tài khoản ở
/// trạng thái chờ xác minh và không phát event này, nên nếu đi đường mời thì toàn bộ site và pin
/// của lô sẽ nằm chờ cho tới khi từng khách bấm vào thư — với một lô vài trăm khách thì việc nạp
/// dữ liệu không bao giờ kết thúc.
/// </para>
/// <para>
/// <b>Mật khẩu:</b> sinh ngẫu nhiên bằng nguồn mã hoá, không gửi cho ai, và không dùng lại ở đâu.
/// Khách tự đặt mật khẩu qua luồng "Quên mật khẩu" — luồng đó chỉ đòi tài khoản đang hoạt động
/// nên dùng được ngay. Cách này tránh việc mật khẩu nằm trong hộp thư của khách, và không phải
/// thêm cột cờ "bắt đổi mật khẩu" vào bảng tài khoản.
/// </para>
/// <para>
/// <b>Email đã tồn tại không phải lỗi.</b> Đối tác nào bàn giao dữ liệu cũng có sẵn một phần khách
/// đã là khách của mình. Ca đó được coi là liên kết vào tài khoản cũ, và vẫn phát
/// <see cref="AccountSyncSnapshotEvent"/> để chắc chắn bản sao bên BatteryService có mặt — tài
/// khoản cũ chưa chắc đã từng được đồng bộ sang đó.
/// </para>
/// </remarks>
public class PartnerCustomerProvisionRequestedConsumer : IConsumer<PartnerCustomerProvisionRequestedEvent>
{
    private const string CustomerRoleName = "Customer";
    private const string CreationSource = "PartnerImport";

    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly IPublisher _publisher;
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<PartnerCustomerProvisionRequestedConsumer> _logger;

    public PartnerCustomerProvisionRequestedConsumer(
        IAuthUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        IPublisher publisher,
        IInboxStore inboxStore,
        ILogger<PartnerCustomerProvisionRequestedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _publisher = publisher;
        _inboxStore = inboxStore;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PartnerCustomerProvisionRequestedEvent> context)
    {
        await context.ProcessOnceAsync(_inboxStore, nameof(PartnerCustomerProvisionRequestedConsumer), async () =>
        {
            var evt = context.Message;
            var ct = context.CancellationToken;
            var email = EmailNormalizer.Normalize(evt.Email);

            var existing = await _unitOfWork.Accounts
                .GetAllAsync()
                .FirstOrDefaultAsync(a => a.Email == email && !a.IsDeleted, ct);

            if (existing is not null)
            {
                await LinkExistingAccountAsync(evt, existing, ct);
                return;
            }

            var role = await _unitOfWork.Roles
                .GetAllAsync()
                .Where(r => r.Name == CustomerRoleName && r.Status == RoleStatusEnum.Active && !r.IsDeleted)
                .Select(r => new { r.Id, r.Name })
                .FirstOrDefaultAsync(ct);

            if (role is null)
            {
                // Không có vai trò Customer thì không cấp được tài khoản, và chờ tiếp cũng vô ích.
                // Báo hỏng ngay để dòng import dừng lại kèm lý do đọc được, thay vì treo tới hết giờ.
                await _messageProducer.PublishAsync(new PartnerCustomerProvisionedEvent(
                    evt.BatchId, evt.RowId, evt.ExternalCustomerCode,
                    AccountId: Guid.Empty,
                    WasExisting: false,
                    FailureReason: "The Customer role does not exist or has been deactivated."), ct);

                await _unitOfWork.SaveChangesAsync(ct);
                _logger.LogError(
                    "Cannot provision partner customer {ExternalCode}: Customer role missing.",
                    evt.ExternalCustomerCode);
                return;
            }

            var phone = await ResolveUsablePhoneAsync(evt.PhoneNumber, ct);

            var account = new Domain.Entities.Account
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = _passwordHasher.Hash(GenerateCompliantPassword()),
                FullName = evt.FullName.Trim(),
                PhoneNumber = phone,
                EmailConfirmed = false,
                Status = AccountStatusEnum.Active,
                RoleId = role.Id,
                RoleAssignedAt = DateTime.UtcNow
            };

            await _unitOfWork.Accounts.AddAsync(account);

            // Outbox: mọi event ghi TRƯỚC SaveChanges để cùng một transaction với tài khoản.
            await _messageProducer.PublishAsync(new AccountActivatedEvent(
                account.Id, account.Email, account.FullName, account.PhoneNumber,
                role.Name, CreationSource), ct);

            await _messageProducer.PublishAsync(new PartnerCustomerProvisionedEvent(
                evt.BatchId, evt.RowId, evt.ExternalCustomerCode,
                account.Id, WasExisting: false, FailureReason: null), ct);

            await _messageProducer.PublishAsync(new SendPartnerImportWelcomeEvent(
                account.Id, account.Email, account.FullName), ct);

            await _publisher.Publish(new AuditTrailNotification(
                AuditActionEnum.AccountProvisionedFromPartnerImport, account.Id, IsSuccess: true,
                TargetEmail: account.Email,
                Metadata: new Dictionary<string, object?>
                {
                    ["batchId"] = evt.BatchId.ToString(),
                    ["externalCustomerCode"] = evt.ExternalCustomerCode,
                    ["role"] = role.Name
                }), ct);

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Provisioned partner customer account {AccountId} for external code {ExternalCode} in batch {BatchId}.",
                account.Id, evt.ExternalCustomerCode, evt.BatchId);
        });
    }

    /// <summary>
    /// Email đã có tài khoản: liên kết vào tài khoản đó và bảo đảm bản sao bên BatteryService tồn tại.
    /// </summary>
    private async Task LinkExistingAccountAsync(
        PartnerCustomerProvisionRequestedEvent evt,
        Domain.Entities.Account existing,
        CancellationToken ct)
    {
        var roleName = await _unitOfWork.Roles
            .GetAllAsync()
            .Where(r => r.Id == existing.RoleId)
            .Select(r => r.Name)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
            existing.Id, existing.Email, existing.FullName, existing.PhoneNumber,
            roleName,
            IsActive: existing.Status.IsNotifiable(),
            IsDeleted: existing.IsDeleted,
            SnapshotAtUtc: DateTime.UtcNow,
            Reason: "PartnerImportLink",
            AccountStatus: (int)existing.Status), ct);

        await _messageProducer.PublishAsync(new PartnerCustomerProvisionedEvent(
            evt.BatchId, evt.RowId, evt.ExternalCustomerCode,
            existing.Id, WasExisting: true, FailureReason: null), ct);

        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Linked existing account {AccountId} for partner external code {ExternalCode} in batch {BatchId}.",
            existing.Id, evt.ExternalCustomerCode, evt.BatchId);
    }

    /// <summary>
    /// Số điện thoại đã thuộc về tài khoản khác thì bỏ trống thay vì làm hỏng cả dòng.
    /// </summary>
    /// <remarks>
    /// Đường tạo tài khoản thủ công trả lỗi 409 khi số trùng, và với thao tác một người một lần thì
    /// đúng. Với nhập hàng loạt thì sai: một số trùng làm mất luôn cả khách hàng, site và pin của
    /// dòng đó, trong khi số điện thoại chỉ là thông tin phụ. Bỏ trống số, giữ lại khách.
    /// </remarks>
    private async Task<string?> ResolveUsablePhoneAsync(string? rawPhone, CancellationToken ct)
    {
        var phone = PhoneNormalizer.Normalize(rawPhone);
        if (phone.Length == 0)
            return null;

        var taken = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.PhoneNumber == phone && !a.IsDeleted, ct);

        return taken ? null : phone;
    }

    /// <summary>
    /// Sinh mật khẩu ngẫu nhiên chắc chắn đạt chính sách mật khẩu (hoa, thường, số, ký tự đặc biệt).
    /// </summary>
    /// <remarks>
    /// Không dùng <c>Guid.NewGuid().ToString("N")</c> như chỗ giữ chỗ của luồng mời: chuỗi đó chỉ có
    /// chữ thường và số nên không đạt chính sách. Ở luồng mời điều đó vô hại vì tài khoản chưa hoạt
    /// động; ở đây tài khoản hoạt động ngay nên mật khẩu phải là một giá trị hợp lệ thật sự.
    /// </remarks>
    private static string GenerateCompliantPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        const string all = upper + lower + digits + special;

        var chars = new char[24];
        chars[0] = Pick(upper);
        chars[1] = Pick(lower);
        chars[2] = Pick(digits);
        chars[3] = Pick(special);
        for (var i = 4; i < chars.Length; i++)
            chars[i] = Pick(all);

        // Xáo trộn để bốn ký tự bắt buộc không luôn nằm ở đầu.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}
