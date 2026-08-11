using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.Application.CQRS.Handler.Sms;

public class ReportSmsResultCommandHandler : IRequestHandler<ReportSmsResultCommand, CommonResponse<string>>
{
    private readonly ISmsUnitOfWork _unitOfWork;
    private readonly IMessageProducerService _messageProducer;

    public ReportSmsResultCommandHandler(
        ISmsUnitOfWork unitOfWork,
        IMessageProducerService messageProducer)
    {
        _unitOfWork = unitOfWork;
        _messageProducer = messageProducer;
    }

    public async Task<CommonResponse<string>> Handle(ReportSmsResultCommand request, CancellationToken cancellationToken)
    {
        var sms = await _unitOfWork.SmsMessages
            .GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.SmsId && !x.IsDeleted, cancellationToken);

        if (sms is null)
            return Fail(404, "SMS not found.");

        if (!string.Equals(sms.GatewayDeviceCode, request.DeviceCode, StringComparison.Ordinal))
            return Fail(403, "This device does not hold that SMS.");

        // ── IDEMPOTENCY ──
        // Chỉ accept khi state == Sending. Mọi state khác (Sent/Failed/Pending/Cancelled) coi như duplicate.
        if (sms.Status != SmsStatus.Sending)
            return new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = $"Report ignored: current status is {sms.Status}.",
                Data = sms.Status.ToString()
            };

        var now = DateTime.UtcNow;
        var status = request.Status?.Trim() ?? string.Empty;

        if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            sms.MarkSent(now);

            var device = await _unitOfWork.SmsGatewayDevices.GetByIdAsync(request.DeviceId);
            device?.IncrementSent(now);

            await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
            {
                Id = Guid.NewGuid(),
                SmsMessageId = sms.Id,
                Event = SmsAuditEvent.Sent,
                DeviceCode = request.DeviceCode,
                CreatedAt = now
            });

            // Outbox: publish TRƯỚC SaveChanges → atomic với business data.
            await _messageProducer.PublishAsync(new SmsDeliveryReportEvent(
                sms.Id, sms.CorrelationId, sms.PhoneNumber, sms.SourceService,
                now, request.DeviceCode), cancellationToken);
        }
        else if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            // Lần (RetryCount + 1) tới — nếu < MaxRetryCount còn retry, ngược lại final.
            if (sms.RetryCount + 1 < sms.MaxRetryCount)
            {
                sms.MarkRetry(request.ErrorMessage, now);
                await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
                {
                    Id = Guid.NewGuid(),
                    SmsMessageId = sms.Id,
                    Event = SmsAuditEvent.Retry,
                    DeviceCode = request.DeviceCode,
                    Detail = request.ErrorMessage,
                    CreatedAt = now
                });
            }
            else
            {
                sms.MarkFailedFinal(request.ErrorMessage, now);
                await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
                {
                    Id = Guid.NewGuid(),
                    SmsMessageId = sms.Id,
                    Event = SmsAuditEvent.Failed,
                    DeviceCode = request.DeviceCode,
                    Detail = request.ErrorMessage,
                    CreatedAt = now
                });

                await _messageProducer.PublishAsync(new SmsFailedEvent(
                    sms.Id, sms.CorrelationId, sms.PhoneNumber, sms.SourceService,
                    request.ErrorMessage, now, FinalFailure: true), cancellationToken);
            }
        }
        else
        {
            // Field-level validation: Status value invalid → ListErrors[Status].
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Invalid data.",
                ListErrors = { new Errors { Field = nameof(request.Status), Detail = "Status must be 'Sent' or 'Failed' (case-insensitive)." } },
                Data = string.Empty
            };
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race xmin (sms_messages hoặc sms_gateway_devices). Trả 200 — Flutter đã idempotent.
            return new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Concurrent report; treated as duplicate.",
                Data = sms.Status.ToString()
            };
        }

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OK",
            Data = sms.Status.ToString()
        };
    }

    private static CommonResponse<string> Fail(int code, string msg) => new()
    {
        IsSuccess = false,
        StatusCode = code,
        Message = msg,
        Data = string.Empty
    };
}
