using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Behaviors;

/// <summary>
/// Rejects any CQRS mutation command targeting a terminal ticket before its handler can alter it.
/// Read requests and commands without a TicketId are intentionally outside this policy.
/// </summary>
public sealed class ClosedTicketMutationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ClosedTicketMutationBehavior(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (UsesCommandSpecificClosedTicketPolicy(request))
            return await next();

        var ticketId = GetTicketId(request);
        if (!ticketId.HasValue)
            return await next();

        var isTerminal = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .AnyAsync(t => t.Id == ticketId.Value && !t.IsDeleted &&
                (t.Status == TicketStatusEnum.Closed || t.CloseReason == TicketCloseReasonEnum.MergedDuplicate), cancellationToken);
        if (!isTerminal)
            return await next();

        var response = Activator.CreateInstance<TResponse>()
            ?? throw new InvalidOperationException($"Cannot create response type {typeof(TResponse).Name} for closed ticket guard.");
        Set(response, "IsSuccess", false);
        Set(response, "StatusCode", 409);
        Set(response, "Message", "Ticket is closed and cannot be changed.");
        return response;
    }

    private static bool UsesCommandSpecificClosedTicketPolicy(TRequest request) => request is
        TicketRateCommand or
        TicketReopenCommand or
        ChatDeleteCommand or
        ChatReplyCommand or
        ChatRestoreCommand or
        ChatOverrideAddCommand or
        ChatOverrideEditCommand or
        ChatOverrideDeleteCommand;

    private static Guid? GetTicketId(TRequest request)
    {
        if (request is null)
            return null;

        var type = request.GetType();
        if (type.Namespace?.Contains(".CQRS.Command.", StringComparison.Ordinal) != true)
            return null;

        return type.GetProperty("TicketId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(request) is Guid id && id != Guid.Empty
            ? id
            : null;
    }

    private static void Set(object response, string propertyName, object value)
        => response.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.SetValue(response, value);
}
