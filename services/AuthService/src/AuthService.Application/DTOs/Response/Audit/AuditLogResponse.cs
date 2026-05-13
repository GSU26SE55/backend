using SharedContracts.Common.Responses;

namespace AuthService.Application.DTOs.Response.Audit;

public class AuditLogListResponse : CommonResponse<PaginationResponse<AuditLogDto>> { }
