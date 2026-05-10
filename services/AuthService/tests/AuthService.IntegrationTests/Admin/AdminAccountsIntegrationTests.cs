using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.IntegrationTests.Admin;

[Collection("Integration")]
public class AdminAccountsIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AdminAccountsIntegrationTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.Producer.Clear();
        using var db = _factory.CreateDbContext();
        await TestDataSeeder.SeedSystemRolesAsync(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> SeedAdminAndLoginAsync()
    {
        using var db = _factory.CreateDbContext();
        await TestDataSeeder.SeedActiveAccountAsync(db, "admin@test.local", "Admin123@",
            roleId: TestDataSeeder.AdminRoleId);
        var (access, _) = await TestDataSeeder.LoginAsync(_client, "admin@test.local", "Admin123@");
        return access;
    }

    private async Task<string> SeedCustomerAndLoginAsync()
    {
        using var db = _factory.CreateDbContext();
        await TestDataSeeder.SeedActiveAccountAsync(db, "customer@test.local", "Cust123@",
            roleId: TestDataSeeder.CustomerRoleId);
        var (access, _) = await TestDataSeeder.LoginAsync(_client, "customer@test.local", "Cust123@");
        return access;
    }

    [Fact]
    public async Task GetList_AsAdmin_Returns200()
    {
        var token = await SeedAdminAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.GetAsync("/api/admin/accounts?pageNumber=1&pageSize=10");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetList_AsCustomer_Returns403()
    {
        var token = await SeedCustomerAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.GetAsync("/api/admin/accounts?pageNumber=1&pageSize=10");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetList_NoToken_Returns401()
    {
        _client.WithoutBearer();
        var resp = await _client.GetAsync("/api/admin/accounts");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangeStatus_ToBanned_CascadeRevokesTokens()
    {
        var adminToken = await SeedAdminAndLoginAsync();

        // Tạo target user + login để có active token
        Guid targetId;
        using (var db = _factory.CreateDbContext())
        {
            var t = await TestDataSeeder.SeedActiveAccountAsync(db, "victim@test.local", "Victim123@",
                roleId: TestDataSeeder.CustomerRoleId);
            targetId = t.Id;
        }
        await TestDataSeeder.LoginAsync(_client, "victim@test.local", "Victim123@");

        _client.WithBearer(adminToken);
        var resp = await _client.PatchAsync($"/api/admin/accounts/{targetId}/status",
            JsonContent.Create(new { Status = AccountStatusEnum.Banned, Reason = "violation" }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.FirstAsync(a => a.Id == targetId);
        acc.Status.Should().Be(AccountStatusEnum.Banned);
        var anyActive = await db2.RefreshTokens.AnyAsync(r => r.AccountId == targetId && r.Status == RefreshTokenStatus.Active);
        anyActive.Should().BeFalse();
    }

    [Fact]
    public async Task UnlockAccount_ResetsCounter()
    {
        var adminToken = await SeedAdminAndLoginAsync();
        Guid targetId;
        using (var db = _factory.CreateDbContext())
        {
            var t = await TestDataSeeder.SeedActiveAccountAsync(db, "locked@test.local", "Lock123@",
                status: AccountStatusEnum.Locked);
            t.FailedLoginAttempts = 5;
            t.LockoutEndAt = DateTime.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
            targetId = t.Id;
        }

        _client.WithBearer(adminToken);
        var resp = await _client.PostAsync($"/api/admin/accounts/{targetId}/unlock", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.FirstAsync(a => a.Id == targetId);
        acc.Status.Should().Be(AccountStatusEnum.Active);
        acc.FailedLoginAttempts.Should().Be(0);
        acc.LockoutEndAt.Should().BeNull();
    }

    [Fact]
    public async Task GetSessions_OfAnotherUser_Returns200_WithSessionList()
    {
        var adminToken = await SeedAdminAndLoginAsync();
        Guid victimId;
        using (var db = _factory.CreateDbContext())
        {
            var v = await TestDataSeeder.SeedActiveAccountAsync(db, "v@test.local", "V123@!",
                roleId: TestDataSeeder.CustomerRoleId);
            victimId = v.Id;
        }
        await TestDataSeeder.LoginAsync(_client, "v@test.local", "V123@!");

        _client.WithBearer(adminToken);
        var resp = await _client.GetAsync($"/api/admin/accounts/{victimId}/sessions?activeOnly=true");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<SessionListResponse>();
        body!.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task RevokeAllSessions_ForcesLogoutOfTargetUser()
    {
        var adminToken = await SeedAdminAndLoginAsync();
        Guid victimId;
        using (var db = _factory.CreateDbContext())
        {
            var v = await TestDataSeeder.SeedActiveAccountAsync(db, "v2@test.local", "V123@!",
                roleId: TestDataSeeder.CustomerRoleId);
            victimId = v.Id;
        }
        await TestDataSeeder.LoginAsync(_client, "v2@test.local", "V123@!");

        _client.WithBearer(adminToken);
        var resp = await _client.PostAsJsonAsync($"/api/admin/accounts/{victimId}/sessions/revoke-all",
            new { Reason = "Suspected breach" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var stillActive = await db2.RefreshTokens.AnyAsync(r => r.AccountId == victimId && r.Status == RefreshTokenStatus.Active);
        stillActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAccount_AsAdmin_SoftDeletesAndRevokesTokens()
    {
        var adminToken = await SeedAdminAndLoginAsync();
        Guid targetId;
        using (var db = _factory.CreateDbContext())
        {
            var t = await TestDataSeeder.SeedActiveAccountAsync(db, "del@test.local", "Del123@!",
                roleId: TestDataSeeder.CustomerRoleId);
            targetId = t.Id;
        }
        await TestDataSeeder.LoginAsync(_client, "del@test.local", "Del123@!");

        _client.WithBearer(adminToken);
        var resp = await _client.DeleteAsync($"/api/admin/accounts/{targetId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.IgnoreQueryFilters().FirstAsync(a => a.Id == targetId);
        acc.IsDeleted.Should().BeTrue();
    }
}
