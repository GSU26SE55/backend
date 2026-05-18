using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Account;

public class AssignRoleTemporaryCommandHandler : IRequestHandler<AssignRoleTemporaryCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public AssignRoleTemporaryCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(AssignRoleTemporaryCommand request, CancellationToken cancellationToken)
    {
        var account = await _unitOfWork.Accounts.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);
        if (account == null)
            return Fail(404, "Account", "Không tìm thấy tài khoản.");

        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.Status == RoleStatusEnum.Active, cancellationToken);
        if (role == null)
            return Fail(404, "RoleId", "Role không tồn tại hoặc đã bị vô hiệu hóa.");

        var existing = await _unitOfWork.AccountRoles
            .GetAllAsync()
            .FirstOrDefaultAsync(ar => ar.AccountId == request.AccountId && ar.RoleId == request.RoleId, cancellationToken);

        if (existing != null)
        {
            existing.IsActive = true;
            existing.AssignedAt = DateTime.UtcNow;
            existing.ExpiredAt = request.ExpiredAt;
            _unitOfWork.AccountRoles.UpdateAsync(existing);
        }
        else
        {
            await _unitOfWork.AccountRoles.AddAsync(new AccountRole
            {
                Id = Guid.NewGuid(),
                AccountId = request.AccountId,
                RoleId = request.RoleId,
                AssignedAt = DateTime.UtcNow,
                ExpiredAt = request.ExpiredAt,
                IsActive = true
            });
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleTemporaryAssigned, request.AccountId, IsSuccess: true,
            TargetEmail: account.Email,
            Metadata: new Dictionary<string, object?>
            {
                ["roleId"] = request.RoleId.ToString(),
                ["roleName"] = role.Name,
                ["expiredAt"] = request.ExpiredAt
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Gán role tạm thời thành công, hết hạn {request.ExpiredAt:u}.",
            Data = account.Id
        };
    }

    private static AccountActionResponse Fail(int statusCode, string field, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = field, Detail = message } }
    };
}
