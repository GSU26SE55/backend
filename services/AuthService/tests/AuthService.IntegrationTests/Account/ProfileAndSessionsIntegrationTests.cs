using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace AuthService.IntegrationTests.Account;

[Collection("Integration")]
public class ProfileAndSessionsIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public ProfileAndSessionsIntegrationTests(AuthApiFactory factory)
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

    private async Task<string> SeedAndLoginAsync(string email = "user@example.com", string password = "MyPass123")
    {
        using var db = _factory.CreateDbContext();
        await TestDataSeeder.SeedActiveAccountAsync(db, email, password, roleId: TestDataSeeder.CustomerRoleId);
        var (access, _) = await TestDataSeeder.LoginAsync(_client, email, password);
        return access;
    }

    [Fact]
    public async Task GetMyProfile_Authorized_ReturnsCurrentUser()
    {
        var token = await SeedAndLoginAsync("profile@example.com");
        _client.WithBearer(token);

        var resp = await _client.GetAsync("/api/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<AccountResponse>();
        body!.Data!.Email.Should().Be("profile@example.com");
        body.Data.Role.Should().Be("Customer");
    }

    [Fact]
    public async Task GetMyProfile_NoToken_Returns401()
    {
        _client.WithoutBearer();
        var resp = await _client.GetAsync("/api/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateMe_ChangePhone_ResetsPhoneConfirmed()
    {
        var token = await SeedAndLoginAsync("updatephone@example.com");
        _client.WithBearer(token);

        // Set phone confirmed = true trực tiếp DB
        using (var db = _factory.CreateDbContext())
        {
            var acc = await db.Users.FirstAsync(a => a.Email == "updatephone@example.com");
            acc.PhoneNumber = "0900111";
            acc.PhoneConfirmed = true;
            await db.SaveChangesAsync();
        }

        var resp = await _client.PutAsJsonAsync("/api/auth/me/profile", new
        {
            FullName = "Updated Name",
            PhoneNumber = "0900222"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var acc2 = await db2.Users.FirstAsync(a => a.Email == "updatephone@example.com");
        // #AUTH-39: PhoneNormalizer convert 09xx → +849xx (E.164).
        acc2.PhoneNumber.Should().Be("+84900222");
        acc2.PhoneConfirmed.Should().BeFalse();
        acc2.FullName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task ChangePassword_CorrectCurrent_RevokesAllSessions()
    {
        var token = await SeedAndLoginAsync("changepw@example.com", "OldPass123");
        _client.WithBearer(token);

        var resp = await _client.PatchAsync("/api/accounts/me/password",
            JsonContent.Create(new
            {
                CurrentPassword = "OldPass123",
                NewPassword = "NewPass456!",
                ConfirmPassword = "NewPass456!"
            }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = _factory.CreateDbContext();
        var anyActive = await db.RefreshTokens
            .AnyAsync(r => r.Account.Email == "changepw@example.com" && r.Status == RefreshTokenStatus.Active);
        anyActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetMySessions_AfterLogin_Returns1Active()
    {
        var token = await SeedAndLoginAsync("sessions@example.com");
        _client.WithBearer(token);

        var resp = await _client.GetAsync("/api/sessions/me?activeOnly=true");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<SessionListResponse>();
        body!.Data.Should().HaveCount(1);
        body.Data![0].Status.Should().Be(RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task RevokeAllSessions_LogsOutEverywhere()
    {
        var token = await SeedAndLoginAsync("revokeall@example.com");
        _client.WithBearer(token);

        var resp = await _client.PostAsJsonAsync("/api/sessions/revoke-all",
            new { ExceptCurrent = false });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = _factory.CreateDbContext();
        var active = await db.RefreshTokens
            .Where(r => r.Account.Email == "revokeall@example.com" && r.Status == RefreshTokenStatus.Active)
            .CountAsync();
        active.Should().Be(0);
    }

    [Fact]
    public async Task DeactivateMe_SetsInactive()
    {
        var token = await SeedAndLoginAsync("deactivate@example.com");
        _client.WithBearer(token);

        var resp = await _client.PostAsync("/api/accounts/me/deactivate", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db = _factory.CreateDbContext();
        var acc = await db.Users.FirstAsync(a => a.Email == "deactivate@example.com");
        acc.Status.Should().Be(AccountStatusEnum.Inactive);
    }
}
