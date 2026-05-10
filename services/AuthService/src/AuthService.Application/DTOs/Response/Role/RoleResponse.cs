using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Role;

public class RoleResponse : CommonResponse<RoleDto> { }

public class RoleListResponse : CommonResponse<PaginationResponse<RoleDto>> { }

public class RoleActionResponse : CommonResponse<Guid> { }
