using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.Persistence.Seeders;

public class AuthDataSeeder
{
    private static readonly Guid AdminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ManagerRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StaffRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AuthDataSeeder> _logger;

    public AuthDataSeeder(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IPasswordHasher passwordHasher,
        ILogger<AuthDataSeeder> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var roles = await SeedRolesAsync(cancellationToken);
        await SeedAdminAccountAsync(roles["ADMIN"], cancellationToken);
    }

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var seedRoles = new[]
        {
            CreateRole(AdminRoleId, "Admin", "ADMIN", "Quản trị viên hệ thống, có toàn quyền."),
            CreateRole(ManagerRoleId, "Manager", "MANAGER", "Quản lý vận hành và điều phối nhân sự."),
            CreateRole(StaffRoleId, "Staff", "STAFF", "Nhân viên vận hành hệ thống."),
            CreateRole(CustomerRoleId, "Customer", "CUSTOMER", "Khách hàng sử dụng dịch vụ.")
        };
        var seedRoleNames = seedRoles.Select(seed => seed.NormalizedName).ToList();

        var existingRoles = await _dbContext.Roles
            .IgnoreQueryFilters()
            .Where(role => seedRoleNames.Contains(role.NormalizedName)
                           || role.NormalizedName == "TECHNICIAN")
            .ToListAsync(cancellationToken);

        var technicianRole = existingRoles.FirstOrDefault(role => role.NormalizedName == "TECHNICIAN");
        if (technicianRole is not null && existingRoles.All(role => role.NormalizedName != "STAFF"))
        {
            technicianRole.Name = "Staff";
            technicianRole.NormalizedName = "STAFF";
            technicianRole.Description = "Nhân viên vận hành hệ thống.";
            technicianRole.Status = RoleStatusEnum.Active;
            technicianRole.IsSystemRole = true;
            technicianRole.IsDeleted = false;
            technicianRole.DeletedAt = null;
        }

        foreach (var seedRole in seedRoles)
        {
            var existingRole = existingRoles.FirstOrDefault(role => role.NormalizedName == seedRole.NormalizedName);
            if (existingRole is null)
            {
                _dbContext.Roles.Add(seedRole);
                existingRoles.Add(seedRole);
                continue;
            }

            existingRole.Name = seedRole.Name;
            existingRole.Description = seedRole.Description;
            existingRole.Status = RoleStatusEnum.Active;
            existingRole.IsSystemRole = true;
            existingRole.IsDeleted = false;
            existingRole.DeletedAt = null;
        }

        if (technicianRole is not null && technicianRole.NormalizedName == "TECHNICIAN")
        {
            technicianRole.IsDeleted = true;
            technicianRole.DeletedAt ??= DateTime.UtcNow;
            technicianRole.Status = RoleStatusEnum.Deprecated;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Roles
            .Where(role => seedRoleNames.Contains(role.NormalizedName))
            .ToDictionaryAsync(role => role.NormalizedName, cancellationToken);
    }

    private async Task SeedAdminAccountAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var adminEmail = GetSeedValue("ADMIN_EMAIL", "AdminSeed:Email", "admin@gmail.com").Trim().ToLowerInvariant();
        var adminPassword = GetSeedValue("ADMIN_PASSWORD", "AdminSeed:Password", "Admin123@");
        var now = DateTime.UtcNow;

        var adminAccount = await _dbContext.Users
            .IgnoreQueryFilters()
            .Include(account => account.AccountRoles)
            .FirstOrDefaultAsync(account => account.Email == adminEmail, cancellationToken);

        if (adminAccount is null)
        {
            adminAccount = new Account
            {
                Id = Guid.NewGuid(),
                Email = adminEmail,
                PasswordHash = _passwordHasher.Hash(adminPassword),
                FullName = "System Admin",
                EmailConfirmed = true,
                PhoneConfirmed = false,
                Status = AccountStatusEnum.Active,
                CreatedAt = now,
                IsDeleted = false
            };

            _dbContext.Users.Add(adminAccount);
            _logger.LogInformation("Seeded admin account {Email}.", adminEmail);
        }
        else
        {
            adminAccount.EmailConfirmed = true;
            adminAccount.Status = AccountStatusEnum.Active;
            adminAccount.IsDeleted = false;
            adminAccount.DeletedAt = null;
        }

        var adminAssignment = adminAccount.AccountRoles
            .FirstOrDefault(accountRole => accountRole.RoleId == adminRole.Id);

        if (adminAssignment is null)
        {
            adminAccount.AccountRoles.Add(new AccountRole
            {
                Id = Guid.NewGuid(),
                AccountId = adminAccount.Id,
                RoleId = adminRole.Id,
                AssignedAt = now,
                IsActive = true,
                CreatedAt = now,
                IsDeleted = false
            });
        }
        else
        {
            adminAssignment.IsActive = true;
            adminAssignment.IsDeleted = false;
            adminAssignment.DeletedAt = null;
            adminAssignment.ExpiredAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private string GetSeedValue(string environmentKey, string configurationKey, string fallbackValue)
    {
        return Environment.GetEnvironmentVariable(environmentKey)
               ?? _configuration[configurationKey]
               ?? fallbackValue;
    }

    private static Role CreateRole(Guid id, string name, string normalizedName, string description)
    {
        return new Role
        {
            Id = id,
            Name = name,
            NormalizedName = normalizedName,
            Description = description,
            Status = RoleStatusEnum.Active,
            IsSystemRole = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IsDeleted = false
        };
    }
}
