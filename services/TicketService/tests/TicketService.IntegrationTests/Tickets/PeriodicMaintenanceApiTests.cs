using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Tickets;

public class PeriodicMaintenanceApiTests : IClassFixture<TicketApiFactory>
{
    private readonly TicketApiFactory _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public PeriodicMaintenanceApiTests(TicketApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
        db.CustomerAccounts.Add(new CustomerAccount
        {
            AccountId = Guid.Parse(TestAuthHandler.UserId),
            Email = "customer@test.com",
            FullName = "Test Customer",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task CustomerSchedule_ValidPeriodicTicket_PersistsScheduleAndOutbox()
    {
        var ticket = await SeedPeriodicTicketAsync();
        var scheduledAt = DateTimeOffset.UtcNow.AddDays(1);

        var response = await _client.PostAsJsonAsync(
            $"/api/customer/tickets/{ticket.Id}/periodic-maintenance/schedule",
            new CustomerSchedulePeriodicMaintenanceCommand
            {
                ScheduledStartAt = scheduledAt
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var persisted = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticket.Id);
        persisted.ScheduledStartAtUtc.Should().BeCloseTo(
            scheduledAt.UtcDateTime,
            TimeSpan.FromSeconds(1));
        persisted.PeriodicMaintenanceCustomerScheduledAtUtc.Should().NotBeNull();
        (await db.OutboxMessages.AsNoTracking().AnyAsync(message =>
            message.Type == nameof(PeriodicMaintenanceScheduleChangedEvent))).Should().BeTrue();
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateBatteryAndMaintenanceDueDate()
    {
        var batteryId = Guid.NewGuid();
        var dueAt = DateTime.UtcNow.AddDays(7);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        db.Tickets.AddRange(
            Ticket("TKT-PERIODIC-1", batteryId, dueAt),
            Ticket("TKT-PERIODIC-2", batteryId, dueAt));

        var action = () => db.SaveChangesAsync();

        await action.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<Ticket> SeedPeriodicTicketAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        var dueAt = DateTime.UtcNow.AddDays(5);
        var ticket = Ticket("TKT-PERIODIC-API", Guid.NewGuid(), dueAt);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    private static Ticket Ticket(string code, Guid batteryId, DateTime dueAt) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        BatteryAssetId = batteryId,
        CustomerId = Guid.Parse(TestAuthHandler.UserId),
        Title = "Periodic maintenance",
        Description = "Periodic maintenance",
        Category = TicketCategoryEnum.Repair,
        Status = TicketStatusEnum.Open,
        Origin = TicketOriginEnum.System,
        PeriodicMaintenanceSourceTicketId = Guid.NewGuid(),
        PeriodicMaintenanceDueAtUtc = dueAt,
        PeriodicMaintenanceScheduleDeadlineAtUtc = dueAt,
        CreatedAt = DateTime.UtcNow
    };
}
