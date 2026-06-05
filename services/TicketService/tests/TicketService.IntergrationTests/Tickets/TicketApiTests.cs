using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Ticket;
using TicketService.Domain.Enums;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Tickets;

public class TicketApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;

    public TicketApiTests(TicketApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateTicket_ValidData_Returns201AndTicketCode()
    {
        // Arrange
        var command = new TicketCreateCommand
        {
            Title = "Integration Test Ticket",
            Description = "Testing ticket creation",
            Category = TicketCategoryEnum.Charging
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/customer/tickets", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>();
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

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>();
        result!.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Dữ liệu đầu vào không hợp lệ");
    }
}
