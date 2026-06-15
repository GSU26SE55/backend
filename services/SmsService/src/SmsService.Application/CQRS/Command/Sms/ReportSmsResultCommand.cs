using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;

namespace SmsService.Application.CQRS.Command.Sms;

/// <summary>
/// Device báo kết quả gửi SMS — <c>Status</c> = "Sent" hoặc "Failed" (case-insensitive).
/// Idempotent: chỉ accept khi <c>SmsStatus == Sending</c> AND <c>GatewayDeviceCode</c> khớp.
/// Mọi trạng thái khác trả 200 no-op để Flutter dừng retry.
/// <para>
/// <c>DeviceId</c> + <c>DeviceCode</c> lấy từ JWT-like claim (controller gán) — KHÔNG bind từ body
/// (<c>[JsonIgnore] + [BindNever]</c>) để chống device A spoof report kết quả cho device B.
/// <c>SmsId</c>, <c>Status</c>, <c>ErrorMessage</c> là body fields (đến từ <c>ReportSmsRequest</c>).
/// </para>
/// </summary>
public class ReportSmsResultCommand : IRequest<CommonResponse<string>>
{
    [JsonIgnore]
    [BindNever]
    public Guid DeviceId { get; set; }

    [JsonIgnore]
    [BindNever]
    public string DeviceCode { get; set; } = string.Empty;

    public Guid SmsId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}
