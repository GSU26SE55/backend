using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SmsService.Application.DTOs.Response.Sms;

namespace SmsService.Application.CQRS.Command.Sms;

/// <summary>
/// Device gọi để claim 1 batch SMS Pending (chuyển trạng thái <c>Pending → Sending</c>).
/// <para>
/// <c>DeviceId</c> + <c>DeviceCode</c> lấy từ <c>GatewayApiKey</c> JWT-like claim (controller gán) —
/// KHÔNG được bind từ body/query (<c>[JsonIgnore] + [BindNever]</c>) để tránh user gửi device khác
/// để escape rate limit hoặc claim cross-device.
/// </para>
/// <para><c>Limit</c> bị clamp về <c>[1..20]</c> trong handler.</para>
/// </summary>
public class ClaimPendingMessagesCommand : IRequest<CommonResponse<List<PendingSmsDto>>>
{
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;

    public int Limit { get; set; }
}
