using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Saga;

public class AlertTicketSagaListResponse : CommonResponse<PaginationResponse<AlertTicketSagaDTO>> { }

public class AlertTicketSagaDetailResponse : CommonResponse<AlertTicketSagaDTO> { }

public class SagaReprocessResponse : CommonResponse<object> { }
