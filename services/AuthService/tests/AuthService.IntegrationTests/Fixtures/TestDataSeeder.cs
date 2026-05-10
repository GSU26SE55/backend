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

        db.Roles.AddRange(
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

        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            EmailConfirmed = true,
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(account);

        if (roleId.HasValue)
        {
            db.AccountRoles.Add(new AccountRole
            {
                Id = Guid.NewGuid(),
                AccountId = account.Id,
                RoleId = roleId.Value,
                AssignedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
        }

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
        return (payload.Data.AccessToken!, payload.Data.RefreshToken!);
    }
}
