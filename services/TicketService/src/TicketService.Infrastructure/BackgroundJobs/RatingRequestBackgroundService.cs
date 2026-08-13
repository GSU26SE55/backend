using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Reminds Customers to rate eligible unrated Closed tickets during the seven-day grace period.
///
/// The reminder threshold is configurable through <c>Ticket:RatingRequest:AfterDays</c> and
/// defaults to day three of the seven-day rating/reopen grace period.
///
/// Idempotent: mỗi ticket chỉ nhắc 1 lần, đánh dấu bằng <see cref="ActivityActionEnum.RatingRequested"/>
/// trong <c>ticket_activities</c> (không cần cột mới / migration).
/// </summary>
public class RatingRequestBackgroundService : BackgroundService
{
    private const int DefaultAfterDays = 3;
    private const int DefaultRatingGracePeriodDays = 7;
    private const int DefaultPollIntervalMinutes = 60;
    private const int BatchSize = 200;

    private readonly ILogger<RatingRequestBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public RatingRequestBackgroundService(
        ILogger<RatingRequestBackgroundService> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    private bool Enabled =>
        !bool.TryParse(_configuration["Ticket:RatingRequest:Enabled"], out var enabled) || enabled;

    private int AfterDays =>
        int.TryParse(_configuration["Ticket:RatingRequest:AfterDays"], out var d) && d > 0
            ? d
            : DefaultAfterDays;

    private int RatingGracePeriodDays =>
        int.TryParse(_configuration["Ticket:RatingRequest:GracePeriodDays"], out var d) && d > 0
            ? d
            : DefaultRatingGracePeriodDays;

    private TimeSpan PollInterval =>
        TimeSpan.FromMinutes(
            int.TryParse(_configuration["Ticket:RatingRequest:PollIntervalMinutes"], out var m) && m > 0
                ? m
                : DefaultPollIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            _logger.LogInformation("RatingRequestBackgroundService disabled via Ticket:RatingRequest:Enabled=false.");
            return;
        }

        _logger.LogInformation(
            "RatingRequestBackgroundService started (afterDays={AfterDays}, interval={Interval}).",
            AfterDays, PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RequestPendingRatingsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RatingRequestBackgroundService tick failed.");
            }

            try
            { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("RatingRequestBackgroundService stopped.");
    }

    /// <summary>Internal cho unit test — quét 1 vòng và publish nhắc đánh giá.</summary>
    public async Task RequestPendingRatingsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>();

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(-AfterDays);

        // Chỉ ticket còn chờ đánh giá, đã quá mốc nhắc, và CHƯA từng được nhắc.
        var candidates = await uow.Tickets.GetAllAsync()
            .Where(t => !t.IsDeleted
                    && t.Status == TicketStatusEnum.Closed
                    && t.RatedAt == null
                    && t.Rating == null
                    && t.CloseReason != TicketCloseReasonEnum.MergedDuplicate
                        && t.ApprovedAt != null
                        && t.ApprovedAt <= threshold
                        && !t.Activities.Any(a => a.Action == ActivityActionEnum.RatingRequested && !a.IsDeleted))
            .OrderBy(t => t.ApprovedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        await uow.ExecuteInTransactionAsync(async transactionCt =>
        {
            foreach (var ticket in candidates)
            {
                transactionCt.ThrowIfCancellationRequested();

                var approvedAt = ticket.ApprovedAt!.Value;
                var daysPending = (int)Math.Floor((now - approvedAt).TotalDays);
                var daysUntilRatingDeadline = Math.Max(0, RatingGracePeriodDays - daysPending);
                var reminder = new TicketRatingRequestedEvent(
                    ticket.Id, ticket.Code, ticket.CustomerId, approvedAt, daysPending, daysUntilRatingDeadline)
                {
                    Id = DeterministicEventId.From(ticket.Id, "ticket-rating-requested")
                };

                await outboxWriter.WriteAsync(reminder, transactionCt);
                await uow.TicketActivities.AddAsync(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    Ticket = ticket,
                    Action = ActivityActionEnum.RatingRequested,
                    ActorRole = ActorRoleEnum.System,
                    ActorDisplayName = "System",
                    Reason = $"Reminded Customer to rate an eligible Closed ticket after {daysPending} day(s) " +
                             $"({daysUntilRatingDeadline} day(s) remaining in the rating grace period)."
                });
            }

            await uow.SaveChangesAsync(transactionCt);
        }, ct);

        _logger.LogInformation("RatingRequest: đã nhắc đánh giá {Count} ticket.", candidates.Count);
    }
}
