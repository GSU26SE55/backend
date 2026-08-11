using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Application.DTOs.Response.Permission;
using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Handler.Permission;

public class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand, PermissionActionResponse>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly IPublisher _publisher;
    private readonly IMessageProducerService _messageProducer;

    public SetRolePermissionsCommandHandler(
        IAuthUnitOfWork unitOfWork,
        IPublisher publisher,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _messageProducer = messageProducer;
    }

    public async Task<PermissionActionResponse> Handle(SetRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        var role = await _unitOfWork.Roles
            .GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && !r.IsDeleted, cancellationToken);

        if (role == null)
            return Fail(404, "Role does not exist.");

        if (role.IsSystemRole && !request.AllowSystemRole)
            return Fail(403, "Cannot change permissions of a default system role. Set AllowSystemRole=true to override.");

        var requestedIds = request.PermissionIds.Distinct().ToList();
        var validIds = await _unitOfWork.Permissions
            .GetAllAsync()
            .Where(p => requestedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var missing = requestedIds.Except(validIds).ToList();
        if (missing.Count > 0)
            return Fail(404, $"{missing.Count} permission(s) do not exist.");

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

        // GH-771 — AuthService có sẵn PermissionsChangedConsumer để xoá cache của PermissionResolver,
        // nhưng KHÔNG NƠI NÀO phát event ⇒ quyền vừa bị thu hồi vẫn còn hiệu lực tới hết TTL 5 phút.
        // Với một thao tác THU HỒI quyền, năm phút là năm phút hệ thống cố tình cho qua thứ mà quản
        // trị viên vừa chặn.
        //
        // Phát ĐÚNG những gì đã xảy ra: thêm và gỡ là hai sự việc khác nhau, mỗi cái mang mã của
        // chính nó. Gộp làm một sẽ phải chọn bừa một `ChangeKind` và làm sai lệch nhật ký. Xoá
        // cache là thao tác lũy đẳng nên phát hai event cho một lần sửa hỗn hợp là vô hại.
        if (toAdd.Count > 0 || toRemove.Count > 0)
        {
            var codeById = await _unitOfWork.Permissions
                .GetAllAsync()
                .Where(p => toAdd.Contains(p.Id) || toRemove.Select(r => r.PermissionId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);

            // RoleCode phải khớp Role.NormalizedName — consumer tra role theo đúng cột đó.
            if (toRemove.Count > 0)
            {
                await _messageProducer.PublishAsync(new PermissionsChangedEvent(
                    ChangeKind: "UnboundFromRole",
                    RoleCode: role.NormalizedName,
                    AffectedPermissionCodes: toRemove
                        .Select(rp => codeById.TryGetValue(rp.PermissionId, out var c) ? c : null)
                        .Where(c => c is not null)
                        .Select(c => c!)
                        .ToArray(),
                    ChangedAt: now), cancellationToken);
            }

            if (toAdd.Count > 0)
            {
                await _messageProducer.PublishAsync(new PermissionsChangedEvent(
                    ChangeKind: "BoundToRole",
                    RoleCode: role.NormalizedName,
                    AffectedPermissionCodes: toAdd
                        .Select(id => codeById.TryGetValue(id, out var c) ? c : null)
                        .Where(c => c is not null)
                        .Select(c => c!)
                        .ToArray(),
                    ChangedAt: now), cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PermissionActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Set {requestedSet.Count} permission(s) for role {role.Name} (+{toAdd.Count} / -{toRemove.Count}).",
            Data = role.Id
        };
    }

    private static PermissionActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message,
    };
}
