using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Role;

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, RoleActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;   // Sprint audit #AUDIT-11

    public UpdateRoleCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<RoleActionResponse> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.Id && !r.IsDeleted, cancellationToken);
        if (role == null)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy role."
            };
        }

        if (role.IsSystemRole)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Không thể chỉnh sửa role hệ thống."
            };
        }

        var normalized = request.Name.Trim().ToUpperInvariant();

        var duplicated = await _unitOfWork.Roles
            .GetAllAsync()
            .AnyAsync(r => r.Id != request.Id && r.NormalizedName == normalized && !r.IsDeleted, cancellationToken);

        if (duplicated)
        {
            return new RoleActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tên role đã tồn tại.",
            };
        }

        role.Name = request.Name.Trim();
        role.NormalizedName = normalized;
        role.Description = request.Description?.Trim();

        _unitOfWork.Roles.UpdateAsync(role);

        // #AUDIT-11
        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.RoleUpdated, null, true,
            Metadata: new Dictionary<string, object?> { ["roleId"] = role.Id, ["roleName"] = role.Name }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new RoleActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật role thành công.",
            Data = role.Id
        };
    }
}
