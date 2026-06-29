using System.Text.Json.Serialization;
using MediatR;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

/// <summary>Export chat history of a ticket to PDF. #568.</summary>
public class ChatExportPdfCommand : IRequest<Stream>
{
    [JsonIgnore] public Guid TicketId { get; set; }
    [JsonIgnore] public ActorRoleEnum ViewerRole { get; set; }
}
