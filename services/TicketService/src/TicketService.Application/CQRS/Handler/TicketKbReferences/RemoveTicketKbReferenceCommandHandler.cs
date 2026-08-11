using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.Interfaces.Repositories;

namespace TicketService.Application.CQRS.Handler.TicketKbReferences;

public class RemoveTicketKbReferenceCommandHandler : IRequestHandler<RemoveTicketKbReferenceCommand, CommonResponse<object>>
{
    private readonly ITicketUnitOfWork _uow;

    public RemoveTicketKbReferenceCommandHandler(ITicketUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<object>> Handle(RemoveTicketKbReferenceCommand command, CancellationToken ct)
    {
        var reference = await _uow.TicketKbReferences.GetAllAsync()
            .FirstOrDefaultAsync(r => r.Id == command.ReferenceId && !r.IsDeleted, ct);

        if (reference == null)
            return new CommonResponse<object> { IsSuccess = false, StatusCode = 404, Message = "Reference not found." };

        _uow.TicketKbReferences.DeleteAsync(reference);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<object> { IsSuccess = true, StatusCode = 200, Message = "Article removed from Ticket." };
    }
}
