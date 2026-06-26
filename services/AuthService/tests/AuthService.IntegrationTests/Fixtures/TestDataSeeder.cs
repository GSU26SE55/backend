using System.Net.Http.Json;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using BCrypt.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Fixtures;

/// <summary>
/// Seed data tiện lợi cho test — tạo account + role nhanh.
/// </summary>
public static class TestDataSeeder
{
    public static readonly Guid AdminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static async Task SeedSystemRolesAsync(ApplicationDbContext db)
    {
        var existed = await db.Roles.IgnoreQueryFilters().AnyAsync();
        if (existed)
            return;

        await db.Roles.AddRangeAsync(
            new global::AuthService.Domain.Entities.Role { Id = AdminRoleId, Name = "Admin", NormalizedName = "ADMIN", Status = RoleStatusEnum.Active, IsSystemRole = true, CreatedAt = DateTime.UtcNow },
            new global::AuthService.Domain.Entities.Role { Id = CustomerRoleId, Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active, IsSystemRole = true, CreatedAt = DateTime.UtcNow }
        );
        await db.SaveChangesAsync();
    }

    public static async Task<global::AuthService.Domain.Entities.Account> SeedActiveAccountAsync(
        ApplicationDbContext db,
        string email,
        string password,
        Guid? roleId = null,
        string fullName = "Test User",
        AccountStatusEnum status = AccountStatusEnum.Active)
    {
        await SeedSystemRolesAsync(db);

        // 1-N refactor: mỗi account bắt buộc có 1 role → default Customer nếu caller không truyền.
        var effectiveRoleId = roleId ?? CustomerRoleId;
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            EmailConfirmed = true,
            Status = status,
            RoleId = effectiveRoleId,
            RoleAssignedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(account);

        await db.SaveChangesAsync();
        return account;
    }

    /// <summary>Login bằng API và trả về tokens — tiện cho test cần authenticated client.</summary>
    public static async Task<(string accessToken, string refreshToken)> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var payload = await response.Content.ReadFromJsonAsync<global::AuthService.Application.DTOs.Response.Auth.LoginResponse>()
                      ?? throw new InvalidOperationException("Login response empty");
        if (!payload.IsSuccess || payload.Data == null)
            throw new InvalidOperationException($"Login failed: {payload.Message}");
        if (payload.Data.RequiresTwoFactor)
            throw new InvalidOperationException("Account requires 2FA. Use LoginViaTwoFactorAsync instead.");
        return (payload.Data.Tokens!.AccessToken!, payload.Data.Tokens!.RefreshToken!);
    }

    /// <summary>Full 2FA login flow: POST /login → POST /login/verify-2fa. Trả về tokens.</summary>
    public static async Task<(string accessToken, string refreshToken)> LoginViaTwoFactorAsync(HttpClient client, string email, string password, string totpCode)
    {
        var loginResp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var login = await loginResp.Content.ReadFromJsonAsync<global::AuthService.Application.DTOs.Response.Auth.LoginResponse>()
                    ?? throw new InvalidOperationException("Login response empty");
        if (login.Data?.Challenge == null)
            throw new InvalidOperationException("Expected 2FA challenge, got direct tokens.");

        var verifyResp = await client.PostAsJsonAsync("/api/auth/login/verify-2fa",
            new { challengeToken = login.Data.Challenge.ChallengeToken, code = totpCode, isBackupCode = false });
        var verify = await verifyResp.Content.ReadFromJsonAsync<global::AuthService.Application.DTOs.Response.Auth.LoginResponse>()
                     ?? throw new InvalidOperationException("Verify-2fa response empty");
        if (!verify.IsSuccess || verify.Data?.Tokens == null)
            throw new InvalidOperationException($"Verify-2FA failed: {verify.Message}");
        return (verify.Data.Tokens.AccessToken!, verify.Data.Tokens.RefreshToken!);
    }
}
