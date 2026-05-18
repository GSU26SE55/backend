using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Application.CQRS.Handler.Account;

public class AssignRolesCommandHandler : IRequestHandler<AssignRolesCommand, AccountActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public AssignRolesCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<AccountActionResponse> Handle(AssignRolesCommand request, CancellationToken cancellationToken)
    {
        var accountExists = await _unitOfWork.Accounts
            .GetAllAsync()
            .AnyAsync(a => a.Id == request.AccountId && !a.IsDeleted, cancellationToken);

        if (!accountExists)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy tài khoản."
            };
        }

        var validRoleIds = await _unitOfWork.Roles
            .GetAllAsync()
            .Where(r => request.RoleIds.Contains(r.Id) && r.Status == RoleStatusEnum.Active)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var missing = request.RoleIds.Except(validRoleIds).ToList();
        if (missing.Count > 0)
        {
            return new AccountActionResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = $"Có {missing.Count} role không tồn tại hoặc đã bị vô hiệu hóa."
            };
        }

        var existingAssignments = await _unitOfWork.AccountRoles
            .GetAllAsync()
            .Where(ar => ar.AccountId == request.AccountId && validRoleIds.Contains(ar.RoleId))
            .ToListAsync(cancellationToken);

        foreach (var roleId in validRoleIds)
        {
            var existing = existingAssignments.FirstOrDefault(ar => ar.RoleId == roleId);
            if (existing != null)
            {
                existing.IsActive = true;
                existing.ExpiredAt = request.ExpiredAt;
                existing.AssignedAt = DateTime.UtcNow;
                _unitOfWork.AccountRoles.UpdateAsync(existing);
            }
            else
            {
                await _unitOfWork.AccountRoles.AddAsync(new AccountRole
                {
                    Id = Guid.NewGuid(),
                    AccountId = request.AccountId,
                    RoleId = roleId,
                    AssignedAt = DateTime.UtcNow,
                    ExpiredAt = request.ExpiredAt,
                    IsActive = true
                });
            }
        }

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleAssigned, request.AccountId, IsSuccess: true,
            Metadata: new Dictionary<string, object?>
            {
                ["roleIds"] = validRoleIds.Select(id => id.ToString()).ToList(),
                ["expiredAt"] = request.ExpiredAt
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AccountActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Đã gán {validRoleIds.Count} role cho tài khoản.",
            Data = request.AccountId
        };
    }
}
