using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.DTOs.Response.Sms;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.Application.CQRS.Handler.Sms;

public class ClaimPendingMessagesCommandHandler
    : IRequestHandler<ClaimPendingMessagesCommand, CommonResponse<List<PendingSmsDto>>>
{
    /// <summary>Stale claim revert timeout — đồng nhất với <c>StaleSmsReaperBackgroundService</c>.</summary>
    private static readonly TimeSpan PickStaleAfter = TimeSpan.FromMinutes(5);

    private readonly ISmsUnitOfWork _unitOfWork;

    public ClaimPendingMessagesCommandHandler(ISmsUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<PendingSmsDto>>> Handle(
        ClaimPendingMessagesCommand request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit, 1, 20);
        var now = DateTime.UtcNow;
        var staleBefore = now - PickStaleAfter;

        var device = await _unitOfWork.SmsGatewayDevices.GetByIdAsync(request.DeviceId);
        if (device is null || !device.IsActive || device.IsDeleted)
            return new CommonResponse<List<PendingSmsDto>>
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "Device không tồn tại hoặc đã bị thu hồi.",
                Data = new List<PendingSmsDto>()
            };

        device.ResetDailyCounterIfNeeded(now);
        if (device.SentToday >= device.DailyLimit)
        {
            // Silent: trả rỗng để device không log spam — đã đạt daily limit.
            return new CommonResponse<List<PendingSmsDto>>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Daily limit reached.",
                Data = new List<PendingSmsDto>()
            };
        }

        var allowance = device.DailyLimit - device.SentToday;
        var take = Math.Min(limit, allowance);

        var candidates = await _unitOfWork.SmsMessages
            .GetAllAsync()
            .Where(x => !x.IsDeleted)
            .Where(x =>
                (x.Status == SmsStatus.Pending && (x.TargetDeviceCode == null || x.TargetDeviceCode == request.DeviceCode))
                || (x.Status == SmsStatus.Sending && x.PickedAt != null && x.PickedAt < staleBefore))
            .OrderBy(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var m in candidates)
        {
            m.Claim(request.DeviceCode, request.DeviceId, now);
            await _unitOfWork.SmsAuditLogs.AddAsync(new SmsAuditLog
            {
                Id = Guid.NewGuid(),
                SmsMessageId = m.Id,
                Event = SmsAuditEvent.Picked,
                DeviceCode = request.DeviceCode,
                CreatedAt = now
            });
        }

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race possible ở 2 chỗ (xmin):
            // (1) Device khác claim cùng row sms_messages.
            // (2) Request khác cùng device update SentToday đồng thời.
            // Trả rỗng — lần poll tiếp client retry với state mới nhất.
            return new CommonResponse<List<PendingSmsDto>>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Concurrent claim, retry next poll.",
                Data = new List<PendingSmsDto>()
            };
        }

        return new CommonResponse<List<PendingSmsDto>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OK",
            Data = candidates
                .Select(m => new PendingSmsDto(m.Id, m.PhoneNumber, m.Message ?? string.Empty))
                .ToList()
        };
    }
}
