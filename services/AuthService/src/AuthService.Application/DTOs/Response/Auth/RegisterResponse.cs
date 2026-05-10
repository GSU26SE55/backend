using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Auth;

public class RegisterResponse : CommonResponse<RegisterResponseData> { }

public class RegisterResponseData
{
    public string Email { get; set; } = string.Empty;
    public int OtpExpiresInSeconds { get; set; }
}
