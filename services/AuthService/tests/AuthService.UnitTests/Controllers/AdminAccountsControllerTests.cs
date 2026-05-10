using AuthService.Api.Controllers.Admin;
using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Session;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Application.CQRS.Query.Session;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.DTOs.Response.RefreshToken;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.UnitTests.Controllers;

public class AdminAccountsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private AdminAccountsController NewCtrl() => new(_mediator.Object);

    [Fact]
    public async Task GetAll_PassesPaginationParams()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetAccountsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountListResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().GetAll(pageNumber: 2, pageSize: 25, keyword: "alice", status: AccountStatusEnum.Active,
            roleId: Guid.NewGuid(), emailConfirmed: true, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetAccountsQuery>(q =>
                q.PageNumber == 2 && q.PageSize == 25 && q.Keyword == "alice"
                && q.Status == AccountStatusEnum.Active && q.EmailConfirmed == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_BuildsQueryWithRouteId()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetAccountByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().GetById(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetAccountByIdQuery>(q => q.Id == id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_AssignsIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UpdateAccountCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new UpdateAccountCommand { FullName = "X" };
        await NewCtrl().Update(id, cmd, CancellationToken.None);

        cmd.Id.Should().Be(id);
    }

    [Fact]
    public async Task ChangeStatus_AssignsIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ChangeAccountStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new ChangeAccountStatusCommand { Status = AccountStatusEnum.Banned };
        await NewCtrl().ChangeStatus(id, cmd, CancellationToken.None);

        cmd.Id.Should().Be(id);
    }

    [Fact]
    public async Task AssignRoles_AssignsAccountIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<AssignRolesCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new AssignRolesCommand { RoleIds = new() { Guid.NewGuid() } };
        await NewCtrl().AssignRoles(id, cmd, CancellationToken.None);

        cmd.AccountId.Should().Be(id);
    }

    [Fact]
    public async Task RevokeRole_BuildsCommandWithRouteParams()
    {
        var aid = Guid.NewGuid();
        var rid = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<RevokeRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().RevokeRole(aid, rid, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RevokeRoleCommand>(c => c.AccountId == aid && c.RoleId == rid),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unlock_BuildsUnlockCommandWithRouteId()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UnlockAccountCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountActionResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().Unlock(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<UnlockAccountCommand>(c => c.Id == id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSessions_BuildsQueryWithRouteIdAndActiveOnly()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetAccountSessionsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionListResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().GetSessions(id, activeOnly: false, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetAccountSessionsQuery>(q => q.AccountId == id && q.ActiveOnly == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAllSessions_AssignsAccountIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<AdminRevokeAccountSessionsCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new AdminRevokeAccountSessionsCommand { Reason = "test" };
        await NewCtrl().RevokeAllSessions(id, cmd, CancellationToken.None);

        cmd.AccountId.Should().Be(id);
    }
}
