namespace AuthService.Domain.Enums;

/// <summary>
/// Trạng thái của refresh token trong vòng đời xác thực.
/// </summary>
public enum RefreshTokenStatus
{
    /// <summary>Token còn hiệu lực, có thể dùng để refresh access token.</summary>
    Active = 1,

    /// <summary>Token đã được dùng để cấp token mới (rotation).</summary>
    Used = 2,

    /// <summary>Token đã bị thu hồi thủ công (logout, đổi mật khẩu, admin revoke...).</summary>
    Revoked = 3,

    /// <summary>Token đã hết hạn theo thời gian.</summary>
    Expired = 4,

    /// <summary>Token bị nghi ngờ tái sử dụng (replay attack) - toàn bộ chain bị invalidate.</summary>
    Compromised = 5
}
