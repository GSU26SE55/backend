using AuthService.Application.Interfaces.Repositories;
using AuthService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Builder gom các mock repository thường dùng trong test handler.
/// Dùng MockQueryable.Moq để mock IQueryable cho GetAllAsync().
///
/// Lưu ý: Sau refactor sang quan hệ Role 1-N (mỗi Account chỉ có 1 Role) —
/// repository <c>AccountRoles</c> đã bị bỏ; role của account được set trực tiếp qua <c>Account.RoleId</c>.
///
/// AuditLogs mock được setup trên uow nhưng KHÔNG return trong tuple để tránh
/// break tests hiện hữu. Truy cập qua <c>uow.Object.AuditLogs</c> nếu cần verify.
/// </summary>
public static class MockUnitOfWork
{
    public static (Mock<IAuthUnitOfWork> uow,
                   Mock<IGenericRepository<Account>> accounts,
                   Mock<IGenericRepository<RefreshToken>> refreshTokens,
                   Mock<IGenericRepository<Role>> roles)
        Build(
            IEnumerable<Account>? accountSeed = null,
            IEnumerable<RefreshToken>? tokenSeed = null,
            IEnumerable<Role>? roleSeed = null,
            IEnumerable<AuditLog>? auditLogSeed = null,
            IEnumerable<AccountProfile>? accountProfileSeed = null,
            IEnumerable<StaffProfile>? staffProfileSeed = null,
            IEnumerable<StaffSkill>? staffSkillSeed = null,
            IEnumerable<Permission>? permissionSeed = null,
            IEnumerable<RolePermission>? rolePermissionSeed = null)
    {
        var accounts = new Mock<IGenericRepository<Account>>();
        accounts.Setup(r => r.GetAllAsync()).Returns((accountSeed ?? Array.Empty<Account>()).AsQueryable().BuildMock());

        var refreshTokens = new Mock<IGenericRepository<RefreshToken>>();
        refreshTokens.Setup(r => r.GetAllAsync()).Returns((tokenSeed ?? Array.Empty<RefreshToken>()).AsQueryable().BuildMock());

        var roles = new Mock<IGenericRepository<Role>>();
        roles.Setup(r => r.GetAllAsync()).Returns((roleSeed ?? Array.Empty<Role>()).AsQueryable().BuildMock());

        var auditLogs = new Mock<IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.GetAllAsync()).Returns((auditLogSeed ?? Array.Empty<AuditLog>()).AsQueryable().BuildMock());

        var loginAttempts = new Mock<IGenericRepository<LoginAttempt>>();
        loginAttempts.Setup(r => r.GetAllAsync()).Returns(Array.Empty<LoginAttempt>().AsQueryable().BuildMock());

        var permissions = new Mock<IGenericRepository<Permission>>();
        permissions.Setup(r => r.GetAllAsync()).Returns((permissionSeed ?? Array.Empty<Permission>()).AsQueryable().BuildMock());

        var rolePermissions = new Mock<IGenericRepository<RolePermission>>();
        rolePermissions.Setup(r => r.GetAllAsync()).Returns((rolePermissionSeed ?? Array.Empty<RolePermission>()).AsQueryable().BuildMock());

        var accountProfiles = new Mock<IGenericRepository<AccountProfile>>();
        accountProfiles.Setup(r => r.GetAllAsync()).Returns((accountProfileSeed ?? Array.Empty<AccountProfile>()).AsQueryable().BuildMock());

        var staffProfiles = new Mock<IGenericRepository<StaffProfile>>();
        staffProfiles.Setup(r => r.GetAllAsync()).Returns((staffProfileSeed ?? Array.Empty<StaffProfile>()).AsQueryable().BuildMock());

        var staffSkills = new Mock<IGenericRepository<StaffSkill>>();
        staffSkills.Setup(r => r.GetAllAsync()).Returns((staffSkillSeed ?? Array.Empty<StaffSkill>()).AsQueryable().BuildMock());

        var uow = new Mock<IAuthUnitOfWork>();
        uow.SetupGet(u => u.Accounts).Returns(accounts.Object);
        uow.SetupGet(u => u.RefreshTokens).Returns(refreshTokens.Object);
        uow.SetupGet(u => u.Roles).Returns(roles.Object);
        uow.SetupGet(u => u.AuditLogs).Returns(auditLogs.Object);
        uow.SetupGet(u => u.LoginAttempts).Returns(loginAttempts.Object);
        uow.SetupGet(u => u.Permissions).Returns(permissions.Object);
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermissions.Object);
        uow.SetupGet(u => u.AccountProfiles).Returns(accountProfiles.Object);
        uow.SetupGet(u => u.StaffProfiles).Returns(staffProfiles.Object);
        uow.SetupGet(u => u.StaffSkills).Returns(staffSkills.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, accounts, refreshTokens, roles);
    }
}
