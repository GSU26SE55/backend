using MediatR;
using Microsoft.Extensions.Logging;
using SharedInfrastructure.Behaviors;

namespace SharedInfrastructure.UnitTests.Behaviors;

public class LoggingBehaviorTests
{
    public record DummyRequest(string Value) : IRequest<string>;

    [Fact]
    public async Task Handle_LogsStartAndEnd_AndReturnsNextResponse()
    {
        var logger = new Mock<ILogger<LoggingBehavior<DummyRequest, string>>>();
        var sut = new LoggingBehavior<DummyRequest, string>(logger.Object);

        RequestHandlerDelegate<string> next = () => Task.FromResult("result");

        var result = await sut.Handle(new DummyRequest("x"), next, CancellationToken.None);

        result.Should().Be("result");
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("START")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("END")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NextThrows_PropagatesException_StartLogged_EndNotLogged()
    {
        var logger = new Mock<ILogger<LoggingBehavior<DummyRequest, string>>>();
        var sut = new LoggingBehavior<DummyRequest, string>(logger.Object);

        RequestHandlerDelegate<string> next = () => throw new InvalidOperationException("nope");

        var act = async () => await sut.Handle(new DummyRequest("x"), next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("nope");

        logger.Verify(l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("END")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
    }
}
