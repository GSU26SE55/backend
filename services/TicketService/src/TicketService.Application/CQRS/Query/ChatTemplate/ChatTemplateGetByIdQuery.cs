using System;
using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Query.ChatTemplate;

public class ChatTemplateGetByIdQuery : IRequest<CommonResponse<ChatTemplateDTO>>
{
    public Guid TemplateId { get; set; }
    public Guid ActorUserId { get; set; }
    public string[] ActorRoles { get; set; } = Array.Empty<string>();
}
