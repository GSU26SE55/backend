using System.Security.Cryptography;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SmsService.Application.CQRS.Command.Admin;
using SmsService.Application.DTOs.Response.Admin;
using SmsService.Application.Interfaces.Repositories;
using SmsService.Application.Interfaces.Services;
using SmsService.Domain.Entities;

namespace SmsService.Application.CQRS.Handler.Admin;

public class CreateGatewayDeviceCommandHandler
    : IRequestHandler<CreateGatewayDeviceCommand, CommonResponse<CreateGatewayDeviceResponseDto>>
{
    private readonly ISmsUnitOfWork _unitOfWork;
    private readonly IGatewayApiKeyHasher _hasher;

    public CreateGatewayDeviceCommandHandler(ISmsUnitOfWork unitOfWork, IGatewayApiKeyHasher hasher)
    {
        _unitOfWork = unitOfWork;
        _hasher = hasher;
    }

    public async Task<CommonResponse<CreateGatewayDeviceResponseDto>> Handle(
        CreateGatewayDeviceCommand request, CancellationToken cancellationToken)
    {
        var deviceCode = request.DeviceCode.Trim();

        var exists = await _unitOfWork.SmsGatewayDevices
            .GetAllAsync()
            .AnyAsync(x => x.DeviceCode == deviceCode && !x.IsDeleted, cancellationToken);
        if (exists)
            return new CommonResponse<CreateGatewayDeviceResponseDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "DeviceCode đã tồn tại.",
                Data = new CreateGatewayDeviceResponseDto(Guid.Empty, deviceCode, string.Empty)
            };

        var apiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var device = new SmsGatewayDevice
        {
            Id = Guid.NewGuid(),
            DeviceName = request.DeviceName.Trim(),
            DeviceCode = deviceCode,
            ApiKeyHash = _hasher.Hash(apiKey),
            DailyLimit = request.DailyLimit,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.SmsGatewayDevices.AddAsync(device);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<CreateGatewayDeviceResponseDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Device created. API key plaintext shown ONCE — store it securely.",
            Data = new CreateGatewayDeviceResponseDto(device.Id, device.DeviceCode, apiKey)
        };
    }
}
