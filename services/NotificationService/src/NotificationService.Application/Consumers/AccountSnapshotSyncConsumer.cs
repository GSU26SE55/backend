using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Events;

namespace NotificationService.Application.Consumers;

/// <summary>
/// 02/08/2026 — Đồng bộ read-model account từ <see cref="AccountSyncSnapshotEvent"/>: upsert TOÀN BỘ
/// trường, kể cả <c>Role</c> và <c>IsActive</c> — hai trường mà 3 consumer vòng đời cũ
/// (Activated / ProfileUpdated / Deleted) không bao giờ cập nhật đúng được.
///
/// <para><b>Vì sao cần:</b> <c>RecipientResolver.GetActiveByRoleAsync</c> lọc theo đúng
/// <c>Role</c> + <c>IsActive</c>. Trước bản vá này:
/// <list type="bullet">
/// <item>Đổi role không phát event nào ⇒ read-model giữ role cũ vĩnh viễn ⇒ gửi thông báo cho
/// đúng nhóm sai người.</item>
/// <item>Khoá/đình chỉ tài khoản (status đổi, chưa xoá) không phát event nào ⇒ read-model vẫn
/// <c>IsActive = true</c> ⇒ vẫn tiếp tục nhận thông báo.</item>
/// <item>Account tạo bằng <c>AuthDataSeeder</c> không phát event nào ⇒ không bao giờ có mặt trong
/// read-model ⇒ <c>GetActiveByRoleAsync("Admin")</c> trả rỗng ⇒ consumer skip, không ai nhận
/// được gì.</item>
/// </list></para>
///
/// <para><b>Idempotent:</b> snapshot có thể được phát lại tuỳ ý (resync chạy bao nhiêu lần cũng
/// được) — handler là upsert thuần, không có tác dụng phụ nghiệp vụ nào (không gửi welcome, không
/// ghi notification).</para>
///
/// <para>Lỗi DB → throw → MassTransit auto-retry.</para>
/// </summary>
public class AccountSnapshotSyncConsumer : IConsumer<AccountSyncSnapshotEvent>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly ILogger<AccountSnapshotSyncConsumer> _logger;

    public AccountSnapshotSyncConsumer(
        INotificationUnitOfWork unitOfWork,
        ILogger<AccountSnapshotSyncConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AccountSyncSnapshotEvent> context)
    {
        var evt = context.Message;

        // GetAllAsync là SYNC (trả IQueryable). KHÔNG lọc !IsDeleted ở đây: một account bị xoá mềm
        // rồi khôi phục bên AuthService phải sống lại được trong read-model, mà muốn vậy thì phải
        // tìm thấy dòng cũ đã. Lọc IsDeleted là việc của RecipientResolver.
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == evt.AccountId, context.CancellationToken);

        if (account is null)
        {
            // Account đã xoá bên AuthService và bên này chưa từng có dòng nào — không có gì để mirror.
            if (evt.IsDeleted)
            {
                _logger.LogDebug(
                    "Snapshot {Reason}: account={AccountId} đã xoá và chưa có trong read-model — bỏ qua.",
                    evt.Reason, evt.AccountId);
                return;
            }

            await _unitOfWork.BeginTransactionAsync();
            try
            {
                // Tạo cả account chưa Active (PendingVerification/Locked/...) với IsActive=false, để
                // read-model là bản sao đúng của AuthService. Resolver đã lọc IsActive nên chúng
                // không lọt vào danh sách gửi; đổi lại, khi account được kích hoạt sau này thì chỉ
                // còn là một lần UPDATE chứ không phải tình huống "không tìm thấy".
                await _unitOfWork.Accounts.AddAsync(new AccountReadModel
                {
                    Id = evt.AccountId,
                    Email = evt.Email.Trim().ToLowerInvariant(),
                    FullName = evt.FullName.Trim(),
                    PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim(),
                    Role = evt.Role.Trim(),
                    IsActive = evt.IsActive,
                    IsDeleted = false,
                    DeletedAt = null,
                    LastSyncedAtUtc = DateTime.UtcNow,
                    LastSnapshotAtUtc = evt.SnapshotAtUtc
                });

                await _unitOfWork.CommitTransactionAsync();
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }

            _logger.LogInformation(
                "Snapshot {Reason}: tạo read-model account={AccountId} role={Role} active={IsActive}.",
                evt.Reason, evt.AccountId, evt.Role, evt.IsActive);
            return;
        }

        // Chặn snapshot về trễ ghi đè snapshot mới hơn. So sánh với mốc CỦA EVENT
        // (LastSnapshotAtUtc), không phải mốc lúc consume (LastSyncedAtUtc) — xem chú thích ở
        // AccountReadModel.LastSnapshotAtUtc.
        //
        // Dùng >= : hai snapshot cùng mốc thời gian thì nội dung như nhau, ghi lại không được gì.
        if (account.LastSnapshotAtUtc is { } applied && applied >= evt.SnapshotAtUtc)
        {
            _logger.LogDebug(
                "Snapshot {Reason}: bỏ qua bản cũ cho account={AccountId} (đã áp {Applied:o} >= bản này {Incoming:o}).",
                evt.Reason, evt.AccountId, applied, evt.SnapshotAtUtc);
            return;
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            account.Email = evt.Email.Trim().ToLowerInvariant();
            account.FullName = evt.FullName.Trim();
            account.PhoneNumber = string.IsNullOrWhiteSpace(evt.PhoneNumber) ? null : evt.PhoneNumber.Trim();
            account.Role = evt.Role.Trim();
            account.IsActive = evt.IsActive;
            account.LastSyncedAtUtc = DateTime.UtcNow;
            account.LastSnapshotAtUtc = evt.SnapshotAtUtc;

            if (evt.IsDeleted)
            {
                // DeleteAsync là VOID; AuditableEntityInterceptor tự chuyển thành soft delete
                // (IsDeleted = true, DeletedAt = UtcNow). Gọi sau khi gán field để các giá trị trên
                // vẫn được ghi kèm.
                _unitOfWork.Accounts.DeleteAsync(account);
            }
            else
            {
                // Khôi phục dòng đã xoá mềm — AuthService có thể đã undelete account.
                account.IsDeleted = false;
                account.DeletedAt = null;
                _unitOfWork.Accounts.UpdateAsync(account);
            }

            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }

        _logger.LogInformation(
            "Snapshot {Reason}: cập nhật read-model account={AccountId} role={Role} active={IsActive} deleted={IsDeleted}.",
            evt.Reason, evt.AccountId, evt.Role, evt.IsActive, evt.IsDeleted);
    }
}
