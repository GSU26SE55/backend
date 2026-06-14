using System.Net.Http.Headers;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using SharedContracts.Common.Responses;

namespace AuthService.IntegrationTests.Auth;

/// <summary>Login bằng backup code → verify-2fa với backup code → single-use → regenerate.</summary>
[Collection("Integration")]
public class TwoFactorBackupCodeFlowTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TwoFactorBackupCodeFlowTests(AuthApiFactory factory)
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
        return new Totp(bytes, step: 30, mode: OtpHashMode.Sha1, totpSize: 6).ComputeTotp();
    }

    private async Task<(string Email, string Secret, List<string> BackupCodes)> EnrollWithBackupCodesAsync(string email)
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, email, "Strong1Pass!");

        var (access, _) = await TestDataSeeder.LoginAsync(_client, email, "Strong1Pass!");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var initResp = await _client.PostAsync("/api/accounts/me/2fa/init", null);
        var init = await initResp.Content.ReadFromJsonAsync<CommonResponse<TwoFactorSetupDto>>();
        var confirmResp = await _client.PostAsJsonAsync("/api/accounts/me/2fa/confirm", new { pendingToken = init!.Data!.PendingToken, code = ComputeTotp(init.Data.Secret) });
        var confirm = await confirmResp.Content.ReadFromJsonAsync<CommonResponse<TwoFactorConfirmDto>>();

        _client.DefaultRequestHeaders.Authorization = null;
        return (email, init.Data.Secret, confirm!.Data!.BackupCodes);
    }

    [Fact]
    public async Task BackupCode_FirstUse_LoginSuccess_MarksRedeemed()
    {
        var enroll = await EnrollWithBackupCodesAsync("bc1@e.com");
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = enroll.Email, Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        var challengeToken = login!.Data!.Challenge!.ChallengeToken;

        var resp = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken, code = enroll.BackupCodes[0], isBackupCode = true });
        var verify = await resp.Content.ReadFromJsonAsync<LoginResponse>();

        verify!.IsSuccess.Should().BeTrue();
        verify.Data!.Tokens!.AccessToken.Should().NotBeNullOrEmpty();

        // DB: code đó đã RedeemedAt
        using var db = _factory.CreateDbContext();
        var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Email == enroll.Email);
        var redeemed = await db.BackupCodes.Where(b => b.AccountId == acc.Id && b.RedeemedAt != null).ToListAsync();
        redeemed.Should().HaveCount(1);
    }

    [Fact]
    public async Task BackupCode_SecondUseSameCode_Fails()
    {
        var enroll = await EnrollWithBackupCodesAsync("bc2@e.com");
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = enroll.Email, Password = "Strong1Pass!" });
        var login1 = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken = login1!.Data!.Challenge!.ChallengeToken, code = enroll.BackupCodes[0], isBackupCode = true });

        // Login lại lần 2 với cùng backup code → fail
        var loginResp2 = await _client.PostAsJsonAsync("/api/auth/login", new { Email = enroll.Email, Password = "Strong1Pass!" });
        var login2 = await loginResp2.Content.ReadFromJsonAsync<LoginResponse>();
        var resp2 = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { challengeToken = login2!.Data!.Challenge!.ChallengeToken, code = enroll.BackupCodes[0], isBackupCode = true });

        ((int)resp2.StatusCode).Should().Be(422);
    }

    [Fact]
    public async Task RegenerateBackupCodes_WithValidTotp_InvalidatesOldReturnsNew()
    {
        var enroll = await EnrollWithBackupCodesAsync("regen@e.com");
        var (access, _) = await TestDataSeeder.LoginViaTwoFactorAsync(_client, enroll.Email, "Strong1Pass!", ComputeTotp(enroll.Secret));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        var totp = ComputeTotp(enroll.Secret);
        var resp = await _client.PostAsJsonAsync("/api/accounts/me/2fa/backup-codes/regenerate", new { totpCode = totp });
        var regen = await resp.Content.ReadFromJsonAsync<CommonResponse<BackupCodesDto>>();

        regen!.IsSuccess.Should().BeTrue();
        regen.Data!.BackupCodes.Should().HaveCount(8);
        regen.Data.BackupCodes.Should().NotContain(enroll.BackupCodes);

        // DB: codes cũ đã bị xóa (soft delete) — only 8 active codes (mới)
        using var db = _factory.CreateDbContext();
        var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Email == enroll.Email);
        var active = await db.BackupCodes.Where(b => b.AccountId == acc.Id).ToListAsync();
        active.Should().HaveCount(8);
    }
}
