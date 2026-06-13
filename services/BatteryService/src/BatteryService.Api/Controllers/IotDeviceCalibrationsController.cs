using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Sprint IoT-2 #IoT2-32 / #IoT2-33 (S7-BE-01..02) — calibration CRUD + expiring filter.
/// </summary>
[ApiController]
[Route("api/iot-devices")]
[Produces("application/json")]
public class IotDeviceCalibrationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public IotDeviceCalibrationsController(IMediator mediator) => _mediator = mediator;

    /// <summary>List calibration profiles cho 1 device.</summary>
    [HttpGet("{deviceId:guid}/calibrations")]
    [Authorize(Roles = "Admin,Manager,Staff")]
    [ProducesResponseType(typeof(CommonResponse<List<IotDeviceCalibrationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid deviceId, [FromQuery] string? channel, [FromQuery] bool includeExpired, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotDeviceCalibrationsQuery
        {
            IotDeviceId = deviceId,
            Channel = channel,
            IncludeExpired = includeExpired
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tạo calibration cho 1 channel của device. Staff/Admin được phép.</summary>
    [HttpPost("{deviceId:guid}/calibrations")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCalibrationDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<IotDeviceCalibrationDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(Guid deviceId, [FromBody] CreateIotDeviceCalibrationCommand cmd, CancellationToken ct)
    {
        cmd.IotDeviceId = deviceId;
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Xoá mềm 1 calibration entry.</summary>
    [HttpDelete("{deviceId:guid}/calibrations/{calibrationId:guid}")]
    [Authorize(Roles = "Admin,Staff")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid deviceId, Guid calibrationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new DeleteIotDeviceCalibrationCommand
        {
            IotDeviceId = deviceId,
            CalibrationId = calibrationId
        }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Sprint IoT-2 #IoT2-32 — calibration sắp hết hạn trong N ngày (default 30, Manager view).</summary>
    [HttpGet("calibrations-expiring")]
    [Authorize(Roles = "Admin,Manager")]
    [ProducesResponseType(typeof(CommonResponse<List<IotDeviceCalibrationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Expiring([FromQuery(Name = "within")] int? withinDays, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetIotCalibrationsExpiringQuery
        {
            WithinDays = withinDays ?? 30
        }, ct);
        return StatusCode(result.StatusCode, result);
    }
}
