using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence.Seeders;
using AuthService.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AccountEntity = AuthService.Domain.Entities.Account;

namespace AuthService.IntegrationTests.Infrastructure;

[Collection("Integration")]
public class AuthDataSeederIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;

    public AuthDataSeederIntegrationTests(AuthApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        await _factory.ResetDatabaseAsync();
        _factory.Producer.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedAsync_SoftDeletedProfileOwnsBootstrapEmployeeCode_DoesNotViolateUniqueIndex()
    {
        var existingOwner = new AccountEntity
        {
            Id = Guid.NewGuid(),
            Email = "existing.staff@production.local",
            FullName = "Existing production staff",
            PasswordHash = "not-used",
            Status = AccountStatusEnum.Active,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow.AddMonths(-1)
        };

        await using (var arrangeDb = _factory.CreateDbContext())
        {
            arrangeDb.Users.Add(existingOwner);
            arrangeDb.StaffProfiles.Add(new StaffProfile
            {
                Id = Guid.NewGuid(),
                AccountId = existingOwner.Id,
                EmployeeCode = "STF-001",
                Department = "Production",
                MaxConcurrentTickets = 4,
                IsAvailable = false,
                SkillTier = StaffSkillTierEnum.Generalist,
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow.AddMonths(-1)
            });
            await arrangeDb.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<AuthDataSeeder>();
            await seeder.SeedAsync();
        }

        await using var verifyDb = _factory.CreateDbContext();
        var tier1AccountId = await verifyDb.Users
            .Where(a => a.Email == "staff1@solars.io.vn")
            .Select(a => a.Id)
            .SingleAsync();
        var allProfiles = await verifyDb.StaffProfiles
            .IgnoreQueryFilters()
            .ToListAsync();

        allProfiles.Should().ContainSingle(p => p.EmployeeCode == "STF-001");
        allProfiles.Single(p => p.EmployeeCode == "STF-001").AccountId
            .Should().Be(existingOwner.Id);
        allProfiles.Should().NotContain(p => p.AccountId == tier1AccountId);
        allProfiles.Should().Contain(p => p.EmployeeCode == "STF-002");
        allProfiles.Should().Contain(p => p.EmployeeCode == "STF-003");
    }
}
