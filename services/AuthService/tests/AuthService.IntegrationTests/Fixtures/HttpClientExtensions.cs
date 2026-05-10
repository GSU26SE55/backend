using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Application.DTOs.Response.Auth;

namespace AuthService.IntegrationTests.Fixtures;

public static class HttpClientExtensions
{
    /// <summary>
    /// POST + parse response body thành type cụ thể. Throws nếu deserialize fail.
    /// </summary>
    public static async Task<T> PostAndReadAsync<T>(this HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body);
        var payload = await response.Content.ReadFromJsonAsync<T>();
        return payload ?? throw new InvalidOperationException($"Empty response body from {url}");
    }

    public static async Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body)
        => await client.PostAsJsonAsync(url, body);

    /// <summary>
    /// Set header Authorization: Bearer {accessToken} cho mọi request tiếp theo trên client.
    /// </summary>
    public static HttpClient WithBearer(this HttpClient client, string? accessToken)
    {
        client.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Reset header Authorization về null.
    /// </summary>
    public static HttpClient WithoutBearer(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
