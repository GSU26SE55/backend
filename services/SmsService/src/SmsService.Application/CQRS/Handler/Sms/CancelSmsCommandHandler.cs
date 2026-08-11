using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.Application.CQRS.Handler.Sms;

public class CancelSmsCommandHandler : IRequestHandler<CancelSmsCommand, CommonResponse<string>>
{
    private readonly ISmsUnitOfWork _unitOfWork;
    private readonly ISmsGatewayNotifier _notifier;
    private readonly ILogger<CancelSmsCommandHandler> _logger;

    public CancelSmsCommandHandler(
        ISmsUnitOfWork unitOfWork,
        ISmsGatewayNotifier notifier,
        ILogger<CancelSmsCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<CommonResponse<string>> Handle(CancelSmsCommand request, CancellationToken cancellationToken)
    {
        var sms = await _unitOfWork.SmsMessages
            .GetAllAsync()
            .FirstOrDefaultAsync(x => x.Id == request.SmsId && !x.IsDeleted, cancellationToken);

        if (sms is null)
            return Fail(404, "SMS not found.");

        if (sms.Status is SmsStatus.Sent or SmsStatus.Failed or SmsStatus.Cancelled)
            return Fail(409, $"SMS is in status {sms.Status}, cannot be cancelled.");

        var now = DateTime.UtcNow;
        var targetDeviceCode = sms.GatewayDeviceCode ?? sms.TargetDeviceCode;
        sms.Cancel(now);

        await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
        {
            Id = Guid.NewGuid(),
            SmsMessageId = sms.Id,
            Event = SmsAuditEvent.Cancelled,
            DeviceCode = sms.GatewayDeviceCode,
            CreatedAt = now
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Báo device để remove khỏi local queue.
        try
        {
            await _notifier.NotifyBatchRevokedAsync(new[] { sms.Id }, targetDeviceCode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR BatchRevoked notify failed for SMS {SmsId}.", sms.Id);
        }

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cancelled.",
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
