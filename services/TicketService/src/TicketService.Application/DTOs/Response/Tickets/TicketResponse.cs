using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketActionResponse : CommonResponse<TicketActionDTO> { }
public class TicketActivityResponse : CommonResponse<List<TicketActivityDTO>> { }
public class TicketCommentResponse : CommonResponse<List<TicketCommentDTO>> { }
public class TicketDetailResponse : CommonResponse<TicketDetailDTO> { }
public class TicketResponse : CommonResponse<List<TicketDTO>> { }
