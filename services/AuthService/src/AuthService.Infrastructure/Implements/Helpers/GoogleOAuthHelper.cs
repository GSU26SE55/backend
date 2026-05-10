using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Application.Interfaces.Helpers;
using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuthService.Infrastructure.Implements.Helpers;

public class GoogleOAuthHelper : IGoogleOAuthHelper
{
    private const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string DefaultScope = "openid email profile";

    private readonly IConfiguration _configuration;
    private readonly ILogger<GoogleOAuthHelper> _logger;
    private readonly HttpClient _httpClient;

    public GoogleOAuthHelper(IConfiguration configuration, ILogger<GoogleOAuthHelper> logger, HttpClient httpClient)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<GoogleUserInfo?> ValidateAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return null;

        try
        {
            var clientId = ResolveClientId();
            var settings = new GoogleJsonWebSignature.ValidationSettings();
            if (!string.IsNullOrWhiteSpace(clientId))
                settings.Audience = new[] { clientId };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return new GoogleUserInfo
            {
                Subject = payload.Subject,
                Email = payload.Email,
                EmailVerified = payload.EmailVerified,
                Name = payload.Name,
                Picture = payload.Picture
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed.");
            return null;
        }
    }

    public string BuildAuthorizationUrl(string state, string redirectUri)
    {
        var clientId = ResolveClientId()
                       ?? throw new InvalidOperationException("Google:ClientId is not configured.");
        var scope = _configuration["GoogleOAuth:Scope"] ?? DefaultScope;

        var queryParams = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scope,
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "select_account",
            ["include_granted_scopes"] = "true"
        };

        var query = string.Join("&", queryParams
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}"));

        return $"{AuthorizeEndpoint}?{query}";
    }

    public async Task<string?> ExchangeCodeForIdTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var clientId = ResolveClientId();
        var clientSecret = _configuration["GoogleOAuth:ClientSecret"]
                           ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogError("Google OAuth client credentials are not configured.");
            return null;
        }

        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            });

            using var response = await _httpClient.PostAsync(TokenEndpoint, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Google token exchange failed: {StatusCode} - {Body}", (int)response.StatusCode, body);
                return null;
            }

            // Parse 1 lần từ string đã đọc (không đọc stream 2 lần).
            var payload = string.IsNullOrWhiteSpace(body)
                ? null
                : JsonSerializer.Deserialize<GoogleTokenResponse>(body);

            if (payload == null)
            {
                _logger.LogWarning("Google token exchange returned empty/invalid body: {Body}", body);
                return null;
            }

            if (string.IsNullOrWhiteSpace(payload.IdToken))
            {
                _logger.LogWarning("Google token exchange success but id_token is missing. Body: {Body}", body);
                return null;
            }

            return payload.IdToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Google token exchange.");
            return null;
        }
    }

    private string? ResolveClientId()
    {
        return _configuration["GoogleOAuth:ClientId"]
               ?? Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
    }

    private class GoogleTokenResponse
    {
        public string? AccessToken { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? Scope { get; set; }
        public string? TokenType { get; set; }
        public int ExpiresIn { get; set; }
    }
}
