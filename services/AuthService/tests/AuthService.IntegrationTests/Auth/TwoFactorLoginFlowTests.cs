using System.Net.Http.Headers;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using SharedContracts.Common.Responses;

namespace AuthService.IntegrationTests.Auth;

/// <summary>
/// End-to-end 2FA enroll → login → verify-2fa flow. Sử dụng real TOTP code computed từ Otp.NET.
/// </summary>
[Collection("Integration")]
public class TwoFactorLoginFlowTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TwoFactorLoginFlowTests(AuthApiFactory factory)
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

    private static string ComputeTotp(string base32Secret)
    {
        var bytes = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(bytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp();
    }

    [Fact]
    public async Task EnrollFlow_Init_Confirm_EnablesAndReturnsBackupCodes()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "enroll@e.com", "Strong1Pass!");
        var (access, _) = await TestDataSeeder.LoginAsync(_client, "enroll@e.com", "Strong1Pass!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        // 1. Init
        var initResp = await _client.PostAsync("/api/accounts/me/2fa/init", null);
        var init = await initResp.Content.ReadFromJsonAsync<CommonResponse<TwoFactorSetupDto>>();
        init!.IsSuccess.Should().BeTrue();
        init.Data!.Secret.Should().NotBeNullOrEmpty();
        init.Data.PendingToken.Should().NotBeNullOrEmpty();

        // DB chưa enable
        using (var db = _factory.CreateDbContext())
        {
            var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Email == "enroll@e.com");
            acc.TwoFactorEnabled.Should().BeFalse();
        }

        // 2. Confirm
        var code = ComputeTotp(init.Data.Secret);
        var confirmResp = await _client.PostAsJsonAsync("/api/accounts/me/2fa/confirm", new { pendingToken = init.Data.PendingToken, code });
        var confirm = await confirmResp.Content.ReadFromJsonAsync<CommonResponse<TwoFactorConfirmDto>>();
        confirm!.IsSuccess.Should().BeTrue();
        confirm.Data!.Enabled.Should().BeTrue();
        confirm.Data.BackupCodes.Should().HaveCount(8);

        // DB enabled + encrypted + 8 backup codes
        using (var db = _factory.CreateDbContext())
        {
            var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Email == "enroll@e.com");
            acc.TwoFactorEnabled.Should().BeTrue();
            acc.TwoFactorSecret.Should().StartWith("enc:v1:");
            acc.TwoFactorSecretEncryptedAt.Should().NotBeNull();

            var codes = await db.BackupCodes.Where(b => b.AccountId == acc.Id).ToListAsync();
            codes.Should().HaveCount(8);
            codes.Should().AllSatisfy(c => c.RedeemedAt.Should().BeNull());
        }
    }

    [Fact]
    public async Task LoginFlow_2FAOn_ReturnsChallenge_NotJwt()
    {
        var (account, secret) = await EnrollAccountAsync("login2fa@e.com");

        // Login bước 1
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "login2fa@e.com", Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();

        login!.IsSuccess.Should().BeTrue();
        login.Data!.RequiresTwoFactor.Should().BeTrue();
        login.Data.Tokens.Should().BeNull();
        login.Data.Challenge!.ChallengeToken.Should().NotBeNullOrEmpty();
        login.Data.Challenge.ExpiresInSeconds.Should().Be(300);
        login.Data.Challenge.Methods.Should().Contain(new[] { "totp", "backupCode" });
    }

    [Fact]
    public async Task LoginFlow_VerifyValidTotp_IssuesJwt()
    {
        var (account, secret) = await EnrollAccountAsync("verify@e.com");
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "verify@e.com", Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        var challengeToken = login!.Data!.Challenge!.ChallengeToken;

        // Wait a moment to avoid using the same TOTP code as enroll (small drift handling)
        var code = ComputeTotp(secret);
        var verifyResp = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code, isBackupCode = false });
        var verify = await verifyResp.Content.ReadFromJsonAsync<LoginResponse>();

        verify!.IsSuccess.Should().BeTrue();
        verify.StatusCode.Should().Be(200);
        verify.Data!.Tokens!.AccessToken.Should().NotBeNullOrEmpty();
        verify.Data.Tokens.RefreshToken.Should().NotBeNullOrEmpty();
        verify.Data.Challenge.Should().BeNull();
    }

    [Fact]
    public async Task LoginFlow_VerifyWrongCode_Returns422_ChallengeStillUsable()
    {
        var (account, secret) = await EnrollAccountAsync("wrong@e.com");
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "wrong@e.com", Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        var challengeToken = login!.Data!.Challenge!.ChallengeToken;

        // Wrong code → 422 (business rule, format OK)
        var resp = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code = "000000", isBackupCode = false });
        ((int)resp.StatusCode).Should().Be(422);

        // Vẫn dùng được challenge — verify đúng → JWT
        var code = ComputeTotp(secret);
        var ok = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code, isBackupCode = false });
        ok.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task LoginFlow_5WrongAttempts_ChallengeInvalidated()
    {
        var (account, secret) = await EnrollAccountAsync("bf@e.com");
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "bf@e.com", Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        var challengeToken = login!.Data!.Challenge!.ChallengeToken;

        // 5 wrong attempts
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code = "000000", isBackupCode = false });
        }
        // 6th → 429 + challenge invalidated
        var sixth = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code = "000000", isBackupCode = false });
        ((int)sixth.StatusCode).Should().Be(429);

        // Even valid code now fails — challenge invalidated → 422
        var validCode = ComputeTotp(secret);
        var afterInvalidate = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code = validCode, isBackupCode = false });
        ((int)afterInvalidate.StatusCode).Should().Be(422); // challenge gone
    }

    [Fact]
    public async Task LoginFlow_2FAOff_StillReturnsJwtImmediately()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "no2fa@e.com", "Strong1Pass!");

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = "no2fa@e.com", Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();

        login!.IsSuccess.Should().BeTrue();
        login.Data!.Tokens!.AccessToken.Should().NotBeNullOrEmpty();
        login.Data.Challenge.Should().BeNull();
        login.Data.RequiresTwoFactor.Should().BeFalse();
    }

    // --- helpers ---

    /// <summary>Enroll: seed account → login → init → confirm → return (account, plaintext secret).</summary>
    private async Task<(Guid AccountId, string Secret)> EnrollAccountAsync(string email)
    {
        Guid accId;
        using (var db = _factory.CreateDbContext())
        {
            var acc = await TestDataSeeder.SeedActiveAccountAsync(db, email, "Strong1Pass!");
            accId = acc.Id;
        }
        var (access, _) = await TestDataSeeder.LoginAsync(_client, email, "Strong1Pass!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var initResp = await _client.PostAsync("/api/accounts/me/2fa/init", null);
        var init = await initResp.Content.ReadFromJsonAsync<CommonResponse<TwoFactorSetupDto>>();
        var code = ComputeTotp(init!.Data!.Secret);
        await _client.PostAsJsonAsync("/api/accounts/me/2fa/confirm", new { pendingToken = init.Data.PendingToken, code });

        _client.DefaultRequestHeaders.Authorization = null;
        return (accId, init.Data.Secret);
    }
}
