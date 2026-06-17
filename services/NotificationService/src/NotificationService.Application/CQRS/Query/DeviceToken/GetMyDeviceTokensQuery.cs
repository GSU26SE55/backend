using MediatR;
using NotificationService.Application.DTOs.Response.DeviceToken;

namespace NotificationService.Application.CQRS.Query.DeviceToken;

/// <summary>
/// Liệt kê các thiết bị (device token) của user hiện tại. UserId set từ JWT claim ở controller.
/// </summary>
public class GetMyDeviceTokensQuery : IRequest<DeviceTokenListResponse>
{
    public Guid UserId { get; set; }
}
