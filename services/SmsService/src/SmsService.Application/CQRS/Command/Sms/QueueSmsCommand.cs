using MediatR;
using SharedContracts.Common.Responses;

namespace SmsService.Application.CQRS.Command.Sms;

/// <summary>
/// Command queue 1 SMS vào DB <c>sms_messages</c> ở trạng thái Pending.
/// Gọi nội bộ từ <c>SendSmsCommandConsumer</c> (RabbitMQ) hoặc <c>SendPhoneOtpConsumer</c> (backward-compat).
/// Trả về Guid của SMS row vừa tạo trong <c>CommonResponse&lt;Guid&gt;.Data</c>.
/// </summary>
public class QueueSmsCommand : IRequest<CommonResponse<Guid>>
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string SourceService { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public string? Category { get; set; }
    public string? TargetDeviceCode { get; set; }
}
