using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Sagas;

namespace TicketService.Infrastructure.Persistence.Seeders;

public class TicketDataSeeder
{
    private readonly TicketDbContext _context;
    private readonly ILogger<TicketDataSeeder>? _logger;

    public TicketDataSeeder(TicketDbContext context, ILogger<TicketDataSeeder>? logger = null)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        var ct = CancellationToken.None;

        var customers = await SeedCustomerAccountsAsync(ct);
        var staffs = await SeedStaffAccountsAsync(ct);
        var kbArticles = await SeedKnowledgeBaseAsync(staffs.First().AccountId, ct);
        var (tickets, ticketAssignments) = await SeedTicketsAsync(customers, staffs, ct);
        if (tickets.Count == 0)
            return;
        await SeedTicketAssignmentsAsync(ticketAssignments, ct);
        await SeedSlaTimersAsync(tickets, ct);
        await SeedActivitiesAsync(tickets, staffs, customers, ticketAssignments, ct);
        await SeedChatsAsync(tickets, staffs, customers, ct);
        await SeedMaintenanceLogsAsync(tickets, staffs, kbArticles, ct);
        await SeedSagaStatesAsync(tickets, ct);
    }

    private async Task SeedSagaStatesAsync(List<Ticket> tickets, CancellationToken ct)
    {
        var hasSaga = await _context.AlertTicketSagaStates.AnyAsync(ct);
        if (hasSaga)
            return;

        // Chỉ seed cho các ticket có Origin = AutoFromAlert (vì saga chỉ chạy với auto flow).
        var autoTickets = tickets.Where(t => t.Origin == TicketOriginEnum.AutoFromAlert).ToList();
        if (autoTickets.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var states = new List<AlertTicketSagaState>();

        foreach (var ticket in autoTickets)
        {
            // Saga state Completed cho ticket Resolved auto
            states.Add(new AlertTicketSagaState
            {
                CorrelationId = ticket.OriginAlertId ?? Guid.NewGuid(),
                CurrentState = "Completed",
                Version = 1,
                AlertId = ticket.OriginAlertId ?? Guid.NewGuid(),
                BatteryAssetId = ticket.BatteryAssetId,
                CustomerId = ticket.CustomerId,
                SiteId = Guid.NewGuid(),
                AssetSerialNumber = "BAT-2026-002",
                AnomalyType = 7, // DeviceOffline / shutdown
                Severity = 3, // Critical
                ThresholdValue = 0m,
                ActualValue = 0m,
                Unit = "V",
                DetectedAt = ticket.CreatedAt,
                TicketId = ticket.Id,
                TicketCode = ticket.Code,
                TicketIsReused = false,
                StartedAt = ticket.CreatedAt,
                CompletedAt = ticket.ResolvedAt
            });
        }

        // Thêm 3 saga demo cho các state khác (không gắn ticket cụ thể, để admin endpoint có data đa dạng)
        states.Add(new AlertTicketSagaState
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "TicketRequested",
            Version = 1,
            AlertId = Guid.NewGuid(),
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            AnomalyType = 1, // Overheat
            Severity = 3,
            ThresholdValue = 60m,
            ActualValue = 72m,
            Unit = "°C",
            DetectedAt = now.AddMinutes(-2),
            StartedAt = now.AddMinutes(-2)
        });

        states.Add(new AlertTicketSagaState
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "AlertLinkRequested",
            Version = 2,
            AlertId = Guid.NewGuid(),
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            AnomalyType = 4, // LowSoc
            Severity = 3,
            ThresholdValue = 10m,
            ActualValue = 4.7m,
            Unit = "%",
            DetectedAt = now.AddMinutes(-10),
            StartedAt = now.AddMinutes(-10),
            TicketId = Guid.NewGuid(),
            TicketCode = "TKT-AUTO-9001"
        });

        states.Add(new AlertTicketSagaState
        {
            CorrelationId = Guid.NewGuid(),
            CurrentState = "Failed",
            Version = 3,
            AlertId = Guid.NewGuid(),
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            AnomalyType = 8, // SohDegradation
            Severity = 2,
            ThresholdValue = 75m,
            ActualValue = 72m,
            Unit = "%",
            DetectedAt = now.AddHours(-6),
            StartedAt = now.AddHours(-6),
            FailedAtStage = "TicketRequested",
            FailureReason = "Bounded retry exhausted (timeout)",
            FailureErrorCode = "TIMEOUT_EXHAUSTED",
            FailedAt = now.AddHours(-5).AddMinutes(-30),
            RetryCount = 3
        });

        _context.AlertTicketSagaStates.AddRange(states);
        await _context.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded {Count} AlertTicketSaga states.", states.Count);
    }

    private async Task<List<CustomerAccount>> SeedCustomerAccountsAsync(CancellationToken ct)
    {
        var existing = await _context.CustomerAccounts.ToListAsync(ct);

        var now = DateTime.UtcNow;
        var customers = new List<CustomerAccount>
        {
            new()
            {
                Id = Guid.Parse("0a334d70-349c-4f76-90f1-4340798e4d1f"), // Fixed ID for seeder consistency
                AccountId = Guid.Parse("6f3a3f3a-3f3a-3f3a-3f3a-3f3a3f3a3f3a"), // Match AuthService demo customer
                Email = "customer.demo@solarbattery.local",
                FullName = "Demo Customer",
                PhoneNumber = "0901000000",
                Status = AccountStatusEnum.Active,
                LastSyncedAt = now,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccountId = Guid.NewGuid(),
                Email = "customer.a@solarbattery.local",
                FullName = "Nguyen Van A",
                PhoneNumber = "0901000001",
                Status = AccountStatusEnum.Active,
                LastSyncedAt = now,
                CreatedAt = now
            }
        };

        foreach (var customer in customers)
        {
            if (existing.Any(e => e.Email == customer.Email))
                continue;
            _context.CustomerAccounts.Add(customer);
            existing.Add(customer);
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(ct);

        return existing;
    }

    private async Task<List<StaffAccount>> SeedStaffAccountsAsync(CancellationToken ct)
    {
        var existing = await _context.StaffAccounts.ToListAsync(ct);

        var now = DateTime.UtcNow;
        var staffs = new List<StaffAccount>
        {
            new()
            {
                Id = Guid.Parse("f6b3d1b0-2b1a-4d7a-8b1a-4d7a8b1a4d7a"),
                AccountId = Guid.Parse("1f3a3f3a-3f3a-3f3a-3f3a-3f3a3f3a3f3a"), // Match staff.tier1
                Email = "staff.tier1@solarbattery.local",
                FullName = "Staff Tier1 Generalist",
                EmployeeCode = "STF-T1-001",
                Status = AccountStatusEnum.Active,
                IsAvailable = true,
                MaxConcurrentTickets = 10,
                SkillTier = StaffSkillTierEnum.Generalist,
                SkillCodes = new List<string> { "general" },
                LastSyncedAt = now,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.Parse("a2d3e4f5-b6c7-4d8e-9f0a-b1c2d3e4f5a6"),
                AccountId = Guid.Parse("2f3a3f3a-3f3a-3f3a-3f3a-3f3a3f3a3f3a"), // Match staff.tier2
                Email = "staff.tier2@solarbattery.local",
                FullName = "Staff Tier2 Specialist",
                EmployeeCode = "STF-T2-001",
                Status = AccountStatusEnum.Active,
                IsAvailable = true,
                MaxConcurrentTickets = 8,
                SkillTier = StaffSkillTierEnum.ModuleSpecialist,
                SkillCodes = new List<string> { "battery", "charging" },
                LastSyncedAt = now,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.Parse("d4e5f6a7-b8c9-4d0e-9f1a-b2c3d4e5f6a7"),
                AccountId = Guid.Parse("3f3a3f3a-3f3a-3f3a-3f3a-3f3a3f3a3f3a"), // Match staff.tier3
                Email = "staff.tier3@solarbattery.local",
                FullName = "Staff Tier3 Senior",
                EmployeeCode = "STF-T3-001",
                Status = AccountStatusEnum.Active,
                IsAvailable = true,
                MaxConcurrentTickets = 5,
                SkillTier = StaffSkillTierEnum.SeniorSpecialist,
                SkillCodes = new List<string> { "battery", "firmware", "incident" },
                LastSyncedAt = now,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.Parse("5f6a7b8c-9d0e-4f1a-b2c3-d4e5f6a7b8c9"),
                AccountId = Guid.Parse("4f3a3f3a-3f3a-3f3a-3f3a-3f3a3f3a3f3a"), // Match manager.demo
                Email = "manager.demo@solarbattery.local",
                FullName = "Demo Manager",
                EmployeeCode = "MGR-001",
                Status = AccountStatusEnum.Active,
                IsAvailable = true,
                MaxConcurrentTickets = 10,
                SkillTier = StaffSkillTierEnum.SeniorSpecialist,
                SkillCodes = new List<string> { "management" },
                LastSyncedAt = now,
                CreatedAt = now
            }
        };

        foreach (var staff in staffs)
        {
            if (existing.Any(e => e.Email == staff.Email))
                continue;
            _context.StaffAccounts.Add(staff);
            existing.Add(staff);
        }

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(ct);

        return existing;
    }

    private async Task<List<KnowledgeBaseArticle>> SeedKnowledgeBaseAsync(Guid authorId, CancellationToken ct)
    {
        var existing = await _context.KnowledgeBaseArticles.ToListAsync(ct);
        if (existing.Count > 0)
            return existing;

        var now = DateTime.UtcNow;
        var articles = new List<KnowledgeBaseArticle>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "KB-CHARGE-001",
                Category = TicketCategoryEnum.Charging,
                Title = "Battery not charging: diagnostic guide",
                Content = S("<h2>Symptoms</h2><p>Indicator light is off, battery capacity does not increase when plugged in to charge.</p><h2>Diagnostic steps</h2><p>1. Check the adapter\n2. Measure input voltage\n3. Check the BMS log</p><h2>Resolution steps</h2><p>1. Replace the adapter if faulty\n2. Reset the BMS\n3. Contact technical support if the issue persists</p>"),
                Tags = new List<string> { "charging", "no-power" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                CreatedByUserId = authorId,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "KB-HEAT-001",
                Category = TicketCategoryEnum.Overheat,
                Title = "Battery overheating during use",
                Content = S("<h2>Symptoms</h2><p>Battery case temperature > 50°C after 30 minutes of operation.</p><h2>Diagnostic steps</h2><p>1. Measure surface temperature\n2. Check load current\n3. Read the threshold config</p><h2>Resolution steps</h2><p>1. Reduce load\n2. Check ventilation\n3. Replace the cell if faulty</p>"),
                Tags = new List<string> { "overheat", "safety" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                CreatedByUserId = authorId,
                CreatedAt = now
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "KB-PERF-001",
                Category = TicketCategoryEnum.Performance,
                Title = "Battery capacity degradation",
                Content = S("<h2>Symptoms</h2><p>Battery only holds half the charge it originally did.</p><h2>Diagnostic steps</h2><p>1. Check SOH\n2. Count the cycle count\n3. Compare against the baseline</p><h2>Resolution steps</h2><p>1. If SOH < 75% → recommend EOL\n2. Calibrate the BMS\n3. Advise the customer</p>"),
                Tags = new List<string> { "soh", "degradation" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                CreatedByUserId = authorId,
                CreatedAt = now
            }
        };

        _context.KnowledgeBaseArticles.AddRange(articles);
        await _context.SaveChangesAsync(ct);
        return articles;
    }

    private async Task SeedTicketAssignmentsAsync(List<TicketAssignment> assignments, CancellationToken ct)
    {
        var hasAssignments = await _context.TicketAssignments.AnyAsync(ct);
        if (hasAssignments)
            return;
        if (assignments.Count == 0)
            return;

        _context.TicketAssignments.AddRange(assignments);
        await _context.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded {Count} ticket assignments.", assignments.Count);
    }

    private async Task<(List<Ticket> Tickets, List<TicketAssignment> Assignments)> SeedTicketsAsync(
        List<CustomerAccount> customers,
        List<StaffAccount> staffs,
        CancellationToken ct)
    {
        var existing = await _context.Tickets.ToListAsync(ct);
        if (existing.Count > 0)
            return (existing, new List<TicketAssignment>());

        var customer1 = customers[0].AccountId;
        var customer2 = customers[1].AccountId;
        var staffTier1 = staffs[0].AccountId;
        var staffTier2 = staffs[1].AccountId;
        var staffTier3 = staffs[2].AccountId;

        var batteryAssetId1 = Guid.NewGuid();
        var batteryAssetId2 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var ticket1 = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2602-0001",
            BatteryAssetId = batteryAssetId1,
            CustomerId = customer1,
            Title = "Battery not charging",
            Description = "The battery unit is plugged in but not accumulating any charge. The indicator light is off.",
            Category = TicketCategoryEnum.Charging,
            Priority = TicketPriorityEnum.P1Critical,
            ImpactScope = ImpactScopeEnum.Site,
            UrgencyLevel = UrgencyLevelEnum.High,
            Status = TicketStatusEnum.Open,
            Origin = TicketOriginEnum.ManualByCustomer,
            IsIncident = true,
            CreatedAt = now.AddDays(-5)
        };
        var ticket2 = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2602-0002",
            BatteryAssetId = batteryAssetId2,
            CustomerId = customer2,
            Title = "Unit overheating during use",
            Description = "The battery becomes unusually hot to the touch after about 30 minutes of operation.",
            Category = TicketCategoryEnum.Overheat,
            Priority = TicketPriorityEnum.P2High,
            ImpactScope = ImpactScopeEnum.SingleAsset,
            UrgencyLevel = UrgencyLevelEnum.Medium,
            Status = TicketStatusEnum.InProgress,
            Origin = TicketOriginEnum.ManualByCustomer,
            IsIncident = false,
            CreatedAt = now.AddDays(-3)
        };
        var ticket3 = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2602-0003",
            BatteryAssetId = batteryAssetId1,
            CustomerId = customer1,
            Title = "Performance degradation",
            Description = "Battery life has significantly decreased. It only holds a charge for about half the advertised time.",
            Category = TicketCategoryEnum.Performance,
            Priority = TicketPriorityEnum.P3Normal,
            ImpactScope = ImpactScopeEnum.SingleAsset,
            UrgencyLevel = UrgencyLevelEnum.Low,
            Status = TicketStatusEnum.Pending,
            PendingContext = PendingContextEnum.Held,
            PendingReason = PauseReasonEnum.CustomerUnavailable,
            Origin = TicketOriginEnum.CreatedByStaff,
            IsIncident = false,
            CreatedAt = now.AddDays(-10)
        };
        var ticket4 = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2602-0004",
            BatteryAssetId = batteryAssetId2,
            CustomerId = customer2,
            Title = "Automatic shutdown alert",
            Description = "System automatically generated an alert for an unexpected shutdown event.",
            Category = TicketCategoryEnum.NoPower,
            Priority = TicketPriorityEnum.P1Critical,
            ImpactScope = ImpactScopeEnum.Site,
            UrgencyLevel = UrgencyLevelEnum.High,
            Status = TicketStatusEnum.Completed,
            Origin = TicketOriginEnum.AutoFromAlert,
            OriginAlertId = Guid.NewGuid(),
            IsIncident = true,
            CreatedAt = now.AddDays(-1),
            ResolvedAt = now,
            ResolvedByStaffId = staffTier3,
            ResolutionSummary = "Firmware updated to version 1.2.3 which addresses the shutdown bug."
        };
        var ticket5 = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2602-0005",
            BatteryAssetId = batteryAssetId1,
            CustomerId = customer1,
            Title = "Request for on-site repair",
            Description = "Customer has requested an on-site technician for a repair.",
            Category = TicketCategoryEnum.Repair,
            Priority = TicketPriorityEnum.P2High,
            ImpactScope = ImpactScopeEnum.SingleAsset,
            UrgencyLevel = UrgencyLevelEnum.Medium,
            Status = TicketStatusEnum.Closed,
            Origin = TicketOriginEnum.ManualByCustomer,
            IsIncident = false,
            CreatedAt = now.AddDays(-20),
            ResolvedAt = now.AddDays(-18),
            ResolvedByStaffId = staffTier2,
            ClosedAt = now.AddDays(-17),
            ResolutionSummary = "Technician replaced the main board.",
            Rating = 5,
            RatingComment = "Support was very fast and professional.",
            RatedAt = now.AddDays(-16)
        };

        var tickets = new List<Ticket> { ticket1, ticket2, ticket3, ticket4, ticket5 };

        _context.Tickets.AddRange(tickets);
        await _context.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded {Count} tickets.", tickets.Count);

        // Create TicketAssignment records for tickets that have a PrimaryHandler
        var assignments = new List<TicketAssignment>
        {
            new() { Id = Guid.NewGuid(), TicketId = ticket1.Id, StaffId = staffTier3, Role = AssignmentRoleEnum.PrimaryHandler, CreatedAt = ticket1.CreatedAt.AddMinutes(15) },
            new() { Id = Guid.NewGuid(), TicketId = ticket2.Id, StaffId = staffTier2, Role = AssignmentRoleEnum.PrimaryHandler, CreatedAt = ticket2.CreatedAt.AddMinutes(15) },
            new() { Id = Guid.NewGuid(), TicketId = ticket4.Id, StaffId = staffTier3, Role = AssignmentRoleEnum.PrimaryHandler, CreatedAt = ticket4.CreatedAt.AddMinutes(15) },
            new() { Id = Guid.NewGuid(), TicketId = ticket5.Id, StaffId = staffTier2, Role = AssignmentRoleEnum.PrimaryHandler, CreatedAt = ticket5.CreatedAt.AddMinutes(15) },
        };

        return (tickets, assignments);
    }

    private async Task SeedSlaTimersAsync(List<Ticket> tickets, CancellationToken ct)
    {
        var hasSla = await _context.SlaTimers.AnyAsync(ct);
        if (hasSla)
            return;

        var timers = new List<SlaTimer>();
        foreach (var ticket in tickets.Where(t => t.Priority.HasValue))
        {
            var sla = ticket.Priority!.Value switch
            {
                TicketPriorityEnum.P1Critical => TimeSpan.FromHours(4),
                TicketPriorityEnum.P2High => TimeSpan.FromHours(24),
                _ => TimeSpan.FromHours(72)
            };

            var status = ticket.Status switch
            {
                TicketStatusEnum.Completed or TicketStatusEnum.Closed => SlaTimerStatusEnum.Met,
                TicketStatusEnum.Pending => SlaTimerStatusEnum.Paused,
                _ => SlaTimerStatusEnum.Running
            };

            var startedAt = ticket.CreatedAt;
            var dueAt = startedAt + sla;

            timers.Add(new SlaTimer
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Priority = ticket.Priority.Value,
                StartedAt = startedAt,
                DueAt = dueAt,
                OriginalDueAt = dueAt,
                Status = status,
                MaxTotalPauseMinutes = 1440,
                MaxPauseEpisodes = 3,
                PauseEpisodesCount = status == SlaTimerStatusEnum.Paused ? 1 : 0,
                CurrentPauseStartedAt = status == SlaTimerStatusEnum.Paused ? DateTime.UtcNow.AddHours(-2) : null,
                CreatedAt = startedAt
            });
        }

        _context.SlaTimers.AddRange(timers);
        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedActivitiesAsync(
        List<Ticket> tickets,
        List<StaffAccount> staffs,
        List<CustomerAccount> customers,
        List<TicketAssignment> assignments,
        CancellationToken ct)
    {
        var hasActivities = await _context.TicketActivities.AnyAsync(ct);
        if (hasActivities)
            return;

        var customerById = customers.ToDictionary(c => c.AccountId);
        var staffById = staffs.ToDictionary(s => s.AccountId);
        var primaryHandlerByTicketId = assignments
            .Where(a => a.Role == AssignmentRoleEnum.PrimaryHandler)
            .ToDictionary(a => a.TicketId, a => a.StaffId);
        var activities = new List<TicketActivity>();

        foreach (var ticket in tickets)
        {
            var customer = customerById.GetValueOrDefault(ticket.CustomerId);
            activities.Add(new TicketActivity
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                ActorUserId = ticket.CustomerId,
                ActorRole = ActorRoleEnum.Customer,
                ActorDisplayName = customer?.FullName ?? "Customer",
                Action = ActivityActionEnum.Created,
                NewValue = ticket.Status.ToString(),
                CreatedAt = ticket.CreatedAt,
                Ticket = ticket
            });

            if (primaryHandlerByTicketId.TryGetValue(ticket.Id, out var staffId))
            {
                var staff = staffById.GetValueOrDefault(staffId);
                activities.Add(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    ActorUserId = staffId,
                    ActorRole = ActorRoleEnum.Manager,
                    ActorDisplayName = "System Manager",
                    Action = ActivityActionEnum.StaffAssigned,
                    NewValue = staff?.FullName ?? "Staff",
                    CreatedAt = ticket.CreatedAt.AddMinutes(15),
                    Ticket = ticket
                });
            }

            if (ticket.Status is TicketStatusEnum.Completed or TicketStatusEnum.Closed && ticket.ResolvedAt.HasValue)
            {
                activities.Add(new TicketActivity
                {
                    Id = Guid.NewGuid(),
                    TicketId = ticket.Id,
                    ActorUserId = ticket.ResolvedByStaffId,
                    ActorRole = ActorRoleEnum.Staff,
                    ActorDisplayName = ticket.ResolvedByStaffId.HasValue
                        ? staffById.GetValueOrDefault(ticket.ResolvedByStaffId.Value)?.FullName
                        : "Staff",
                    Action = ActivityActionEnum.Resolved,
                    NewValue = TicketStatusEnum.Completed.ToString(),
                    CreatedAt = ticket.ResolvedAt.Value,
                    Ticket = ticket
                });
            }
        }

        _context.TicketActivities.AddRange(activities);
        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedChatsAsync(
        List<Ticket> tickets,
        List<StaffAccount> staffs,
        List<CustomerAccount> customers,
        CancellationToken ct)
    {
        var hasChats = await _context.TicketChats.AnyAsync(ct);
        if (hasChats)
            return;

        var firstStaff = staffs.First();
        var firstCustomer = customers.First();
        var chats = new List<TicketChat>();

        foreach (var ticket in tickets.Take(3))
        {
            chats.Add(new TicketChat
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                AuthorUserId = ticket.CustomerId,
                AuthorRole = ActorRoleEnum.Customer,
                AuthorDisplayName = firstCustomer.FullName,
                Body = $"Please assist soon — ticket {ticket.Code}.",
                IsInternal = false,
                CreatedAt = ticket.CreatedAt.AddMinutes(5),
                Ticket = ticket
            });

            chats.Add(new TicketChat
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                AuthorUserId = firstStaff.AccountId,
                AuthorRole = ActorRoleEnum.Staff,
                AuthorDisplayName = firstStaff.FullName,
                Body = "Request received, a technician will contact you within 2 hours.",
                IsInternal = false,
                CreatedAt = ticket.CreatedAt.AddMinutes(30),
                Ticket = ticket
            });

            chats.Add(new TicketChat
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                AuthorUserId = firstStaff.AccountId,
                AuthorRole = ActorRoleEnum.Staff,
                AuthorDisplayName = firstStaff.FullName,
                Body = $"[INTERNAL] Need to check the threshold config for asset {ticket.BatteryAssetId}.",
                IsInternal = true,
                CreatedAt = ticket.CreatedAt.AddMinutes(35),
                Ticket = ticket
            });
        }

        _context.TicketChats.AddRange(chats);
        await _context.SaveChangesAsync(ct);
    }

    private async Task SeedMaintenanceLogsAsync(
        List<Ticket> tickets,
        List<StaffAccount> staffs,
        List<KnowledgeBaseArticle> kbArticles,
        CancellationToken ct)
    {
        var hasLogs = await _context.MaintenanceLogs.AnyAsync(ct);
        if (hasLogs)
            return;

        var staff = staffs.First();
        var logs = new List<MaintenanceLog>();

        // Closed KHÔNG kéo theo đã-resolve: ticket đóng vì ngoài scope / khách rút yêu cầu thì
        // ResolvedAt vẫn null. Trước đây chỗ này ép `.Value` nên gặp một ticket như vậy là seeder
        // ném NullReference và service crash-loop ngay lúc khởi động — mà seeder chỉ chạy khi bảng
        // maintenance_logs còn rỗng, nên lỗi chỉ lộ ra trên môi trường vừa reset DB.
        var resolvedTickets = tickets
            .Where(t => t.Status is TicketStatusEnum.Completed or TicketStatusEnum.Closed)
            .Where(t => t.ResolvedAt.HasValue)
            .ToList();
        foreach (var ticket in resolvedTickets)
        {
            var startedAt = ticket.ResolvedAt!.Value.AddHours(-2);
            logs.Add(new MaintenanceLog
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                StaffId = ticket.ResolvedByStaffId ?? staff.AccountId,
                LogType = MaintenanceLogTypeEnum.OnSite,
                Summary = ticket.ResolutionSummary ?? "Resolution completed on-site.",
                DiagnosisDetails = "Checked voltage, current, and temperature — all within allowed thresholds.",
                ActionsTaken = "Reset the BMS and updated to the latest firmware.",
                ResolutionNote = "Customer confirmed the system is operating normally.",
                DurationMinutes = 120,
                StartedAt = startedAt,
                CompletedAt = ticket.ResolvedAt,
                CheckInAt = startedAt,
                CheckInLatitude = 10.695m,
                CheckInLongitude = 106.243m,
                RelatedKbArticleIds = kbArticles.Take(1).Select(k => k.Id).ToList(),
                CreatedAt = startedAt
            });
        }

        if (logs.Count > 0)
        {
            _context.MaintenanceLogs.AddRange(logs);
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task SeedKnowledgeBaseArticlesAsync()
    {
        if (await _context.KnowledgeBaseArticles.AnyAsync())
        {
            Console.WriteLine("[TicketService] KB articles already exist. Seeding skipped.");
            return;
        }

        Console.WriteLine("[TicketService] Seeding KB articles...");

        var managerUserId = Guid.NewGuid(); // Placeholder for manager

        var articles = new List<KnowledgeBaseArticle>
        {
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0001",
                Category = TicketCategoryEnum.Charging,
                Title = "Slow charging - Check the cable and port",
                Content = S("<h2>Symptoms</h2><ul><li>Charging indicator light blinking or off</li><li>Charging time &gt; 8 hours (normally 4-6 hours)</li><li>Battery temperature rises high while charging (&gt;45°C)</li></ul><h2>Diagnostic steps</h2><p>Step 1: Check the power source — Measure input voltage: 220V ±10%, verify power stability. Step 2: Check the connecting cable — cable intact, connector plugged in firmly. Step 3: Check the charging port — no dust, rust, or oxidation.</p><h2>Resolution steps</h2><p>1. Replace the charging cable if damaged (CAB-USB-C-2M). 2. Clean the charging port with compressed air (AIR-CLEAN-500ML). 3. Update firmware if the version is &lt; 1.2.3.</p>"),
                Tags = new List<string> { "charging", "cable", "port", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0002",
                Category = TicketCategoryEnum.Overheat,
                Title = "Overheating during operation - Temperature management",
                Content = S("<h2>Symptoms</h2><p>Battery hot &gt;45°C, cooling fan not running, temperature warning on the display</p><h2>Diagnostic steps</h2><p>1. Check ambient temperature\n2. Check the ventilation vents\n3. Check the cooling fan</p><h2>Resolution steps</h2><p>1. Ensure 10cm of clearance around the unit\n2. Clean dust off the ventilation vents\n3. Replace the fan if faulty (FAN-COOL-120MM)</p>"),
                Tags = new List<string> { "overheat", "thermal", "cooling", "fan", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0003",
                Category = TicketCategoryEnum.NoPower,
                Title = "Sudden power loss - Check firmware and connections",
                Content = S("<h2>Symptoms</h2><p>Battery shuts down suddenly, will not restart, unresponsive</p><h2>Diagnostic steps</h2><p>1. Check the input power source\n2. Check the protection fuse\n3. Check the firmware log</p><h2>Resolution steps</h2><p>1. Reset using the hardware reset button\n2. Update the firmware\n3. Replace the fuse if needed (FUSE-10A)</p>"),
                Tags = new List<string> { "power", "firmware", "connection", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0004",
                Category = TicketCategoryEnum.Performance,
                Title = "Performance degradation - Charge cycle analysis",
                Content = S("<h2>Symptoms</h2><p>Battery capacity dropped &gt;20%, usage time shorter than normal</p><h2>Diagnostic steps</h2><p>1. Check the number of charge cycles\n2. Measure cell voltage\n3. Check SOH (State of Health)</p><h2>Resolution steps</h2><p>1. Balance the cells if the difference is &gt;100mV\n2. Replace faulty cells (CELL-LI-ION-3.7V)\n3. Recommend battery replacement if SOH &lt;70%</p>"),
                Tags = new List<string> { "performance", "capacity", "soh", "cycle", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            },
            new KnowledgeBaseArticle
            {
                Id = Guid.NewGuid(),
                Code = "KB-2026-0005",
                Category = TicketCategoryEnum.Repair,
                Title = "Physical damage - Component replacement guide",
                Content = S("<h2>Symptoms</h2><p>Battery casing deformed, liquid leakage, unusual noise</p><h2>Diagnostic steps</h2><p>1. Assess the extent of damage\n2. Identify the affected components\n3. Check whether the BMS is still working</p><h2>Resolution steps</h2><p>1. Isolate the battery from the system\n2. Replace the damaged components (CASE-REPLACEMENT-MODEL-A)\n3. Perform a full check before returning to operation</p>"),
                Tags = new List<string> { "repair", "physical-damage", "replacement", "example", "template" },
                Status = KbArticleStatusEnum.Published,
                Version = 1,
                ViewCount = 0,
                HelpfulCount = 0,
                CreatedByUserId = managerUserId,
                CreatedAt = DateTime.UtcNow
            }
        };

        await _context.KnowledgeBaseArticles.AddRangeAsync(articles);
        await _context.SaveChangesAsync();

        Console.WriteLine($"[TicketService] Successfully seeded {articles.Count} KB articles.");
    }
    private static JsonDocument S(string? v) =>
            string.IsNullOrWhiteSpace(v) ? JsonDocument.Parse("{}") :
            JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(v));
}
