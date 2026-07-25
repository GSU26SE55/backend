using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Tickets;

public class TicketLifecycleApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

    public TicketLifecycleApiTests(TicketApiFactory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        // Ensure the database is clean before each test
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

    [Fact]
    public async Task FullTicketLifecycle_Success()
    {
        // 1. Create Ticket (Customer)
        var createCmd = new TicketCreateCommand
        {
            Title = "Lifecycle Test",
            Description = "Testing full flow",
            Category = TicketCategoryEnum.Other,
            IncidentDetectedFrom = DateTime.UtcNow.AddHours(-1)
        };
        var createRes = await _client.PostAsJsonAsync("/api/customer/tickets", createCmd);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = (await createRes.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions))!.Data!;

        // 2. Triage Ticket (Manager)
        var triageCmd = new TicketTriageCommand
        {
            Impact = ImpactScopeEnum.SingleAsset,
            Urgency = UrgencyLevelEnum.Medium,
            ManagerComment = "Approved for processing"
        };
        var triageRes = await _client.PostAsJsonAsync($"/api/admin/tickets/{ticket.Id}/triage", triageCmd);
        triageRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Assign Ticket (Manager)
        var assignCmd = new TicketAssignCommand
        {
            PrimaryHandlerStaffId = Guid.Parse(TestAuthHandler.UserId),
            Notes = "Assigned to staff"
        };
        var assignRes = await _client.PostAsJsonAsync($"/api/admin/tickets/{ticket.Id}/assign", assignCmd);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Start Ticket (Staff)
        var startRes = await _client.PostAsJsonAsync($"/api/staff/tickets/{ticket.Id}/start", new { });
        startRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Resolve Ticket (Staff)
        var resolveCmd = new TicketResolveCommand
        {
            ResolutionSummary = "Fixed by integration test"
        };
        var resolveRes = await _client.PostAsJsonAsync($"/api/staff/tickets/{ticket.Id}/resolve", resolveCmd);
        resolveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Approve Ticket (Manager)
        var approveRes = await _client.PostAsJsonAsync($"/api/admin/tickets/{ticket.Id}/approve", new { });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var finalTicket = (await approveRes.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions))!.Data!;
        finalTicket.Status.Should().Be(TicketStatusEnum.ClosedPendingRate);
    }
}
