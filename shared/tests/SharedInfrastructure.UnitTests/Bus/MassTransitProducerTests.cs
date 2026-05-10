using MassTransit;
using SharedContracts.Events.Root;
using SharedInfrastructure.Bus;

namespace SharedInfrastructure.UnitTests.Bus;

public class MassTransitProducerTests
{
    private record TestEvent(string Payload) : IntegrationEvent;

    [Fact]
    public async Task PublishAsync_DelegatesToPublishEndpoint_WithSameMessageAndCancellationToken()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint.Setup(e => e.Publish(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var sut = new MassTransitProducer(endpoint.Object);
        var evt = new TestEvent("hello");
        var cts = new CancellationTokenSource();

        await sut.PublishAsync(evt, cts.Token);

        endpoint.Verify(e => e.Publish(evt, cts.Token), Times.Once);
        endpoint.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PublishAsync_PropagatesEndpointException()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        endpoint.Setup(e => e.Publish(It.IsAny<TestEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("broker down"));

        var sut = new MassTransitProducer(endpoint.Object);

        var act = async () => await sut.PublishAsync(new TestEvent("x"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("broker down");
    }
}
