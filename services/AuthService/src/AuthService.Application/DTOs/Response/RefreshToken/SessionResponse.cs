using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.RefreshToken;

public class SessionListResponse : CommonResponse<List<SessionDto>> { }

public class SessionActionResponse : CommonResponse<int> { }
