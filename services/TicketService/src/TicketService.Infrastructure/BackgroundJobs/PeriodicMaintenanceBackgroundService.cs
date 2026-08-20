using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SharedContracts.Events;
using SharedContracts.Events.Root;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.BackgroundJobs;

public sealed class PeriodicMaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PeriodicMaintenanceOptions> _options;
    private readonly ILogger<PeriodicMaintenanceBackgroundService> _logger;
    private readonly TimeProvider _timeProvider;

    public PeriodicMaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PeriodicMaintenanceOptions> options,
        ILogger<PeriodicMaintenanceBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Periodic maintenance worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Periodic maintenance tick failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds),
                stoppingToken);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        await CreateDueTicketsAsync(nowUtc, ct);
        await PublishDueRemindersAsync(nowUtc, ct);
    }

    internal static DateTime CalculateDueAtUtc(DateTime closedAtUtc, int cycleMonths) =>
        EnsureUtc(closedAtUtc).AddMonths(cycleMonths);

    internal static DateOnly CalculateCreationLocalDate(
        DateTime dueAtUtc,
        TimeZoneInfo timeZone,
        int leadDays) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(dueAtUtc), timeZone))
            .AddDays(-leadDays);

    internal static DateTime AddLocalCalendarDays(
        DateTime utc,
        TimeZoneInfo timeZone,
        int days)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(EnsureUtc(utc), timeZone).AddDays(days);
        return TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            timeZone);
    }

    private async Task CreateDueTicketsAsync(DateTime nowUtc, CancellationToken ct)
    {
        var options = _options.Value;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
        var nowLocalDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone));
        var candidateMaxDueAtUtc = nowUtc.AddDays(options.LeadDays + 1);

        List<PeriodicAnchor> anchors;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
            var tickets = uow.Tickets.GetAllAsync();

            anchors = await BuildCreationAnchorQuery(
                    tickets,
                    options,
                    candidateMaxDueAtUtc)
                .ToListAsync(ct);
        }

        foreach (var anchor in anchors)
        {
            var dueAtUtc = CalculateDueAtUtc(anchor.ClosedAtUtc, options.CycleMonths);
            if (CalculateCreationLocalDate(dueAtUtc, timeZone, options.LeadDays) > nowLocalDate)
                continue;

            await CreateOneAsync(anchor, dueAtUtc, nowUtc, timeZone, ct);
        }
    }

    internal static IQueryable<PeriodicAnchor> BuildCreationAnchorQuery(
        IQueryable<Ticket> tickets,
        PeriodicMaintenanceOptions options,
        DateTime candidateMaxDueAtUtc) =>
        tickets
                .AsNoTracking()
                .Where(ticket =>
                    !ticket.IsDeleted &&
                    ticket.Status == TicketStatusEnum.Closed &&
                    ticket.ClosedAt.HasValue &&
                    ticket.BatteryAssetId != Guid.Empty)
                .GroupBy(ticket => ticket.BatteryAssetId)
                .Select(group => group
                    .OrderByDescending(ticket => ticket.ClosedAt)
                    .ThenByDescending(ticket => ticket.Id)
                    .Select(ticket => new PeriodicAnchor(
                        ticket.Id,
                        ticket.BatteryAssetId,
                        ticket.CustomerId,
                        ticket.ClosedAt!.Value,
                        ticket.BatterySerialNumber))
                    .First())
                .Where(anchor =>
                    anchor.ClosedAtUtc.AddMonths(options.CycleMonths) <= candidateMaxDueAtUtc &&
                    !tickets.Any(ticket =>
                        !ticket.IsDeleted &&
                        ticket.BatteryAssetId == anchor.BatteryAssetId &&
                        ticket.PeriodicMaintenanceDueAtUtc ==
                            anchor.ClosedAtUtc.AddMonths(options.CycleMonths)))
                .OrderBy(anchor => anchor.ClosedAtUtc)
                .Take(options.BatchSize);

    private async Task CreateOneAsync(
        PeriodicAnchor anchor,
        DateTime dueAtUtc,
        DateTime nowUtc,
        TimeZoneInfo timeZone,
        CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var codeGenerator = scope.ServiceProvider.GetRequiredService<ITicketCodeGenerator>();

        try
        {
            await uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var exists = await uow.Tickets.GetAllAsync().AnyAsync(ticket =>
                    !ticket.IsDeleted &&
                    ticket.BatteryAssetId == anchor.BatteryAssetId &&
                    ticket.PeriodicMaintenanceDueAtUtc == dueAtUtc,
                    transactionCt);
                if (exists)
                    return;

                var ticketId = Guid.NewGuid();
                var code = await codeGenerator.GenerateAsync();
                var isOverdue = dueAtUtc < nowUtc;
                var deadlineAtUtc = isOverdue
                    ? AddLocalCalendarDays(
                        nowUtc,
                        timeZone,
                        _options.Value.OverdueScheduleWindowDays)
                    : dueAtUtc;

                var ticket = new Ticket
                {
                    Id = ticketId,
                    Code = code,
                    BatteryAssetId = anchor.BatteryAssetId,
                    CustomerId = anchor.CustomerId,
                    Title = "Periodic battery maintenance",
                    Description = $"Scheduled six-month maintenance for battery {anchor.BatteryAssetId}.",
                    Category = TicketCategoryEnum.Repair,
                    Priority = null,
                    Status = TicketStatusEnum.Open,
                    Origin = TicketOriginEnum.System,
                    ReopenCount = 0,
                    IsIncident = false,
                    BatterySerialNumber = anchor.BatterySerialNumber,
                    AiVerifyStatus = TicketVerifyStatusEnum.Skipped,
                    PeriodicMaintenanceSourceTicketId = anchor.SourceTicketId,
                    PeriodicMaintenanceDueAtUtc = dueAtUtc,
                    PeriodicMaintenanceScheduleDeadlineAtUtc = deadlineAtUtc,
                    CreatedAt = nowUtc
                };

                await uow.Tickets.AddAsync(ticket);
                await uow.TicketBatteryAssets.AddAsync(new TicketBatteryAsset
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticketId,
                    BatteryAssetId = anchor.BatteryAssetId,
                    Ticket = ticket
                });
                await uow.TicketParticipants.AddAsync(new TicketParticipant
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticketId,
                    Ticket = ticket,
                    UserId = anchor.CustomerId,
                    UserRole = ActorRoleEnum.Customer,
                    ParticipantType = ParticipantTypeEnum.Owner,
                    CanPost = true,
                    CanViewInternal = false,
                    AddedByUserId = anchor.CustomerId,
                    AddedAt = nowUtc
                });

                await uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _logger.LogInformation(
                "Periodic ticket already exists for battery {BatteryAssetId} and due date {DueAtUtc}.",
                anchor.BatteryAssetId,
                dueAtUtc);
        }
    }

    private async Task PublishDueRemindersAsync(DateTime nowUtc, CancellationToken ct)
    {
        List<Guid> candidateIds;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
            candidateIds = await uow.Tickets.GetAllAsync()
                .AsNoTracking()
                .Where(ticket =>
                    !ticket.IsDeleted &&
                    ticket.Status == TicketStatusEnum.Open &&
                    ticket.PeriodicMaintenanceSourceTicketId.HasValue &&
                    ticket.PeriodicMaintenanceDueAtUtc.HasValue &&
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.HasValue &&
                    ticket.ScheduledStartAtUtc == null &&
                    ticket.PeriodicMaintenanceManagerEscalatedAtUtc == null)
                .OrderBy(ticket => ticket.CreatedAt)
                .Select(ticket => ticket.Id)
                .Take(_options.Value.BatchSize)
                .ToListAsync(ct);
        }

        foreach (var ticketId in candidateIds)
            await PublishNextReminderAsync(ticketId, nowUtc, ct);
    }

    private async Task PublishNextReminderAsync(Guid ticketId, DateTime nowUtc, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<ITicketUnitOfWork>();
        var outboxWriter = scope.ServiceProvider.GetRequiredService<IIntegrationEventOutboxWriter>();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(_options.Value.TimeZoneId);

        try
        {
            await uow.ExecuteInTransactionAsync(async transactionCt =>
            {
                var ticket = await uow.Tickets.GetAllAsync().FirstOrDefaultAsync(candidate =>
                    candidate.Id == ticketId &&
                    !candidate.IsDeleted &&
                    candidate.Status == TicketStatusEnum.Open &&
                    candidate.ScheduledStartAtUtc == null,
                    transactionCt);
                if (ticket?.PeriodicMaintenanceDueAtUtc is null ||
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc is null)
                    return;

                var stage = GetDueReminderStage(ticket, nowUtc, timeZone, _options.Value.ReminderTime);
                if (!stage.HasValue)
                    return;

                var evt = new PeriodicMaintenanceReminderDueEvent(
                    ticket.Id,
                    ticket.Code,
                    ticket.BatteryAssetId,
                    ticket.CustomerId,
                    ticket.PeriodicMaintenanceDueAtUtc.Value,
                    ticket.PeriodicMaintenanceScheduleDeadlineAtUtc.Value,
                    stage.Value,
                    ticket.PeriodicMaintenanceDueAtUtc.Value < nowUtc)
                {
                    Id = DeterministicEventId.From(
                        ticket.Id,
                        $"periodic-maintenance-reminder:{(int)stage.Value}")
                };

                await outboxWriter.WriteAsync(evt, transactionCt);
                switch (stage.Value)
                {
                    case PeriodicMaintenanceReminderStage.CustomerFirstReminder:
                        ticket.PeriodicMaintenanceReminder1SentAtUtc = nowUtc;
                        break;
                    case PeriodicMaintenanceReminderStage.CustomerSecondReminder:
                        ticket.PeriodicMaintenanceReminder2SentAtUtc = nowUtc;
                        break;
                    case PeriodicMaintenanceReminderStage.ManagerEscalation:
                        ticket.PeriodicMaintenanceManagerEscalatedAtUtc = nowUtc;
                        break;
                }

                await uow.SaveChangesAsync(transactionCt);
            }, ct);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _logger.LogInformation(
                "Periodic reminder already persisted for ticket {TicketId}.",
                ticketId);
        }
    }

    internal static PeriodicMaintenanceReminderStage? GetDueReminderStage(
        Ticket ticket,
        DateTime nowUtc,
        TimeZoneInfo timeZone,
        TimeSpan reminderTime)
    {
        if (ticket.ScheduledStartAtUtc.HasValue)
            return null;

        var createdLocalDate = TimeZoneInfo.ConvertTimeFromUtc(
            EnsureUtc(ticket.CreatedAt),
            timeZone).Date;
        var nowUtcValue = EnsureUtc(nowUtc);

        bool IsDue(int dayOffset)
        {
            var local = DateTime.SpecifyKind(
                createdLocalDate.AddDays(dayOffset).Add(reminderTime),
                DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, timeZone) <= nowUtcValue;
        }

        if (!ticket.PeriodicMaintenanceReminder1SentAtUtc.HasValue && IsDue(0))
            return PeriodicMaintenanceReminderStage.CustomerFirstReminder;
        if (!ticket.PeriodicMaintenanceReminder2SentAtUtc.HasValue && IsDue(1))
            return PeriodicMaintenanceReminderStage.CustomerSecondReminder;
        if (!ticket.PeriodicMaintenanceManagerEscalatedAtUtc.HasValue && IsDue(2))
            return PeriodicMaintenanceReminderStage.ManagerEscalation;
        return null;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    internal sealed record PeriodicAnchor(
        Guid SourceTicketId,
        Guid BatteryAssetId,
        Guid CustomerId,
        DateTime ClosedAtUtc,
        string? BatterySerialNumber);
}
