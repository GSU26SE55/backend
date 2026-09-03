using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.Infrastructure.Persistence.Seeders;

public class AuthDataSeeder
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMessageProducerService _messageProducer;
    private readonly ILogger<AuthDataSeeder> _logger;

    /// <summary>
    /// Account do CHÍNH lượt seed này tạo ra, kèm tên role. Chỉ những account ở đây mới cần phát
    /// snapshot: account đã tồn tại từ lượt trước thì lượt trước đã phát rồi, phát lại mỗi lần khởi
    /// động chỉ tạo rác. Worker reconciliation sẽ tự đối soát định kỳ; khi cần chạy ngay có thể
    /// dùng <c>POST /api/admin/accounts/resync</c>.
    /// </summary>
    private readonly List<(Account Account, string RoleName)> _createdAccounts = new();

    public AuthDataSeeder(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        IPasswordHasher passwordHasher,
        IMessageProducerService messageProducer,
        ILogger<AuthDataSeeder> logger)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _messageProducer = messageProducer;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _createdAccounts.Clear();

        var roles = await SeedRolesAsync(cancellationToken);
        var permissionsByCode = await SeedPermissionsAsync(cancellationToken);
        await SeedRolePermissionsAsync(roles, permissionsByCode, cancellationToken);
        var adminAccount = await SeedAdminAccountAsync(roles["ADMIN"], cancellationToken);
        var operationalAccounts = await SeedOperationalAccountsAsync(roles, cancellationToken);
        await SeedAccountProfilesAsync(adminAccount, operationalAccounts, cancellationToken);
        await SeedStaffProfilesAsync(operationalAccounts, cancellationToken);
        await PublishSeededAccountSnapshotsAsync(cancellationToken);
    }

    /// <summary>
    /// 02/08/2026 — Phát <see cref="AccountSyncSnapshotEvent"/> cho account vừa được seed tạo.
    ///
    /// Seeder ghi thẳng <see cref="ApplicationDbContext"/>, không đi qua CQRS handler nào, nên
    /// trước bản vá này KHÔNG có event tích hợp nào được phát cho account seed. Hậu quả đo được
    /// trên môi trường đang chạy: 6 account seed (1 Admin, 1 Manager, 3 Staff, 1 Customer) không hề
    /// tồn tại trong read-model của NotificationService, khiến
    /// <c>GetActiveByRoleAsync("Admin")</c> trả rỗng và mọi thông báo gửi cho nhóm Admin bị bỏ qua
    /// im lặng.
    ///
    /// Đi qua Outbox nên an toàn kể cả khi RabbitMQ chưa sẵn sàng lúc service khởi động: event nằm
    /// trong <c>outbox_messages</c> cùng transaction, <c>OutboxRelayBackgroundService</c> phát sau.
    /// </summary>
    private async Task PublishSeededAccountSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (_createdAccounts.Count == 0)
            return;

        // Một mốc chung cho cả lượt seed — consumer dùng mốc này để loại snapshot về trễ.
        var snapshotAtUtc = DateTime.UtcNow;

        foreach (var (account, roleName) in _createdAccounts)
        {
            await _messageProducer.PublishAsync(new AccountSyncSnapshotEvent(
                account.Id,
                account.Email,
                account.FullName,
                account.PhoneNumber,
                roleName,
                IsActive: account.Status.IsNotifiable(),
                IsDeleted: false,
                SnapshotAtUtc: snapshotAtUtc,
                Reason: "Bootstrap",
                AccountStatus: (int)account.Status), cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Queued {Count} bootstrap account snapshot(s) for downstream read models.",
            _createdAccounts.Count);
    }

    private async Task<Dictionary<string, Permission>> SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Permissions
            .IgnoreQueryFilters()
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var item in PermissionSeed.All)
        {
            if (existing.TryGetValue(item.Code, out var current))
            {
                current.Module = item.Module;
                current.Description = item.Description;
                current.IsSystemPermission = true;
                current.IsDeleted = false;
                current.DeletedAt = null;
                continue;
            }

            var entity = PermissionSeed.BuildEntity(item, Guid.NewGuid());
            _dbContext.Permissions.Add(entity);
            existing[item.Code] = entity;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private async Task SeedRolePermissionsAsync(
        IReadOnlyDictionary<string, Role> roles,
        IReadOnlyDictionary<string, Permission> permissionsByCode,
        CancellationToken cancellationToken)
    {
        var roleIds = roles.Values.Select(r => r.Id).ToList();
        var existing = await _dbContext.RolePermissions
            .IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;

        foreach (var pair in PermissionSeed.RoleDefaults)
        {
            if (!roles.TryGetValue(pair.Key, out var role))
                continue;

            foreach (var code in pair.Value)
            {
                if (!permissionsByCode.TryGetValue(code, out var permission))
                {
                    _logger.LogWarning("Permission {Code} không tồn tại khi seed role {Role}.", code, pair.Key);
                    continue;
                }

                var current = existing.FirstOrDefault(rp =>
                    rp.RoleId == role.Id && rp.PermissionId == permission.Id);

                if (current is null)
                {
                    _dbContext.RolePermissions.Add(new RolePermission
                    {
                        Id = Guid.NewGuid(),
                        RoleId = role.Id,
                        PermissionId = permission.Id,
                        AssignedAt = now,
                        CreatedAt = now,
                        IsDeleted = false
                    });
                }
                else if (current.IsDeleted)
                {
                    current.IsDeleted = false;
                    current.DeletedAt = null;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var seedRoles = new[]
        {
            CreateRole(Guid.NewGuid(), "Admin", "ADMIN", "System administrator with full access."),
            CreateRole(Guid.NewGuid(), "Manager", "MANAGER", "Manages operations and coordinates staff."),
            CreateRole(Guid.NewGuid(), "Staff", "STAFF", "System operations staff."),
            CreateRole(Guid.NewGuid(), "Customer", "CUSTOMER", "Customer using the service.")
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
            technicianRole.Description = "System operations staff.";
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

    private async Task<Account> SeedAdminAccountAsync(Role adminRole, CancellationToken cancellationToken)
    {
        var adminEmail = GetSeedValue("ADMIN_EMAIL", "AdminSeed:Email", "admin@solars.io.vn").Trim().ToLowerInvariant();
        var adminPassword = GetSeedValue("ADMIN_PASSWORD", "AdminSeed:Password", "Pasword123@");
        var now = DateTime.UtcNow;

        var adminAccount = await _dbContext.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(account => account.Email == adminEmail, cancellationToken);

        if (adminAccount is null)
        {
            adminAccount = new Account
            {
                Id = Guid.NewGuid(),
                Email = adminEmail,
                PasswordHash = _passwordHasher.Hash(adminPassword),
                FullName = "Alex",
                EmailConfirmed = true,
                PhoneConfirmed = false,
                Status = AccountStatusEnum.Active,
                RoleId = adminRole.Id,
                RoleAssignedAt = now,
                CreatedAt = now,
                IsDeleted = false
            };

            _dbContext.Users.Add(adminAccount);
            _createdAccounts.Add((adminAccount, adminRole.Name));
            _logger.LogInformation("Seeded admin account {Email}.", adminEmail);
        }
        else
        {
            adminAccount.FullName = "Alex";
            adminAccount.EmailConfirmed = true;
            adminAccount.Status = AccountStatusEnum.Active;
            adminAccount.IsDeleted = false;
            adminAccount.DeletedAt = null;

            // Ensure admin có đúng AdminRole — sửa nếu bị đổi tay trên DB.
            if (adminAccount.RoleId != adminRole.Id)
            {
                adminAccount.RoleId = adminRole.Id;
                adminAccount.RoleAssignedAt = now;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return adminAccount;
    }

    private async Task<List<Account>> SeedOperationalAccountsAsync(
        IReadOnlyDictionary<string, Role> roles,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var definitions = new[]
        {
            ("manager@solars.io.vn", "Bùi Phước Thắng", "MANAGER"),
            ("staff1@solars.io.vn", "Trần Minh Trí", "STAFF"),
            ("staff2@solars.io.vn", "Nguyễn Phúc Duy", "STAFF"),
            ("staff3@solars.io.vn", "Mai Hồng Thái", "STAFF"),
            ("dienhoanguyen11@gmail.com", "Nguyễn Nhật Minh", "CUSTOMER")
        };

        var emails = definitions.Select(s => s.Item1).ToList();
        var existing = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => emails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, cancellationToken);

        var accounts = new List<Account>();
        foreach (var (email, fullName, roleKey) in definitions)
        {
            if (!roles.TryGetValue(roleKey, out var role))
                continue;

            if (existing.TryGetValue(email, out var current))
            {
                current.FullName = fullName;
                current.EmailConfirmed = true;
                current.Status = AccountStatusEnum.Active;
                current.IsDeleted = false;
                current.DeletedAt = null;
                if (current.RoleId != role.Id)
                {
                    current.RoleId = role.Id;
                    current.RoleAssignedAt = now;
                }

                accounts.Add(current);
                continue;
            }

            var account = new Account
            {
                Id = Guid.NewGuid(),
                Email = email,
                // Hash separately so every account receives an independent salt even though the
                // requested initial password is shared.
                PasswordHash = _passwordHasher.Hash("Password123@"),
                FullName = fullName,
                EmailConfirmed = true,
                PhoneConfirmed = false,
                Status = AccountStatusEnum.Active,
                RoleId = role.Id,
                RoleAssignedAt = now,
                CreatedAt = now,
                IsDeleted = false
            };
            _dbContext.Users.Add(account);
            _createdAccounts.Add((account, role.Name));
            accounts.Add(account);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return accounts;
    }

    private async Task SeedAccountProfilesAsync(
        Account adminAccount,
        List<Account> operationalAccounts,
        CancellationToken cancellationToken)
    {
        var accountIds = new List<Guid> { adminAccount.Id };
        accountIds.AddRange(operationalAccounts.Select(a => a.Id));

        var existing = await _dbContext.AccountProfiles
            .IgnoreQueryFilters()
            .Where(p => accountIds.Contains(p.AccountId))
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var toAdd = new List<AccountProfile>();

        foreach (var id in accountIds)
        {
            if (existing.Contains(id))
                continue;
            toAdd.Add(new AccountProfile
            {
                Id = Guid.NewGuid(),
                AccountId = id,
                AvatarSource = AvatarSourceEnum.None,
                TimeZone = "Asia/Ho_Chi_Minh",
                CreatedAt = now
            });
        }

        if (toAdd.Count == 0)
            return;

        _dbContext.AccountProfiles.AddRange(toAdd);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedStaffProfilesAsync(List<Account> operationalAccounts, CancellationToken cancellationToken)
    {
        var staffMap = new (string Email, string EmployeeCode, StaffSkillTierEnum Tier, int MaxTickets, string[] Skills)[]
        {
            ("staff1@solars.io.vn", "STF-001", StaffSkillTierEnum.Generalist, 10, new[] { "general" }),
            ("staff2@solars.io.vn", "STF-002", StaffSkillTierEnum.ModuleSpecialist, 8, new[] { "battery", "charging" }),
            ("staff3@solars.io.vn", "STF-003", StaffSkillTierEnum.SeniorSpecialist, 5, new[] { "battery", "firmware", "incident" })
        };

        var emailToAccount = operationalAccounts.ToDictionary(a => a.Email, a => a);
        var staffAccountIds = staffMap
            .Where(m => emailToAccount.ContainsKey(m.Email))
            .Select(m => emailToAccount[m.Email].Id)
            .ToList();
        var seedEmployeeCodes = staffMap
            .Select(m => m.EmployeeCode)
            .ToList();

        // EmployeeCode is unique across every row, including soft-deleted profiles. Production
        // data may legitimately retain one of these employee codes on another account (for
        // example after an account was recreated or data was repaired manually). Looking up only
        // by the current seed AccountIds would then attempt a duplicate insert and prevent the
        // entire AuthService from starting. Preserve the existing owner and skip only the
        // conflicting bootstrap profile instead of mutating production data during startup.
        var existingProfiles = await _dbContext.StaffProfiles
            .IgnoreQueryFilters()
            .Where(p => staffAccountIds.Contains(p.AccountId) ||
                        (p.EmployeeCode != null && seedEmployeeCodes.Contains(p.EmployeeCode)))
            .Select(p => new { p.AccountId, p.EmployeeCode })
            .ToListAsync(cancellationToken);
        var existingProfileAccountIds = existingProfiles
            .Select(p => p.AccountId)
            .ToHashSet();
        var occupiedEmployeeCodes = existingProfiles
            .Where(p => !string.IsNullOrWhiteSpace(p.EmployeeCode))
            .Select(p => p.EmployeeCode!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        var newProfiles = new List<StaffProfile>();
        var newSkills = new List<StaffSkill>();

        foreach (var entry in staffMap)
        {
            if (!emailToAccount.TryGetValue(entry.Email, out var account))
                continue;
            if (existingProfileAccountIds.Contains(account.Id))
                continue;
            if (!occupiedEmployeeCodes.Add(entry.EmployeeCode))
            {
                _logger.LogWarning(
                    "Skipping bootstrap StaffProfile for AccountId {AccountId}: EmployeeCode {EmployeeCode} is already assigned to another profile.",
                    account.Id,
                    entry.EmployeeCode);
                continue;
            }

            newProfiles.Add(new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                EmployeeCode = entry.EmployeeCode,
                Department = "Technical Operations",
                MaxConcurrentTickets = entry.MaxTickets,
                IsAvailable = true,
                SkillTier = entry.Tier,
                CreatedAt = now
            });
            existingProfileAccountIds.Add(account.Id);

            foreach (var skillCode in entry.Skills)
            {
                newSkills.Add(new StaffSkill
                {
                    Id = Guid.NewGuid(),
                    StaffAccountId = account.Id,
                    SkillCode = skillCode,
                    SkillLevel = (int)entry.Tier,
                    CreatedAt = now
                });
            }
        }

        if (newProfiles.Count == 0)
            return;

        _dbContext.StaffProfiles.AddRange(newProfiles);
        _dbContext.StaffSkills.AddRange(newSkills);
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
