using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.StateMachine.Rules;

public class TransitionRuleProvider : ITransitionRuleProvider
{
    public Dictionary<TicketStatusEnum, Dictionary<TicketStatusEnum, Func<Ticket, ActorRoleEnum, Guid, TransitionResult>>> GetRules() =>
        new()
        {
            [TicketStatusEnum.Open] = new()
            {
                [TicketStatusEnum.InProgress] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin, ActorRoleEnum.System),
                [TicketStatusEnum.Pending] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin),
                [TicketStatusEnum.ClosedRejected] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin),
                [TicketStatusEnum.Closed] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin)
            },
            [TicketStatusEnum.Pending] = new()
            {
                [TicketStatusEnum.InProgress] = PrimaryStaffOr(ActorRoleEnum.Manager, ActorRoleEnum.Admin, ActorRoleEnum.System)
            },
            [TicketStatusEnum.InProgress] = new()
            {
                [TicketStatusEnum.Pending] = PrimaryStaffOr(ActorRoleEnum.Admin),
                [TicketStatusEnum.Request] = PrimaryStaffOr(ActorRoleEnum.Admin),
                [TicketStatusEnum.Completed] = PrimaryStaffOr(ActorRoleEnum.Admin),
                [TicketStatusEnum.ReAssign] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin, ActorRoleEnum.System)
            },
            [TicketStatusEnum.Request] = new()
            {
                [TicketStatusEnum.InProgress] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin),
                [TicketStatusEnum.ReAssign] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin)
            },
            [TicketStatusEnum.ReAssign] = new()
            {
                [TicketStatusEnum.InProgress] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin, ActorRoleEnum.System),
                [TicketStatusEnum.Pending] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin)
            },
            [TicketStatusEnum.Completed] = new()
            {
                [TicketStatusEnum.Closed] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin),
                [TicketStatusEnum.InProgress] = Allow(ActorRoleEnum.Manager, ActorRoleEnum.Admin)
            },
            [TicketStatusEnum.Closed] = new()
            {
                [TicketStatusEnum.Open] = OwnerOrAdmin()
            }
        };

    private static Func<Ticket, ActorRoleEnum, Guid, TransitionResult> Allow(params ActorRoleEnum[] roles) =>
        (_, role, _) => Result(roles.Contains(role), "Actor is not allowed to perform this transition.");

    private static Func<Ticket, ActorRoleEnum, Guid, TransitionResult> PrimaryStaffOr(params ActorRoleEnum[] roles) =>
        (ticket, role, userId) => Result(
            roles.Contains(role) || role == ActorRoleEnum.Staff && ticket.PrimaryHandlerStaffId == userId,
            "Only the active PrimaryHandler or an authorized actor can perform this transition.");

    private static Func<Ticket, ActorRoleEnum, Guid, TransitionResult> OwnerOrAdmin() =>
        (ticket, role, userId) => Result(
            role == ActorRoleEnum.Admin || role == ActorRoleEnum.Customer && ticket.CustomerId == userId,
            "Only the ticket owner or Admin can reopen this ticket.");

    private static TransitionResult Result(bool allowed, string deniedReason) =>
        new() { IsAllowed = allowed, Reason = allowed ? null : deniedReason };
}
