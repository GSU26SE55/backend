using MediatR;
using NotificationService.Application.CQRS.Query.Setting;
using NotificationService.Application.DTOs.Response.Setting;
using NotificationService.Application.Services;

namespace NotificationService.Application.CQRS.Handler.Setting;

public class GetPushTransportQueryHandler : IRequestHandler<GetPushTransportQuery, PushTransportResponse>
{
    private readonly IPushTransportSettingService _settingService;

    public GetPushTransportQueryHandler(IPushTransportSettingService settingService)
    {
        _settingService = settingService;
    }

    public async Task<PushTransportResponse> Handle(
        GetPushTransportQuery request, CancellationToken cancellationToken)
    {
        var current = await _settingService.GetAsync(cancellationToken);

        return new PushTransportResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = PushTransportDtoFactory.Build(current),
        };
    }
}
