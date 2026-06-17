using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace SmsService.Application.CQRS.Command.Admin;

/// <summary>
/// Admin thu hồi device gateway (<c>IsActive=false</c> + <c>RevokedAt=now</c>). Idempotent.
/// <para>
/// <c>DeviceId</c> chỉ được set bởi controller từ route param <c>{id:guid}</c>; KHÔNG bind từ body
/// (<c>[JsonIgnore]</c>) để user không thể override route id qua JSON khi endpoint DELETE.
/// </para>
/// </summary>
public class RevokeGatewayDeviceCommand : IRequest<CommonResponse<string>>
{
    [JsonIgnore]
    public Guid DeviceId { get; set; }
}
