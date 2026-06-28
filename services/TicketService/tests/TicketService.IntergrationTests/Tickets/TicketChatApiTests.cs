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

public class TicketChatApiTests : IClassFixture<TicketApiFactory>
{
    private readonly HttpClient _client;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;
    private readonly TicketDbContext _db;
    private readonly Guid _ticketId = Guid.NewGuid();

    public TicketChatApiTests(TicketApiFactory factory)
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
    public async Task AddChat_ValidData_Returns201Created()
    {
        // Arrange
        var command = new ChatAddCommand
        {
            Body = "This is a test comment from integration tests.",
            IsInternal = false
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AddChat_InvalidData_Returns400BadRequest()
    {
        // Arrange
        var command = new ChatAddCommand
        {
            Body = "" // Invalid
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/tickets/{_ticketId}/chats", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetChats_Returns200OK()
    {
        // Arrange
        var chat = new TicketChat
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
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/tickets/{_ticketId}/chats?page=1&pageSize=10");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CommonResponse<PaginationResponse<TicketChatDTO>>>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data!.Items.Should().Contain(x => x.Id == chat.Id.ToString());
    }

    [Fact]
    public async Task EditChat_AuthorWithinWindow_Returns200OK()
    {
        // Arrange — chat vừa tạo, trong 15 phút edit window, author = test user
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Original body",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        var command = new ChatEditCommand { Body = "Edited body via integration test" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(chat.Id.ToString());

        // _db vẫn track entity "chat" từ lúc seed nên phải reload từ DB, không thì đọc lại giá trị cũ trong memory
        await _db.Entry(chat).ReloadAsync();
        chat.Body.Should().Be("Edited body via integration test");
        chat.EditCount.Should().Be(1);
    }

    [Fact]
    public async Task EditChat_EmptyBody_Returns400BadRequest()
    {
        // Arrange
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Original body",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        var command = new ChatEditCommand { Body = "" };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EditChat_ChatBelongsToDifferentTicket_Returns404NotFound()
    {
        // Arrange — chat thuộc 1 ticket khác, PUT qua route của _ticketId
        var otherTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = otherTicketId,
            Code = "TKT-COMMENT-2",
            Title = "Other Ticket",
            Description = "Other ticket details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = Guid.Parse(TestAuthHandler.UserId),
            AssignedStaffId = Guid.Parse(TestAuthHandler.UserId),
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = otherTicketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Original body",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        var command = new ChatEditCommand { Body = "Trying to edit via wrong ticket route" };

        // Act — gọi qua route của _ticketId, không phải otherTicketId
        var response = await _client.PutAsJsonAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}", command);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.Body.Should().Be("Original body");
    }

    [Fact]
    public async Task DeleteChat_Author_Returns200OK()
    {
        // Arrange — chat do chính test user tạo
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Body to delete",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(chat.Id.ToString());

        await _db.Entry(chat).ReloadAsync();
        chat.IsDeleted.Should().BeTrue();
        chat.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteChat_WithJsonBodyOnDeleteVerb_BindsSuccessfully()
    {
        // Arrange — xác nhận ASP.NET Core thực sự bind [FromBody] trên [HttpDelete]:
        // một số HTTP client/proxy có thể strip body trên DELETE, nên test gửi body
        // qua HttpRequestMessage.Content (giống cách FE phải gọi) thay vì DeleteAsync() rỗng.
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Body to delete with reason",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/tickets/{_ticketId}/chats/{chat.Id}")
        {
            Content = JsonContent.Create(new { deleteReason = "Spam content" })
        };

        // Act
        var response = await _client.SendAsync(request);

        // Assert — body trên DELETE được model bind đúng, không bị 400/415 do framework/proxy
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _db.Entry(chat).ReloadAsync();
        chat.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteChat_NonAuthor_Returns403Forbidden()
    {
        // Arrange — chat thuộc người khác, test user (resolve role Customer) không phải Manager/Admin
        var otherAuthorId = Guid.NewGuid();
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = otherAuthorId,
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Other Customer",
            Body = "Body of someone else",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChat_TicketClosed_Returns400BadRequest()
    {
        // Arrange — ticket riêng đã Closed
        var closedTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = closedTicketId,
            Code = "TKT-COMMENT-3",
            Title = "Closed Ticket",
            Description = "Closed ticket details",
            Status = TicketStatusEnum.Closed,
            CustomerId = Guid.Parse(TestAuthHandler.UserId),
            AssignedStaffId = Guid.Parse(TestAuthHandler.UserId),
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = closedTicketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Body on closed ticket",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.DeleteAsync($"/api/tickets/{closedTicketId}/chats/{chat.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChat_ChatBelongsToDifferentTicket_Returns404NotFound()
    {
        // Arrange — chat thuộc 1 ticket khác, DELETE qua route của _ticketId
        var otherTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = otherTicketId,
            Code = "TKT-COMMENT-4",
            Title = "Other Ticket",
            Description = "Other ticket details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = Guid.Parse(TestAuthHandler.UserId),
            AssignedStaffId = Guid.Parse(TestAuthHandler.UserId),
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = otherTicketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Original body",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act — gọi qua route của _ticketId, không phải otherTicketId
        var response = await _client.DeleteAsync($"/api/tickets/{_ticketId}/chats/{chat.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChat_ChatNotFound_Returns404NotFound()
    {
        // Act
        var response = await _client.DeleteAsync($"/api/tickets/{_ticketId}/chats/{Guid.NewGuid()}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RestoreChat_DeletedChat_Returns200OK()
    {
        // Arrange — chat đã bị soft-delete trước đó
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.NewGuid(),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Other Customer",
            Body = "Body to restore",
            IsInternal = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act — request không gửi TestAuthHandler.RolesHeader nên giữ default đủ cả 4 role (gồm Admin)
        var response = await _client.PatchAsync($"/api/admin/tickets/{_ticketId}/chats/{chat.Id}/restore", new StringContent(string.Empty));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeTrue();
        result.Data!.Id.Should().Be(chat.Id.ToString());

        await _db.Entry(chat).ReloadAsync();
        chat.IsDeleted.Should().BeFalse();
        chat.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task RestoreChat_TicketClosed_Returns200OK()
    {
        // Arrange — restore là hành động data-correction của Admin, không bị chặn dù ticket đã Closed
        var closedTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = closedTicketId,
            Code = "TKT-COMMENT-5",
            Title = "Closed Ticket",
            Description = "Closed ticket details",
            Status = TicketStatusEnum.Closed,
            CustomerId = Guid.Parse(TestAuthHandler.UserId),
            AssignedStaffId = Guid.Parse(TestAuthHandler.UserId),
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = closedTicketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Body on closed ticket",
            IsInternal = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.PatchAsync($"/api/admin/tickets/{closedTicketId}/chats/{chat.Id}/restore", new StringContent(string.Empty));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _db.Entry(chat).ReloadAsync();
        chat.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreChat_ChatNotDeleted_Returns400BadRequest()
    {
        // Arrange — chat đang active, chưa từng bị xóa
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Active body",
            IsInternal = false,
            IsDeleted = false
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act
        var response = await _client.PatchAsync($"/api/admin/tickets/{_ticketId}/chats/{chat.Id}/restore", new StringContent(string.Empty));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<TicketActionResponse>(_jsonOptions);
        result!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreChat_ChatBelongsToDifferentTicket_Returns404NotFound()
    {
        // Arrange — chat thuộc 1 ticket khác, PATCH qua route của _ticketId
        var otherTicketId = Guid.NewGuid();
        _db.Tickets.Add(new Ticket
        {
            Id = otherTicketId,
            Code = "TKT-COMMENT-6",
            Title = "Other Ticket",
            Description = "Other ticket details",
            Status = TicketStatusEnum.InProgress,
            CustomerId = Guid.Parse(TestAuthHandler.UserId),
            AssignedStaffId = Guid.Parse(TestAuthHandler.UserId),
            Category = TicketCategoryEnum.Other,
            IsDeleted = false
        });

        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = otherTicketId,
            Ticket = null!,
            AuthorUserId = Guid.Parse(TestAuthHandler.UserId),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Test Customer",
            Body = "Original body",
            IsInternal = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act — gọi qua route của _ticketId, không phải otherTicketId
        var response = await _client.PatchAsync($"/api/admin/tickets/{_ticketId}/chats/{chat.Id}/restore", new StringContent(string.Empty));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreChat_ChatNotFound_Returns404NotFound()
    {
        // Act
        var response = await _client.PatchAsync($"/api/admin/tickets/{_ticketId}/chats/{Guid.NewGuid()}/restore", new StringContent(string.Empty));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RestoreChat_NonAdminRole_Returns403Forbidden()
    {
        // Arrange — chat đã bị soft-delete trước đó
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = _ticketId,
            Ticket = null!,
            AuthorUserId = Guid.NewGuid(),
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Other Customer",
            Body = "Body not restored",
            IsInternal = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        };
        _db.TicketChats.Add(chat);
        await _db.SaveChangesAsync();

        // Act — override role claim qua TestAuthHandler.RolesHeader, bỏ Admin → [Authorize(Roles="Admin")] phải chặn
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/admin/tickets/{_ticketId}/chats/{chat.Id}/restore")
        {
            Content = new StringContent(string.Empty)
        };
        request.Headers.Add(TestAuthHandler.RolesHeader, "Customer,Staff,Manager");
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updated = await _db.TicketChats.FindAsync(chat.Id);
        updated!.IsDeleted.Should().BeTrue();
    }
}
