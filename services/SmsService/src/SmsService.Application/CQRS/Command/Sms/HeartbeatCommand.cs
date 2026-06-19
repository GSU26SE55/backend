using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace SmsService.Application.CQRS.Command.Sms;

/// <summary>
/// Device gọi định kỳ 60s để báo "alive" — cập nhật <c>LastSeenAt</c> + <c>LastSeenIp</c>.
/// <para>
/// <c>DeviceId</c> lấy từ JWT-like claim (controller gán) — KHÔNG bind từ body/query
/// (<c>[JsonIgnore] + [BindNever]</c>) để chống device A heartbeat thay cho device B.
/// <c>Ip</c> lấy từ <c>HttpContext.Connection.RemoteIpAddress</c>, cũng do controller gán.
/// </para>
/// </summary>
public class HeartbeatCommand : IRequest<CommonResponse<string>>
{
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string? Ip { get; set; }
}
