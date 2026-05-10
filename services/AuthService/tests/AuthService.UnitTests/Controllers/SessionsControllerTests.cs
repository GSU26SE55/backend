using AuthService.Api.Controllers;
using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Query.Session;
using AuthService.Application.DTOs.Response.RefreshToken;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.UnitTests.Controllers;

public class SessionsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task GetMySessions_DelegatesQuery_PassesActiveOnly()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetMySessionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionListResponse { IsSuccess = true, StatusCode = 200, Data = new() });

        var ctrl = new SessionsController(_mediator.Object);
        var result = await ctrl.GetMySessions(activeOnly: false, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
        _mediator.Verify(m => m.Send(
            It.Is<GetMySessionsQuery>(q => q.ActiveOnly == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Revoke_BuildsCommandWithSessionIdFromRoute()
    {
        var sessionId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<RevokeSessionCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionActionResponse { IsSuccess = true, StatusCode = 200, Data = 1 });

        var ctrl = new SessionsController(_mediator.Object);
        await ctrl.Revoke(sessionId, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RevokeSessionCommand>(c => c.SessionId == sessionId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAll_PassesCommandThrough()
    {
        var cmd = new RevokeAllSessionsCommand { ExceptCurrent = true, CurrentRefreshToken = "tok" };
        _mediator.Setup(m => m.Send(cmd, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionActionResponse { IsSuccess = true, StatusCode = 200, Data = 5 });

        var ctrl = new SessionsController(_mediator.Object);
        var result = await ctrl.RevokeAll(cmd, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
    }
}
