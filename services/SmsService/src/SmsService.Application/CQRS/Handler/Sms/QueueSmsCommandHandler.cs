using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SmsService.Application.Common;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.Application.CQRS.Handler.Sms;

public class QueueSmsCommandHandler : IRequestHandler<QueueSmsCommand, CommonResponse<Guid>>
{
    private const int MaxMessageLength = 1600;

    private readonly ISmsUnitOfWork _unitOfWork;
    private readonly ISmsGatewayNotifier _notifier;
    private readonly ILogger<QueueSmsCommandHandler> _logger;

    public QueueSmsCommandHandler(
        ISmsUnitOfWork unitOfWork,
        ISmsGatewayNotifier notifier,
        ILogger<QueueSmsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<CommonResponse<Guid>> Handle(QueueSmsCommand request, CancellationToken cancellationToken)
    {
        // Field-level validation — gom mọi lỗi field vào ListErrors, KHÔNG fail sớm.
        var validationResponse = new CommonResponse<Guid> { Data = Guid.Empty };

        var normalized = PhoneNumberNormalizer.NormalizeVn(request.PhoneNumber);
        if (normalized is null)
            validationResponse.ListErrors.Add(new Errors
            {
                Field = nameof(request.PhoneNumber),
                Detail = "Số điện thoại không hợp lệ (E.164 / VN)."
            });

        if (string.IsNullOrWhiteSpace(request.Message))
            validationResponse.ListErrors.Add(new Errors
            {
                Field = nameof(request.Message),
                Detail = "Nội dung SMS rỗng."
            });
        else if (request.Message.Length > MaxMessageLength)
            validationResponse.ListErrors.Add(new Errors
            {
                Field = nameof(request.Message),
                Detail = $"Nội dung SMS vượt quá {MaxMessageLength} ký tự."
            });

        if (validationResponse.ListErrors.Count > 0)
        {
            validationResponse.IsSuccess = false;
            validationResponse.StatusCode = 400;
            validationResponse.Message = "Dữ liệu không hợp lệ.";
            return validationResponse;
        }

        var now = DateTime.UtcNow;
        var sms = new SmsMessage
        {
            Id = Guid.NewGuid(),
            PhoneNumber = normalized!, // non-null đảm bảo bởi validation block trên (early-return nếu null)
            Message = request.Message.Trim(),
            Status = SmsStatus.Pending,
            Category = request.Category,
            SourceService = request.SourceService,
            CorrelationId = request.CorrelationId,
            TargetDeviceCode = request.TargetDeviceCode,
            CreatedAt = now
        };
        await _unitOfWork.SmsMessages.AddAsync(sms);

        await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
        {
            Id = Guid.NewGuid(),
            SmsMessageId = sms.Id,
            Event = SmsAuditEvent.Queued,
            Detail = $"source={request.SourceService}, category={request.Category}",
            CreatedAt = now
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // SignalR push primary — Flutter app sẽ nhận `NewPendingSms` < 1s.
        // Polling REST fallback nếu SignalR fail (đã log warning, không throw).
        try
        {
            await _notifier.NotifyNewPendingSmsAsync(sms.Id, sms.PhoneNumber, request.TargetDeviceCode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR notify failed for SMS {SmsId}; will fall back to polling.", sms.Id);
        }

        return new CommonResponse<Guid>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Queued.",
            Data = sms.Id
        };
    }

}
