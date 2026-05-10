using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Helper sinh token theo ý muốn (kể cả expired) — bypass validation của JwtHelper để test
/// validation paths không thể trigger qua API thường.
/// </summary>
internal static class TestJwtBuilder
{
    public static Task<string> BuildExpiredResetTokenAsync(string secret, string issuer, string audience)
    {
        var key = Encoding.UTF8.GetBytes(secret);

        // Sinh JwtSecurityToken trực tiếp (không qua SecurityTokenDescriptor) → bypass check Expires > NotBefore.
        var now = DateTime.UtcNow;
        var jwt = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("AccountId", Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Email, "expired@x.com"),
                new Claim("purpose", "password-reset")
            },
            notBefore: now.AddMinutes(-10),
            expires: now.AddMinutes(-5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature));

        return Task.FromResult(new JwtSecurityTokenHandler().WriteToken(jwt));
    }
}
