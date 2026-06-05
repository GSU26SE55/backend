using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;
using Xunit;

namespace TicketService.IntergrationTests.BackgroundJobs;

public class AutoCloseBackgroundServiceTests : IClassFixture<TicketApiFactory>
{
    private readonly TicketApiFactory _factory;

    public AutoCloseBackgroundServiceTests(TicketApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AutoCloseBackgroundService_Should_Close_Old_Tickets()
    {
        // Arrange
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AutoCloseBackgroundService>>();
        var serviceProvider = scope.ServiceProvider;

        var ticket = new Ticket
        {
            Code = "TKT-202606-001",
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = "Old Ticket",
            Description = "This ticket should be closed.",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.ClosedPendingRate,
            Origin = TicketOriginEnum.ManualByCustomer,
            ApprovedAt = DateTime.UtcNow.AddDays(-8),
            IsDeleted = false
        };

        await context.Tickets.AddAsync(ticket);
        await context.SaveChangesAsync();

        var backgroundService = new AutoCloseBackgroundService(logger, serviceProvider);

        // Act
        // Since it's a hosted service, we'll invoke the core logic directly for the test.
        // We need to use reflection to call the private method
        var method = typeof(AutoCloseBackgroundService).GetMethod("CloseOldTickets", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        await (Task)method!.Invoke(backgroundService, new object[] { CancellationToken.None })!;


        // Assert
        using var assertScope = _factory.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var closedTicket = await assertContext.Tickets.FindAsync(ticket.Id);

        Assert.NotNull(closedTicket);
        Assert.Equal(TicketStatusEnum.Closed, closedTicket!.Status);
        Assert.NotNull(closedTicket.ClosedAt);
    }
}
