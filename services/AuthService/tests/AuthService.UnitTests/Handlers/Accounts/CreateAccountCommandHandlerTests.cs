using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

public class CreateAccountCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();

    public CreateAccountCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASH");
    }

    [Fact]
    public async Task Create_NewEmail_WithValidRole_CreatesActiveAccount()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Staff",
            NormalizedName = "STAFF",
            Status = RoleStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            PhoneNumber = "+84900111",
            RoleId = role.Id
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(201);
        accounts.Verify(r => r.AddAsync(It.Is<global::AuthService.Domain.Entities.Account>(a =>
            a.EmailConfirmed == true &&
            a.Status == AccountStatusEnum.Active &&
            a.RoleId == role.Id &&
            a.RoleAssignedAt != null
        )), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        var existing = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "dup@example.com",
            PasswordHash = "x",
            FullName = "U"
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "dup@example.com",
            Password = "Pass123!",
            FullName = "Other",
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_DuplicatePhone_Returns409()
    {
        var existing = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "old@example.com",
            PasswordHash = "x",
            FullName = "U",
            PhoneNumber = "+84900111"
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            PhoneNumber = "+84900111",
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_WithMissingRole_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
