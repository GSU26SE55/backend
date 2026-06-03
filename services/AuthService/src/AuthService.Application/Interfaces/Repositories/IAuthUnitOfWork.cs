using AuthService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuthService.Application.Interfaces.Repositories;

public interface IAuthUnitOfWork : IUnitOfWork
{
    IGenericRepository<Account> Accounts { get; }
    IGenericRepository<RefreshToken> RefreshTokens { get; }
    IGenericRepository<Role> Roles { get; }
    IGenericRepository<AuditLog> AuditLogs { get; }
    IGenericRepository<LoginAttempt> LoginAttempts { get; }
    IGenericRepository<Permission> Permissions { get; }
    IGenericRepository<RolePermission> RolePermissions { get; }
    IGenericRepository<AccountProfile> AccountProfiles { get; }
    IGenericRepository<StaffProfile> StaffProfiles { get; }
    IGenericRepository<StaffSkill> StaffSkills { get; }
}
