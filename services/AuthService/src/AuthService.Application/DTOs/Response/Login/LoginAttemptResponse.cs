using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Login;

public class LoginAttemptListResponse : CommonResponse<PaginationResponse<LoginAttemptDto>> { }
