namespace AuthService.Domain.Enums;

/// <summary>
/// Trạng thái của một role trong hệ thống.
/// </summary>
public enum RoleStatusEnum
{
    /// <summary>Role đang được sử dụng và có thể gán cho account.</summary>
    Active = 1,

    /// <summary>Role tạm thời bị vô hiệu hóa, không thể gán mới.</summary>
    Inactive = 2,

    /// <summary>Role đã bị deprecated, không dùng nữa.</summary>
    Deprecated = 3
}
