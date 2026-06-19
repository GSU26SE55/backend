using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Infrastructure.Implements.Helpers;

public class JwtHelper : IJwtHelper
{
    private const string ResetTokenPurposeClaim = "purpose";
    private const string ResetTokenPurposeValue = "password-reset";

    /// <summary>Default TTL khi config <c>JwtSettings:AccessTokenExpirationMinutes</c> không set.</summary>
    private const int DefaultAccessTokenExpirationMinutes = 60;

    private readonly IConfiguration _configuration;

    public JwtHelper(IConfiguration configuration)
    {
        _configuration = configuration;

    }

    /// <summary>Claim type cho permission code (compact để giảm size JWT).</summary>
    public const string PermissionClaimType = "perm";

    private string SecretKey => _configuration["JwtSettings:SecretKey"]
        ?? throw new InvalidOperationException("Missing configuration: JwtSettings:SecretKey");

    public Task<string> GenerateAccessToken(Account account, string role, IEnumerable<string>? permissions = null)
    {
        var Issuer = _configuration["JwtSettings:Issuer"];
        var Audience = _configuration["JwtSettings:Audience"];

        var Key = Encoding.UTF8.GetBytes(SecretKey);
        var TokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim("AccountId", account.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, account.Email ?? string.Empty),
                new Claim("FullName", account.FullName ?? string.Empty),
            };

        if (!string.IsNullOrWhiteSpace(role))
            claims.Add(new Claim(ClaimTypes.Role, role));

        foreach (var permission in (permissions ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(permission))
                claims.Add(new Claim(PermissionClaimType, permission));
        }

        var expirationMinutes = ResolveAccessTokenExpirationMinutes();

        // #AUTH-59: kid header để support key rotation. Multi-key validation đọc kid để chọn key đúng.
        var kid = _configuration["JwtSettings:SigningKeyId"] ?? "v1";
        var signingKey = new SymmetricSecurityKey(Key) { KeyId = kid };

        var TokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(expirationMinutes),
            Issuer = Issuer,
            Audience = Audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256Signature)
        };

        var Token = TokenHandler.CreateToken(TokenDescriptor);
        var AccessToken = TokenHandler.WriteToken(Token);

        return Task.FromResult(AccessToken);
    }

    public string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Đọc TTL access token từ <c>JwtSettings:AccessTokenExpirationMinutes</c>.
    /// Map từ env var <c>JwtSettings__AccessTokenExpirationMinutes</c> trong .env / .env.Docker.
    /// Trả về <see cref="DefaultAccessTokenExpirationMinutes"/> (60) nếu config thiếu, parse fail hoặc &lt;= 0.
    /// </summary>
    private int ResolveAccessTokenExpirationMinutes()
    {
        var raw = _configuration["JwtSettings:AccessTokenExpirationMinutes"];
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultAccessTokenExpirationMinutes;

        if (!int.TryParse(raw, out var minutes) || minutes <= 0)
            return DefaultAccessTokenExpirationMinutes;

        return minutes;
    }

    public bool IsTokenValid(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var (ok, _) = ValidateToken(token);
        return ok;
    }

    public DateTime ConvertUnixTimeToDateTime(long utcExpiredDate)
    {
        var DateTimeInterval = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        return DateTimeInterval.AddSeconds(utcExpiredDate).ToLocalTime();
    }

    public (bool, string?) ValidateToken(string AccessToken)
    {
        if (string.IsNullOrWhiteSpace(AccessToken))
            return (false, "Token is required.");

        var TokenHandler = new JwtSecurityTokenHandler();

        // #AUTH-59: support multi-key validation — current + previous key cho phép token đã issue
        // với key cũ vẫn pass cho đến khi tự expire.
        var keys = new List<SecurityKey>
        {
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey))
            {
                KeyId = _configuration["JwtSettings:SigningKeyId"] ?? "v1"
            }
        };
        var previousKey = _configuration["JwtSettings:PreviousSecretKey"];
        if (!string.IsNullOrEmpty(previousKey))
        {
            keys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(previousKey))
            {
                KeyId = _configuration["JwtSettings:PreviousSigningKeyId"] ?? "v0"
            });
        }

        var tokenValidateParam = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _configuration["JwtSettings:Issuer"],
            ValidateAudience = true,
            ValidAudience = _configuration["JwtSettings:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            TokenHandler.ValidateToken(AccessToken, tokenValidateParam, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtSecurityToken ||
                string.IsNullOrEmpty(jwtSecurityToken.Header.Alg) ||
                !jwtSecurityToken.Header.Alg.Equals(
                    SecurityAlgorithms.HmacSha256,
                    StringComparison.InvariantCultureIgnoreCase))
            {
                return (false, "Invalid token algorithm.");
            }

            return (true, null);
        }
        catch (SecurityTokenExpiredException)
        {
            return (false, "Token expired.");
        }
        catch (SecurityTokenInvalidIssuerException)
        {
            return (false, "Invalid token issuer.");
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            return (false, "Invalid token audience.");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return (false, "Invalid token signature.");
        }
        catch (SecurityTokenException ex)
        {
            return (false, ex.Message);
        }
        catch (Exception)
        {
            return (false, "Invalid token.");
        }
    }

    public string GenerateResetToken(Guid accountId, string email, int expiresInMinutes)
    {
        var key = Encoding.UTF8.GetBytes(SecretKey);
        var handler = new JwtSecurityTokenHandler();

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                    new Claim("AccountId", accountId.ToString()),
                    new Claim(JwtRegisteredClaimNames.Email, email),
                    new Claim(ResetTokenPurposeClaim, ResetTokenPurposeValue)
                }),
            Expires = DateTime.UtcNow.AddMinutes(expiresInMinutes),
            Issuer = _configuration["JwtSettings:Issuer"],
            Audience = _configuration["JwtSettings:Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    public (Guid? accountId, string? errorMessage) ValidateResetToken(string token)
    {
        var (id, _, _, err) = ValidateResetTokenDetailed(token);
        return (id, err);
    }

    public (Guid? accountId, string? jti, DateTime? expiresAtUtc, string? errorMessage) ValidateResetTokenDetailed(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (null, null, null, "Reset token không được để trống.");

        try
        {
            var key = Encoding.UTF8.GetBytes(SecretKey);
            var handler = new JwtSecurityTokenHandler();

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out var validated);

            if (principal.FindFirst(ResetTokenPurposeClaim)?.Value != ResetTokenPurposeValue)
                return (null, null, null, "Token không phải dành cho reset password.");

            var raw = principal.FindFirst("AccountId")?.Value;
            if (!Guid.TryParse(raw, out var id))
                return (null, null, null, "Token thiếu AccountId.");

            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var exp = validated is JwtSecurityToken jwt ? (DateTime?)jwt.ValidTo : null;

            return (id, jti, exp, null);
        }
        catch (SecurityTokenExpiredException)
        {
            return (null, null, null, "Reset token đã hết hạn.");
        }
        catch (Exception)
        {
            return (null, null, null, "Reset token không hợp lệ.");
        }
    }
}
