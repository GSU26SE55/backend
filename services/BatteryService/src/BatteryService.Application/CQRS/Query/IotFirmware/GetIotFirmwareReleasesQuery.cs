using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Query.IotFirmware;

public class GetIotFirmwareReleasesQuery : IRequest<CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>>
{
    public string? HardwareRevision { get; set; }
    public bool? PublishedOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
