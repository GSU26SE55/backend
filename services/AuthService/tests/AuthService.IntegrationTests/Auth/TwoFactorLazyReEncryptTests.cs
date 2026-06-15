using AuthService.Application.DTOs.Response.Auth;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace AuthService.IntegrationTests.Auth;

/// <summary>
/// Account legacy có TwoFactorSecret lưu plaintext (chưa migrate) → verify-2fa lần đầu thành công
/// → handler tự encrypt secret + set TwoFactorSecretEncryptedAt. Không break user.
/// </summary>
[Collection("Integration")]
public class TwoFactorLazyReEncryptTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public TwoFactorLazyReEncryptTests(AuthApiFactory factory)
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

    [Fact]
    public async Task LegacyPlaintextSecret_AfterVerify_RowGetsEncryptedAndStampedEncryptedAt()
    {
        // 1. Seed account với plaintext secret (mô phỏng dữ liệu pre-GH-295)
        const string email = "legacy@e.com";
        var plaintextSecret = KeyGeneration.GenerateRandomKey(20);
        var base32 = Base32Encoding.ToString(plaintextSecret);

        Guid accId;
        using (var db = _factory.CreateDbContext())
        {
            var acc = await TestDataSeeder.SeedActiveAccountAsync(db, email, "Strong1Pass!");
            acc.TwoFactorEnabled = true;
            acc.TwoFactorSecret = base32;             // plaintext — KHÔNG có prefix enc:v1:
            acc.TwoFactorSecretEncryptedAt = null;     // chưa encrypt
            await db.SaveChangesAsync();
            accId = acc.Id;
        }

        // 2. Login → challenge
        var loginResp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = "Strong1Pass!" });
        var login = await loginResp.Content.ReadFromJsonAsync<LoginResponse>();
        login!.Data!.RequiresTwoFactor.Should().BeTrue();

        // 3. Verify với TOTP đúng (compute từ plaintext secret)
        var totp = new Totp(plaintextSecret, step: 30, mode: OtpHashMode.Sha1, totpSize: 6);
        var code = totp.ComputeTotp();
        var verifyResp = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa",
            new { challengeToken = login.Data.Challenge!.ChallengeToken, code, isBackupCode = false });
        var verify = await verifyResp.Content.ReadFromJsonAsync<LoginResponse>();
        verify!.IsSuccess.Should().BeTrue();
        verify.Data!.Tokens!.AccessToken.Should().NotBeNullOrEmpty();

        // 4. DB: secret đã được encrypt + EncryptedAt set
        using (var db = _factory.CreateDbContext())
        {
            var acc = await db.Users.IgnoreQueryFilters().FirstAsync(a => a.Id == accId);
            acc.TwoFactorSecret.Should().StartWith("enc:v1:");
            acc.TwoFactorSecretEncryptedAt.Should().NotBeNull();
        }
    }
}
