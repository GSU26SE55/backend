using System.Text.Json;
using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Services;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class SetBmsSwitchCommandHandler
    : IRequestHandler<SetBmsSwitchCommand, CommonResponse<BmsSwitchCommandAcceptedDto>>
{
    private const string CommandType = "set_bms_switch";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUser;
    private readonly IMqttBridgePublisher _mqttPublisher;
    private readonly ILogger<SetBmsSwitchCommandHandler> _logger;

    public SetBmsSwitchCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IBatteryCurrentUserService currentUser,
        IMqttBridgePublisher mqttPublisher,
        ILogger<SetBmsSwitchCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _mqttPublisher = mqttPublisher;
        _logger = logger;
    }

    public async Task<CommonResponse<BmsSwitchCommandAcceptedDto>> Handle(
        SetBmsSwitchCommand request,
        CancellationToken cancellationToken)
    {
        var target = request.Target.Trim().ToLowerInvariant();
        if (target is not ("charge" or "discharge"))
            return Fail(400, "Target must be either 'charge' or 'discharge'.");

        var scope = BatteryTenantScopeHelper.Resolve(_currentUser.UserId, _currentUser.Roles);
        if (scope.IsDenied
            || !Guid.TryParse(_currentUser.UserId, out var issuerId)
            || issuerId == Guid.Empty)
        {
            return Fail(401, "The current user could not be identified for audit logging.");
        }

        var assetQuery = _unitOfWork.BatteryAssets.GetAllAsync()
            .Where(asset => asset.Id == request.BatteryAssetId && !asset.IsDeleted);
        if (scope.IsCustomerScoped)
            assetQuery = assetQuery.Where(asset => asset.CustomerId == scope.CustomerId);

        var asset = await assetQuery.FirstOrDefaultAsync(cancellationToken);
        if (asset is null)
            return Fail(404, "Battery asset not found.");

        if (asset.SiteId is null)
            return Fail(404, "The battery site has no active IoT gateway.");

        var devices = await _unitOfWork.IotDevices.GetAllAsync()
            .Where(device => !device.IsDeleted
                             && device.SiteId == asset.SiteId.Value
                             && device.Status == IotDeviceStatusEnum.Active)
            .Take(2)
            .ToListAsync(cancellationToken);

        if (devices.Count == 0)
            return Fail(404, "The battery site has no active IoT gateway.");
        if (devices.Count > 1)
            return Fail(409, "The site has more than one active IoT gateway; a gateway cannot be selected safely.");

        var pending = await _unitOfWork.IotDeviceCommands.GetAllAsync()
            .AsNoTracking()
            .Where(command => !command.IsDeleted
                              && command.BatteryAssetId == asset.Id
                              && command.Type == CommandType
                              && command.Status == IotDeviceCommandStatusEnum.Pending)
            .ToListAsync(cancellationToken);
        if (pending.Any(command => HasTarget(command.ParamsJson, target)))
            return Fail(409, "A previous command for this switch is still awaiting a response.");

        var now = DateTime.UtcNow;
        var cmdId = Guid.NewGuid().ToString("N");
        var commandParams = new
        {
            serial = asset.SerialNumber,
            target,
            enable = request.Enable
        };
        var paramsJson = JsonSerializer.Serialize(commandParams, JsonOptions);
        var command = new IotDeviceCommand
        {
            Id = Guid.NewGuid(),
            IotDeviceId = devices[0].Id,
            BatteryAssetId = asset.Id,
            CmdId = cmdId,
            Type = CommandType,
            ParamsJson = paramsJson,
            Status = IotDeviceCommandStatusEnum.Pending,
            IssuedByAccountId = issuerId,
            CreatedBy = issuerId,
            CreatedAt = now
        };

        await _unitOfWork.IotDeviceCommands.AddAsync(command);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            cmdId,
            type = CommandType,
            @params = commandParams,
            issuedAt = now
        }, JsonOptions);

        try
        {
            await _mqttPublisher.PublishCommandAsync(devices[0].DeviceCode, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            command.Status = IotDeviceCommandStatusEnum.Failed;
            command.AckError = $"The MQTT bridge is unavailable: {ex.Message}";
            command.AckedAt = DateTime.UtcNow;
            _unitOfWork.IotDeviceCommands.UpdateAsync(command);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Failed publishing BMS switch command {CmdId}", cmdId);
            return Fail(503, command.AckError);
        }

        return new CommonResponse<BmsSwitchCommandAcceptedDto>
        {
            IsSuccess = true,
            StatusCode = 202,
            Message = "The command was sent to the gateway and is awaiting BMS confirmation.",
            Data = new BmsSwitchCommandAcceptedDto
            {
                CmdId = cmdId,
                Target = target,
                Enable = request.Enable,
                Topic = $"solar/{devices[0].DeviceCode.Trim().ToLowerInvariant()}/cmd"
            }
        };
    }

    private static bool HasTarget(string paramsJson, string target)
    {
        try
        {
            using var doc = JsonDocument.Parse(paramsJson);
            return doc.RootElement.TryGetProperty("target", out var value)
                   && string.Equals(value.GetString(), target, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CommonResponse<BmsSwitchCommandAcceptedDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
