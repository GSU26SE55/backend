namespace AuthService.Domain.Enums;

/// <summary>
/// Phân loại kết quả của 1 login attempt được persist vào DB.
/// </summary>
public enum LoginAttemptResult
{
    Success = 1,
    WrongPassword = 2,
    AccountNotFound = 3,
    AccountLocked = 4,
    AccountSuspended = 5,
    AccountBanned = 6,
    AccountInactive = 7,
    AccountNotVerified = 8,
}
