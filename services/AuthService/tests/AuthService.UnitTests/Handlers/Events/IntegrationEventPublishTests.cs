using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Events;

/// <summary>
/// Verify rằng các handler publish đúng integration event ra outbox sau khi xử lý xong.
/// </summary>
public class IntegrationEventPublishTests
{
    private readonly Mock<IMessageProducerService> _producer = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IJwtHelper> _jwt = new();

    public IntegrationEventPublishTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASHED");
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<global::AuthService.Domain.Entities.Account>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>?>())).ReturnsAsync("access");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh");
    }

    [Fact]
    public async Task AdminCreateAccount_Success_PublishesAccountActivatedEvent_WithAdminCreateSource()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Staff",
            NormalizedName = "STAFF",
            Status = RoleStatusEnum.Active
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });

        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "staff@example.com",
            Password = "Password1!",
            FullName = "Staff One",
            PhoneNumber = "0901234567",
            RoleId = role.Id
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(
            It.Is<AccountActivatedEvent>(e =>
                e.Email == "staff@example.com" &&
                e.FullName == "Staff One" &&
                e.PhoneNumber == "+84901234567" &&
                e.CreationSource == "AdminCreate" &&
                e.Role == "Staff"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAccount_Success_PublishesAccountProfileUpdatedEvent()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "Old Name",
            PhoneNumber = "0900000000",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new UpdateAccountCommandHandler(uow.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var resp = await handler.Handle(new UpdateAccountCommand
        {
            Id = account.Id,
            FullName = "New Name",
            PhoneNumber = "0911111111",
            AvatarUrl = "https://cdn/avatar.png"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(
            It.Is<AccountProfileUpdatedEvent>(e =>
                e.AccountId == account.Id &&
                e.FullName == "New Name" &&
                e.PhoneNumber == "+84911111111" &&
                e.AvatarUrl == "https://cdn/avatar.png"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminDeleteAccount_PublishesAccountDeletedEvent_WithAdminDeleteSource()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new DeleteAccountCommandHandler(uow.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());
        await handler.Handle(new DeleteAccountCommand { Id = account.Id }, CancellationToken.None);

        _producer.Verify(p => p.PublishAsync(
            It.Is<AccountDeletedEvent>(e =>
                e.AccountId == account.Id &&
                e.Email == "u@example.com" &&
                e.DeletionSource == "AdminDelete"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SelfDeleteMe_PublishesAccountDeletedEvent_WithSelfDeleteSource()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new DeleteMeCommandHandler(uow.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());
        await handler.Handle(new DeleteMeCommand { AccountId = account.Id }, CancellationToken.None);

        _producer.Verify(p => p.PublishAsync(
            It.Is<AccountDeletedEvent>(e =>
                e.AccountId == account.Id &&
                e.DeletionSource == "SelfDelete"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAccount_NotFound_DoesNotPublishEvent()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new UpdateAccountCommandHandler(uow.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        await handler.Handle(new UpdateAccountCommand
        {
            Id = Guid.NewGuid(),
            FullName = "Whatever"
        }, CancellationToken.None);

        _producer.Verify(p => p.PublishAsync(It.IsAny<AccountProfileUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
