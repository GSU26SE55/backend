using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.Realtime;
using Xunit;

namespace TicketService.UnitTest.Infrastructure;

public class TicketChatHubTests
{
    private readonly Mock<IChatAuthorizationService> _authService = new();
    private readonly Mock<IUserConnectionTracker> _tracker = new();
    private readonly Mock<ILogger<TicketChatHub>> _logger = new();
    private readonly Mock<IGroupManager> _groups = new();
    private readonly Mock<IHubCallerClients> _clients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    public TicketChatHubTests()
    {
        _groups.Setup(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _groups.Setup(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _clients.Setup(c => c.OthersInGroup(It.IsAny<string>())).Returns(_clientProxy.Object);
    }

    private TicketChatHub CreateHub(Guid userId, string connectionId = "conn-1")
    {
        var hub = new TicketChatHub(_authService.Object, _tracker.Object, _logger.Object);
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns(connectionId);
        context.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        var claims = new List<Claim>
        {
            new("AccountId", userId.ToString()),
            new(ClaimTypes.Role, "Staff"),
            new("FullName", "Test User"),
        };
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        hub.Context = context.Object;
        hub.Groups = _groups.Object;
        hub.Clients = _clients.Object;
        return hub;
    }

    private TicketChatHub CreateHubNoUserId(string connectionId = "conn-anon")
    {
        var hub = new TicketChatHub(_authService.Object, _tracker.Object, _logger.Object);
        var context = new Mock<HubCallerContext>();
        context.Setup(c => c.ConnectionId).Returns(connectionId);
        context.Setup(c => c.Items).Returns(new Dictionary<object, object?>());
        context.Setup(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "Test")));
        hub.Context = context.Object;
        hub.Groups = _groups.Object;
        hub.Clients = _clients.Object;
        return hub;
    }

    // ── OnConnectedAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task OnConnectedAsync_ValidUser_TracksConnection()
    {
        var userId = Guid.NewGuid();
        var hub = CreateHub(userId, "conn-42");

        await hub.OnConnectedAsync();

        _tracker.Verify(t => t.Track(userId, "conn-42"), Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_NoUserId_DoesNotTrack()
    {
        var hub = CreateHubNoUserId();

        await hub.OnConnectedAsync();

        _tracker.Verify(t => t.Track(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    // ── OnDisconnectedAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task OnDisconnectedAsync_ValidUser_UntracksConnection()
    {
        var userId = Guid.NewGuid();
        var hub = CreateHub(userId, "conn-42");

        await hub.OnDisconnectedAsync(null);

        _tracker.Verify(t => t.Untrack(userId, "conn-42"), Times.Once);
    }

    // ── JoinTicket ──────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinTicket_InvalidGuid_ThrowsHubException()
    {
        var hub = CreateHub(Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket("not-a-guid"));

        ex.Message.Should().Contain("Invalid ticket ID format");
    }

    [Fact]
    public async Task JoinTicket_NoUserId_ThrowsHubException()
    {
        var hub = CreateHubNoUserId();

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket(Guid.NewGuid().ToString()));

        ex.Message.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task JoinTicket_NoAccessToTicket_ThrowsHubException()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        var ex = await Assert.ThrowsAsync<HubException>(() => hub.JoinTicket(ticketId.ToString()));

        ex.Message.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task JoinTicket_HasAccessNoInternal_AddsToPublicGroupOnly()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        _authService.Setup(a => a.CanViewInternalChatsAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        await hub.JoinTicket(ticketId.ToString());

        _groups.Verify(g => g.AddToGroupAsync("conn-1", TicketChatHub.PublicGroup(ticketId), default), Times.Once);
        _groups.Verify(g => g.AddToGroupAsync("conn-1", TicketChatHub.InternalGroup(ticketId), default), Times.Never);
    }

    [Fact]
    public async Task JoinTicket_HasAccessAndCanViewInternal_AddsToBothGroups()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        _authService.Setup(a => a.CanViewInternalChatsAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        var hub = CreateHub(userId);

        await hub.JoinTicket(ticketId.ToString());

        _groups.Verify(g => g.AddToGroupAsync("conn-1", TicketChatHub.PublicGroup(ticketId), default), Times.Once);
        _groups.Verify(g => g.AddToGroupAsync("conn-1", TicketChatHub.InternalGroup(ticketId), default), Times.Once);
    }

    [Fact]
    public async Task JoinTicket_CalledTwice_DbCalledOnceGroupsAddedOnce()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        _authService.Setup(a => a.CanViewInternalChatsAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        await hub.JoinTicket(ticketId.ToString());
        await hub.JoinTicket(ticketId.ToString());

        _authService.Verify(
            a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default),
            Times.Once);
        _groups.Verify(g => g.AddToGroupAsync("conn-1", TicketChatHub.PublicGroup(ticketId), default), Times.Once);
    }

    // ── LeaveTicket ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveTicket_InvalidGuid_ReturnsWithoutException()
    {
        var hub = CreateHub(Guid.NewGuid());

        // should not throw
        await hub.LeaveTicket("not-a-guid");

        _groups.Verify(g => g.RemoveFromGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task LeaveTicket_ValidGuid_RemovesFromBothGroups()
    {
        var ticketId = Guid.NewGuid();
        var hub = CreateHub(Guid.NewGuid());

        await hub.LeaveTicket(ticketId.ToString());

        _groups.Verify(g => g.RemoveFromGroupAsync("conn-1", TicketChatHub.PublicGroup(ticketId), default), Times.Once);
        _groups.Verify(g => g.RemoveFromGroupAsync("conn-1", TicketChatHub.InternalGroup(ticketId), default), Times.Once);
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Typing_InvalidGuid_ReturnsWithoutBroadcast()
    {
        var hub = CreateHub(Guid.NewGuid());

        await hub.Typing("not-a-guid");

        _clients.Verify(c => c.OthersInGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Typing_ValidGuid_BroadcastsUserTypingToOthersInPublicGroup()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        _authService.Setup(a => a.CanViewInternalChatsAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        await hub.Typing(ticketId.ToString());

        _clients.Verify(c => c.OthersInGroup(TicketChatHub.PublicGroup(ticketId)), Times.Once);
        _clientProxy.Verify(p => p.SendCoreAsync(
            "UserTyping",
            It.Is<object[]>(args => args.Length == 3),
            default), Times.Once);
    }

    [Fact]
    public async Task Typing_NoAccess_ReturnsSilentlyWithoutBroadcast()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        await hub.Typing(ticketId.ToString());

        _clients.Verify(c => c.OthersInGroup(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task JoinTicket_ThenTyping_DbCalledOnlyOnce()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _authService.Setup(a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(true);
        _authService.Setup(a => a.CanViewInternalChatsAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(false);
        var hub = CreateHub(userId);

        await hub.JoinTicket(ticketId.ToString());
        await hub.Typing(ticketId.ToString());

        _authService.Verify(
            a => a.CanAccessTicketAsync(ticketId, userId, It.IsAny<IReadOnlyCollection<string>>(), default),
            Times.Once);
    }
}
