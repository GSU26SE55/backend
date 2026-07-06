using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Models;
using TicketService.Application.Common.Utils;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;

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

        if (TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.AssignedStaffId, actorUserId, actorRoles))
            return true;

        return await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(p => p.TicketId == ticketId && p.UserId == actorUserId && p.RemovedAt == null && !p.IsDeleted, cancellationToken);
    }

    public bool CanViewInternalChats(IReadOnlyCollection<string> actorRoles)
    {
        return TicketQueryHelper.CanViewInternalChats(actorRoles);
    }

    public async Task<bool> CanViewInternalChatsAsync(Guid ticketId, Guid actorUserId, IReadOnlyCollection<string> actorRoles, CancellationToken cancellationToken = default)
    {
        if (TicketQueryHelper.CanViewInternalChats(actorRoles))
            return true;

        return await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(p => p.TicketId == ticketId && p.UserId == actorUserId && p.RemovedAt == null && !p.IsDeleted && p.CanViewInternal, cancellationToken);
    }

    public ChatAuthorizationResult CanEditChat(
        TicketChat chat,
        Guid actorUserId,
        IReadOnlyCollection<string> actorPermissions,
        bool reasonProvided,
        int editWindowMinutes)
    {
        if (chat.AuthorUserId == actorUserId)
        {
            var elapsed = DateTime.UtcNow - chat.CreatedAt;
            return elapsed > TimeSpan.FromMinutes(editWindowMinutes)
                ? ChatAuthorizationResult.EditWindowExpired
                : ChatAuthorizationResult.Allowed;
        }

        return ChatAuthorizationResult.Forbidden;
    }

    public ChatAuthorizationResult CanDeleteChat(
        TicketChat chat,
        Guid actorUserId,
        IReadOnlyCollection<string> actorPermissions,
        bool reasonProvided)
    {
        if (chat.AuthorUserId == actorUserId)
            return ChatAuthorizationResult.Allowed;

        return ChatAuthorizationResult.Forbidden;
    }

    public bool CanCreateChat(bool isInternal, IReadOnlyCollection<string> actorPermissions)
        => actorPermissions.Contains(isInternal ? ChatPermissionCodes.ChatCreateInternal : ChatPermissionCodes.ChatCreatePublic);

    public bool CanPinChat(IReadOnlyCollection<string> actorPermissions)
        => actorPermissions.Contains(ChatPermissionCodes.ChatPin);

    public bool CanViewInternalChat(IReadOnlyCollection<string> actorPermissions)
        => actorPermissions.Contains(ChatPermissionCodes.ChatViewInternal);
}
