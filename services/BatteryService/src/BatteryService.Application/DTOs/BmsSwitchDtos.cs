using BatteryService.Domain.Enums;

namespace BatteryService.Application.DTOs;

public class SetBmsSwitchRequestDto
{
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }
}

public class BmsSwitchCommandAcceptedDto
{
    public string CmdId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }
    public string Topic { get; set; } = string.Empty;
}

public class BmsSwitchPendingCommandDto
{
    public string CmdId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }
    public DateTime IssuedAt { get; set; }
}

public class BmsSwitchLastCommandDto
{
    public string CmdId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Enable { get; set; }
    public IotDeviceCommandStatusEnum Status { get; set; }
    public string? Error { get; set; }
    public DateTime? AckedAt { get; set; }
}

public class BmsSwitchStateDto
{
    public bool? ChargeEnabled { get; set; }
    public bool? DischargeEnabled { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public BmsSwitchPendingCommandDto? PendingCommand { get; set; }

    // The polling client uses this to surface asynchronous failure/rejection/timeout.
    public BmsSwitchLastCommandDto? LastCommand { get; set; }
}
