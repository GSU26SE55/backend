using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using SharedInfrastructure.Behaviors;

namespace AuthService.UnitTests.Behaviors;

// ============== ValidationBehavior ==============

public class TestRequestWithValidation : IRequest<TestResponse>, IValidatable<TestResponse>
{
    public bool ShouldFail { get; set; }
    public Task<TestResponse> ValidateAsync()
    {
        var resp = new TestResponse();
        if (ShouldFail)
        {
            resp.IsSuccess = false;
            resp.StatusCode = 400;
            resp.Message = "validation-failed";
            resp.ListErrors.Add(new Errors { Field = "X", Detail = "bad" });
        }
        return Task.FromResult(resp);
    }
}

public class TestRequestWithoutValidation : IRequest<TestResponse>
{
}

public class TestResponse : CommonResponse<string>
{
}

public class ValidationBehaviorTests
{
    [Fact]
    public async Task IValidatable_ValidationFails_SkipsHandler_ReturnsValidationResult()
    {
        var behavior = new ValidationBehavior<TestRequestWithValidation, TestResponse>(
            NullLogger<ValidationBehavior<TestRequestWithValidation, TestResponse>>.Instance);

        var nextCalled = false;
        Task<TestResponse> Next()
        { nextCalled = true; return Task.FromResult(new TestResponse { Message = "from-handler" }); }

        var req = new TestRequestWithValidation { ShouldFail = true };
        var result = await behavior.Handle(req, Next, CancellationToken.None);

        nextCalled.Should().BeFalse(); // handler không chạy
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Be("validation-failed");
        result.ListErrors.Should().ContainSingle();
    }

    [Fact]
    public async Task IValidatable_ValidationPasses_CallsHandler_ReturnsHandlerResult()
    {
        var behavior = new ValidationBehavior<TestRequestWithValidation, TestResponse>(
            NullLogger<ValidationBehavior<TestRequestWithValidation, TestResponse>>.Instance);

        var nextCalled = false;
        Task<TestResponse> Next()
        { nextCalled = true; return Task.FromResult(new TestResponse { IsSuccess = true, Message = "from-handler" }); }

        var req = new TestRequestWithValidation { ShouldFail = false };
        var result = await behavior.Handle(req, Next, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.Message.Should().Be("from-handler");
    }

    [Fact]
    public async Task NonValidatableRequest_AlwaysCallsHandler()
    {
        var behavior = new ValidationBehavior<TestRequestWithoutValidation, TestResponse>(
            NullLogger<ValidationBehavior<TestRequestWithoutValidation, TestResponse>>.Instance);

        var nextCalled = false;
        Task<TestResponse> Next()
        { nextCalled = true; return Task.FromResult(new TestResponse { IsSuccess = true, Message = "ok" }); }

        var result = await behavior.Handle(new TestRequestWithoutValidation(), Next, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.Message.Should().Be("ok");
    }
}

// ============== LoggingBehavior ==============

public class LoggingBehaviorTests
{
    /// <summary>
    /// Custom test logger để capture log entries — verify được start/end log.
    /// </summary>
    private class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task LoggingBehavior_LogsStart_ThenCallsHandler_ThenLogsEnd()
    {
        var logger = new CapturingLogger<LoggingBehavior<TestRequestWithoutValidation, TestResponse>>();
        var behavior = new LoggingBehavior<TestRequestWithoutValidation, TestResponse>(logger);

        var nextCalled = false;
        Task<TestResponse> Next()
        {
            nextCalled = true;
            // Verify start log đã ghi TRƯỚC khi handler chạy
            logger.Messages.Should().ContainSingle(m => m.Contains("START"));
            return Task.FromResult(new TestResponse { IsSuccess = true });
        }

        var result = await behavior.Handle(new TestRequestWithoutValidation(), Next, CancellationToken.None);

        nextCalled.Should().BeTrue();
        logger.Messages.Should().HaveCount(2);
        logger.Messages[0].Should().Contain("START").And.Contain("TestRequestWithoutValidation");
        logger.Messages[1].Should().Contain("END").And.Contain("TestRequestWithoutValidation");
    }

    [Fact]
    public async Task LoggingBehavior_HandlerThrows_StartLoggedButNotEnd()
    {
        var logger = new CapturingLogger<LoggingBehavior<TestRequestWithoutValidation, TestResponse>>();
        var behavior = new LoggingBehavior<TestRequestWithoutValidation, TestResponse>(logger);

        Task<TestResponse> Next() => throw new InvalidOperationException("boom");

        var act = async () => await behavior.Handle(new TestRequestWithoutValidation(), Next, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // START đã log; END KHÔNG vì handler throw trước khi reach
        logger.Messages.Should().HaveCount(1);
        logger.Messages[0].Should().Contain("START");
    }
}
