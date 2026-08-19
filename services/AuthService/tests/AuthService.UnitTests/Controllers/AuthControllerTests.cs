using System.Security.Claims;
using AuthService.Api.Controllers;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.DTOs.Response.Auth;
using AuthService.Application.Interfaces.Helpers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace AuthService.UnitTests.Controllers;

/// <summary>
/// Test thin AuthController logic: MediatR delegation, Google OAuth state cookie,
/// status code mapping, redirect logic. Không test handler/business.
/// </summary>
public class AuthControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IGoogleOAuthHelper> _google = new();
    private readonly IConfiguration _config;

    public AuthControllerTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleOAuth:RedirectUri"] = "https://web.test/auth/google/callback",
                ["ASPNETCORE_ENVIRONMENT"] = "Production"
            })
            .Build();
    }

    private AuthController NewCtrl(HttpContext? context = null)
    {
        var ctrl = new AuthController(_mediator.Object, _google.Object, _config);
        ctrl.ControllerContext = new ControllerContext { HttpContext = context ?? new DefaultHttpContext() };
        return ctrl;
    }

    // ============== Simple delegation actions ==============

    [Fact]
    public async Task Login_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { IsSuccess = true, StatusCode = 200, Data = new LoginResultDto { Tokens = new TokenDTO { AccessToken = "a", RefreshToken = "r" } } });

        var result = await NewCtrl().Login(new LoginCommand { Email = "u@e.com", Password = "p" }, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Login_StatusCodeZero_DefaultsTo200()
    {
        _mediator.Setup(m => m.Send(It.IsAny<LoginCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { IsSuccess = true, StatusCode = 0 });

        var result = await NewCtrl().Login(new LoginCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Register_PassesCommandThrough_MapsStatusCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RegisterCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegisterResponse { IsSuccess = true, StatusCode = 201 });

        var result = await NewCtrl().Register(new RegisterCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task VerifyOtp_FailureStatus_PropagatedToResponse()
    {
        _mediator.Setup(m => m.Send(It.IsAny<VerifyOtpCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = false, StatusCode = 423, Message = "Locked" });

        var result = await NewCtrl().VerifyOtp(new VerifyOtpCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(423);
    }

    [Fact]
    public async Task ResendOtp_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ResendOtpCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = true, StatusCode = 200 });

        var result = await NewCtrl().ResendOtp(new ResendOtpCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task RefreshToken_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<RefreshTokenCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { IsSuccess = true, StatusCode = 200, Data = new LoginResultDto { Tokens = new TokenDTO() } });

        var result = await NewCtrl().RefreshToken(new RefreshTokenCommand { RefreshToken = "x" }, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Logout_DelegatesToMediator()
    {
        var accountId = Guid.NewGuid();
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, accountId.ToString()) },
                "TestAuth"))
        };

        _mediator.Setup(m => m.Send(It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = true, StatusCode = 200 });

        var result = await NewCtrl(ctx).Logout(new LogoutCommand { RefreshToken = "rt" }, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
        _mediator.Verify(m => m.Send(
            It.Is<LogoutCommand>(c => c.AccountId == accountId && c.RefreshToken == "rt"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logout_WithoutUserClaim_Returns401()
    {
        var result = await NewCtrl().Logout(new LogoutCommand { RefreshToken = "rt" }, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _mediator.Verify(m => m.Send(It.IsAny<LogoutCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForgotPassword_AlwaysReturnsCmdResult()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ForgotPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = true, StatusCode = 200 });

        var result = await NewCtrl().ForgotPassword(new ForgotPasswordCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task VerifyResetOtp_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<VerifyResetOtpCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<ResetTokenDto> { IsSuccess = true, StatusCode = 200, Data = new ResetTokenDto { ResetToken = "tok" } });

        var result = await NewCtrl().VerifyResetOtp(new VerifyResetOtpCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ResetPassword_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ResetPasswordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = true, StatusCode = 200 });

        var result = await NewCtrl().ResetPassword(new ResetPasswordCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task ResendResetOtp_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<ResendResetOtpCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<string> { IsSuccess = true, StatusCode = 200 });

        var result = await NewCtrl().ResendResetOtp(new ResendResetOtpCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    // ============== Google OAuth ==============

    [Fact]
    public void GoogleLogin_NoConfig_Returns500()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var ctrl = new AuthController(_mediator.Object, _google.Object, emptyConfig);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = ctrl.GoogleLogin() as ObjectResult;

        result!.StatusCode.Should().Be(500);
    }

    [Fact]
    public void GoogleLogin_BuildsAuthUrl_SetsStateCookie_Redirects()
    {
        _google.Setup(g => g.BuildAuthorizationUrl(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((state, redirect) => $"https://accounts.google.com/oauth?state={state}&redirect_uri={redirect}");

        var ctx = new DefaultHttpContext();
        var ctrl = NewCtrl(ctx);

        var result = ctrl.GoogleLogin() as RedirectResult;

        result.Should().NotBeNull();
        result!.Url.Should().StartWith("https://accounts.google.com/oauth?state=");
        // State cookie phải được append
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("g_oauth_state=");
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("path=/api/auth");
        ctx.Response.Headers.SetCookie.ToString().Should().Contain("secure");
        _google.Verify(g => g.BuildAuthorizationUrl(
            It.IsAny<string>(),
            "https://web.test/auth/google/callback"), Times.Once);
    }

    [Fact]
    public async Task GoogleCallback_NoConfig_Returns500()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var ctrl = new AuthController(_mediator.Object, _google.Object, emptyConfig);
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };

        var result = await ctrl.GoogleCallback("code", "state", null, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task GoogleCallback_GoogleReturnsError_Returns401()
    {
        var ctrl = NewCtrl();
        var result = await ctrl.GoogleCallback(null, null, "access_denied", CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(401);
        var resp = result.Value as LoginResponse;
        resp!.Message.Should().Be("access_denied");
    }

    [Fact]
    public async Task GoogleCallback_MissingCodeOrState_Returns400()
    {
        var ctrl = NewCtrl();
        var result = await ctrl.GoogleCallback(null, "state", null, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task GoogleCallback_StateMismatch_Returns401()
    {
        // Cookie có state khác query state
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = "g_oauth_state=cookie-state";
        var ctrl = NewCtrl(ctx);

        var result = await ctrl.GoogleCallback("code-x", "different-state", null, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(401);
        ((LoginResponse)result.Value!).Message.Should().Be("invalid_state");
    }

    [Fact]
    public async Task GoogleCallback_StateMatch_DelegatesToMediator()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = "g_oauth_state=match-state";

        _mediator.Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { IsSuccess = true, StatusCode = 200, Data = new LoginResultDto { Tokens = new TokenDTO { AccessToken = "a", RefreshToken = "r" } } });

        var ctrl = NewCtrl(ctx);
        var result = await ctrl.GoogleCallback("code-y", "match-state", null, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
        _mediator.Verify(m => m.Send(
            It.Is<GoogleCallbackCommand>(c =>
                c.Code == "code-y"
                && c.RedirectUri == "https://web.test/auth/google/callback"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GoogleCallback_HandlerFails_PropagatesStatusCode()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["Cookie"] = "g_oauth_state=stt";

        _mediator.Setup(m => m.Send(It.IsAny<GoogleCallbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponse { IsSuccess = false, StatusCode = 409, Message = "Pending verify" });

        var ctrl = NewCtrl(ctx);
        var result = await ctrl.GoogleCallback("c", "stt", null, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(409);
    }
}
