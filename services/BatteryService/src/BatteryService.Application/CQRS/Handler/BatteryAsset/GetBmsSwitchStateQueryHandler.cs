using System.Text.Json;
using BatteryService.Application.CQRS.Query.BatteryAsset;
using BatteryService.Application.DTOs;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryAsset;

public class GetBmsSwitchStateQueryHandler
    : IRequestHandler<GetBmsSwitchStateQuery, CommonResponse<BmsSwitchStateDto>>
{
    private const string CommandType = "set_bms_switch";
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IBatteryCurrentUserService _currentUser;

    public GetBmsSwitchStateQueryHandler(
        IBatteryUnitOfWork unitOfWork,
        IBatteryCurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<BmsSwitchStateDto>> Handle(
        GetBmsSwitchStateQuery request,
        CancellationToken cancellationToken)
    {
        var scope = BatteryTenantScopeHelper.Resolve(_currentUser.UserId, _currentUser.Roles);
        if (scope.IsDenied)
            return Fail(401, "The current user could not be identified.");

        var assetQuery = _unitOfWork.BatteryAssets.GetAllAsync()
            .AsNoTracking()
            .Where(asset => asset.Id == request.BatteryAssetId && !asset.IsDeleted);
        if (scope.IsCustomerScoped)
            assetQuery = assetQuery.Where(asset => asset.CustomerId == scope.CustomerId);

        var asset = await assetQuery.FirstOrDefaultAsync(cancellationToken);
        if (asset is null)
            return Fail(404, "Battery asset not found.");
        if (asset.SiteId is null)
            return Fail(404, "The battery site has no active IoT gateway.");

        var deviceCount = await _unitOfWork.IotDevices.GetAllAsync()
            .AsNoTracking()
            .CountAsync(device => !device.IsDeleted
                                  && device.SiteId == asset.SiteId.Value
                                  && device.Status == IotDeviceStatusEnum.Active,
                cancellationToken);
        if (deviceCount == 0)
            return Fail(404, "The battery site has no active IoT gateway.");
        if (deviceCount > 1)
            return Fail(409, "The site has more than one active IoT gateway; a gateway cannot be selected safely.");

        var commands = await _unitOfWork.IotDeviceCommands.GetAllAsync()
            .AsNoTracking()
            .Where(command => !command.IsDeleted
                              && command.BatteryAssetId == asset.Id
                              && command.Type == CommandType)
            .OrderByDescending(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        var dto = new BmsSwitchStateDto();
        var lastVerified = commands.FirstOrDefault(command =>
            command.Status == IotDeviceCommandStatusEnum.Ok
            && !string.IsNullOrWhiteSpace(command.ResultJson));
        if (lastVerified is not null && TryReadState(lastVerified.ResultJson!, out var charge, out var discharge))
        {
            dto.ChargeEnabled = charge;
            dto.DischargeEnabled = discharge;
            dto.UpdatedAt = lastVerified.AckedAt;
        }

        var pending = commands.FirstOrDefault(command => command.Status == IotDeviceCommandStatusEnum.Pending);
        if (pending is not null && TryReadParams(pending.ParamsJson, out var pendingTarget, out var pendingEnable))
        {
            dto.PendingCommand = new BmsSwitchPendingCommandDto
            {
                CmdId = pending.CmdId,
                Target = pendingTarget,
                Enable = pendingEnable,
                IssuedAt = pending.CreatedAt
            };
        }

        var lastCompleted = commands.FirstOrDefault(command => command.Status != IotDeviceCommandStatusEnum.Pending);
        if (lastCompleted is not null && TryReadParams(lastCompleted.ParamsJson, out var target, out var enable))
        {
            dto.LastCommand = new BmsSwitchLastCommandDto
            {
                CmdId = lastCompleted.CmdId,
                Target = target,
                Enable = enable,
                Status = lastCompleted.Status,
                Error = NormalizeCommandError(lastCompleted.Status, lastCompleted.AckError),
                AckedAt = lastCompleted.AckedAt
            };
        }

        return new CommonResponse<BmsSwitchStateDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = dto
        };
    }

    private static string? NormalizeCommandError(
        IotDeviceCommandStatusEnum status,
        string? error)
    {
        if (status == IotDeviceCommandStatusEnum.Ok) return null;
        if (status == IotDeviceCommandStatusEnum.Rejected)
            return "The BMS rejected the control command.";
        if (status == IotDeviceCommandStatusEnum.Unknown)
            return "The firmware did not recognize the BMS control command.";
        if (status == IotDeviceCommandStatusEnum.Failed)
            return "The BMS control command failed.";
        if (status == IotDeviceCommandStatusEnum.TimedOut)
            return "The BMS control command timed out.";
        return string.IsNullOrWhiteSpace(error) ? null : "The BMS control command could not be completed.";
    }

    private static bool TryReadState(string json, out bool charge, out bool discharge)
    {
        charge = false;
        discharge = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("chargeEnabled", out var chargeValue)
                || !root.TryGetProperty("dischargeEnabled", out var dischargeValue)
                || chargeValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || dischargeValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            charge = chargeValue.GetBoolean();
            discharge = dischargeValue.GetBoolean();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadParams(string json, out string target, out bool enable)
    {
        target = string.Empty;
        enable = false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("target", out var targetValue)
                || targetValue.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("enable", out var enableValue)
                || enableValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return false;
            target = targetValue.GetString() ?? string.Empty;
            enable = enableValue.GetBoolean();
            return target.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CommonResponse<BmsSwitchStateDto> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
