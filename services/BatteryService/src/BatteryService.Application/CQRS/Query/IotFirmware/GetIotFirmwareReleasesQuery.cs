using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotFirmware;

public class GetIotFirmwareReleasesQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>>
{
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Chỉ trả release đã publish + chưa archive.</summary>
    public bool? PublishedOnly { get; set; }

    /// <summary>
    /// Cột sort. Whitelist: version | hardwareRevision | channel | status | artifactSizeBytes | createdAt.
    /// status = rank lifecycle (Draft=0 &lt; Published=1 &lt; Archived=2). Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
