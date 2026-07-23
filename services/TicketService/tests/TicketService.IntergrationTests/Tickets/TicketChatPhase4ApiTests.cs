using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;
using TicketService.IntergrationTests.Fixtures;

namespace TicketService.IntergrationTests.Tickets;

public class TicketChatPhase4ApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
    private readonly TicketDbContext _db;
    private readonly Guid _ticketId = Guid.NewGuid();
    private readonly Guid _userId = Guid.Parse(TestAuthHandler.UserId);

    public TicketChatPhase4ApiTests(TicketApiFactory factory)
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

        _db.Tickets.Add(new Ticket
        {
            Id = _ticketId,
            Code = "TKT-CHAT-P4",
            Title = "Phase 4 Threading/Pin Test Ticket",
            Description = "Initial details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = _userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });
        _db.TicketAssignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            StaffId = _userId,
            Role = AssignmentRoleEnum.PrimaryHandler
        });

        _db.SaveChanges();
    }

    private TicketChat SeedChat(bool isInternal = false, bool isPinned = false, Guid? parentChatId = null)
    {
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = _userId,
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Seed Author",
            Body = "Seed chat body",
            IsInternal = isInternal,
            IsDeleted = false,
            IsPinned = isPinned,
            ParentChatId = parentChatId
        };
        _db.TicketChats.Add(chat);
        _db.SaveChanges();
        return chat;
    }

    // ---------- AddChatReply ----------

    [Fact]
    public async Task AddChatReply_ValidData_Returns201Created()
    {
        var parent = SeedChat();
        var command = new ChatReplyCommand { Body = "This is a reply" };

        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats/{parent.Id}/replies", command);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();

        await _db.Entry(parent).ReloadAsync();
        parent.ReplyCount.Should().Be(1);
    }

    [Fact]
    public async Task AddChatReply_ReplyToAReply_Returns400BadRequest()
    {
        var rootChat = SeedChat();
        var existingReply = SeedChat(parentChatId: rootChat.Id);
        var command = new ChatReplyCommand { Body = "Reply lồng cấp 2" };

        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats/{existingReply.Id}/replies", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddChatReply_ParentBelongsToDifferentTicket_Returns404NotFound()
    {
        var otherTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = otherTicketId,
            Code = "TKT-CHAT-P4-OTHER",
            Title = "Other Ticket",
            Description = "Other ticket details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = _userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });
        _db.TicketAssignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = otherTicketId,
            StaffId = _userId,
            Role = AssignmentRoleEnum.PrimaryHandler
        });
        var parent = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = otherTicketId,
            Ticket = null!,
            AuthorUserId = _userId,
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Seed Author",
            Body = "Parent on other ticket",
            IsDeleted = false
        };
        _db.TicketChats.Add(parent);
        await _db.SaveChangesAsync();

        var command = new ChatReplyCommand { Body = "Reply via wrong ticket route" };
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats/{parent.Id}/replies", command);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddChatReply_ParentNotFound_Returns404NotFound()
    {
        var command = new ChatReplyCommand { Body = "Reply to nothing" };

        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats/{Guid.NewGuid()}/replies", command);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddChatReply_TicketClosed_Returns400BadRequest()
    {
        var closedTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = closedTicketId,
            Code = "TKT-CHAT-P4-CLOSED",
            Title = "Closed Ticket",
            Description = "Closed ticket details",
            Status = TicketStatusEnum.Closed,
            CustomerId = _userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });
        _db.TicketAssignments.Add(new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = closedTicketId,
            StaffId = _userId,
            Role = AssignmentRoleEnum.PrimaryHandler
        });
        var parent = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = closedTicketId,
            Ticket = null!,
            AuthorUserId = _userId,
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Seed Author",
            Body = "Parent on closed ticket",
            IsDeleted = false
        };
        _db.TicketChats.Add(parent);
        await _db.SaveChangesAsync();

        var command = new ChatReplyCommand { Body = "Reply on closed ticket" };
        var response = await _client.PostAsJsonAsync($"/api/tickets/{closedTicketId}/chats/{parent.Id}/replies", command);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- GetChatReplies ----------

    [Fact]
    public async Task GetChatReplies_Returns200OK()
    {
        var parent = SeedChat();
        var reply = SeedChat(parentChatId: parent.Id);

        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats/{parent.Id}/replies?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<PaginationResponse<TicketChatDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().Contain(x => x.Id == reply.Id.ToString());
    }

    [Fact]
    public async Task GetChatReplies_ParentNotFound_Returns404NotFound()
    {
        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats/{Guid.NewGuid()}/replies?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- PinChat ----------

    [Fact]
    public async Task PinChat_ValidData_Returns200OK()
    {
        var chat = SeedChat();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{_ticketId}/chats/{chat.Id}/pin")
        {
            Content = new StringContent(string.Empty)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _db.Entry(chat).ReloadAsync();
        chat.IsPinned.Should().BeTrue();
        chat.PinnedByUserId.Should().Be(_userId);
    }

    [Fact]
    public async Task PinChat_AlreadyAtMaxLimit_Returns400BadRequest()
    {
        SeedChat(isPinned: true);
        SeedChat(isPinned: true);
        SeedChat(isPinned: true);
        var fourthChat = SeedChat();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{_ticketId}/chats/{fourthChat.Id}/pin")
        {
            Content = new StringContent(string.Empty)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await _db.Entry(fourthChat).ReloadAsync();
        fourthChat.IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task PinChat_NonPrivilegedRole_Returns403Forbidden()
    {
        var chat = SeedChat();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{_ticketId}/chats/{chat.Id}/pin")
        {
            Content = new StringContent(string.Empty)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Customer");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await _db.Entry(chat).ReloadAsync();
        chat.IsPinned.Should().BeFalse();
    }

    // ---------- UnpinChat ----------

    [Fact]
    public async Task UnpinChat_ValidData_Returns200OK()
    {
        var chat = SeedChat(isPinned: true);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/tickets/{_ticketId}/chats/{chat.Id}/pin");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _db.Entry(chat).ReloadAsync();
        chat.IsPinned.Should().BeFalse();
    }

    [Fact]
    public async Task UnpinChat_NotPinned_Returns400BadRequest()
    {
        var chat = SeedChat();

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/tickets/{_ticketId}/chats/{chat.Id}/pin");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnpinChat_NonPrivilegedRole_Returns403Forbidden()
    {
        var chat = SeedChat(isPinned: true);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/tickets/{_ticketId}/chats/{chat.Id}/pin");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Customer");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await _db.Entry(chat).ReloadAsync();
        chat.IsPinned.Should().BeTrue();
    }
}
