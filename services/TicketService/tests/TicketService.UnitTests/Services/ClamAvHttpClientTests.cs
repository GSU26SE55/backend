using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq.Protected;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Services;

public class ClamAvHttpClientTests
{
    private readonly Mock<ILogger<ClamAvHttpClient>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;

    public ClamAvHttpClientTests()
    {
        _loggerMock = new Mock<ILogger<ClamAvHttpClient>>();
        _handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);

        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:3000")
        };
    }

    [Fact]
    public async Task ScanAsync_WhenResponseContainsOK_ShouldReturnClean()
    {
        // Arrange
        var content = new StringContent("Everything OK");
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = content
            });

        var client = new ClamAvHttpClient(_httpClient, _loggerMock.Object);
        using var stream = new MemoryStream("test data"u8.ToArray());

        // Act
        var result = await client.ScanAsync(stream, "clean.txt", CancellationToken.None);

        // Assert
        result.Should().Be(VirusScanStatusEnum.Clean);
    }

    [Fact]
    public async Task ScanAsync_WhenResponseContainsFOUND_ShouldReturnInfected()
    {
        // Arrange
        var content = new StringContent("FOUND Eicar-Test-Signature");
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = content
            });

        var client = new ClamAvHttpClient(_httpClient, _loggerMock.Object);
        using var stream = new MemoryStream("malicious data"u8.ToArray());

        // Act
        var result = await client.ScanAsync(stream, "virus.txt", CancellationToken.None);

        // Assert
        result.Should().Be(VirusScanStatusEnum.Infected);
    }

    [Fact]
    public async Task ScanAsync_WhenResponseIsUnexpected_ShouldReturnFailedAndLogWarning()
    {
        // Arrange
        var content = new StringContent("Unknown status: BLOCKED");
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = content
            });

        var client = new ClamAvHttpClient(_httpClient, _loggerMock.Object);
        using var stream = new MemoryStream("data"u8.ToArray());

        // Act
        var result = await client.ScanAsync(stream, "unknown.txt", CancellationToken.None);

        // Assert
        result.Should().Be(VirusScanStatusEnum.Failed);

        // Verify Warning was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ClamAV unexpected response")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_WhenHttpThrowsException_ShouldReturnFailedAndLogError()
    {
        // Arrange
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = new ClamAvHttpClient(_httpClient, _loggerMock.Object);
        using var stream = new MemoryStream("data"u8.ToArray());

        // Act
        var result = await client.ScanAsync(stream, "error.txt", CancellationToken.None);

        // Assert
        result.Should().Be(VirusScanStatusEnum.Failed);

        // Verify Error was logged
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ClamAV scan failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
