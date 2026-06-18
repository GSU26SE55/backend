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

    // #AUTH-21: retry 2 lần exponential backoff cho transient failure (network blip / 5xx).
    // 4xx (bad code, bad client_secret) → KHÔNG retry vì retry không fix logic.
    private const int MaxRetries = 2;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(500);

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

        var formValues = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        };

        Exception? lastException = null;
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var backoff = TimeSpan.FromMilliseconds(InitialBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
                _logger.LogWarning("Google token exchange retry attempt {Attempt}/{Max} after {Backoff}ms.",
                    attempt, MaxRetries, (int)backoff.TotalMilliseconds);
                try { await Task.Delay(backoff, cancellationToken); }
                catch (OperationCanceledException) { return null; }
            }

            try
            {
                // FormUrlEncodedContent không reuse được giữa retry → tạo mới mỗi lần.
                using var form = new FormUrlEncodedContent(formValues);
                using var response = await _httpClient.PostAsync(TokenEndpoint, form, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // 4xx → bad request (invalid code/secret/redirect) — KHÔNG retry.
                    // 5xx → server transient → retry.
                    var statusCode = (int)response.StatusCode;
                    _logger.LogWarning("Google token exchange failed: {StatusCode} - {Body}", statusCode, body);
                    if (statusCode >= 400 && statusCode < 500)
                        return null;
                    continue; // retry on 5xx
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnect — KHÔNG retry.
                return null;
            }
            catch (TaskCanceledException ex)
            {
                // HttpClient timeout (10s) — transient, retry.
                lastException = ex;
                _logger.LogWarning("Google token exchange timed out (attempt {Attempt}).", attempt);
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Google token exchange network error (attempt {Attempt}).", attempt);
            }
            catch (Exception ex)
            {
                // Lỗi bất ngờ (parse fail, ...) — KHÔNG retry.
                _logger.LogError(ex, "Exception during Google token exchange.");
                return null;
            }
        }

        _logger.LogError(lastException, "Google token exchange exhausted retries.");
        return null;
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
