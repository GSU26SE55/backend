using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Permission;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.Application.CQRS.Handler.Permission;

public class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand, PermissionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;

    public SetRolePermissionsCommandHandler(IAuthUnitOfWork unitOfWork, IPublisher publisher)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
    }

    public async Task<PermissionActionResponse> Handle(SetRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && !r.IsDeleted, cancellationToken);

        if (role == null)
            return Fail(404, "Role không tồn tại.");

        if (role.IsSystemRole && !request.AllowSystemRole)
            return Fail(400, "Không thể thay đổi permission của system role mặc định. Set AllowSystemRole=true nếu muốn override.");

        var requestedIds = request.PermissionIds.Distinct().ToList();
        var validIds = await _unitOfWork.Permissions
            .GetAllAsync()
            .Where(p => requestedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = requestedIds.Except(validIds).ToList();
        if (missing.Count > 0)
            return Fail(400, $"Có {missing.Count} permission không tồn tại.");

        var existing = await _unitOfWork.RolePermissions
            .GetAllAsync()
            .Where(rp => rp.RoleId == request.RoleId && !rp.IsDeleted)
            .ToListAsync(cancellationToken);

        var existingPermissionIds = existing.Select(rp => rp.PermissionId).ToHashSet();
        var requestedSet = validIds.ToHashSet();

        var toAdd = validIds.Where(id => !existingPermissionIds.Contains(id)).ToList();
        var toRemove = existing.Where(rp => !requestedSet.Contains(rp.PermissionId)).ToList();

        var now = DateTime.UtcNow;
        foreach (var permissionId in toAdd)
        {
            await _unitOfWork.RolePermissions.AddAsync(new RolePermission
            {
                Id = Guid.NewGuid(),
                RoleId = request.RoleId,
                PermissionId = permissionId,
                AssignedAt = now
            });
        }

        foreach (var rp in toRemove)
            _unitOfWork.RolePermissions.DeleteAsync(rp);

        await _publisher.Publish(new AuditTrailNotification(
            AuditActionEnum.PermissionGranted, TargetAccountId: null, IsSuccess: true,
            Reason: $"Set permissions for role {role.Name}.",
            Metadata: new Dictionary<string, object?>
            {
                ["roleId"] = role.Id.ToString(),
                ["roleName"] = role.Name,
                ["addedCount"] = toAdd.Count,
                ["removedCount"] = toRemove.Count,
                ["totalAfter"] = requestedSet.Count
            }), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PermissionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Đã set {requestedSet.Count} permission cho role {role.Name} (+{toAdd.Count} / -{toRemove.Count}).",
            Data = role.Id
        };
    }

    private static PermissionActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
        ListErrors = { new Errors { Field = "Permission", Detail = message } }
    };
}
