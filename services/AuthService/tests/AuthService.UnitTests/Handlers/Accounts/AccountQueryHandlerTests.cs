using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Handlers.Accounts;

public class GetAccountByIdQueryHandlerTests
{
    [Fact]
    public async Task Found_ReturnsAccountDto_WithRoles()
    {
        var role = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active };
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            EmailConfirmed = true,
            AccountRoles = new List<AccountRole> { new() { Id = Guid.NewGuid(), RoleId = role.Id, IsActive = true, Role = role } }
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new GetAccountByIdQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountByIdQuery { Id = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Email.Should().Be("u@e.com");
        resp.Data.Roles.Should().Contain("Customer");
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetAccountByIdQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class GetMyProfileQueryHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();

    [Fact]
    public async Task ValidUser_ReturnsProfile()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "me@e.com",
            PasswordHash = "x",
            FullName = "Me",
            Status = AccountStatusEnum.Active,
            AccountRoles = new List<AccountRole>()
        };
        _currentUser.SetupGet(c => c.UserId).Returns(account.Id.ToString());
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new GetMyProfileQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Email.Should().Be("me@e.com");
    }

    [Fact]
    public async Task NoUserId_Returns401()
    {
        _currentUser.SetupGet(c => c.UserId).Returns((string?)null);
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetMyProfileQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task UserIdValid_ButAccountDeleted_Returns404()
    {
        _currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid().ToString());
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetMyProfileQueryHandler(uow.Object, _currentUser.Object);

        var resp = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class GetAccountsQueryHandlerTests
{
    private static IEnumerable<global::AuthService.Domain.Entities.Account> Seed()
    {
        var role = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active };
        return new[]
        {
            new global::AuthService.Domain.Entities.Account
            {
                Id = Guid.NewGuid(), Email = "alice@e.com", PasswordHash = "x", FullName = "Alice",
                PhoneNumber = "0900111", Status = AccountStatusEnum.Active, EmailConfirmed = true, CreatedAt = DateTime.UtcNow.AddDays(-3),
                AccountRoles = new List<AccountRole> { new() { Id = Guid.NewGuid(), RoleId = role.Id, IsActive = true, Role = role } }
            },
            new global::AuthService.Domain.Entities.Account
            {
                Id = Guid.NewGuid(), Email = "bob@e.com", PasswordHash = "x", FullName = "Bob",
                Status = AccountStatusEnum.Inactive, EmailConfirmed = false, CreatedAt = DateTime.UtcNow.AddDays(-2),
                AccountRoles = new List<AccountRole>()
            },
            new global::AuthService.Domain.Entities.Account
            {
                Id = Guid.NewGuid(), Email = "carol@e.com", PasswordHash = "x", FullName = "Carol",
                Status = AccountStatusEnum.Active, EmailConfirmed = true, CreatedAt = DateTime.UtcNow.AddDays(-1),
                AccountRoles = new List<AccountRole>()
            }
        };
    }

    [Fact]
    public async Task NoFilter_PagedAll()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: Seed());
        var handler = new GetAccountsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountsQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalItems.Should().Be(3);
    }

    [Fact]
    public async Task FilterByStatus_ReturnsMatching()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: Seed());
        var handler = new GetAccountsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountsQuery { PageNumber = 1, PageSize = 10, Status = AccountStatusEnum.Inactive }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
        resp.Data.Items[0].Email.Should().Be("bob@e.com");
    }

    [Fact]
    public async Task FilterByEmailConfirmed_True()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: Seed());
        var handler = new GetAccountsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountsQuery { PageNumber = 1, PageSize = 10, EmailConfirmed = true }, CancellationToken.None);

        resp.Data!.Items.Should().OnlyContain(a => a.EmailConfirmed);
        resp.Data.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task SearchByKeyword_MatchesEmailFullNameOrPhone()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: Seed());
        var handler = new GetAccountsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountsQuery { PageNumber = 1, PageSize = 10, Keyword = "Alice" }, CancellationToken.None);

        resp.Data!.Items.Should().HaveCount(1);
        resp.Data.Items[0].FullName.Should().Be("Alice");
    }
}
