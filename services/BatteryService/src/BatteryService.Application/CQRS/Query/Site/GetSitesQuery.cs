using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.Site;

public class GetSitesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<SiteDto>>>
{
    /// <summary>Từ khoá search (case-insensitive).</summary>
    public string? Keyword { get; set; }

    /// <summary>ID Customer (Guid).</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public SiteStatusEnum? Status { get; set; }

    /// <summary>Bao gồm soft-deleted records.</summary>
    public bool IncludeDeleted { get; set; }
}
