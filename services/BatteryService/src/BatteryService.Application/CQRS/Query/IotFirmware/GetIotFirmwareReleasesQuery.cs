using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotFirmware;

public class GetIotFirmwareReleasesQuery : IRequest<CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>>
{
    /// <summary>Hardware revision (vd "v1.0-S3-MAX485").</summary>
    public string? HardwareRevision { get; set; }
    /// <summary>Chỉ trả release đã publish + chưa archive.</summary>
    public bool? PublishedOnly { get; set; }
    /// <summary>Số trang (1-based).</summary>
    public int Page { get; set; } = 1;
    /// <summary>Số bản ghi mỗi trang (clamp [1, 100]).</summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Cột sort. Whitelist: version | hardwareRevision | channel | status | artifactSizeBytes | createdAt.
    /// status = rank lifecycle (Draft=0 &lt; Published=1 &lt; Archived=2). Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Mặc định desc.</summary>
    public string? SortDir { get; set; }
}
