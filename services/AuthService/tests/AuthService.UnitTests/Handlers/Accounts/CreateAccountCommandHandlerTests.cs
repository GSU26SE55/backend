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
    public async Task Create_NewEmail_NoRoles_CreatesActiveAccount()
    {
        var (uow, accounts, _, _, accountRoles) = MockUnitOfWork.Build();
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            PhoneNumber = "0900111"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(201);
        accounts.Verify(r => r.AddAsync(It.Is<global::AuthService.Domain.Entities.Account>(a =>
            a.EmailConfirmed == true &&
            a.Status == AccountStatusEnum.Active
        )), Times.Once);
        accountRoles.Verify(r => r.AddAsync(It.IsAny<AccountRole>()), Times.Never);
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
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "dup@example.com",
            Password = "Pass123!",
            FullName = "Other"
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
            PhoneNumber = "0900111"
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            PhoneNumber = "0900111"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Create_WithValidRoles_AssignsAll()
    {
        var roleA = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "A", NormalizedName = "A", Status = RoleStatusEnum.Active };
        var roleB = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "B", NormalizedName = "B", Status = RoleStatusEnum.Active };
        var (uow, _, _, _, accountRoles) = MockUnitOfWork.Build(roleSeed: new[] { roleA, roleB });
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            RoleIds = new List<Guid> { roleA.Id, roleB.Id }
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accountRoles.Verify(r => r.AddAsync(It.IsAny<AccountRole>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_WithMissingRole_Returns400()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new CreateAccountCommandHandler(uow.Object, _hasher.Object);

        var resp = await handler.Handle(new CreateAccountCommand
        {
            Email = "new@example.com",
            Password = "Pass123!",
            FullName = "New",
            RoleIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}
