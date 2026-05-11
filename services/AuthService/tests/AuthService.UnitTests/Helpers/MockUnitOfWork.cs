using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Builder gom các mock repository thường dùng trong test handler.
/// Dùng MockQueryable.Moq để mock IQueryable cho GetAllAsync().
///
/// Lưu ý: AuditLogs mock được setup trên uow nhưng KHÔNG return trong tuple để tránh
/// break tests hiện hữu. Truy cập qua <c>uow.Object.AuditLogs</c> nếu cần verify.
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
            IEnumerable<AccountRole>? accountRoleSeed = null,
            IEnumerable<AuditLog>? auditLogSeed = null)
    {
        var accounts = new Mock<IGenericRepository<Account>>();
        accounts.Setup(r => r.GetAllAsync()).Returns((accountSeed ?? Array.Empty<Account>()).AsQueryable().BuildMock());

        var refreshTokens = new Mock<IGenericRepository<RefreshToken>>();
        refreshTokens.Setup(r => r.GetAllAsync()).Returns((tokenSeed ?? Array.Empty<RefreshToken>()).AsQueryable().BuildMock());

        var roles = new Mock<IGenericRepository<Role>>();
        roles.Setup(r => r.GetAllAsync()).Returns((roleSeed ?? Array.Empty<Role>()).AsQueryable().BuildMock());

        var accountRoles = new Mock<IGenericRepository<AccountRole>>();
        accountRoles.Setup(r => r.GetAllAsync()).Returns((accountRoleSeed ?? Array.Empty<AccountRole>()).AsQueryable().BuildMock());

        var auditLogs = new Mock<IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.GetAllAsync()).Returns((auditLogSeed ?? Array.Empty<AuditLog>()).AsQueryable().BuildMock());

        var loginAttempts = new Mock<IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.GetAllAsync()).Returns(Array.Empty<LoginAttempt>().AsQueryable().BuildMock());

        var permissions = new Mock<IGenericRepository<Permission>>();
        permissions.Setup(r => r.GetAllAsync()).Returns(Array.Empty<Permission>().AsQueryable().BuildMock());

        var rolePermissions = new Mock<IGenericRepository<RolePermission>>();
        rolePermissions.Setup(r => r.GetAllAsync()).Returns(Array.Empty<RolePermission>().AsQueryable().BuildMock());

        var uow = new Mock<IAuthUnitOfWork>();
        uow.SetupGet(u => u.Accounts).Returns(accounts.Object);
        uow.SetupGet(u => u.RefreshTokens).Returns(refreshTokens.Object);
        uow.SetupGet(u => u.Roles).Returns(roles.Object);
        uow.SetupGet(u => u.AccountRoles).Returns(accountRoles.Object);
        uow.SetupGet(u => u.AuditLogs).Returns(auditLogs.Object);
        uow.SetupGet(u => u.LoginAttempts).Returns(loginAttempts.Object);
        uow.SetupGet(u => u.Permissions).Returns(permissions.Object);
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermissions.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, accounts, refreshTokens, roles, accountRoles);
    }
}
