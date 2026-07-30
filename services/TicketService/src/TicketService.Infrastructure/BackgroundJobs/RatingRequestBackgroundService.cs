using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

/// <summary>
/// Sprint 6.2 NOTI-07 (#678) — nhắc Customer đánh giá ticket đang treo ở CLOSED_PENDING_RATE.
///
/// Spec §4.1 (reviewnotification.md) liệt kê "Rating request — Customer nhận nhắc rating (auto sau
/// 7 ngày)" là gap 🔴 chưa tồn tại. Lưu ý về mốc thời gian: <see cref="AutoCloseBackgroundService"/>
/// TỰ ĐÓNG ticket đúng mốc 7 ngày kể từ <c>ApprovedAt</c>, nên nhắc đúng ngày thứ 7 thì Customer
/// gần như không còn cơ hội đánh giá. Vì vậy mốc nhắc để CẤU HÌNH được
/// (<c>Ticket:RatingRequest:AfterDays</c>) và mặc định 3 ngày — nằm giữa cửa sổ 7 ngày.
/// Đặt lại thành 7 nếu muốn bám nguyên văn spec.
///
/// Idempotent: mỗi ticket chỉ nhắc 1 lần, đánh dấu bằng <see cref="ActivityActionEnum.RatingRequested"/>
/// trong <c>ticket_activities</c> (không cần cột mới / migration).
/// </summary>
public class RatingRequestBackgroundService : BackgroundService
{
    private const int DefaultAfterDays = 3;
    private const int DefaultAutoCloseAfterDays = 7;
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

    private int AutoCloseAfterDays =>
        int.TryParse(_configuration["Ticket:RatingRequest:AutoCloseAfterDays"], out var d) && d > 0
            ? d
            : DefaultAutoCloseAfterDays;

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
        var producer = scope.ServiceProvider.GetRequiredService<IMessageProducerService>();

        var now = DateTime.UtcNow;
        var threshold = now.AddDays(-AfterDays);

        // Chỉ ticket còn chờ đánh giá, đã quá mốc nhắc, và CHƯA từng được nhắc.
        var candidates = await uow.Tickets.GetAllAsync()
            .Where(t => !t.IsDeleted
                        && t.Status == TicketStatusEnum.ClosedPendingRate
                        && t.ApprovedAt != null
                        && t.ApprovedAt <= threshold
                        && !t.Activities.Any(a => a.Action == ActivityActionEnum.RatingRequested && !a.IsDeleted))
            .OrderBy(t => t.ApprovedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return;

        foreach (var ticket in candidates)
        {
            if (ct.IsCancellationRequested)
                break;

            var approvedAt = ticket.ApprovedAt!.Value;
            var daysPending = (int)Math.Floor((now - approvedAt).TotalDays);
            var daysUntilAutoClose = Math.Max(0, AutoCloseAfterDays - daysPending);

            await producer.PublishAsync(new TicketRatingRequestedEvent(
                ticket.Id, ticket.Code, ticket.CustomerId, approvedAt, daysPending, daysUntilAutoClose), ct);

            await uow.TicketActivities.AddAsync(new TicketActivity
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Ticket = ticket,
                Action = ActivityActionEnum.RatingRequested,
                ActorRole = ActorRoleEnum.System,
                ActorDisplayName = "System",
                Reason = $"Nhắc Customer đánh giá sau {daysPending} ngày ở CLOSED_PENDING_RATE " +
                         $"(còn {daysUntilAutoClose} ngày trước khi tự đóng)."
            });
        }

        await uow.SaveChangesAsync(ct);

        _logger.LogInformation("RatingRequest: đã nhắc đánh giá {Count} ticket.", candidates.Count);
    }
}
