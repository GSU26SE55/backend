using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// Đổi role của account sang role mới. Quan hệ 1-N: mỗi account chỉ có 1 role.
/// Nếu role mới trùng role hiện tại → 200 OK nhưng không phát sinh thay đổi.
///
/// 02/08/2026 — phát thêm <see cref="AccountSyncSnapshotEvent"/>. Trước đó handler này không phát
/// event tích hợp nào, nên read-model account bên NotificationService giữ role CŨ vĩnh viễn; mà
/// <c>RecipientResolver</c> lại resolve người nhận theo đúng trường role đó, nên đổi role xong là
/// thông báo nhóm gửi sai người cho tới khi có ai đó đối soát thủ công.
/// </summary>
public class ChangeAccountRoleCommandHandler : IRequestHandler<ChangeAccountRoleCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly IMessageProducerService _messageProducer;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public ChangeAccountRoleCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IMessageProducerService messageProducer,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _messageProducer = messageProducer;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AccountActionResponse> Handle(ChangeAccountRoleCommand request, CancellationToken cancellationToken)
    {
        // GH-769 — Include(Role) để biết TÊN role cũ. Consumer bên Battery/Ticket cần cả vế cũ
        // lẫn vế mới: chỉ có role mới thì không suy ra được bản sao NÀO phải dọn.
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (account == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.Status == RoleStatusEnum.Active && !r.IsDeleted, cancellationToken);

        if (role == null)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Role không tồn tại hoặc đã bị vô hiệu hóa."
            };
        }

        var previousRoleId = account.RoleId;
        // Chụp TÊN role cũ ngay đây — sau khi gán RoleId mới thì navigation Role có thể đã đổi.
        // Role rỗng là hợp lệ: account Google OAuth chưa onboard chưa có role nào.
        var previousRoleName = account.Role?.Name ?? string.Empty;

        if (previousRoleId == request.RoleId)
        {
            return new AccountActionResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Role không thay đổi.",
                Data = account.Id
            };
        }

        var actorId = ResolveActorId();
        var newRoleId = request.RoleId;

        // #AUTH-34: retry trên DbUpdateConcurrencyException — race với ChangePassword / Disable / admin khác.
        var auditPublished = false;
        var snapshotPublished = false;
        var accountInvalidAfterReload = false;

        // Tính MỘT LẦN ngoài vòng retry: mốc này là khoá chống-về-trễ ở consumer, retry mà đổi mốc
        // thì hai lần thử của cùng một thao tác sẽ trông như hai thao tác khác nhau.
        var snapshotAtUtc = DateTime.UtcNow;

        await ConcurrencyRetryHelper.ExecuteAsync<bool>(
            operation: async (attempt, ct) =>
            {
                if (attempt > 1 && account.IsDeleted)
                {
                    accountInvalidAfterReload = true;
                    return false;
                }

                // Re-check trùng role sau reload (admin khác có thể đã đổi sang role này)
                if (attempt > 1 && account.RoleId == newRoleId)
                {
                    accountInvalidAfterReload = true;
                    return false;
                }

                account.RoleId = newRoleId;
                account.RoleAssignedAt = DateTime.UtcNow;
                account.RoleAssignedBy = actorId;
                _unitOfWork.Accounts.UpdateAsync(account);

                if (!auditPublished)
                {
                    await _publisher.Publish(new AuditTrailNotification(
                        AuditActionEnum.RoleAssigned, request.AccountId, IsSuccess: true,
                        TargetEmail: account.Email,
                        Metadata: new Dictionary<string, object?>
                        {
                            ["previousRoleId"] = previousRoleId.ToString(),
                            ["newRoleId"] = newRoleId.ToString(),
                            ["newRoleName"] = role.Name
                        }), ct);
                    auditPublished = true;
                }

                if (!snapshotPublished)
                {
                    // Outbox: INSERT vào OutboxMessages của cùng DbContext → atomic với việc đổi role
                    // ở SaveChangesAsync bên dưới. Role vừa gán là role.Name (đã kiểm tra Active).
                    await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
                        account.Id,
                        account.Email,
                        account.FullName,
                        account.PhoneNumber,
                        role.Name,
                        IsActive: account.Status.IsNotifiable(),
                        IsDeleted: false,
                        SnapshotAtUtc: snapshotAtUtc,
                        Reason: "RoleChanged"), ct);

                    // GH-769 — Battery và Ticket KHÔNG nghe AccountSyncSnapshotEvent (chỉ
                    // NotificationService nghe), nên trước đây đổi role xong bản sao ở hai service
                    // đó giữ nguyên role cũ: thiếu StaffAccount thì không giao ticket được, thừa
                    // CustomerAccount thì vẫn giữ quyền nghiệp vụ cũ.
                    await _messageProducer.PublishAsync(new AccountRoleChangedEvent(
                        account.Id,
                        account.Email,
                        account.FullName,
                        account.PhoneNumber,
                        OldRole: previousRoleName,
                        NewRole: role.Name,
                        ChangedAtUtc: snapshotAtUtc), ct);

                    snapshotPublished = true;
                }

                await _unitOfWork.SaveChangesAsync(ct);
                return true;
            },
            reload: ct => _unitOfWork.Accounts.ReloadAsync(account, ct),
            cancellationToken: cancellationToken);

        if (accountInvalidAfterReload)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tài khoản đã bị thay đổi bởi tiến trình khác. Vui lòng thử lại."
            };
        }

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Đã đổi role sang {role.Name}.",
            Data = account.Id
        };
    }

    private Guid? ResolveActorId()
    {
        var raw = _httpContextAccessor?.HttpContext?.User?.FindFirst("AccountId")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
