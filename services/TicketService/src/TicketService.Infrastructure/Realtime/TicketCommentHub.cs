using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Realtime;

[Authorize]
public class TicketCommentHub : Hub
{
    private readonly ICommentAuthorizationService _authService;
    private readonly ILogger<TicketCommentHub> _logger;

    public TicketCommentHub(ICommentAuthorizationService authService, ILogger<TicketCommentHub> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public static string PublicGroup(Guid ticketId) => $"ticket:{ticketId}:public";
    public static string InternalGroup(Guid ticketId) => $"ticket:{ticketId}:internal";

    /// <summary>
    /// Client joins the ticket realtime channel. Quyền truy cập được xác thực tại đây.
    /// </summary>
    public async Task JoinTicket(string ticketIdStr)
    {
        if (!Guid.TryParse(ticketIdStr, out var ticketId))
        {
            _logger.LogWarning("[TicketCommentHub] JoinTicket failed. Invalid ticket ID format: {TicketIdStr}", ticketIdStr);
            throw new HubException("Invalid ticket ID format.");
        }

        var actorUserId = GetCurrentUserId();
        if (!actorUserId.HasValue)
        {
            _logger.LogWarning("[TicketCommentHub] JoinTicket failed. User unauthorized.");
            throw new HubException("Unauthorized.");
        }

        var actorRoles = GetCurrentRoles();

        var canAccess = await _authService.CanAccessTicketAsync(ticketId, actorUserId.Value, actorRoles);
        if (!canAccess)
        {
            _logger.LogWarning("[TicketCommentHub] JoinTicket failed. User {UserId} does not have access to ticket {TicketId}", actorUserId, ticketId);
            throw new HubException("Forbidden: No access to this ticket.");
        }

        // Add to public comments group
        await Groups.AddToGroupAsync(Context.ConnectionId, PublicGroup(ticketId));

        // Add to internal comments group if user is Staff/Manager/Admin
        if (_authService.CanViewInternalComments(actorRoles))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, InternalGroup(ticketId));
        }

        _logger.LogInformation("[TicketCommentHub] User {UserId} connected to ticket {TicketId} (ConnId: {ConnId})", actorUserId, ticketId, Context.ConnectionId);
    }

    /// <summary>
    /// Client leaves the ticket realtime channel.
    /// </summary>
    public async Task LeaveTicket(string ticketIdStr)
    {
        if (!Guid.TryParse(ticketIdStr, out var ticketId))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, PublicGroup(ticketId));
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, InternalGroup(ticketId));

        _logger.LogInformation("[TicketCommentHub] User {UserId} disconnected from ticket {TicketId} (ConnId: {ConnId})", GetCurrentUserId(), ticketId, Context.ConnectionId);
    }

    /// <summary>
    /// Client broadcasts that they are typing a comment.
    /// </summary>
    public async Task Typing(string ticketIdStr)
    {
        if (!Guid.TryParse(ticketIdStr, out var ticketId))
        {
            return;
        }

        var actorUserId = GetCurrentUserId();
        if (!actorUserId.HasValue)
            return;

        var actorRoles = GetCurrentRoles();
        var canAccess = await _authService.CanAccessTicketAsync(ticketId, actorUserId.Value, actorRoles);
        if (!canAccess)
            return;

        var displayName = Context.User?.FindFirstValue("FullName") ?? Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Unknown";

        // Broadcast to others in the public group
        var pubGroup = PublicGroup(ticketId);
        await Clients.OthersInGroup(pubGroup).SendAsync("UserTyping", ticketIdStr, actorUserId.ToString(), displayName);
    }

    private Guid? GetCurrentUserId()
    {
        var val = Context.User?.FindFirstValue("AccountId") ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(val, out var guid) ? guid : null;
    }

    private IReadOnlyCollection<string> GetCurrentRoles()
    {
        var roles = new List<string>();
        if (Context.User != null)
        {
            foreach (var claim in Context.User.FindAll(ClaimTypes.Role))
            {
                roles.Add(claim.Value);
            }
            var roleClaim = Context.User.FindFirstValue("role");
            if (!string.IsNullOrEmpty(roleClaim) && !roles.Contains(roleClaim))
            {
                roles.Add(roleClaim);
            }
        }
        return roles;
    }
}
