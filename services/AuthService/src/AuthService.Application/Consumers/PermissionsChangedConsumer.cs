using AuthService.Application.Authorization;
using AuthService.Application.Interfaces.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;

namespace AuthService.Application.Consumers;

/// <summary>
/// #AUTH-15 + #AUTH-16: khi role-permission mapping thay đổi (seed mới hoặc admin bind/unbind),
/// invalidate <see cref="PermissionResolver"/> cache cho role bị ảnh hưởng để JWT issuance lần kế tiếp
/// đọc lại từ DB.
///
/// JWT đã cấp trước đó vẫn còn permission cũ trong claim cho đến khi expire / refresh — Token Revocation
/// List (TRL) để invalidate jti realtime sẽ implement trong #AUTH-54 (Phase D, out of current scope).
/// </summary>
public class PermissionsChangedConsumer : IConsumer<PermissionsChangedEvent>
{
    private readonly IAuthUnitOfWork _unitOfWork;
    private readonly ILogger<PermissionsChangedConsumer> _logger;

    public PermissionsChangedConsumer(IAuthUnitOfWork unitOfWork, ILogger<PermissionsChangedConsumer> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PermissionsChangedEvent> context)
    {
        var evt = context.Message;

        if (string.IsNullOrWhiteSpace(evt.RoleCode))
        {
            PermissionResolver.InvalidateAll();
            _logger.LogInformation(
                "PermissionsChangedEvent without RoleCode (kind={Kind}) → flush full permission cache.",
                evt.ChangeKind);
            return;
        }

        // PermissionsChangedEvent.RoleCode khớp với Role.NormalizedName (ví dụ "ADMIN", "MANAGER").
        var normalized = evt.RoleCode.Trim().ToUpperInvariant();
        var roleId = await _unitOfWork.Roles.GetAllAsync()
            .Where(r => !r.IsDeleted && r.NormalizedName == normalized)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (roleId is null)
        {
            _logger.LogWarning(
                "PermissionsChangedEvent for unknown RoleCode={RoleCode} kind={Kind}. Flushing full cache as safety net.",
                evt.RoleCode, evt.ChangeKind);
            PermissionResolver.InvalidateAll();
            return;
        }

        PermissionResolver.InvalidateRole(roleId.Value);
        _logger.LogInformation(
            "Invalidated permission cache for RoleCode={RoleCode} RoleId={RoleId} (kind={Kind}, affected={Count}).",
            evt.RoleCode, roleId.Value, evt.ChangeKind, evt.AffectedPermissionCodes.Length);
    }
}
