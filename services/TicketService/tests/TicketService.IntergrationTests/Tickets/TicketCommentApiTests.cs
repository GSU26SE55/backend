using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.CommentAdd;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Tickets;

public class TicketCommentApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
    private readonly TicketDbContext _db;
    private readonly Guid _ticketId = Guid.NewGuid();

    public TicketCommentApiTests(TicketApiFactory factory)
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
            Code = "TKT-COMMENT-1",
            Title = "Comment Test Ticket",
            Description = "Initial details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = userId,
            AssignedStaffId = userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task AddComment_ValidData_Returns201Created()
    {
        // Arrange
        var command = new CommentAddCommand
        {
            Body = "This is a test comment from integration tests.",
            IsInternal = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/comments", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AddComment_InvalidData_Returns400BadRequest()
    {
        // Arrange
        var command = new CommentAddCommand
        {
            Body = "" // Invalid
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/comments", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetComments_Returns200OK()
    {
        // Arrange
        var comment = new TicketComment
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Staff,
            AuthorDisplayName = "Test Staff",
            Body = "Seed Comment Summary",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketComments.Add(comment);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/comments?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<PaginationResponse<TicketCommentDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data!.Items.Should().Contain(x => x.Id == comment.Id.ToString());
    }
}
