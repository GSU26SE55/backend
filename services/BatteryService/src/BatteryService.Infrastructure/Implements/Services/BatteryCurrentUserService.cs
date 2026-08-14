using System.Security.Claims;
using BatteryService.Application.Interfaces;
using Microsoft.AspNetCore.Http;

namespace BatteryService.Infrastructure.Implements.Services;

/// <summary>
/// GH-722 — hiện thực <see cref="IBatteryCurrentUserService"/>.
/// </summary>
/// <remarks>
/// <para>
/// <c>UserId</c> đọc ĐÚNG claim mà <c>SharedInfrastructure.CurrentUserService</c> đang dùng
/// (<see cref="ClaimTypes.NameIdentifier"/>). Không đổi sang claim khác: các endpoint
/// <c>/me</c> hiện có (<c>GetMyBatteryAssetsQueryHandler</c>, <c>GetMySitesQueryHandler</c>)
/// đã coi giá trị này là <c>CustomerId</c> và đang chạy đúng trên production — đổi nguồn
/// claim ở đây sẽ âm thầm làm lệch quyền sở hữu.
/// </para>
/// <para>
/// <c>Roles</c> đọc <see cref="ClaimTypes.Role"/> — giống hệt
/// <c>SensorTelemetryStreamController</c>, và là claim mà <c>[Authorize(Roles = …)]</c> dùng.
/// </para>
/// </remarks>
public class BatteryCurrentUserService : IBatteryCurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BatteryCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public IReadOnlyCollection<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
        ?? Array.Empty<string>();
}
