using MediatR;
using Microsoft.Extensions.Logging;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.Interfaces.Services;

namespace SmsService.Infrastructure.Implements.Services;

/// <summary>
/// Sprint 6.3 NOTI3-05 (#705) — hiện thực <see cref="ISmsProvider"/> bằng gateway Android hiện có.
///
/// Provider này **xếp hàng** tin nhắn (<c>QueueSmsCommand</c>) rồi thiết bị gateway kéo về qua
/// SignalR/polling. Vì vậy trả <c>true</c> chỉ nghĩa là "đã nhận đơn", không phải "đã gửi".
/// Đây chính là giới hạn R-44: chỉ có một chiếc điện thoại làm gateway.
/// </summary>
public class GatewaySmsProvider : ISmsProvider
{
    private readonly IMediator _mediator;
    private readonly ILogger<GatewaySmsProvider> _logger;

    public GatewaySmsProvider(IMediator mediator, ILogger<GatewaySmsProvider> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => "android-gateway";

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string phoneNumber,
        string message,
        string sourceService,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new QueueSmsCommand
        {
            PhoneNumber = phoneNumber,
            Message = message,
            SourceService = sourceService,
            // QueueSmsCommand dùng Guid non-nullable; không có correlation thì để Empty.
            CorrelationId = correlationId ?? Guid.Empty,
        }, ct);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "GatewaySmsProvider: không xếp hàng được SMS tới {Phone} — {Reason}.",
                phoneNumber, result.Message);
        }

        return result.IsSuccess;
    }
}
