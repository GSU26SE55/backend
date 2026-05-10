using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Account;

public class AccountResponse : CommonResponse<AccountDto> { }

public class AccountListResponse : CommonResponse<PaginationResponse<AccountDto>> { }

public class AccountActionResponse : CommonResponse<Guid> { }
