using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Services;

public class ChatAuthorizationServiceTests
{
    private static Ticket MakeTicket(Guid? customerId = null, Guid? assignedStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        CustomerId = customerId ?? Guid.NewGuid(),
        AssignedStaffId = assignedStaffId,
        Title = "Test",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };

    private static TicketParticipant MakeParticipant(Ticket ticket, Guid userId, ParticipantTypeEnum type, bool canViewInternal, DateTime? removedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticket.Id,
        Ticket = ticket,
        UserId = userId,
        UserRole = ActorRoleEnum.Staff,
        ParticipantType = type,
        CanPost = true,
        CanViewInternal = canViewInternal,
        AddedByUserId = Guid.NewGuid(),
        AddedAt = DateTime.UtcNow,
        RemovedAt = removedAt
    };

    #region CanAccessTicketAsync

    [Fact]
    public async Task CanAccessTicketAsync_ActiveParticipant_ReturnsTrue()
    {
        var ticket = MakeTicket();
        var collaboratorId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, collaboratorId, ParticipantTypeEnum.Collaborator, canViewInternal: false);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, collaboratorId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessTicketAsync_RemovedParticipant_ReturnsFalse()
    {
        var ticket = MakeTicket();
        var removedUserId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, removedUserId, ParticipantTypeEnum.Watcher, canViewInternal: false, removedAt: DateTime.UtcNow);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, removedUserId, new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAccessTicketAsync_NotParticipantNotCustomerNotStaff_ReturnsFalse()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, Guid.NewGuid(), new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAccessTicketAsync_TicketOwnerCustomer_ReturnsTrueWithoutParticipantRow()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(ticket.Id, customerId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAccessTicketAsync_TicketNotFound_ReturnsFalse()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanAccessTicketAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { "Admin" });

        result.Should().BeFalse();
    }

    #endregion

    #region CanViewInternalChatsAsync

    [Fact]
    public async Task CanViewInternalChatsAsync_StaffRole_ReturnsTrue()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(Guid.NewGuid(), Guid.NewGuid(), new[] { "Staff" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_ParticipantWithCanViewInternalTrue_ReturnsTrue()
    {
        var ticket = MakeTicket();
        var previousAssigneeId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, previousAssigneeId, ParticipantTypeEnum.PreviousAssignee, canViewInternal: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, previousAssigneeId, new[] { "Customer" });

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_ParticipantWithCanViewInternalFalse_ReturnsFalse()
    {
        var ticket = MakeTicket();
        var watcherId = Guid.NewGuid();
        var participant = MakeParticipant(ticket, watcherId, ParticipantTypeEnum.Watcher, canViewInternal: false);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }, participantSeed: new[] { participant });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, watcherId, new[] { "Customer" });

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanViewInternalChatsAsync_NotParticipantNotStaff_ReturnsFalse()
    {
        var ticket = MakeTicket();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(ticketSeed: new[] { ticket });

        var service = new ChatAuthorizationService(uow.Object);

        var result = await service.CanViewInternalChatsAsync(ticket.Id, Guid.NewGuid(), new[] { "Customer" });

        result.Should().BeFalse();
    }

    #endregion

    #region Permission matrix — 4 role x 5 action = 20 case (#515/#516)

    // Permission set per role — khớp PermissionSeed.RoleDefaults (AuthService) cho domain chat.*.
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> RolePermissions = new Dictionary<string, IReadOnlyCollection<string>>
    {
        ["Customer"] = new[] { ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatDeleteOwn },
        ["Staff"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatDeleteOwn,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal
        },
        ["Manager"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatEditAny,
            ChatPermissionCodes.ChatDeleteOwn, ChatPermissionCodes.ChatDeleteAny,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal, ChatPermissionCodes.ChatTemplateCreateGlobal
        },
        ["Admin"] = new[]
        {
            ChatPermissionCodes.ChatCreatePublic, ChatPermissionCodes.ChatCreateInternal,
            ChatPermissionCodes.ChatEditOwn, ChatPermissionCodes.ChatEditAny,
            ChatPermissionCodes.ChatDeleteOwn, ChatPermissionCodes.ChatDeleteAny,
            ChatPermissionCodes.ChatPin, ChatPermissionCodes.ChatViewInternal, ChatPermissionCodes.ChatTemplateCreateGlobal
        }
    };

    public static readonly IEnumerable<object[]> AllRoles = RolePermissions.Keys.Select(r => new object[] { r });

    private static Ticket MakeChatTicket() => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        Title = "Test",
        Description = "desc"
    };

    private static (ChatAuthorizationService service, TicketChat chat) MakeServiceAndOwnChat(Guid actorId)
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var ticket = MakeChatTicket();
        var chat = new TicketChat { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, AuthorUserId = actorId, AuthorRole = ActorRoleEnum.Customer, Body = "x", CreatedAt = DateTime.UtcNow };
        return (new ChatAuthorizationService(uow.Object), chat);
    }

    private static (ChatAuthorizationService service, TicketChat chat) MakeServiceAndOthersChat()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var ticket = MakeChatTicket();
        var chat = new TicketChat { Id = Guid.NewGuid(), TicketId = ticket.Id, Ticket = ticket, AuthorUserId = Guid.NewGuid(), AuthorRole = ActorRoleEnum.Customer, Body = "x", CreatedAt = DateTime.UtcNow };
        return (new ChatAuthorizationService(uow.Object), chat);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void CanEditChat_AsAuthorWithinWindow_AlwaysAllowed_RegardlessOfRole(string role)
    {
        var actorId = Guid.NewGuid();
        var (service, chat) = MakeServiceAndOwnChat(actorId);

        var result = service.CanEditChat(chat, actorId, RolePermissions[role], reasonProvided: false, editWindowMinutes: 15);

        result.Should().Be(ChatAuthorizationResult.Allowed);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void CanEditChat_AsNonAuthorWithReason_AllowedOnlyWithEditAnyPermission(string role)
    {
        var (service, chat) = MakeServiceAndOthersChat();
        var hasEditAny = RolePermissions[role].Contains(ChatPermissionCodes.ChatEditAny);

        var result = service.CanEditChat(chat, Guid.NewGuid(), RolePermissions[role], reasonProvided: true, editWindowMinutes: 15);

        result.Should().Be(hasEditAny ? ChatAuthorizationResult.Allowed : ChatAuthorizationResult.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void CanDeleteChat_AsAuthor_AlwaysAllowed_RegardlessOfRole(string role)
    {
        var actorId = Guid.NewGuid();
        var (service, chat) = MakeServiceAndOwnChat(actorId);

        var result = service.CanDeleteChat(chat, actorId, RolePermissions[role], reasonProvided: false);

        result.Should().Be(ChatAuthorizationResult.Allowed);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void CanDeleteChat_AsNonAuthorWithReason_AllowedOnlyWithDeleteAnyPermission(string role)
    {
        var (service, chat) = MakeServiceAndOthersChat();
        var hasDeleteAny = RolePermissions[role].Contains(ChatPermissionCodes.ChatDeleteAny);

        var result = service.CanDeleteChat(chat, Guid.NewGuid(), RolePermissions[role], reasonProvided: true);

        result.Should().Be(hasDeleteAny ? ChatAuthorizationResult.Allowed : ChatAuthorizationResult.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AllRoles))]
    public void CanPinChat_AllowedOnlyWithPinPermission(string role)
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var service = new ChatAuthorizationService(uow.Object);
        var hasPin = RolePermissions[role].Contains(ChatPermissionCodes.ChatPin);

        var result = service.CanPinChat(RolePermissions[role]);

        result.Should().Be(hasPin);
    }

    #endregion
}
