using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

/// <summary>
/// Đổi role của account sang role mới. Quan hệ 1-N: mỗi account chỉ có 1 role.
/// Nếu role mới trùng role hiện tại → 200 OK nhưng không phát sinh thay đổi.
/// </summary>
public class ChangeAccountRoleCommandHandler : IRequestHandler<ChangeAccountRoleCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public ChangeAccountRoleCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AccountActionResponse> Handle(ChangeAccountRoleCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts
            .GetAllAsync()
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
                StatusCode = 400,
                Message = "Role không tồn tại hoặc đã bị vô hiệu hóa."
            };
        }

        var previousRoleId = account.RoleId;

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

        account.RoleId = request.RoleId;
        account.RoleAssignedAt = DateTime.UtcNow;
        account.RoleAssignedBy = ResolveActorId();
        _unitOfWork.Accounts.UpdateAsync(account);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleAssigned, request.AccountId, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?>
            {
                ["previousRoleId"] = previousRoleId.ToString(),
                ["newRoleId"] = request.RoleId.ToString(),
                ["newRoleName"] = role.Name
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
