using MediatR;
using SharedContracts.Common.Responses;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.TicketKbReferences;

public class AddTicketKbReferenceCommand : IRequest<CommonResponse<object>>
{
    public Guid TicketId { get; set; }
    public Guid KbArticleId { get; set; }
    public KbReferenceTypeEnum ReferenceType { get; set; }
    public string? Note { get; set; }
    public Guid CurrentUserId { get; set; }
}
