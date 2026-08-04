using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.MaintenanceLogs;
using TicketService.Application.DTOs.Response.Maintenances;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Tickets;

public class MaintenanceLogApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
    private readonly TicketDbContext _db;
    private readonly Guid _ticketId = Guid.NewGuid();

    public MaintenanceLogApiTests(TicketApiFactory factory)
    {
        _client = factory.CreateClient();
        _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        var scope = factory.Services.CreateScope();
        _db = scope.ServiceProvider.GetRequiredService<TicketDbContext>();

        // Clean & Seed before each test
        _db.Database.EnsureDeleted();
        _db.Database.EnsureCreated();

        var userId = Guid.Parse(TestAuthHandler.UserId);

        _db.CustomerAccounts.Add(new CustomerAccount
        {
            AccountId = userId,
            Email = "customer@test.com",
            FullName = "Test Customer",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow
        });

        _db.StaffAccounts.Add(new StaffAccount
        {
            AccountId = userId,
            Email = "staff@test.com",
            FullName = "Test Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            LastSyncedAt = DateTime.UtcNow
        });

        _db.Tickets.Add(new Ticket
        {
            Id = _ticketId,
            Code = "TKT-LOG-1",
            Title = "Maintenance Test Ticket",
            Description = "Initial details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });
        _db.TicketAssignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            StaffId = userId,
            Role = AssignmentRoleEnum.PrimaryHandler
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task AddLog_ValidData_Returns201Created()
    {
        // Arrange
        var command = new MaintenanceLogAddCommand
        {
            LogType = MaintenanceLogTypeEnum.Inspection,
            Summary = "Completed Diagnostic checks on cell balance.",
            DurationMinutes = 45,
            StartedAt = DateTime.UtcNow
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/maintenance-logs", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetLogsByTicket_Returns200OK()
    {
        // Arrange
        var log = new MaintenanceLog
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            StaffId = Guid.Parse(TestAuthHandler.UserId),
            LogType = MaintenanceLogTypeEnum.Inspection,
            Summary = "Seed Log Summary",
            DurationMinutes = 20,
            StartedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.MaintenanceLogs.Add(log);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/maintenance-logs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<List<MaintenanceLogDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull().And.NotBeEmpty();
        result.Data!.Should().Contain(x => x.Id == log.Id.ToString());
    }

    [Fact]
    public async Task GetMyLogs_Returns200OK()
    {
        // Arrange
        var log = new MaintenanceLog
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            StaffId = Guid.Parse(TestAuthHandler.UserId),
            LogType = MaintenanceLogTypeEnum.Inspection,
            Summary = "Seed Log Summary",
            DurationMinutes = 20,
            StartedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.MaintenanceLogs.Add(log);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync("/api/staff/tickets/maintenance-logs/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<List<StaffMaintenanceLogGroupDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull().And.NotBeEmpty();
    }

    [Fact]
    public async Task UpdateLog_ValidData_Returns200OK()
    {
        // Arrange
        var log = new MaintenanceLog
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            StaffId = Guid.Parse(TestAuthHandler.UserId),
            LogType = MaintenanceLogTypeEnum.Inspection,
            Summary = "Seed Log Summary",
            DurationMinutes = 20,
            StartedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        _db.MaintenanceLogs.Add(log);
        await _db.SaveChangesAsync();

        var updateCommand = new MaintenanceLogUpdateCommand
        {
            Summary = "Updated summary via PATCH",
            DurationMinutes = 35
        };

        // Act
        var response = await _client.PatchAsJsonAsync($"/api/tickets/{_ticketId}/maintenance-logs/{log.Id}", updateCommand);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();

        // Verify changes in DB
        _db.Entry(log).State = EntityState.Detached;
        var updatedLog = await _db.MaintenanceLogs.FindAsync(log.Id);
        updatedLog!.Summary.Should().Be("Updated summary via PATCH");
        updatedLog.DurationMinutes.Should().Be(35);
    }
}
