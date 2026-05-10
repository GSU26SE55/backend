using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events.Root;
using SharedInfrastructure.Bus;
using SharedInfrastructure.Middleware;

namespace SharedInfrastructure.UnitTests.Bus;

// Public để Castle DynamicProxy tạo được proxy (assembly MassTransit.Abstractions là strong-named)
public record CorrFilterTestEvent : IntegrationEvent;

public class CorrelationIdFilterTests
{
    [Fact]
    public async Task PublishFilter_HttpContextHasCorrelationId_WritesToMessageHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items[CorrelationIdMiddleware.ItemKey] = "trace-123";

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var filter = new CorrelationIdPublishFilter<CorrFilterTestEvent>(accessor);

        var headers = new Mock<SendHeaders>();
        var publishCtx = new Mock<PublishContext<CorrFilterTestEvent>>();
        publishCtx.SetupGet(p => p.Headers).Returns(headers.Object);

        var nextPipe = new Mock<IPipe<PublishContext<CorrFilterTestEvent>>>();

        await filter.Send(publishCtx.Object, nextPipe.Object);

        // Production gọi `Set(string, string?)` 2-arg overload (correlationId là string)
        headers.Verify(h => h.Set(CorrelationIdMiddleware.HeaderName, (string?)"trace-123"), Times.Once);
        nextPipe.Verify(p => p.Send(publishCtx.Object), Times.Once);
    }

    [Fact]
    public async Task PublishFilter_NoHttpContext_DoesNotSetHeader()
    {
        var accessor = new HttpContextAccessor { HttpContext = null };
        var filter = new CorrelationIdPublishFilter<CorrFilterTestEvent>(accessor);

        var headers = new Mock<SendHeaders>();
        var publishCtx = new Mock<PublishContext<CorrFilterTestEvent>>();
        publishCtx.SetupGet(p => p.Headers).Returns(headers.Object);
        var nextPipe = new Mock<IPipe<PublishContext<CorrFilterTestEvent>>>();

        await filter.Send(publishCtx.Object, nextPipe.Object);

        headers.Verify(h => h.Set(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        nextPipe.Verify(p => p.Send(publishCtx.Object), Times.Once);
    }

    [Fact]
    public async Task PublishFilter_HttpContextWithoutCorrelationId_DoesNotSetHeader()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var filter = new CorrelationIdPublishFilter<CorrFilterTestEvent>(accessor);

        var headers = new Mock<SendHeaders>();
        var publishCtx = new Mock<PublishContext<CorrFilterTestEvent>>();
        publishCtx.SetupGet(p => p.Headers).Returns(headers.Object);
        var nextPipe = new Mock<IPipe<PublishContext<CorrFilterTestEvent>>>();

        await filter.Send(publishCtx.Object, nextPipe.Object);

        headers.Verify(h => h.Set(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ConsumeFilter_HeaderPresent_PassesToNextPipe()
    {
        var filter = new CorrelationIdConsumeFilter<CorrFilterTestEvent>(NullLogger<CorrelationIdConsumeFilter<CorrFilterTestEvent>>.Instance);

        var consumeCtx = new Mock<ConsumeContext<CorrFilterTestEvent>>();
        var headers = new Mock<Headers>();
        headers.Setup(h => h.Get<string>(CorrelationIdMiddleware.HeaderName, It.IsAny<string?>()))
               .Returns("incoming-trace-789");
        consumeCtx.SetupGet(c => c.Headers).Returns(headers.Object);

        var nextPipe = new Mock<IPipe<ConsumeContext<CorrFilterTestEvent>>>();

        await filter.Send(consumeCtx.Object, nextPipe.Object);

        nextPipe.Verify(p => p.Send(consumeCtx.Object), Times.Once);
    }

    [Fact]
    public async Task ConsumeFilter_NoHeader_StillCallsNextPipe()
    {
        var filter = new CorrelationIdConsumeFilter<CorrFilterTestEvent>(NullLogger<CorrelationIdConsumeFilter<CorrFilterTestEvent>>.Instance);

        var consumeCtx = new Mock<ConsumeContext<CorrFilterTestEvent>>();
        var headers = new Mock<Headers>();
        headers.Setup(h => h.Get<string>(CorrelationIdMiddleware.HeaderName, It.IsAny<string?>()))
               .Returns((string?)null);
        consumeCtx.SetupGet(c => c.Headers).Returns(headers.Object);

        var nextPipe = new Mock<IPipe<ConsumeContext<CorrFilterTestEvent>>>();

        await filter.Send(consumeCtx.Object, nextPipe.Object);

        nextPipe.Verify(p => p.Send(consumeCtx.Object), Times.Once);
    }
}
