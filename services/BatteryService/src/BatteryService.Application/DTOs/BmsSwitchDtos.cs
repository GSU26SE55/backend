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

    /// <summary>Câu đã chuẩn hoá để hiển thị cho người dùng. KHÔNG dùng để dò từ khoá.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Lý do THÔ firmware gửi kèm ack (vd <c>"unsupported target"</c>), <c>null</c> nếu không có.
    /// Dành cho client cần phân biệt nguyên nhân — vd ẩn hẳn control BMS khi thiết bị không hỗ
    /// trợ lệnh — vì <see cref="Error"/> đã bị chuẩn hoá thành câu cố định theo status.
    /// </summary>
    public string? DeviceReason { get; set; }

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
