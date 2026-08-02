using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Chats;

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
public class MyMentionsResponse : CommonResponse<PaginationResponse<TicketChatMentionDTO>> { }
public class ChatReactionActionResponse : CommonResponse<TicketChatReactionsAggregateDTO> { }
public class ChatMarkAsReadResponse : CommonResponse<int> { }
public class ChatReadersResponse : CommonResponse<List<ChatReaderDTO>> { }
public class TicketUnreadCountResponse : CommonResponse<int> { }
