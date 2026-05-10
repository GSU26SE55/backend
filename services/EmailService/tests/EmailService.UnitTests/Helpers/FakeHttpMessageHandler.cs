using System.Net;

namespace EmailService.UnitTests.Helpers;

/// <summary>
/// HttpMessageHandler giả: ghi lại request gần nhất và trả về response cố định.
/// Dùng để test consumer mà không gọi HTTP thật tới Mailjet.
/// </summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{\"Sent\":[]}";
    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        if (request.Content != null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(ResponseStatus)
        {
            Content = new StringContent(ResponseBody)
        };
    }
}
