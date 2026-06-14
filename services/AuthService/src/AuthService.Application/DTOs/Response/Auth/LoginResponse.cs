using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Auth;

/// <summary>
/// Response của <c>POST /api/auth/login</c>. Data có thể là token (login complete) hoặc challenge (cần 2FA).
/// </summary>
public class LoginResponse : CommonResponse<LoginResultDto> { }
