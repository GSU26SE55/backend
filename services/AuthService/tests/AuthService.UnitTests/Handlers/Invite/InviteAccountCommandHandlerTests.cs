using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Invite;

public class InviteAccountCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMessageProducerService> _producer = new();
    private readonly Mock<MediatR.IPublisher> _publisher = MockPublisher.NoOp();

    public InviteAccountCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("PLACEHOLDER-HASH");
    }

    [Fact]
    public async Task Invite_NewEmail_CreatesAccountInPendingWithToken_PublishesInviteEvent()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Staff",
            NormalizedName = "STAFF",
            Status = RoleStatusEnum.Active
        };
        global::AuthService.Domain.Entities.Account? captured = null;
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });
        accounts.Setup(r => r.AddAsync(It.IsAny<global::AuthService.Domain.Entities.Account>()))
            .Callback<global::AuthService.Domain.Entities.Account>(a => captured = a)
            .Returns(Task.CompletedTask);

        var handler = new InviteAccountCommandHandler(uow.Object, _hasher.Object, _producer.Object, _publisher.Object);
        var resp = await handler.Handle(new InviteAccountCommand
        {
            Email = "newstaff@example.com",
            FullName = "New Staff",
            RoleIds = new List<Guid> { role.Id }
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(201);

        captured.Should().NotBeNull();
        captured!.Status.Should().Be(AccountStatusEnum.PendingVerification);
        captured.EmailConfirmed.Should().BeFalse();
        captured.InvitationToken.Should().NotBeNullOrEmpty();
        captured.InvitationExpiredAt.Should().NotBeNull();
        captured.InvitationExpiredAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddHours(72), TimeSpan.FromMinutes(1));

        _producer.Verify(p => p.PublishAsync(
            It.Is<SendAdminInviteEvent>(e =>
                e.Email == "newstaff@example.com" &&
                e.Roles.Contains("Staff") &&
                !string.IsNullOrEmpty(e.InvitationToken)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Invite_EmailExists_Returns409()
    {
        var existing = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "exists@example.com",
            PasswordHash = "x",
            FullName = "E",
            Status = AccountStatusEnum.Active
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new InviteAccountCommandHandler(uow.Object, _hasher.Object, _producer.Object, _publisher.Object);

        var resp = await handler.Handle(new InviteAccountCommand
        {
            Email = "exists@example.com",
            FullName = "X",
            RoleIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendAdminInviteEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Invite_RoleNotActive_Returns400()
    {
        var inactiveRole = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Old",
            NormalizedName = "OLD",
            Status = RoleStatusEnum.Deprecated
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { inactiveRole });
        var handler = new InviteAccountCommandHandler(uow.Object, _hasher.Object, _producer.Object, _publisher.Object);

        var resp = await handler.Handle(new InviteAccountCommand
        {
            Email = "newstaff@example.com",
            FullName = "X",
            RoleIds = new List<Guid> { inactiveRole.Id }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}
