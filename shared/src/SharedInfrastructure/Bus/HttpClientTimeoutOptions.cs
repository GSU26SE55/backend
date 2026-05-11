namespace SharedInfrastructure.Bus;

/// <summary>
/// Timeout (giây) cho các external HTTP client. Bind từ section <c>HttpClientTimeouts</c>:
/// <code>
/// HttpClientTimeouts__GoogleOAuthSeconds=15
/// HttpClientTimeouts__MailjetSeconds=30
/// HttpClientTimeouts__DefaultSeconds=30
/// </code>
///
/// Tại sao không dùng default 100s của <see cref="System.Net.Http.HttpClient.Timeout"/>?
/// - 100s quá dài → user request hanging gần 2 phút → bad UX + tốn thread.
/// - Network call tới 3rd party (Google, Mailjet) bình thường < 2s.
/// - Set 15-30s đủ cover slow case nhưng vẫn fail-fast khi peer down.
/// </summary>
public class HttpClientTimeoutOptions
{
    public const string SectionName = "HttpClientTimeouts";

    /// <summary>Google OAuth token exchange. Expected < 3s. Default 15s.</summary>
    public int GoogleOAuthSeconds { get; set; } = 15;

    /// <summary>Mailjet REST API send email. Expected < 5s. Default 30s.</summary>
    public int MailjetSeconds { get; set; } = 30;

    /// <summary>Fallback cho HttpClient không có config riêng. Default 30s.</summary>
    public int DefaultSeconds { get; set; } = 30;

    public TimeSpan GoogleOAuth => TimeSpan.FromSeconds(GoogleOAuthSeconds > 0 ? GoogleOAuthSeconds : 15);
    public TimeSpan Mailjet => TimeSpan.FromSeconds(MailjetSeconds > 0 ? MailjetSeconds : 30);
    public TimeSpan Default => TimeSpan.FromSeconds(DefaultSeconds > 0 ? DefaultSeconds : 30);
}
