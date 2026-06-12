using System.Net;
using System.Text;
using BatteryService.Infrastructure.Implements.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// Sprint 5B #90 — HttpMessageHandler stub tests cho OpenMeteoClient.
/// </summary>
public class OpenMeteoClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string ResponseBody { get; init; } = "";
        public Uri? CapturedUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private static OpenMeteoClient Make(StubHandler stub, string? baseUrl = "https://api.open-meteo.com/")
    {
        var http = new HttpClient(stub) { BaseAddress = new Uri(baseUrl!) };
        return new OpenMeteoClient(http, NullLogger<OpenMeteoClient>.Instance);
    }

    [Fact]
    public async Task GetCurrent_ValidResponse_ShouldReturnSample()
    {
        var json = """
        {
          "current": {
            "time": "2026-07-20T12:00:00",
            "temperature_2m": 31.5,
            "relative_humidity_2m": 65.0
          }
        }
        """;
        var stub = new StubHandler { Status = HttpStatusCode.OK, ResponseBody = json };
        var client = Make(stub);

        var result = await client.GetCurrentAsync(10.695m, 106.243m);

        result.Should().NotBeNull();
        result!.TemperatureCelsius.Should().Be(31.5m);
        result.Humidity.Should().Be(65m);
        result.SampleTimeUtc.Should().Be(new DateTime(2026, 7, 20, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetCurrent_LatLonInUrl_ShouldUseInvariantCulture()
    {
        var stub = new StubHandler
        {
            ResponseBody = """{"current":{"time":"2026-07-20T12:00:00","temperature_2m":30,"relative_humidity_2m":60}}"""
        };
        var client = Make(stub);

        await client.GetCurrentAsync(10.5m, 106.25m);

        stub.CapturedUri!.Query.Should().Contain("latitude=10.5");
        stub.CapturedUri.Query.Should().Contain("longitude=106.25");
    }

    [Fact]
    public async Task GetCurrent_500Error_ShouldReturnNull()
    {
        var stub = new StubHandler { Status = HttpStatusCode.InternalServerError, ResponseBody = "{}" };
        var client = Make(stub);

        var result = await client.GetCurrentAsync(10m, 106m);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrent_MissingCurrentNode_ShouldReturnNull()
    {
        var stub = new StubHandler { Status = HttpStatusCode.OK, ResponseBody = """{"hourly":{}}""" };
        var client = Make(stub);

        var result = await client.GetCurrentAsync(10m, 106m);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrent_MissingTemperatureField_ShouldReturnNull()
    {
        var stub = new StubHandler
        {
            Status = HttpStatusCode.OK,
            ResponseBody = """{"current":{"time":"2026-07-20T12:00:00","relative_humidity_2m":65}}"""
        };
        var client = Make(stub);

        var result = await client.GetCurrentAsync(10m, 106m);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrent_NetworkException_ShouldReturnNull()
    {
        var throwingHandler = new ThrowingHandler();
        var http = new HttpClient(throwingHandler) { BaseAddress = new Uri("https://api.open-meteo.com/") };
        var client = new OpenMeteoClient(http, NullLogger<OpenMeteoClient>.Instance);

        var result = await client.GetCurrentAsync(10m, 106m);
        result.Should().BeNull();
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
    }
}
