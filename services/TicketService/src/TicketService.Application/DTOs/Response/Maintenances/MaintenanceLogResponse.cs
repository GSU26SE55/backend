using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Maintenances;

public class MaintenanceLogResponse : CommonResponse<MaintenanceLogDTO> { }
public class StaffMaintenanceLogResponse : CommonResponse<StaffMaintenanceLogGroupDTO> { }
public class MaintenanceLogActionResponse : CommonResponse<MaintenanceLogActionDTO> { }
