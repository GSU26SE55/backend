using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;

namespace AuthService.IntegrationTests.Auth;

/// <summary>
/// End-to-end auth flow: register → verify OTP → login → refresh → logout.
/// Dùng PostgreSQL Testcontainer thật, MassTransit InMemory, Mailjet capture stub.
/// </summary>
[Collection("Integration")]
public class AuthFlowIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowIntegrationTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.Producer.Clear();
        // Re-seed system roles after truncate (handler dùng RoleId Customer khi gán role).
        using var db = _factory.CreateDbContext();
        await TestDataSeeder.SeedSystemRolesAsync(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_NewEmail_CreatesPendingAccount_PublishesEvent()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "alice@example.com",
            Password = "Strong1Pass!",
            FullName = "Alice"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<RegisterResponse>();
        body!.IsSuccess.Should().BeTrue();
        body.Data!.Email.Should().Be("alice@example.com");

        // DB
        using var db = _factory.CreateDbContext();
        var acc = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Email == "alice@example.com");
        acc.Should().NotBeNull();
        acc!.Status.Should().Be(AccountStatusEnum.PendingVerification);
        acc.OtpCode.Should().NotBeNullOrEmpty();
        acc.OtpPurpose.Should().Be(OtpPurposeEnum.Register);

        // Event
        _factory.Producer.Published.Should().ContainSingle(e => e is SendOtpRegisterEvent);
        var evt = (SendOtpRegisterEvent)_factory.Producer.Published[0];
        evt.ToEmail.Should().Be("alice@example.com");
        evt.Otp.Should().Be(acc.OtpCode);
    }

    [Fact]
    public async Task Register_DuplicateActiveEmail_Returns409()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "dup@example.com", "X1!stronger");

        var resp = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "dup@example.com",
            Password = "Strong1Pass!",
            FullName = "Other"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task VerifyOtp_CorrectOtp_ActivatesAccount_NoTokenIssued()
    {
        // Step 1: Register
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "verify@example.com",
            Password = "Strong1Pass!",
            FullName = "Verify Me"
        });

        // Step 2: Lấy OTP từ event capture
        var otpEvent = _factory.Producer.Published.OfType<SendOtpRegisterEvent>().Single();

        // Step 3: VerifyOtp - chỉ kích hoạt account, KHÔNG cấp token. Client phải tự gọi login sau.
        var resp = await _client.PostAsJsonAsync("/api/auth/verify-otp", new
        {
            Email = "verify@example.com",
            Otp = otpEvent.Otp
        });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<CommonResponse<string>>();
        body!.IsSuccess.Should().BeTrue();
        body.Message.Should().Contain("kích hoạt");

        // 1-N refactor: account.Role là single — verify trực tiếp Role.Name.
        using var db = _factory.CreateDbContext();
        var acc = await db.Users
            .Include(a => a.Role)
            .FirstAsync(a => a.Email == "verify@example.com");
        acc.Status.Should().Be(AccountStatusEnum.Active);
        acc.EmailConfirmed.Should().BeTrue();
        acc.Role!.Name.Should().Be("Customer");

        (await db.RefreshTokens.AnyAsync(r => r.AccountId == acc.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtp_WrongOtp_Returns401_IncrementsCounter()
    {
        await _client.PostAsJsonAsync("/api/auth/register", new
        {
            Email = "wrong@example.com",
            Password = "Strong1Pass!",
            FullName = "X"
        });

        var resp = await _client.PostAsJsonAsync("/api/auth/verify-otp", new
        {
            Email = "wrong@example.com",
            Otp = "999999"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var db = _factory.CreateDbContext();
        var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Email == "wrong@example.com");
        acc.FailedLoginAttempts.Should().Be(1);
        acc.Status.Should().Be(AccountStatusEnum.PendingVerification);
    }

    [Fact]
    public async Task Login_ActiveAccount_CorrectPassword_ReturnsTokens()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "login@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var (access, refresh) = await TestDataSeeder.LoginAsync(_client, "login@example.com", "MyPass123");
        access.Should().NotBeNullOrEmpty();
        refresh.Should().NotBeNullOrEmpty();

        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.FirstAsync(a => a.Email == "login@example.com");
        acc.LastLoginAt.Should().NotBeNull();
        acc.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Login_WrongPassword_5Times_LocksAccount()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "lock@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        for (int i = 0; i < 5; i++)
        {
            var resp = await _client.PostAsJsonAsync("/api/auth/login",
                new { Email = "lock@example.com", Password = "WrongPass" });
            // last attempt should be 423; previous 401
            (i < 4 ? HttpStatusCode.Unauthorized : HttpStatusCode.Locked)
                .Should().Be(resp.StatusCode);
        }

        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.FirstAsync(a => a.Email == "lock@example.com");
        acc.Status.Should().Be(AccountStatusEnum.Locked);
        acc.LockoutEndAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RefreshToken_RotatesPair_MarksOldAsUsed()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "rotate@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var (_, oldRefresh) = await TestDataSeeder.LoginAsync(_client, "rotate@example.com", "MyPass123");

        var resp = await _client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = oldRefresh });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        body!.Data!.RefreshToken.Should().NotBe(oldRefresh);
        body.Data.AccessToken.Should().NotBeNullOrEmpty();

        using var db2 = _factory.CreateDbContext();
        var oldRt = await db2.RefreshTokens.FirstAsync(r => r.Token == oldRefresh);
        oldRt.Status.Should().Be(RefreshTokenStatus.Used);
        oldRt.UsedAt.Should().NotBeNull();
        oldRt.ReplacedByToken.Should().Be(body.Data.RefreshToken);
    }

    [Fact]
    public async Task RefreshToken_ReuseAttack_RevokesAll()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "reuse@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var (_, originalRefresh) = await TestDataSeeder.LoginAsync(_client, "reuse@example.com", "MyPass123");

        // Rotate 1 lần (token cũ thành Used)
        await _client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = originalRefresh });

        // Lần 2: dùng lại token CŨ (đã Used) → reuse-attack
        var resp = await _client.PostAsJsonAsync("/api/auth/refresh-token", new { RefreshToken = originalRefresh });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var db2 = _factory.CreateDbContext();
        var allTokens = await db2.RefreshTokens
            .Where(r => r.Account.Email == "reuse@example.com")
            .ToListAsync();
        allTokens.Should().NotBeEmpty();
        allTokens.Should().OnlyContain(r => r.Status != RefreshTokenStatus.Active);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "logout@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var (access, refresh) = await TestDataSeeder.LoginAsync(_client, "logout@example.com", "MyPass123");
        _client.WithBearer(access);

        var resp = await _client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = refresh });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var db2 = _factory.CreateDbContext();
        var rt = await db2.RefreshTokens.FirstAsync(r => r.Token == refresh);
        rt.Status.Should().Be(RefreshTokenStatus.Revoked);
        rt.RevokedReason.Should().Be("UserLogout");
    }

    [Fact]
    public async Task Logout_WithoutBearer_Returns401()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "logout-no-bearer@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var (_, refresh) = await TestDataSeeder.LoginAsync(_client, "logout-no-bearer@example.com", "MyPass123");
        _client.DefaultRequestHeaders.Authorization = null;

        var resp = await _client.PostAsJsonAsync("/api/auth/logout", new { RefreshToken = refresh });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
