using System.Security.Claims;
using AuthService.Api.Controllers;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.DTOs.Response.Account;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.UnitTests.Controllers;

/// <summary>
/// Test thin controller logic: claim extraction (GetCurrentUserId, IsSelf), status code mapping,
/// MediatR delegation. Không test handler/business — đó là job của handler tests.
/// </summary>
public class AccountsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();

    private AccountsController BuildController(Guid? userId)
    {
        var controller = new AccountsController(_mediator.Object);
        var claims = new List<Claim>();
        if (userId.HasValue)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, userId.HasValue ? "TestAuth" : null));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    [Fact]
    public async Task GetMyProfile_DelegatesToMediator()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetMyProfileQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse { IsSuccess = true, StatusCode = 200, Data = new AccountDto { Email = "u@e.com" } });

        var ctrl = BuildController(Guid.NewGuid());
        var result = await ctrl.GetMyProfile(CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
        _mediator.Verify(m => m.Send(It.IsAny<GetMyProfileQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_NotSelf_Returns403_NoMediatorCall()
    {
        var meId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var ctrl = BuildController(meId);

        var result = await ctrl.Update(otherId, new UpdateAccountCommand(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(403);
        _mediator.Verify(m => m.Send(It.IsAny<UpdateAccountCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_IsSelf_DelegatesToMediator()
    {
        var meId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UpdateAccountCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200, Data = meId });

        var ctrl = BuildController(meId);
        var cmd = new UpdateAccountCommand { FullName = "New Name" };
        var result = await ctrl.Update(meId, cmd, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(200);
        cmd.Id.Should().Be(meId); // Controller phải gán Id từ route
        _mediator.Verify(m => m.Send(cmd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeMyPassword_NoUserId_Returns401()
    {
        var ctrl = BuildController(null); // không có claim
        var cmd = new ChangePasswordCommand();

        var result = await ctrl.ChangeMyPassword(cmd, CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
        _mediator.Verify(m => m.Send(It.IsAny<ChangePasswordCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeMyPassword_WithUserId_AssignsAccountId()
    {
        var meId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ChangePasswordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        var ctrl = BuildController(meId);
        var cmd = new ChangePasswordCommand { CurrentPassword = "old", NewPassword = "new", ConfirmPassword = "new" };
        await ctrl.ChangeMyPassword(cmd, CancellationToken.None);

        cmd.AccountId.Should().Be(meId);
    }

    [Fact]
    public async Task UpdateMe_AssignsIdFromClaim()
    {
        var meId = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UpdateAccountCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        var ctrl = BuildController(meId);
        var cmd = new UpdateAccountCommand { FullName = "X" };
        await ctrl.UpdateMe(cmd, CancellationToken.None);

        cmd.Id.Should().Be(meId);
    }

    [Fact]
    public async Task UpdateMe_NoUserId_Returns401()
    {
        var ctrl = BuildController(null);
        var result = await ctrl.UpdateMe(new UpdateAccountCommand(), CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
