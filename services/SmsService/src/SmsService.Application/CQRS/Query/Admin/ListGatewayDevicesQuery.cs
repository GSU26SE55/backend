using MediatR;
using SharedContracts.Common.Responses;
using SmsService.Application.DTOs.Response.Admin;

namespace SmsService.Application.CQRS.Query.Admin;

/// <summary>List tất cả device gateway. Mặc định include cả revoked để admin xem lịch sử.</summary>
public class ListGatewayDevicesQuery : IRequest<CommonResponse<List<GatewayDeviceDto>>>
{
    public bool IncludeRevoked { get; set; } = true;
}
