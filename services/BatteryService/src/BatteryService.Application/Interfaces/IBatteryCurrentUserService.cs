using SharedInfrastructure.Services;

namespace BatteryService.Application.Interfaces;

/// <summary>
/// GH-722 — mở rộng <see cref="ICurrentUserService"/> cho BatteryService để handler đọc được
/// role của caller, phục vụ giới hạn dữ liệu theo tenant.
///
/// Vì sao không thêm thẳng vào <see cref="ICurrentUserService"/>: interface đó nằm ở
/// SharedInfrastructure và đang có 15 lớp hiện thực trên cả 9 service (kể cả design-time
/// factory và test double). Thêm thành viên ở đó làm vỡ toàn bộ. Đây là cùng khuôn với
/// <c>ITicketCurrentUserService</c> mà TicketService đã dùng.
/// </summary>
public interface IBatteryCurrentUserService : ICurrentUserService
{
    /// <summary>
    /// Role của caller, lấy từ đúng claim mà <c>[Authorize(Roles = …)]</c> dùng
    /// (<see cref="System.Security.Claims.ClaimTypes.Role"/>) — giống hệt cách
    /// <c>SensorTelemetryStreamController</c> lấy để gọi
    /// <see cref="IBatteryRealtimeAuthorizationService"/>.
    /// Rỗng khi chưa đăng nhập.
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }
}
