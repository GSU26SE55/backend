using MediatR;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Admin;
using SmsService.Application.Interfaces.Repositories;

namespace SmsService.Application.CQRS.Handler.Admin;

public class RevokeGatewayDeviceCommandHandler
    : IRequestHandler<RevokeGatewayDeviceCommand, CommonResponse<string>>
{
    private readonly ISmsUnitOfWork _unitOfWork;

    public RevokeGatewayDeviceCommandHandler(ISmsUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(RevokeGatewayDeviceCommand request, CancellationToken cancellationToken)
    {
        var device = await _unitOfWork.SmsGatewayDevices.GetByIdAsync(request.DeviceId);
        if (device is null || device.IsDeleted)
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Device not found.",
                Data = string.Empty
            };

        // Idempotent: revoke lại device đã revoke vẫn trả 200.
        device.Revoke(DateTime.UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Device revoked.",
            Data = device.DeviceCode
        };
    }
}
