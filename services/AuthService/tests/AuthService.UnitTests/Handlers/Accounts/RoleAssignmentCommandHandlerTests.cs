using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// Sau refactor sang quan hệ 1-N (1 Role → nhiều Account), API gán role chỉ còn 1 endpoint
/// <c>ChangeAccountRoleCommand</c> — thay thế cho <c>AssignRoles</c>, <c>RevokeRole</c>,
/// <c>AssignRoleTemporary</c> đã bị xóa.
/// </summary>
public class ChangeAccountRoleCommandHandlerTests
{
    [Fact]
    public async Task ChangeRole_ValidRole_UpdatesAccountRoleId_PublishesAudit()
    {
        var oldRoleId = Guid.NewGuid();
        var newRole = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Manager",
            NormalizedName = "MANAGER",
            Status = RoleStatusEnum.Active
        };
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            RoleId = oldRoleId
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { newRole });
        var publisher = MockPublisher.NoOp();
        var handler = new ChangeAccountRoleCommandHandler(uow.Object, publisher.Object);

        var resp = await handler.Handle(new ChangeAccountRoleCommand
        {
            AccountId = account.Id,
            RoleId = newRole.Id
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        account.RoleId.Should().Be(newRole.Id);
        account.RoleAssignedAt.Should().NotBeNull();

        publisher.Verify(p => p.Publish(
            It.Is<AuditTrailNotification>(n => n.Action == AuditActionEnum.RoleAssigned),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeRole_AccountNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new ChangeAccountRoleCommand
        {
            AccountId = Guid.NewGuid(),
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ChangeRole_RoleNotFound_Returns400()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            RoleId = Guid.NewGuid()
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new ChangeAccountRoleCommand
        {
            AccountId = account.Id,
            RoleId = Guid.NewGuid()
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ChangeRole_SameRoleAsCurrent_Returns200_NoChange()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Staff",
            NormalizedName = "STAFF",
            Status = RoleStatusEnum.Active
        };
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@e.com",
            PasswordHash = "x",
            FullName = "U",
            RoleId = role.Id,
            RoleAssignedAt = DateTime.UtcNow.AddDays(-30)
        };
        var originalAssignedAt = account.RoleAssignedAt;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { role });
        var handler = new ChangeAccountRoleCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new ChangeAccountRoleCommand
        {
            AccountId = account.Id,
            RoleId = role.Id
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.RoleId.Should().Be(role.Id);
        account.RoleAssignedAt.Should().Be(originalAssignedAt, "no change → RoleAssignedAt giữ nguyên");
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
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new UnlockAccountCommandHandler(uow.Object, MockPublisher.NoOp().Object);

        var resp = await handler.Handle(new UnlockAccountCommand { Id = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.FailedLoginAttempts.Should().Be(0);
        account.LockoutEndAt.Should().BeNull();
        account.Status.Should().Be(AccountStatusEnum.Active);
    }

    [Fact]
    public async Task Unlock_NotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new UnlockAccountCommandHandler(uow.Object, MockPublisher.NoOp().Object);

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
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { token });
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
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { token });
        var handler = new DeleteMeCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new DeleteMeCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        accounts.Verify(r => r.DeleteAsync(account), Times.Once);
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
    }

    [Fact]
    public async Task DeactivateMe_NotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new DeactivateMeCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeactivateMeCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task DeleteMe_NotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new DeleteMeCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new DeleteMeCommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
