using SharedContracts.Common.Responses;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketActionResponse : CommonResponse<TicketActionDTO> { }
public class TicketActivityResponse : CommonResponse<List<TicketActivityDTO>> { }
public class TicketChatResponse : CommonResponse<List<TicketChatDTO>> { }
public class TicketDetailResponse : CommonResponse<TicketDetailDTO> { }
public class TicketResponse : CommonResponse<List<TicketDTO>> { }
public class ParticipantActionResponse : CommonResponse<TicketParticipantDTO> { }
public class ParticipantBulkActionResponse : CommonResponse<List<TicketParticipantDTO>> { }
public class TicketParticipantsResponse : CommonResponse<List<TicketParticipantDTO>> { }
public class ParticipantHistoryResponse : CommonResponse<List<ParticipantHistoryDTO>> { }
