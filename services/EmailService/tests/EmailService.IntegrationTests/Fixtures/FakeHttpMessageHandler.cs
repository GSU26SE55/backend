using System.Net;

namespace EmailService.IntegrationTests.Fixtures;

/// <summary>
/// HttpMessageHandler giả: capture toàn bộ request gửi tới Mailjet để integration test assert,
/// trả về response cố định.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<CapturedRequest> _requests = new();
    private readonly object _gate = new();

    public IReadOnlyList<CapturedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{\"Sent\":[]}";

    public CapturedRequest? LastRequest
    {
        get
        {
            lock (_gate)
            {
                return _requests.LastOrDefault();
            }
        }
    }

    public int CallCount
    {
        get
        {
            lock (_gate)
            {
                return _requests.Count;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _requests.Clear();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_gate)
        {
            _requests.Add(new CapturedRequest
            {
                Method = request.Method,
                Uri = request.RequestUri,
                AuthorizationScheme = request.Headers.Authorization?.Scheme,
                AuthorizationParameter = request.Headers.Authorization?.Parameter,
                Body = body
            });
        }

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
