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
    /// động chỉ tạo rác. Muốn đối soát chủ động thì dùng <c>POST /api/admin/accounts/resync</c>.
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
        var roles = await SeedRolesAsync(cancellationToken);
        var permissionsByCode = await SeedPermissionsAsync(cancellationToken);
        await SeedRolePermissionsAsync(roles, permissionsByCode, cancellationToken);
        var adminAccount = await SeedAdminAccountAsync(roles["ADMIN"], cancellationToken);
        var sampleAccounts = await SeedSampleAccountsAsync(roles, cancellationToken);
        await SeedAccountProfilesAsync(adminAccount, sampleAccounts, cancellationToken);
        await SeedStaffProfilesAsync(sampleAccounts, cancellationToken);
        await SeedLoginAttemptsAsync(adminAccount, sampleAccounts, cancellationToken);
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
                Reason: "Seed"), cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded {Count} account snapshot(s) vào outbox để các service khác dựng read-model.",
            _createdAccounts.Count);
    }

    private async Task SeedLoginAttemptsAsync(
        Account adminAccount,
        List<Account> sampleAccounts,
        CancellationToken cancellationToken)
    {
        var hasAny = await _dbContext.LoginAttempts.AnyAsync(cancellationToken);
        if (hasAny)
            return;

        var now = DateTime.UtcNow;
        var attempts = new List<LoginAttempt>();

        // Admin: 3 success login từ 3 thiết bị / IP khác nhau
        attempts.Add(NewAttempt(adminAccount.Id, adminAccount.Email, LoginAttemptResult.Success,
            "Password", "203.0.113.10", "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) Safari/605.1.15", "mac-pro-01", now.AddHours(-1)));
        attempts.Add(NewAttempt(adminAccount.Id, adminAccount.Email, LoginAttemptResult.Success,
            "Password", "203.0.113.11", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0", "win-desk-01", now.AddDays(-1)));
        attempts.Add(NewAttempt(adminAccount.Id, adminAccount.Email, LoginAttemptResult.WrongPassword,
            "Password", "203.0.113.99", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/124.0", "unknown-01", now.AddHours(-3),
            "Wrong password — 4 attempts remaining"));

        // Sample accounts: 2 success + 1 fail mỗi account
        foreach (var account in sampleAccounts)
        {
            attempts.Add(NewAttempt(account.Id, account.Email, LoginAttemptResult.Success,
                "Password", "192.168.1.50", "ExpoMobile/1.0 (Android 14)", "expo-android-001", now.AddHours(-2)));
            attempts.Add(NewAttempt(account.Id, account.Email, LoginAttemptResult.Success,
                "Google", "192.168.1.51", "Mozilla/5.0 Chrome/124.0", "web-chrome-001", now.AddDays(-2)));
            attempts.Add(NewAttempt(account.Id, account.Email, LoginAttemptResult.WrongPassword,
                "Password", "10.0.0.5", "ExpoMobile/1.0 (iOS 17.4)", "expo-ios-001", now.AddHours(-12),
                "Wrong password"));
        }

        // 1 attempt cho email không tồn tại (AccountNotFound)
        attempts.Add(NewAttempt(null, "ghost@solarbattery.local", LoginAttemptResult.AccountNotFound,
            "Password", "198.51.100.1", "curl/8.6.0", null, now.AddHours(-6),
            "Email does not exist"));

        _dbContext.LoginAttempts.AddRange(attempts);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded {Count} login attempts.", attempts.Count);
    }

    private static LoginAttempt NewAttempt(
        Guid? accountId,
        string email,
        LoginAttemptResult result,
        string method,
        string ip,
        string userAgent,
        string? deviceId,
        DateTime at,
        string? note = null)
    {
        return new LoginAttempt
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            AttemptedEmail = email,
            Result = result,
            Method = method,
            IpAddress = ip,
            UserAgent = userAgent,
            DeviceId = deviceId,
            Note = note,
            CreatedAt = at
        };
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
        var adminEmail = GetSeedValue("ADMIN_EMAIL", "AdminSeed:Email", "admin@gmail.com").Trim().ToLowerInvariant();
        var adminPassword = GetSeedValue("ADMIN_PASSWORD", "AdminSeed:Password", "Admin123@");
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
                FullName = "System Admin",
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

    private async Task<List<Account>> SeedSampleAccountsAsync(
        IReadOnlyDictionary<string, Role> roles,
        CancellationToken cancellationToken)
    {
        var defaultPasswordHash = _passwordHasher.Hash("Password123@");
        var now = DateTime.UtcNow;

        var samples = new[]
        {
            ("manager.demo@solarbattery.local", "Demo Manager", "MANAGER"),
            ("staff.tier1@solarbattery.local", "Staff Tier1 Generalist", "STAFF"),
            ("staff.tier2@solarbattery.local", "Staff Tier2 Specialist", "STAFF"),
            ("staff.tier3@solarbattery.local", "Staff Tier3 Senior", "STAFF"),
            ("customer.demo@solarbattery.local", "Demo Customer", "CUSTOMER")
        };

        var emails = samples.Select(s => s.Item1).ToList();
        var existing = await _dbContext.Users
            .IgnoreQueryFilters()
            .Where(u => emails.Contains(u.Email))
            .ToDictionaryAsync(u => u.Email, cancellationToken);

        var added = new List<Account>();
        foreach (var (email, fullName, roleKey) in samples)
        {
            if (existing.TryGetValue(email, out var current))
            {
                added.Add(current);
                continue;
            }
            if (!roles.TryGetValue(roleKey, out var role))
                continue;

            var account = new Account
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = defaultPasswordHash,
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
            added.Add(account);
        }

        if (added.Any(a => _dbContext.Entry(a).State == EntityState.Added))
            await _dbContext.SaveChangesAsync(cancellationToken);

        return added;
    }

    private async Task SeedAccountProfilesAsync(
        Account adminAccount,
        List<Account> sampleAccounts,
        CancellationToken cancellationToken)
    {
        var accountIds = new List<Guid> { adminAccount.Id };
        accountIds.AddRange(sampleAccounts.Select(a => a.Id));

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

    private async Task SeedStaffProfilesAsync(List<Account> sampleAccounts, CancellationToken cancellationToken)
    {
        var staffMap = new (string Email, string EmployeeCode, StaffSkillTierEnum Tier, int MaxTickets, string[] Skills)[]
        {
            ("staff.tier1@solarbattery.local", "STF-T1-001", StaffSkillTierEnum.Generalist, 10, new[] { "general" }),
            ("staff.tier2@solarbattery.local", "STF-T2-001", StaffSkillTierEnum.ModuleSpecialist, 8, new[] { "battery", "charging" }),
            ("staff.tier3@solarbattery.local", "STF-T3-001", StaffSkillTierEnum.SeniorSpecialist, 5, new[] { "battery", "firmware", "incident" })
        };

        var emailToAccount = sampleAccounts.ToDictionary(a => a.Email, a => a);
        var staffAccountIds = staffMap
            .Where(m => emailToAccount.ContainsKey(m.Email))
            .Select(m => emailToAccount[m.Email].Id)
            .ToList();

        var existingProfileIds = await _dbContext.StaffProfiles
            .IgnoreQueryFilters()
            .Where(p => staffAccountIds.Contains(p.AccountId))
            .Select(p => p.AccountId)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var newProfiles = new List<StaffProfile>();
        var newSkills = new List<StaffSkill>();

        foreach (var entry in staffMap)
        {
            if (!emailToAccount.TryGetValue(entry.Email, out var account))
                continue;
            if (existingProfileIds.Contains(account.Id))
                continue;

            newProfiles.Add(new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                EmployeeCode = entry.EmployeeCode,
                Department = "Operations",
                MaxConcurrentTickets = entry.MaxTickets,
                IsAvailable = true,
                SkillTier = entry.Tier,
                CreatedAt = now
            });

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
