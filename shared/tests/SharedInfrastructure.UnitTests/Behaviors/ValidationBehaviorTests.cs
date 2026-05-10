using MediatR;
using Microsoft.Extensions.Logging;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using SharedInfrastructure.Behaviors;

namespace SharedInfrastructure.UnitTests.Behaviors;

public class ValidationBehaviorTests
{
    public class FooResponse : CommonResponseBase { }

    /// <summary>Request KHÔNG implement IValidatable — behavior phải pass-through.</summary>
    public record NonValidatableRequest : IRequest<FooResponse>;

    /// <summary>Request implement IValidatable — control kết quả validate qua delegate.</summary>
    public record ValidatableRequest(Func<Task<FooResponse>> ValidateImpl) : IRequest<FooResponse>, IValidatable<FooResponse>
    {
        public Task<FooResponse> ValidateAsync() => ValidateImpl();
    }

    private static ValidationBehavior<TReq, FooResponse> Sut<TReq>() where TReq : IRequest<FooResponse>
        => new(new Mock<ILogger<ValidationBehavior<TReq, FooResponse>>>().Object);

    [Fact]
    public async Task Handle_NonValidatableRequest_AlwaysCallsNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<FooResponse> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new FooResponse { IsSuccess = true, StatusCode = 200 });
        };

        var result = await Sut<NonValidatableRequest>()
            .Handle(new NonValidatableRequest(), next, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidatableRequest_ValidationPasses_CallsNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<FooResponse> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new FooResponse { IsSuccess = true, StatusCode = 200 });
        };

        var req = new ValidatableRequest(() => Task.FromResult(new FooResponse { IsSuccess = true }));
        var result = await Sut<ValidatableRequest>().Handle(req, next, CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidatableRequest_ValidationFails_ShortCircuits_DoesNotCallNext()
    {
        var nextCalled = false;
        RequestHandlerDelegate<FooResponse> next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new FooResponse { IsSuccess = true, StatusCode = 200 });
        };

        var failResp = new FooResponse { IsSuccess = false, StatusCode = 400, Message = "bad input" };
        var req = new ValidatableRequest(() => Task.FromResult(failResp));

        var result = await Sut<ValidatableRequest>().Handle(req, next, CancellationToken.None);

        nextCalled.Should().BeFalse("validation failed → handler không được gọi");
        result.Should().BeSameAs(failResp);
        result.Message.Should().Be("bad input");
    }
}
