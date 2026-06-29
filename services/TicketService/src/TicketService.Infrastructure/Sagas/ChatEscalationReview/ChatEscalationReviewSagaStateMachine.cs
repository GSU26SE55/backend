using MassTransit;
using Microsoft.Extensions.Logging;
using SharedContracts.Events.Chats;

namespace TicketService.Infrastructure.Sagas.ChatEscalationReview;

/// <summary>
/// Saga: khi Manager được mention trên ticket P1 Critical, bắt đầu 30 phút review window.
/// Nếu Manager ACK trước timeout → Reviewed (terminal).
/// Nếu timeout → publish ChatEscalatedToAdminEvent → Escalated (terminal).
/// CorrelationId = ChatId. #566.
/// </summary>
public class ChatEscalationReviewSagaStateMachine : MassTransitStateMachine<ChatEscalationReviewSagaState>
{
    private static readonly TimeSpan ReviewTimeout = TimeSpan.FromMinutes(30);

    public State Pending { get; private set; } = null!;
    public State Reviewed { get; private set; } = null!;
    public State Escalated { get; private set; } = null!;

    public Event<ChatEscalationReviewRequestedEvent> EscalationRequested { get; private set; } = null!;
    public Event<ChatEscalationReviewAckedEvent> EscalationAcked { get; private set; } = null!;

    public Schedule<ChatEscalationReviewSagaState, ChatEscalationReviewTimeoutFired> EscalationTimer { get; private set; } = null!;

    public ChatEscalationReviewSagaStateMachine(ILogger<ChatEscalationReviewSagaStateMachine> logger)
    {
        InstanceState(x => x.CurrentState);

        // CorrelationId = ChatId
        Event(() => EscalationRequested,
            c => c.CorrelateById(ctx => ctx.Message.ChatId).SelectId(ctx => ctx.Message.ChatId));

        Event(() => EscalationAcked,
            c => c.CorrelateById(ctx => ctx.Message.CorrelationId));

        Schedule(() => EscalationTimer, instance => instance.PendingTimeoutTokenId,
            s =>
            {
                s.Delay = ReviewTimeout;
                s.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
            });

        // Initial → Pending: Manager mention trên P1
        Initially(
            When(EscalationRequested)
                .Then(ctx =>
                {
                    ctx.Saga.TicketId = ctx.Message.TicketId;
                    ctx.Saga.TicketCode = ctx.Message.TicketCode;
                    ctx.Saga.ManagerUserId = ctx.Message.ManagerUserId;
                    ctx.Saga.EscalationReason = ctx.Message.EscalationReason;
                    ctx.Saga.StartedAt = DateTime.UtcNow;
                    logger.LogInformation(
                        "[ChatEscalationReview] Started saga ChatId={ChatId} TicketCode={Code} Manager={Manager}",
                        ctx.Saga.CorrelationId, ctx.Saga.TicketCode, ctx.Saga.ManagerUserId);
                })
                .Schedule(EscalationTimer, ctx => new ChatEscalationReviewTimeoutFired(ctx.Saga.CorrelationId))
                .TransitionTo(Pending)
        );

        // Pending: Manager ACK → Reviewed (cancel timer)
        During(Pending,
            When(EscalationAcked)
                .Unschedule(EscalationTimer)
                .Then(ctx =>
                {
                    ctx.Saga.ResolvedAt = DateTime.UtcNow;
                    logger.LogInformation(
                        "[ChatEscalationReview] Reviewed ChatId={ChatId} by Manager={Manager}",
                        ctx.Saga.CorrelationId, ctx.Message.ManagerUserId);
                })
                .TransitionTo(Reviewed),

            When(EscalationTimer.Received)
                .Then(ctx =>
                {
                    ctx.Saga.ResolvedAt = DateTime.UtcNow;
                    logger.LogWarning(
                        "[ChatEscalationReview] Timeout — escalating to Admin. ChatId={ChatId} TicketCode={Code}",
                        ctx.Saga.CorrelationId, ctx.Saga.TicketCode);
                })
                .PublishAsync(ctx => ctx.Init<ChatEscalatedToAdminEvent>(new ChatEscalatedToAdminEvent(
                    ctx.Saga.CorrelationId,
                    ctx.Saga.TicketId,
                    ctx.Saga.TicketCode,
                    ctx.Saga.ManagerUserId)))
                .TransitionTo(Escalated),

            // Bỏ qua nếu event đến sau khi timeout đã fire
            When(EscalationRequested)
                .Then(_ => { })
        );

        // Terminal states — không xóa row (giữ tombstone cho audit)
        SetCompletedWhenFinalized();
    }
}

/// <summary>Quartz timeout message cho ChatEscalationReview saga.</summary>
public record ChatEscalationReviewTimeoutFired(Guid CorrelationId);
