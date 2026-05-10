using System.Net;

namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// HttpMessageHandler giả: capture toàn bộ request gửi tới Mailjet để integration test assert,
/// trả về response cố định.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = new();
    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{\"Sent\":[]}";

    public CapturedRequest? LastRequest => Requests.LastOrDefault();
    public int CallCount => Requests.Count;

    public void Clear() => Requests.Clear();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest
        {
            Method = request.Method,
            Uri = request.RequestUri,
            AuthorizationScheme = request.Headers.Authorization?.Scheme,
            AuthorizationParameter = request.Headers.Authorization?.Parameter,
            Body = body
        });

        return new HttpResponseMessage(ResponseStatus)
        {
            Content = new StringContent(ResponseBody)
        };
    }
}

public class CapturedRequest
{
    public HttpMethod Method { get; init; } = HttpMethod.Get;
    public Uri? Uri { get; init; }
    public string? AuthorizationScheme { get; init; }
    public string? AuthorizationParameter { get; init; }
    public string? Body { get; init; }
}
