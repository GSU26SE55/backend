using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Scenarios;

/// <summary>
/// Sprint 7 #119 — E2E scenario: hoàn tất golden path (rate → Closed) + reopen lifecycle.
/// Bổ sung cho TicketLifecycleApiTests (dừng ở ClosedPendingRate) + SlaBreachScenarioTests.
/// </summary>
public class TicketCompletionAndReopenScenarioTests : IClassFixture<TicketApiFactory>
{
    private readonly TicketApiFactory _factory;
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _json;

    public TicketCompletionAndReopenScenarioTests(TicketApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _json = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        _json.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

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
        db.StaffAccounts.Add(new StaffAccount
        {
            AccountId = Guid.Parse(TestAuthHandler.UserId),
            Email = "staff@test.com",
            FullName = "Test Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            LastSyncedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    /// <summary>Drive ticket qua create → triage → assign → start → resolve → approve → ClosedPendingRate.</summary>
    private async Task<Guid> DriveToClosedPendingRateAsync(string title)
    {
        var createRes = await _client.PostAsJsonAsync("/api/customer/tickets", new TicketCreateCommand
        {
            Title = title,
            Description = "E2E scenario",
            Category = TicketCategoryEnum.Other
        });
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await createRes.Content.ReadFromJsonAsync<TicketActionResponse>(_json))!.Data!;
        var id = Guid.Parse(ticket.Id);

        (await _client.PostAsJsonAsync($"/api/admin/tickets/{id}/triage", new TicketTriageCommand
        {
            Impact = ImpactScopeEnum.SingleAsset,
            Urgency = UrgencyLevelEnum.Medium,
            ManagerComment = "Triaged"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostAsJsonAsync($"/api/admin/tickets/{id}/assign", new TicketAssignCommand
        {
            PrimaryHandlerStaffId = Guid.Parse(TestAuthHandler.UserId),
            Notes = "Assigned"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostAsJsonAsync($"/api/staff/tickets/{id}/start", new { }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await _client.PostAsJsonAsync($"/api/staff/tickets/{id}/resolve", new TicketResolveCommand
        {
            ResolutionSummary = "Resolved by E2E"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var approveRes = await _client.PostAsJsonAsync($"/api/admin/tickets/{id}/approve", new { });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = (await approveRes.Content.ReadFromJsonAsync<TicketActionResponse>(_json))!.Data!;
        approved.Status.Should().Be(TicketStatusEnum.ClosedPendingRate);

        return id;
    }

    private async Task<Ticket?> LoadTicketAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();
        return await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
    }

    [Fact]
    public async Task GoldenPath_Rate_ClosesTicket()
    {
        var id = await DriveToClosedPendingRateAsync("Golden path → rate");

        var rateRes = await _client.PostAsJsonAsync($"/api/customer/tickets/{id}/rate", new TicketRateCommand
        {
            Rating = 5,
            RatingComment = "Great service"
        });

        rateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ticket = await LoadTicketAsync(id);
        ticket!.Status.Should().Be(TicketStatusEnum.Closed);
        ticket.Rating.Should().Be((short)5);
    }

    [Fact]
    public async Task Reopen_FromClosedPendingRate_ReturnsToOpen_AndIncrementsReopenCount()
    {
        var id = await DriveToClosedPendingRateAsync("Reopen scenario");

        var reopenRes = await _client.PostAsJsonAsync($"/api/customer/tickets/{id}/reopen", new TicketReopenCommand
        {
            ReopenReason = "Vấn đề tái diễn"
        });

        reopenRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var ticket = await LoadTicketAsync(id);
        ticket!.ReopenCount.Should().Be(1);
        ticket.Status.Should().BeOneOf(TicketStatusEnum.Open, TicketStatusEnum.Escalated);
    }
}
