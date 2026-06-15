using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;

namespace SmsService.Application.CQRS.Command.Sms;

/// <summary>
/// Admin huỷ 1 SMS trước khi device gửi. Chỉ accept khi status còn ở
/// <c>Pending</c> hoặc <c>Sending</c> — đã <c>Sent</c>/<c>Failed</c>/<c>Cancelled</c> → 409.
/// <para>
/// <c>SmsId</c> chỉ được set bởi controller từ route param <c>{id:guid}</c>; KHÔNG bind từ body
/// (<c>[JsonIgnore]</c>) để chống override route id qua JSON.
/// </para>
/// </summary>
public class CancelSmsCommand : IRequest<CommonResponse<string>>
{
    [JsonIgnore]
    public Guid SmsId { get; set; }
}
