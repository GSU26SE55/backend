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

public class TicketChatPhase1ApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
    private readonly TicketDbContext _db;
    private readonly Guid _ticketId = Guid.NewGuid();
    private readonly Guid _userId = Guid.Parse(TestAuthHandler.UserId);

    public TicketChatPhase1ApiTests(TicketApiFactory factory)
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
            Code = "TKT-CHAT-P1",
            Title = "Phase 1 Chat Endpoints Test Ticket",
            Description = "Initial details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = _userId,
            AssignedStaffId = _userId,
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        _db.SaveChanges();
    }

    private TicketChat SeedChat(Guid authorId, bool isInternal = false)
    {
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = authorId,
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Seed Author",
            Body = "Seed chat body",
            IsInternal = isInternal,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        _db.SaveChanges();
        return chat;
    }

    [Fact]
    public async Task GetChatById_Author_Returns200OK()
    {
        var chat = SeedChat(_userId);

        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<TicketChatDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(chat.Id.ToString());
        result.Data!.Mentions.Should().BeEmpty();
        result.Data!.Reactions.ThumbsUp.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetChatById_InternalChatHiddenFromCustomer_Returns404NotFound()
    {
        var chat = SeedChat(Guid.NewGuid(), isInternal: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tickets/{_ticketId}/chats/{chat.Id}");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Customer");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetChatHistory_Author_Returns200OK()
    {
        var chat = SeedChat(_userId);
        _db.TicketChatEdits.Add(new TicketChatEdit
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            Chat = null!,
            OldBody = "old",
            NewBody = "new",
            EditedAt = DateTime.UtcNow,
            EditedByUserId = _userId,
            EditedByRole = ActorRoleEnum.Customer
        });
        await _db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}/history");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<List<ChatEditHistoryDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetChatHistory_CustomerCannotSeeOthersHistory_Returns403Forbidden()
    {
        var chat = SeedChat(Guid.NewGuid());

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/tickets/{_ticketId}/chats/{chat.Id}/history");
        request.Headers.Add(TestAuthHandler.RolesHeader, "Customer");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMyChats_ReturnsOwnChats_Returns200OK()
    {
        SeedChat(_userId);
        SeedChat(_userId);
        SeedChat(Guid.NewGuid());

        var response = await _client.GetAsync("/api/chats/me?page=1&pageSize=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<PaginationResponse<TicketChatDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data!.Items.Should().OnlyContain(c => c.AuthorUserId == _userId.ToString());
    }

    [Fact]
    public async Task AddChatAttachment_Author_Returns201Created()
    {
        var chat = SeedChat(_userId);
        var command = new ChatAttachmentAddCommand
        {
            FileId = Guid.NewGuid(),
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024
        };

        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}/attachments", command);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<TicketAttachmentDTO>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.FileName.Should().Be("photo.jpg");
    }

    [Fact]
    public async Task AddChatAttachment_NonAuthorNonManager_Returns403Forbidden()
    {
        var chat = SeedChat(Guid.NewGuid());
        var command = new ChatAttachmentAddCommand
        {
            FileId = Guid.NewGuid(),
            FileName = "photo.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 1024
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{_ticketId}/chats/{chat.Id}/attachments")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Staff");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveChatAttachment_Author_Returns200OK()
    {
        var chat = SeedChat(_userId);
        var attachment = new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            ChatId = chat.Id,
            UploadedByUserId = _userId,
            FileId = Guid.NewGuid(),
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            SizeBytes = 2048
        };
        _db.TicketAttachments.Add(attachment);
        await _db.SaveChangesAsync();

        var response = await _client.DeleteAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}/attachments/{attachment.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _db.Entry(attachment).ReloadAsync();
        attachment.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task ListChatAttachments_ReturnsAttachments_Returns200OK()
    {
        var chat = SeedChat(_userId);
        _db.TicketAttachments.Add(new TicketAttachment
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            ChatId = chat.Id,
            UploadedByUserId = _userId,
            FileId = Guid.NewGuid(),
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            SizeBytes = 2048
        });
        await _db.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}/attachments");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<List<TicketAttachmentDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].FileName.Should().Be("doc.pdf");
    }
}
