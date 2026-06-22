using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Helpers;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

public class ChatAuthorizationService : IChatAuthorizationService
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatAuthorizationService(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> CanAccessTicketAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == ticketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, t.AssignedStaffId })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return false;

        return TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, actorUserId, actorRoles);
    }

    public bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles)
    {
        return TicketQueryHelper.CanViewInternalChats(actorRoles);
    }
}
