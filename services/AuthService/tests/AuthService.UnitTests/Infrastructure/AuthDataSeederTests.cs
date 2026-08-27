using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using AuthService.Infrastructure.Persistence.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Interfaces;
using SharedInfrastructure.Persistence.Interceptors;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Infrastructure;

/// <summary>
/// AuthDataSeeder tests bằng EF InMemory:
/// - Idempotent role + admin seed
/// - Legacy migration TECHNICIAN → STAFF (rename khi chưa có STAFF)
/// - Soft-delete TECHNICIAN khi STAFF cũng tồn tại
/// - Re-activate admin nếu bị disable
/// - Assign Admin role nếu admin chưa có role
/// </summary>
public class AuthDataSeederTests : IDisposable
{
    private readonly ApplicationDbContext _ctx;
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly IConfiguration _config;

    public AuthDataSeederTests()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(u => u.UserId).Returns((string?)null);
        var interceptor = new AuditableEntityInterceptor(currentUser.Object);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"seeder-{Guid.NewGuid()}")
            .Options;
        _ctx = new ApplicationDbContext(options, interceptor);

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminSeed:Email"] = "admin@test.local",
                ["AdminSeed:Password"] = "Test123!"
            })
            .Build();

        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"hashed:{p}");
    }

    public void Dispose() => _ctx.Dispose();

    // 02/08/2026 — seeder nay phát AccountSyncSnapshotEvent cho account nó vừa tạo, để
    // read-model account bên NotificationService không bỏ sót tài khoản seed.
    private readonly Mock<IMessageProducerService> _producer = new();

    private AuthDataSeeder NewSeeder() =>
        new(_ctx, _config, _hasher.Object, _producer.Object, NullLogger<AuthDataSeeder>.Instance);

    [Fact]
    public async Task SeedAsync_FreshDb_Creates4Roles_AndAdminAccountWithAdminRole()
    {
        await NewSeeder().SeedAsync();

        var roles = await _ctx.Roles.ToListAsync();
        roles.Select(r => r.NormalizedName).Should().BeEquivalentTo(new[] { "ADMIN", "MANAGER", "STAFF", "CUSTOMER" });
        roles.Should().OnlyContain(r => r.IsSystemRole && r.Status == RoleStatusEnum.Active);

        var admin = await _ctx.Users.FirstAsync();
        admin.Email.Should().Be("admin@test.local");
        admin.PasswordHash.Should().Be("hashed:Test123!");
        admin.EmailConfirmed.Should().BeTrue();
        admin.Status.Should().Be(AccountStatusEnum.Active);

        // 1-N refactor: admin role được set trực tiếp qua Account.RoleId.
        var adminRoleId = roles.First(r => r.NormalizedName == "ADMIN").Id;
        admin.RoleId.Should().Be(adminRoleId);
        admin.RoleAssignedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SeedAsync_RunTwice_Idempotent_NoDuplicates()
    {
        await NewSeeder().SeedAsync();
        await NewSeeder().SeedAsync();

        (await _ctx.Roles.CountAsync()).Should().Be(4);
        // Seeder tạo 1 admin + 5 sample accounts (1 Manager + 3 Staff tier 1/2/3 + 1 Customer).
        // Idempotent — lần 2 không tạo thêm.
        (await _ctx.Users.CountAsync()).Should().Be(6);
    }

    [Fact]
    public async Task SeedAsync_DemoEmployeeCodeOwnedByAnotherProfile_PreservesOwnerAndStarts()
    {
        var existingOwnerAccountId = Guid.NewGuid();
        _ctx.StaffProfiles.Add(new StaffProfile
        {
            Id = Guid.NewGuid(),
            AccountId = existingOwnerAccountId,
            EmployeeCode = "STF-T1-001",
            Department = "Production",
            MaxConcurrentTickets = 4,
            IsAvailable = true,
            SkillTier = StaffSkillTierEnum.Generalist,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddMonths(-1)
        });
        await _ctx.SaveChangesAsync();

        await NewSeeder().SeedAsync();

        var tier1AccountId = await _ctx.Users
            .Where(a => a.Email == "staff.tier1@solarbattery.local")
            .Select(a => a.Id)
            .SingleAsync();
        var allProfiles = await _ctx.StaffProfiles
            .IgnoreQueryFilters()
            .ToListAsync();

        allProfiles.Should().ContainSingle(p => p.EmployeeCode == "STF-T1-001");
        allProfiles.Single(p => p.EmployeeCode == "STF-T1-001").AccountId
            .Should().Be(existingOwnerAccountId);
        allProfiles.Should().NotContain(p => p.AccountId == tier1AccountId);
        allProfiles.Should().Contain(p => p.EmployeeCode == "STF-T2-001");
        allProfiles.Should().Contain(p => p.EmployeeCode == "STF-T3-001");
    }

    [Fact]
    public async Task SeedAsync_LegacyTechnicianRole_NoStaff_RenamesTechnicianToStaff()
    {
        // Pre-seed: legacy TECHNICIAN role tồn tại, không có STAFF.
        var techId = Guid.NewGuid();
        _ctx.Roles.Add(new Role
        {
            Id = techId,
            Name = "Technician",
            NormalizedName = "TECHNICIAN",
            Status = RoleStatusEnum.Active,
            IsSystemRole = true,
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        });
        await _ctx.SaveChangesAsync();

        await NewSeeder().SeedAsync();

        // TECHNICIAN entry đã được rename thành STAFF (giữ nguyên Id để không mồ côi FK).
        var renamed = await _ctx.Roles.FirstAsync(r => r.Id == techId);
        renamed.Name.Should().Be("Staff");
        renamed.NormalizedName.Should().Be("STAFF");
        renamed.Status.Should().Be(RoleStatusEnum.Active);
        renamed.IsDeleted.Should().BeFalse();

        // Không có TECHNICIAN nữa.
        (await _ctx.Roles.AnyAsync(r => r.NormalizedName == "TECHNICIAN")).Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_LegacyTechnician_AND_StaffBothExist_TechnicianMarkedDeletedDeprecated()
    {
        // Cả TECHNICIAN cũ và STAFF mới cùng tồn tại → TECHNICIAN bị soft-delete + Deprecated.
        var techId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        await _ctx.Roles.AddRangeAsync(
            new Role
            {
                Id = techId,
                Name = "Technician",
                NormalizedName = "TECHNICIAN",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = DateTime.UtcNow.AddYears(-1)
            },
            new Role
            {
                Id = staffId,
                Name = "Staff",
                NormalizedName = "STAFF",
                Status = RoleStatusEnum.Active,
                IsSystemRole = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-1)
            });
        await _ctx.SaveChangesAsync();

        await NewSeeder().SeedAsync();

        var tech = await _ctx.Roles.IgnoreQueryFilters().FirstAsync(r => r.Id == techId);
        tech.IsDeleted.Should().BeTrue();
        tech.DeletedAt.Should().NotBeNull();
        tech.Status.Should().Be(RoleStatusEnum.Deprecated);

        var staff = await _ctx.Roles.FirstAsync(r => r.Id == staffId);
        staff.NormalizedName.Should().Be("STAFF");
        staff.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SeedAsync_AdminWasDisabled_ReactivatesAndUnDeletes()
    {
        // Pre-seed: admin tồn tại nhưng bị disable + soft-delete.
        var adminId = Guid.NewGuid();
        _ctx.Users.Add(new Account
        {
            Id = adminId,
            Email = "admin@test.local",
            FullName = "Old Admin",
            PasswordHash = "old-hash",
            Status = AccountStatusEnum.Inactive,
            EmailConfirmed = false,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow.AddYears(-1)
        });
        await _ctx.SaveChangesAsync();

        await NewSeeder().SeedAsync();

        var admin = await _ctx.Users.IgnoreQueryFilters()
            .FirstAsync(a => a.Id == adminId);
        admin.Status.Should().Be(AccountStatusEnum.Active);
        admin.EmailConfirmed.Should().BeTrue();
        admin.IsDeleted.Should().BeFalse();
        admin.DeletedAt.Should().BeNull();
        admin.PasswordHash.Should().Be("old-hash", "không re-hash mật khẩu của admin đã tồn tại");

        // 1-N refactor: admin phải có RoleId = AdminRole.
        var adminRoleId = await _ctx.Roles.Where(r => r.NormalizedName == "ADMIN").Select(r => r.Id).FirstAsync();
        admin.RoleId.Should().Be(adminRoleId);
    }

    [Fact]
    public async Task SeedAsync_AdminWithWrongRoleId_ResetsBackToAdminRole()
    {
        // 1-N refactor: nếu admin bị manual đổi RoleId trên DB → seeder phải reset về Admin role.
        await NewSeeder().SeedAsync();
        var admin = await _ctx.Users.FirstAsync();
        var wrongRoleId = Guid.NewGuid();
        admin.RoleId = wrongRoleId;
        await _ctx.SaveChangesAsync();

        await NewSeeder().SeedAsync();

        var adminRoleId = await _ctx.Roles.Where(r => r.NormalizedName == "ADMIN").Select(r => r.Id).FirstAsync();
        var refreshed = await _ctx.Users.FirstAsync();
        refreshed.RoleId.Should().Be(adminRoleId);
    }

    [Fact]
    public async Task SeedAsync_AdminEmailFromEnvVar_TakesPrecedenceOverConfig()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "from-env@x.com");
        Environment.SetEnvironmentVariable("ADMIN_PASSWORD", "EnvPass1!");
        try
        {
            await NewSeeder().SeedAsync();

            var admin = await _ctx.Users.FirstAsync();
            admin.Email.Should().Be("from-env@x.com", "env var phải override config");
            admin.PasswordHash.Should().Be("hashed:EnvPass1!");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAIL", null);
            Environment.SetEnvironmentVariable("ADMIN_PASSWORD", null);
        }
    }

    [Fact]
    public async Task SeedAsync_AdminEmail_NormalizedToLowerInvariant()
    {
        Environment.SetEnvironmentVariable("ADMIN_EMAIL", "  ADMIN@MIXED.Case  ");
        try
        {
            await NewSeeder().SeedAsync();
            (await _ctx.Users.FirstAsync()).Email.Should().Be("admin@mixed.case");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ADMIN_EMAIL", null);
        }
    }
}
