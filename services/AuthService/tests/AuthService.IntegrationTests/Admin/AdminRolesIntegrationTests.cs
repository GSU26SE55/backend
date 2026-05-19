using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AuthService.IntegrationTests.Admin;

[Collection("Integration")]
public class AdminRolesIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AdminRolesIntegrationTests(AuthApiFactory factory)
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

    [Fact]
    public async Task CreateRole_AsAdmin_Returns201()
    {
        var token = await SeedAdminAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.PostAsJsonAsync("/api/admin/roles",
            new { Name = "Inspector", Description = "Inspector role" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var db = _factory.CreateDbContext();
        var role = await db.Roles.FirstAsync(r => r.NormalizedName == "INSPECTOR");
        role.IsSystemRole.Should().BeFalse();
        role.Status.Should().Be(RoleStatusEnum.Active);
    }

    [Fact]
    public async Task UpdateRole_SystemRole_Returns403()
    {
        var token = await SeedAdminAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.PutAsJsonAsync($"/api/admin/roles/{TestDataSeeder.AdminRoleId}",
            new { Name = "AdminRenamed" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRole_SystemRole_Returns403()
    {
        var token = await SeedAdminAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.DeleteAsync($"/api/admin/roles/{TestDataSeeder.AdminRoleId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRoleList_ReturnsSystemRoles()
    {
        var token = await SeedAdminAndLoginAsync();
        _client.WithBearer(token);

        var resp = await _client.GetAsync("/api/admin/roles?pageNumber=1&pageSize=20");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RoleListResponse>();
        body!.Data!.Items.Should().Contain(r => r.Name == "Admin");
        body.Data.Items.Should().Contain(r => r.Name == "Customer");
    }

    [Fact]
    public async Task ChangeRole_UpdatesAccountRoleId_AndAudit()
    {
        // 1-N refactor: thay vì gán role tạm thời (đã xóa), endpoint mới là PUT /role
        // đổi role hiện tại của account sang role mới (account chỉ có 1 role).
        var adminToken = await SeedAdminAndLoginAsync();
        Guid targetId;
        using (var db = _factory.CreateDbContext())
        {
            var t = await TestDataSeeder.SeedActiveAccountAsync(db, "changeme@test.local", "P@ss123",
                roleId: TestDataSeeder.CustomerRoleId);
            targetId = t.Id;
        }

        _client.WithBearer(adminToken);
        var resp = await _client.PutAsJsonAsync($"/api/admin/accounts/{targetId}/role",
            new { RoleId = TestDataSeeder.AdminRoleId });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var account = await db2.Users.FirstAsync(a => a.Id == targetId);
        account.RoleId.Should().Be(TestDataSeeder.AdminRoleId);
        account.RoleAssignedAt.Should().NotBeNull();
    }
}
