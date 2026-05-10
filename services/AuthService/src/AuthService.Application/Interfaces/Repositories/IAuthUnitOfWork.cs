using AuthService.Domain.Entities;
using SharedKernels.Interfaces;

namespace AuthService.Application.Interfaces.Repositories;

public interface IAuthUnitOfWork : IUnitOfWork
{
    IGenericRepository<Account> Accounts { get; }
    IGenericRepository<RefreshToken> RefreshTokens { get; }
    IGenericRepository<Role> Roles { get; }
    IGenericRepository<AccountRole> AccountRoles { get; }
}
