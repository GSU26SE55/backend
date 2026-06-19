using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Handlers.Auth;

public class GoogleCallbackCommandHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IGoogleOAuthHelper> _google = new();
    private readonly IConfiguration _configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleOAuth:AllowedRedirectUris:0"] = "https://api/cb"
        })
        .Build();

    [Fact]
    public async Task Callback_ValidCode_ExchangesAndDelegatesToGoogleAuth()
    {
        _google.Setup(g => g.ExchangeCodeForIdTokenAsync("auth-code", "https://api/cb", It.IsAny<CancellationToken>()))
               .ReturnsAsync("idtoken-xyz");

        _mediator.Setup(m => m.Send(
                It.Is<GoogleAuthCommand>(c => c.IdToken == "idtoken-xyz"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new LoginResultDto { Tokens = new TokenDTO { AccessToken = "acc", RefreshToken = "ref" } }
            });

        var handler = new GoogleCallbackCommandHandler(_mediator.Object, _google.Object, _configuration);
        var resp = await handler.Handle(new GoogleCallbackCommand { Code = "auth-code", RedirectUri = "https://api/cb" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Tokens!.AccessToken.Should().Be("acc");
        _google.Verify(g => g.ExchangeCodeForIdTokenAsync("auth-code", "https://api/cb", It.IsAny<CancellationToken>()), Times.Once);
        _mediator.Verify(m => m.Send(It.IsAny<GoogleAuthCommand>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Callback_TokenExchangeFails_Returns401_NoMediatorCall()
    {
        _google.Setup(g => g.ExchangeCodeForIdTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((string?)null);

        var handler = new GoogleCallbackCommandHandler(_mediator.Object, _google.Object, _configuration);
        var resp = await handler.Handle(new GoogleCallbackCommand { Code = "bad-code", RedirectUri = "https://api/cb" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        _mediator.Verify(m => m.Send(It.IsAny<GoogleAuthCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Callback_DelegatedHandlerFails_PropagatesFailure()
    {
        _google.Setup(g => g.ExchangeCodeForIdTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("idtoken");
        _mediator.Setup(m => m.Send(It.IsAny<GoogleAuthCommand>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new LoginResponse { IsSuccess = false, StatusCode = 409, Message = "Email pending verify" });

        var handler = new GoogleCallbackCommandHandler(_mediator.Object, _google.Object, _configuration);
        var resp = await handler.Handle(new GoogleCallbackCommand { Code = "code", RedirectUri = "https://api/cb" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        resp.Message.Should().Contain("pending");
    }

    /// <summary>#AUTH-86: RedirectUri không trong whitelist → reject (chống open redirect).</summary>
    [Fact]
    public async Task Callback_RedirectUriNotWhitelisted_Returns400_NoExchange()
    {
        var handler = new GoogleCallbackCommandHandler(_mediator.Object, _google.Object, _configuration);
        var resp = await handler.Handle(
            new GoogleCallbackCommand { Code = "code", RedirectUri = "https://evil.example.com/steal" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        // KHÔNG được call Google API với redirectUri lạ.
        _google.Verify(g => g.ExchangeCodeForIdTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mediator.Verify(m => m.Send(It.IsAny<GoogleAuthCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// #AUTH-86: Google API timeout (Polly retry exhausted) → ExchangeCodeForIdTokenAsync trả null →
    /// handler 401 (semantic explicit: timeout == failed exchange từ user perspective).
    /// </summary>
    [Fact]
    public async Task Callback_GoogleApiTimeout_TreatsAsExchangeFail_Returns401()
    {
        // Helper trả null khi exhaust retry — đã wrap exception OperationCanceledException/HttpRequestException.
        _google.Setup(g => g.ExchangeCodeForIdTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((string?)null);

        var handler = new GoogleCallbackCommandHandler(_mediator.Object, _google.Object, _configuration);
        var resp = await handler.Handle(
            new GoogleCallbackCommand { Code = "valid-code", RedirectUri = "https://api/cb" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        _mediator.Verify(m => m.Send(It.IsAny<GoogleAuthCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Note: CSRF state mismatch test thuộc về AuthController layer (xem AuthController.ProcessCallbackAsync
    // dùng SecureCompareHelper.FixedTimeEquals). Handler chỉ nhận code+redirectUri sau khi controller
    // đã verify state. Email mismatch thuộc về GoogleAuthCommandHandler (downstream mediator).
}
