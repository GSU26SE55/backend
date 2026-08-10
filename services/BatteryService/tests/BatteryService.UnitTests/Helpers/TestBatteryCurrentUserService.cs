using BatteryService.Application.Interfaces;

namespace BatteryService.UnitTests.Helpers;

/// <summary>
/// GH-722 — test double cho <see cref="IBatteryCurrentUserService"/>.
///
/// <see cref="Admin"/> là mặc định dùng cho các test CÓ TRƯỚC issue này: chúng kiểm tra
/// hành vi nghiệp vụ chứ không kiểm tra phân quyền tenant, nên phải chạy ở phạm vi
/// không giới hạn — đúng như trước khi thêm scope. Nhờ vậy assertion cũ giữ nguyên ý nghĩa.
/// </summary>
public sealed class TestBatteryCurrentUserService : IBatteryCurrentUserService
{
    public TestBatteryCurrentUserService(string? userId, params string[] roles)
    {
        UserId = userId;
        Roles = roles;
    }

    public string? UserId { get; }

    public IReadOnlyCollection<string> Roles { get; }

    /// <summary>Không giới hạn tenant — dùng cho test không liên quan phân quyền.</summary>
    public static TestBatteryCurrentUserService Admin() =>
        new(Guid.NewGuid().ToString(), "Admin");

    /// <summary>
    /// GH-774 — Manager: cùng phạm vi dữ liệu với Admin, nhưng cần tách riêng vì có endpoint chỉ
    /// mở cho Admin/Manager (thống kê toàn hệ thống) mà không mở cho Staff.
    /// </summary>
    public static TestBatteryCurrentUserService Manager() =>
        new(Guid.NewGuid().ToString(), "Manager");

    /// <summary>Staff: theo spec §34.10.6 vẫn xem được mọi asset.</summary>
    public static TestBatteryCurrentUserService Staff() =>
        new(Guid.NewGuid().ToString(), "Staff");

    /// <summary>Customer cụ thể — chỉ thấy dữ liệu của <paramref name="customerId"/>.</summary>
    public static TestBatteryCurrentUserService Customer(Guid customerId) =>
        new(customerId.ToString(), "Customer");

    /// <summary>Customer nhưng token không đọc được id ⇒ phải bị chặn (fail closed).</summary>
    public static TestBatteryCurrentUserService CustomerWithBrokenToken() =>
        new(null, "Customer");
}
