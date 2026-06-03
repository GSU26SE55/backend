using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;
using TicketService.Infrastructure.BackgroundServices;

namespace TicketService.UnitTests.BackgroundServices;

public class OutboxRelayBackgroundServiceTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IOutboxRelayService> _relayService = new();
    private readonly Mock<ILogger<OutboxRelayBackgroundService>> _logger = new();
    private readonly OutboxOptions _options = new() { IntervalSeconds = 1, BatchSize = 10 };

    public OutboxRelayBackgroundServiceTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(_scope.Object);
        _scope.Setup(x => x.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider.Setup(x => x.GetService(typeof(IOutboxRelayService))).Returns(_relayService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_CallsRelayService()
    {
        // Arrange
        var sut = new OutboxRelayBackgroundService(_scopeFactory.Object, Options.Create(_options), _logger.Object);
        var cts = new CancellationTokenSource();

        _relayService.Setup(x => x.RelayBatchAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboxRelayResult { Published = 1 });

        // Act
        var task = sut.StartAsync(cts.Token);
        await Task.Delay(500); // Give it some time to run one loop
        await sut.StopAsync(cts.Token);

        // Assert
        _relayService.Verify(x => x.RelayBatchAsync(_options.BatchSize, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
