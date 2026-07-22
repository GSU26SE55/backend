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
        await SeedChatTemplatesAsync(staffs.First().AccountId, ct);
        var tickets = await SeedTicketsAsync(customers, staffs, ct);
        if (tickets.Count == 0)
            return;
        await SeedSlaTimersAsync(tickets, ct);
        await SeedActivitiesAsync(tickets, staffs, customers, ct);
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
                Title = "Pin không sạc: hướng dẫn chẩn đoán",
                Content = S("<h2>Triệu chứng</h2><p>Đèn báo không sáng, dung lượng pin không tăng khi cắm sạc.</p><h2>Các bước chẩn đoán</h2><p>1. Kiểm tra adapter\n2. Đo điện áp đầu vào\n3. Kiểm tra BMS log</p><h2>Các bước giải quyết</h2><p>1. Thay adapter nếu hỏng\n2. Reset BMS\n3. Liên hệ kỹ thuật nếu vẫn lỗi</p>"),
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
                Title = "Pin quá nhiệt khi sử dụng",
                Content = S("<h2>Triệu chứng</h2><p>Nhiệt độ vỏ pin > 50°C sau 30 phút hoạt động.</p><h2>Các bước chẩn đoán</h2><p>1. Đo nhiệt độ bề mặt\n2. Kiểm tra dòng tải\n3. Đọc threshold config</p><h2>Các bước giải quyết</h2><p>1. Giảm tải\n2. Kiểm tra thông gió\n3. Thay cell nếu hỏng</p>"),
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
                Title = "Suy giảm dung lượng pin",
                Content = S("<h2>Triệu chứng</h2><p>Pin chỉ giữ điện được nửa so với ban đầu.</p><h2>Các bước chẩn đoán</h2><p>1. Kiểm tra SOH\n2. Đếm cycle count\n3. So sánh với baseline</p><h2>Các bước giải quyết</h2><p>1. Nếu SOH < 75% → đề xuất EOL\n2. Calibrate BMS\n3. Tư vấn khách hàng</p>"),
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

    private async Task<List<Ticket>> SeedTicketsAsync(
        List<CustomerAccount> customers,
        List<StaffAccount> staffs,
        CancellationToken ct)
    {
        var existing = await _context.Tickets.ToListAsync(ct);
        if (existing.Count > 0)
            return existing;

        var customer1 = customers[0].AccountId;
        var customer2 = customers[1].AccountId;
        var staffTier1 = staffs[0].AccountId;
        var staffTier2 = staffs[1].AccountId;
        var staffTier3 = staffs[2].AccountId;

        var batteryAssetId1 = Guid.NewGuid();
        var batteryAssetId2 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var tickets = new List<Ticket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0001",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customer1,
                AssignedStaffId = staffTier3,
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
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0002",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customer2,
                AssignedStaffId = staffTier2,
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
            },
            new()
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
                Status = TicketStatusEnum.WaitingCustomer,
                Origin = TicketOriginEnum.CreatedByStaff,
                IsIncident = false,
                CreatedAt = now.AddDays(-10)
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0004",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customer2,
                AssignedStaffId = staffTier3,
                Title = "Automatic shutdown alert",
                Description = "System automatically generated an alert for an unexpected shutdown event.",
                Category = TicketCategoryEnum.NoPower,
                Priority = TicketPriorityEnum.P1Critical,
                ImpactScope = ImpactScopeEnum.Site,
                UrgencyLevel = UrgencyLevelEnum.High,
                Status = TicketStatusEnum.Resolved,
                Origin = TicketOriginEnum.AutoFromAlert,
                OriginAlertId = Guid.NewGuid(),
                IsIncident = true,
                CreatedAt = now.AddDays(-1),
                ResolvedAt = now,
                ResolvedByStaffId = staffTier3,
                ResolutionSummary = "Firmware updated to version 1.2.3 which addresses the shutdown bug."
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0005",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customer1,
                AssignedStaffId = staffTier2,
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
                RatingComment = "Hỗ trợ rất nhanh và chuyên nghiệp.",
                RatedAt = now.AddDays(-16)
            }
        };

        _context.Tickets.AddRange(tickets);
        await _context.SaveChangesAsync(ct);
        _logger?.LogInformation("Seeded {Count} tickets.", tickets.Count);
        return tickets;
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
                TicketStatusEnum.Resolved or TicketStatusEnum.Closed => SlaTimerStatusEnum.Met,
                TicketStatusEnum.WaitingCustomer => SlaTimerStatusEnum.Paused,
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
        CancellationToken ct)
    {
        var hasActivities = await _context.TicketActivities.AnyAsync(ct);
        if (hasActivities)
            return;

        var customerById = customers.ToDictionary(c => c.AccountId);
        var staffById = staffs.ToDictionary(s => s.AccountId);
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

            if (ticket.AssignedStaffId is { } staffId)
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

            if (ticket.Status is TicketStatusEnum.Resolved or TicketStatusEnum.Closed && ticket.ResolvedAt.HasValue)
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
                    NewValue = TicketStatusEnum.Resolved.ToString(),
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
                Body = $"Vui lòng hỗ trợ sớm — ticket {ticket.Code}.",
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
                Body = "Đã ghi nhận yêu cầu, kỹ thuật sẽ liên hệ trong 2h.",
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
                Body = $"[INTERNAL] Cần check threshold config của asset {ticket.BatteryAssetId}.",
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

        var resolvedTickets = tickets.Where(t => t.Status is TicketStatusEnum.Resolved or TicketStatusEnum.Closed).ToList();
        foreach (var ticket in resolvedTickets)
        {
            var startedAt = ticket.ResolvedAt!.Value.AddHours(-2);
            logs.Add(new MaintenanceLog
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                StaffId = ticket.ResolvedByStaffId ?? staff.AccountId,
                LogType = MaintenanceLogTypeEnum.OnSite,
                Summary = ticket.ResolutionSummary ?? "Xử lý hoàn tất tại site.",
                DiagnosisDetails = "Đã kiểm tra điện áp, dòng, nhiệt độ — trong ngưỡng cho phép.",
                ActionsTaken = "Reset BMS + cập nhật firmware mới nhất.",
                ResolutionNote = "Khách hàng xác nhận hệ thống hoạt động bình thường.",
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
                Title = "Pin sạc chậm - Kiểm tra cáp và cổng",
                Content = S("<h2>Triệu chứng</h2><ul><li>Đèn báo sạc nhấp nháy hoặc không sáng</li><li>Thời gian sạc &gt; 8 giờ (bình thường 4-6 giờ)</li><li>Nhiệt độ pin tăng cao khi sạc (&gt;45°C)</li></ul><h2>Các bước chẩn đoán</h2><p>Bước 1: Kiểm tra nguồn điện — Đo điện áp đầu vào: 220V ±10%, kiểm tra ổn định nguồn. Bước 2: Kiểm tra cáp kết nối — Cáp nguyên vẹn, đầu cắm chặt. Bước 3: Kiểm tra cổng sạc — Không có bụi, gỉ, oxy hóa.</p><h2>Các bước giải quyết</h2><p>1. Thay cáp sạc nếu hư hỏng (CAB-USB-C-2M). 2. Làm sạch cổng sạc bằng khí nén (AIR-CLEAN-500ML). 3. Cập nhật firmware nếu phiên bản &lt; 1.2.3.</p>"),
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
                Title = "Quá nhiệt khi vận hành - Quản lý nhiệt độ",
                Content = S("<h2>Triệu chứng</h2><p>Pin nóng &gt;45°C, quạt tản nhiệt không hoạt động, cảnh báo nhiệt độ trên màn hình</p><h2>Các bước chẩn đoán</h2><p>1. Kiểm tra nhiệt độ môi trường\n2. Kiểm tra khe tản nhiệt\n3. Kiểm tra quạt làm mát</p><h2>Các bước giải quyết</h2><p>1. Đảm bảo khoảng trống 10cm xung quanh\n2. Vệ sinh bụi trên khe tản nhiệt\n3. Thay quạt nếu hỏng (FAN-COOL-120MM)</p>"),
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
                Title = "Mất nguồn đột ngột - Kiểm tra firmware và kết nối",
                Content = S("<h2>Triệu chứng</h2><p>Pin tắt đột ngột, không khởi động lại, không phản hồi</p><h2>Các bước chẩn đoán</h2><p>1. Kiểm tra nguồn điện đầu vào\n2. Kiểm tra cầu chì bảo vệ\n3. Kiểm tra log firmware</p><h2>Các bước giải quyết</h2><p>1. Reset bằng nút reset phần cứng\n2. Cập nhật firmware\n3. Thay cầu chì nếu cần (FUSE-10A)</p>"),
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
                Title = "Suy giảm hiệu năng - Phân tích chu kỳ sạc",
                Content = S("<h2>Triệu chứng</h2><p>Dung lượng pin giảm &gt;20%, thời gian sử dụng ngắn hơn bình thường</p><h2>Các bước chẩn đoán</h2><p>1. Kiểm tra số chu kỳ sạc\n2. Đo điện áp cell\n3. Kiểm tra SOH (State of Health)</p><h2>Các bước giải quyết</h2><p>1. Cân bằng cell nếu chênh lệch &gt;100mV\n2. Thay cell hỏng (CELL-LI-ION-3.7V)\n3. Khuyến nghị thay pin nếu SOH &lt;70%</p>"),
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
                Title = "Hư hỏng vật lý - Hướng dẫn thay thế linh kiện",
                Content = S("<h2>Triệu chứng</h2><p>Vỏ pin bị biến dạng, rò rỉ chất lỏng, tiếng kêu bất thường</p><h2>Các bước chẩn đoán</h2><p>1. Đánh giá mức độ hư hỏng\n2. Xác định linh kiện bị ảnh hưởng\n3. Kiểm tra BMS còn hoạt động không</p><h2>Các bước giải quyết</h2><p>1. Cách ly pin khỏi hệ thống\n2. Thay thế linh kiện hư hỏng (CASE-REPLACEMENT-MODEL-A)\n3. Kiểm tra toàn bộ trước khi đưa vào vận hành</p>"),
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

    private async Task SeedChatTemplatesAsync(Guid staffId, CancellationToken ct)
    {
        var hasTemplates = await _context.ChatTemplates.AnyAsync(ct);
        if (hasTemplates)
            return;

        var now = DateTime.UtcNow;
        var templates = new List<ChatTemplate>
        {
            new ChatTemplate
            {
                Id = Guid.Parse("1a5113d0-349c-4f76-90f1-4340798e4d1a"),
                Name = "Lời chào hỗ trợ kỹ thuật",
                Content = "Xin chào {{CustomerName}}, tôi là {{StaffName}} từ bộ phận kỹ thuật. Rất tiếc vì sự cố hệ thống pin mặt trời của bạn. Tôi có thể giúp gì cho bạn hôm nay?",
                Category = ChatTemplateCategoryEnum.Greeting,
                IsInternalDefault = false,
                Scope = ChatTemplateScopeEnum.Global,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 15,
                CreatedAt = now
            },
            new ChatTemplate
            {
                Id = Guid.Parse("2b5113d0-349c-4f76-90f1-4340798e4d1b"),
                Name = "Yêu cầu thông tin thông số kỹ thuật",
                Content = "Chào bạn, để chẩn đoán chính xác hơn về lỗi pin mặt trời, bạn vui lòng chụp ảnh bảng điện của bộ inverter và gửi kèm chỉ số điện áp đo được lúc này giúp tôi nhé.",
                Category = ChatTemplateCategoryEnum.RequestInfo,
                IsInternalDefault = false,
                Scope = ChatTemplateScopeEnum.Global,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 42,
                CreatedAt = now
            },
            new ChatTemplate
            {
                Id = Guid.Parse("3c5113d0-349c-4f76-90f1-4340798e4d1c"),
                Name = "Báo cáo cập nhật tiến độ xử lý",
                Content = "Chào bạn, kỹ thuật viên đã hoàn tất việc thay thế Cell pin bị lỗi tại thực địa. Hệ thống đang được chạy kiểm tra xả sạc trong vòng 15-30 phút tới.",
                Category = ChatTemplateCategoryEnum.Update,
                IsInternalDefault = false,
                Scope = ChatTemplateScopeEnum.Global,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 8,
                CreatedAt = now
            },
            new ChatTemplate
            {
                Id = Guid.Parse("4d5113d0-349c-4f76-90f1-4340798e4d1d"),
                Name = "Thông báo hoàn thành & Nghiệm thu",
                Content = "Xin chào {{CustomerName}}, ticket bảo trì pin mã số {{TicketCode}} đã được xử lý hoàn tất. Bạn vui lòng xác nhận nghiệm thu và đánh giá chất lượng phục vụ nhé. Cảm ơn bạn!",
                Category = ChatTemplateCategoryEnum.Resolution,
                IsInternalDefault = true,
                Scope = ChatTemplateScopeEnum.Global,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 29,
                CreatedAt = now
            },
            new ChatTemplate
            {
                Id = Guid.Parse("5e5113d0-349c-4f76-90f1-4340798e4d1e"),
                Name = "Bàn giao nội bộ - Ca tiếp theo",
                Content = "[NỘI BỘ] Đã kiểm tra dây nối ngoại vi, nghi ngờ mạch BMS bị chập chờn. Nhờ kỹ thuật ca tiếp theo mang theo bo mạch BMS thay thế (Model BMS-A1) để thử nghiệm.",
                Category = ChatTemplateCategoryEnum.Internal,
                IsInternalDefault = true,
                Scope = ChatTemplateScopeEnum.Team,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 3,
                CreatedAt = now
            },
            new ChatTemplate
            {
                Id = Guid.Parse("6f5113d0-349c-4f76-90f1-4340798e4d1f"),
                Name = "Mẫu ghi chú cá nhân nháp",
                Content = "Nháp: Ghi nhận nhiệt độ tăng cao bất thường khi sạc dòng cao. Cần theo dõi thêm điện áp từng cell.",
                Category = ChatTemplateCategoryEnum.Other,
                IsInternalDefault = false,
                Scope = ChatTemplateScopeEnum.Personal,
                CreatedByUserId = staffId,
                IsActive = true,
                UsageCount = 0,
                CreatedAt = now
            }
        };

        await _context.ChatTemplates.AddRangeAsync(templates, ct);
        await _context.SaveChangesAsync(ct);

        if (_logger != null)
        {
            _logger.LogInformation("[TicketService] Successfully seeded {Count} chat templates.", templates.Count);
        }
        else
        {
            Console.WriteLine($"[TicketService] Successfully seeded {templates.Count} chat templates.");
        }
    }

    private static JsonDocument S(string? v) =>
        string.IsNullOrWhiteSpace(v) ? JsonDocument.Parse("{}") :
        JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(v));
}
