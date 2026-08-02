using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntegrationTests.Fixtures;

namespace TicketService.IntegrationTests.Tickets;

public class TicketApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

    public TicketApiTests(TicketApiFactory factory)
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
        db.SaveChanges();
    }

    [Fact]
    public async Task CreateTicket_ValidData_Returns201AndTicketCode()
    {
        // Arrange
        var command = new TicketCreateCommand
        {
            Title = "Integration Test Ticket",
            Description = "Testing ticket creation",
            Category = TicketCategoryEnum.Charging,
            BatteryAssetIds = [Guid.NewGuid()],
            IncidentDetectedAt = DateTime.UtcNow.AddMinutes(-5)
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/customer/tickets", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Code.Should().StartWith("TKT-");
        result.Data.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CreateTicket_InvalidData_Returns400()
    {
        // Arrange
        var command = new TicketCreateCommand
        {
            Title = "", // Invalid
            Description = "No title",
            Category = TicketCategoryEnum.Charging
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/customer/tickets", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Dữ liệu đầu vào không hợp lệ");
    }
}
