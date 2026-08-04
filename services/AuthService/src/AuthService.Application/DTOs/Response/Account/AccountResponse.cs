using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Account;

public class AccountResponse : CommonResponse<AccountDto> { }

public class AccountListResponse : CommonResponse<PaginationResponse<AccountDto>> { }

public class AccountStatsResponse : CommonResponse<AccountStatsDto> { }

public class AccountActionResponse : CommonResponse<Guid> { }

public class AccountResyncResponse : CommonResponse<AccountResyncDto> { }

public class StaffAssignmentProfileResponse : CommonResponse<StaffAssignmentProfileDto> { }

public class StaffAssignmentProfileListResponse : CommonResponse<List<StaffAssignmentProfileDto>> { }
