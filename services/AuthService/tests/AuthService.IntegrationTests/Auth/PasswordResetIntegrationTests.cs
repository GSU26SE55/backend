using AuthService.Application.CQRS.Command.Auth;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events;

namespace AuthService.IntegrationTests.Auth;

[Collection("Integration")]
public class PasswordResetIntegrationTests : IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public PasswordResetIntegrationTests(AuthApiFactory factory)
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
    public async Task ForgotPassword_NonExistent_Returns200_NoLeak_NoEvent()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "ghost@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Producer.Published.OfType<SendPasswordResetOtpEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task ForgotPassword_Active_GeneratesOtp_PublishesEvent()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "user@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        var resp = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "user@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.Producer.Published.OfType<SendPasswordResetOtpEvent>().Should().ContainSingle();
        using var db2 = _factory.CreateDbContext();
        var acc = await db2.Users.FirstAsync(a => a.Email == "user@example.com");
        acc.OtpPurpose.Should().Be(OtpPurposeEnum.PasswordReset);
        acc.OtpCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task FullResetFlow_Forgot_VerifyOtp_ResetPassword_LoginWithNew()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "reset@example.com", "OldPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        // Step 1: forgot
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "reset@example.com" });
        var otpEvent = _factory.Producer.Published.OfType<SendPasswordResetOtpEvent>().Single();

        // Step 2: verify reset OTP → trả ResetToken
        var verifyResp = await _client.PostAsJsonAsync("/api/auth/verify-reset-otp",
            new { Email = "reset@example.com", Otp = otpEvent.Otp });
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyBody = await verifyResp.Content.ReadFromJsonAsync<CommonResponse<ResetTokenDto>>();
        var resetToken = verifyBody!.Data!.ResetToken;
        resetToken.Should().NotBeNullOrEmpty();

        // Step 3: reset password
        var resetResp = await _client.PostAsJsonAsync("/api/auth/reset-password",
            new { ResetToken = resetToken, NewPassword = "NewStrong1!" });
        resetResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: login bằng password mới OK, password cũ fail
        var oldLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = "reset@example.com", Password = "OldPass123" });
        oldLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var (access, _) = await TestDataSeeder.LoginAsync(_client, "reset@example.com", "NewStrong1!");
        access.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ResendResetOtp_RateLimit_Returns429()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "resend@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);

        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "resend@example.com" });

        var resp = await _client.PostAsJsonAsync("/api/auth/resend-reset-otp", new { Email = "resend@example.com" });
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task VerifyResetOtp_WrongOtp_Returns401()
    {
        using (var db = _factory.CreateDbContext())
            await TestDataSeeder.SeedActiveAccountAsync(db, "wrongotp@example.com", "MyPass123",
                roleId: TestDataSeeder.CustomerRoleId);
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = "wrongotp@example.com" });

        var resp = await _client.PostAsJsonAsync("/api/auth/verify-reset-otp",
            new { Email = "wrongotp@example.com", Otp = "999999" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
