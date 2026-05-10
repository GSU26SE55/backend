using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

public class AssignRolesCommandHandlerTests
{
    [Fact]
    public async Task Assign_AllValidRoles_AddsNewAssignments()
    {
        var accountId = Guid.NewGuid();
        var roleA = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "A", NormalizedName = "A", Status = RoleStatusEnum.Active };
        var roleB = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "B", NormalizedName = "B", Status = RoleStatusEnum.Active };
        var account = new global::AuthService.Domain.Entities.Account { Id = accountId, Email = "u@e.com", PasswordHash = "x", FullName = "U" };
        var (uow, _, _, _, accountRoles) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { roleA, roleB });
        var handler = new AssignRolesCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRolesCommand
        {
            AccountId = accountId,
            RoleIds = new List<Guid> { roleA.Id, roleB.Id }
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accountRoles.Verify(r => r.AddAsync(It.IsAny<AccountRole>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Assign_AccountNotFound_Returns404()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new AssignRolesCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRolesCommand
        {
            AccountId = Guid.NewGuid(),
            RoleIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Assign_SomeMissingRoles_Returns400()
    {
        var account = new global::AuthService.Domain.Entities.Account { Id = Guid.NewGuid(), Email = "u@e.com", PasswordHash = "x", FullName = "U" };
        var roleA = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "A", NormalizedName = "A", Status = RoleStatusEnum.Active };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { roleA });
        var handler = new AssignRolesCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRolesCommand
        {
            AccountId = account.Id,
            RoleIds = new List<Guid> { roleA.Id, Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Assign_ExistingAssignment_ReactivatesAndUpdatesExpiry()
    {
        var account = new global::AuthService.Domain.Entities.Account { Id = Guid.NewGuid(), Email = "u@e.com", PasswordHash = "x", FullName = "U" };
        var role = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "R", NormalizedName = "R", Status = RoleStatusEnum.Active };
        var existingAssignment = new AccountRole
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            RoleId = role.Id,
            IsActive = false,
            AssignedAt = DateTime.UtcNow.AddDays(-30)
        };
        var expiredAt = DateTime.UtcNow.AddDays(7);
        var (uow, _, _, _, accountRoles) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            roleSeed: new[] { role },
            accountRoleSeed: new[] { existingAssignment });
        var handler = new AssignRolesCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRolesCommand
        {
            AccountId = account.Id,
            RoleIds = new List<Guid> { role.Id },
            ExpiredAt = expiredAt
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        existingAssignment.IsActive.Should().BeTrue();
        existingAssignment.ExpiredAt.Should().Be(expiredAt);
        accountRoles.Verify(r => r.AddAsync(It.IsAny<AccountRole>()), Times.Never);
    }
}

public class RevokeRoleCommandHandlerTests
{
    [Fact]
    public async Task Revoke_ExistingAssignment_DeletesIt()
    {
        var assignment = new AccountRole
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            IsActive = true
        };
        var (uow, _, _, _, accountRoles) = MockUnitOfWork.Build(accountRoleSeed: new[] { assignment });
        var handler = new RevokeRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new RevokeRoleCommand
        {
            AccountId = assignment.AccountId,
            RoleId = assignment.RoleId
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accountRoles.Verify(r => r.DeleteAsync(assignment), Times.Once);
    }

    [Fact]
    public async Task Revoke_NotFound_Returns404()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        var handler = new RevokeRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new RevokeRoleCommand
        {
            AccountId = Guid.NewGuid(),
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class AssignRoleTemporaryCommandHandlerTests
{
    [Fact]
    public async Task AssignTemp_NewAssignment_AddsWithExpiry()
    {
        var account = new global::AuthService.Domain.Entities.Account { Id = Guid.NewGuid(), Email = "u@e.com", PasswordHash = "x", FullName = "U" };
        var role = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "R", NormalizedName = "R", Status = RoleStatusEnum.Active };
        var (uow, accounts, _, _, accountRoles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new AssignRoleTemporaryCommandHandler(uow.Object);

        var expiredAt = DateTime.UtcNow.AddDays(7);
        var resp = await handler.Handle(new AssignRoleTemporaryCommand
        {
            AccountId = account.Id,
            RoleId = role.Id,
            ExpiredAt = expiredAt
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accountRoles.Verify(r => r.AddAsync(It.Is<AccountRole>(ar =>
            ar.AccountId == account.Id &&
            ar.RoleId == role.Id &&
            ar.IsActive == true &&
            ar.ExpiredAt == expiredAt
        )), Times.Once);
    }

    [Fact]
    public async Task AssignTemp_RoleNotFound_Returns404()
    {
        var account = new global::AuthService.Domain.Entities.Account { Id = Guid.NewGuid(), Email = "u@e.com", PasswordHash = "x", FullName = "U" };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new AssignRoleTemporaryCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRoleTemporaryCommand
        {
            AccountId = account.Id,
            RoleId = Guid.NewGuid(),
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignTemp_AccountNotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new AssignRoleTemporaryCommandHandler(uow.Object);

        var resp = await handler.Handle(new AssignRoleTemporaryCommand
        {
            AccountId = Guid.NewGuid(),
            RoleId = Guid.NewGuid(),
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class UnlockAccountCommandHandlerTests
{
    [Fact]
    public async Task Unlock_LockedAccount_ResetsCounters_SetsActive()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Locked,
            FailedLoginAttempts = 5,
            LockoutEndAt = DateTime.UtcNow.AddMinutes(10)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new UnlockAccountCommandHandler(uow.Object);

        var resp = await handler.Handle(new UnlockAccountCommand { Id = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.FailedLoginAttempts.Should().Be(0);
        account.LockoutEndAt.Should().BeNull();
        account.Status.Should().Be(AccountStatusEnum.Active);
    }

    [Fact]
    public async Task Unlock_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new UnlockAccountCommandHandler(uow.Object);

        var resp = await handler.Handle(new UnlockAccountCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class DeactivateAndDeleteMeCommandHandlerTests
{
    [Fact]
    public async Task Deactivate_SetsStatusInactive_RevokesAllTokens()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new DeactivateMeCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeactivateMeCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.Status.Should().Be(AccountStatusEnum.Inactive);
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
    }

    [Fact]
    public async Task DeleteMe_SoftDeletes_RevokesTokens()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new DeleteMeCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteMeCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accounts.Verify(r => r.DeleteAsync(account), Times.Once);
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
    }

    [Fact]
    public async Task DeactivateMe_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new DeactivateMeCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeactivateMeCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteMe_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new DeleteMeCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteMeCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
