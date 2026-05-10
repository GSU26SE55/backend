using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Builder gom các mock repository thường dùng trong test handler.
/// Dùng MockQueryable.Moq để mock IQueryable cho GetAllAsync().
/// </summary>
public static class MockUnitOfWork
{
    public static (Mock<IAuthUnitOfWork> uow,
                   Mock<IGenericRepository<Account>> accounts,
                   Mock<IGenericRepository<RefreshToken>> refreshTokens,
                   Mock<IGenericRepository<Role>> roles,
                   Mock<IGenericRepository<AccountRole>> accountRoles)
        Build(
            IEnumerable<Account>? accountSeed = null,
            IEnumerable<RefreshToken>? tokenSeed = null,
            IEnumerable<Role>? roleSeed = null,
            IEnumerable<AccountRole>? accountRoleSeed = null)
    {
        var accounts = new Mock<IGenericRepository<Account>>();
        accounts.Setup(r => r.GetAllAsync()).Returns((accountSeed ?? Array.Empty<Account>()).AsQueryable().BuildMock());

        var refreshTokens = new Mock<IGenericRepository<RefreshToken>>();
        refreshTokens.Setup(r => r.GetAllAsync()).Returns((tokenSeed ?? Array.Empty<RefreshToken>()).AsQueryable().BuildMock());

        var roles = new Mock<IGenericRepository<Role>>();
        roles.Setup(r => r.GetAllAsync()).Returns((roleSeed ?? Array.Empty<Role>()).AsQueryable().BuildMock());

        var accountRoles = new Mock<IGenericRepository<AccountRole>>();
        accountRoles.Setup(r => r.GetAllAsync()).Returns((accountRoleSeed ?? Array.Empty<AccountRole>()).AsQueryable().BuildMock());

        var uow = new Mock<IAuthUnitOfWork>();
        uow.SetupGet(u => u.Accounts).Returns(accounts.Object);
        uow.SetupGet(u => u.RefreshTokens).Returns(refreshTokens.Object);
        uow.SetupGet(u => u.Roles).Returns(roles.Object);
        uow.SetupGet(u => u.AccountRoles).Returns(accountRoles.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, accounts, refreshTokens, roles, accountRoles);
    }
}
