using MediatR;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Sms;
using SmsService.Application.Interfaces.Repositories;

namespace SmsService.Application.CQRS.Handler.Sms;

public class HeartbeatCommandHandler : IRequestHandler<HeartbeatCommand, CommonResponse<string>>
{
    private readonly ISmsUnitOfWork _unitOfWork;

    public HeartbeatCommandHandler(ISmsUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(HeartbeatCommand request, CancellationToken cancellationToken)
    {
        var device = await _unitOfWork.SmsGatewayDevices.GetByIdAsync(request.DeviceId);
        if (device is null || device.IsDeleted)
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Device không tồn tại.",
                Data = string.Empty
            };

        device.Touch(request.Ip, DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "OK",
            Data = "pong"
        };
    }
}
