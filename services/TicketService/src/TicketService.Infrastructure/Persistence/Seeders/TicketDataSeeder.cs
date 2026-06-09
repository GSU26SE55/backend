using Microsoft.EntityFrameworkCore;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Persistence.Seeders;

public class TicketDataSeeder
{
    private readonly TicketDbContext _context;

    public TicketDataSeeder(TicketDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        if (await _context.Tickets.AnyAsync())
        {
            Console.WriteLine("[TicketService] Ticket data already exists. Seeding skipped.");
            return;
        }

        Console.WriteLine("[TicketService] Seeding ticket data...");

        var customerId1 = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();
        var batteryAssetId1 = Guid.NewGuid();
        var batteryAssetId2 = Guid.NewGuid();
        var staffId1 = Guid.NewGuid();

        var tickets = new List<Ticket>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0001",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                AssignedStaffId = staffId1,
                Title = "Battery not charging",
                Description = "The battery unit is plugged in but not accumulating any charge. The indicator light is off.",
                Category = TicketCategoryEnum.Charging,
                Priority = TicketPriorityEnum.P1Critical,
                Status = TicketStatusEnum.Open,
                Origin = TicketOriginEnum.ManualByCustomer,
                IsIncident = true,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0002",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customerId2,
                Title = "Unit overheating during use",
                Description = "The battery becomes unusually hot to the touch after about 30 minutes of operation.",
                Category = TicketCategoryEnum.Overheat,
                Priority = TicketPriorityEnum.P2High,
                Status = TicketStatusEnum.InProgress,
                Origin = TicketOriginEnum.ManualByCustomer,
                AssignedStaffId = staffId1,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-3),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0003",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                Title = "Performance degradation",
                Description = "Battery life has significantly decreased. It only holds a charge for about half the advertised time.",
                Category = TicketCategoryEnum.Performance,
                Priority = TicketPriorityEnum.P3Normal,
                Status = TicketStatusEnum.WaitingCustomer,
                Origin = TicketOriginEnum.CreatedByStaff,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-10),
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0004",
                BatteryAssetId = batteryAssetId2,
                CustomerId = customerId2,
                Title = "Automatic shutdown alert",
                Description = "System automatically generated an alert for an unexpected shutdown event.",
                Category = TicketCategoryEnum.NoPower,
                Priority = TicketPriorityEnum.P1Critical,
                Status = TicketStatusEnum.Resolved,
                Origin = TicketOriginEnum.AutoFromAlert,
                OriginAlertId = Guid.NewGuid(),
                IsIncident = true,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ResolvedAt = DateTime.UtcNow,
                ResolutionSummary = "Firmware updated to version 1.2.3 which addresses the shutdown bug."
            },
            new()
            {
                Id = Guid.NewGuid(),
                Code = "TKT-2602-0005",
                BatteryAssetId = batteryAssetId1,
                CustomerId = customerId1,
                Title = "Request for on-site repair",
                Description = "Customer has requested an on-site technician for a repair.",
                Category = TicketCategoryEnum.Repair,
                Priority = TicketPriorityEnum.P2High,
                Status = TicketStatusEnum.Closed,
                Origin = TicketOriginEnum.ManualByCustomer,
                IsIncident = false,
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                ResolvedAt = DateTime.UtcNow.AddDays(-18),
                ClosedAt = DateTime.UtcNow.AddDays(-17),
                ResolutionSummary = "Technician replaced the main board."
            }
        };

        await _context.Tickets.AddRangeAsync(tickets);
        await _context.SaveChangesAsync();

        Console.WriteLine($"[TicketService] Successfully seeded {tickets.Count} tickets.");
    }
}
