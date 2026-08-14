using MediatR;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Setting;
using NotificationService.Application.DTOs.Response.Setting;
using NotificationService.Application.Services;

namespace NotificationService.Application.CQRS.Handler.Setting;

public class UpdatePushTransportCommandHandler : IRequestHandler<UpdatePushTransportCommand, PushTransportResponse>
{
    private readonly IPushTransportSettingService _settingService;
    private readonly ILogger<UpdatePushTransportCommandHandler> _logger;

    public UpdatePushTransportCommandHandler(
        IPushTransportSettingService settingService,
        ILogger<UpdatePushTransportCommandHandler> logger)
    {
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<PushTransportResponse> Handle(
        UpdatePushTransportCommand request, CancellationToken cancellationToken)
    {
        var previous = await _settingService.GetAsync(cancellationToken);

        if (previous == request.Transport)
        {
            // Không ghi lại giá trị y hệt: tránh sinh dòng audit rác và tránh xoá cache vô ích.
            return new PushTransportResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = $"Push transport is already {request.Transport} — nothing changed.",
                Data = PushTransportDtoFactory.Build(previous),
            };
        }

        await _settingService.SetAsync(request.Transport, cancellationToken);

        // Đây là công tắc ảnh hưởng tới toàn bộ thông báo đẩy của hệ thống. Ghi mức Information kèm
        // cả giá trị cũ để khi có sự cố còn dò được thời điểm đổi trong log.
        _logger.LogInformation(
            "PushTransport: đổi đường vận chuyển push {Previous} → {Current}.",
            previous, request.Transport);

        return new PushTransportResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Changed push transport from {previous} to {request.Transport}.",
            Data = PushTransportDtoFactory.Build(request.Transport),
        };
    }
}
