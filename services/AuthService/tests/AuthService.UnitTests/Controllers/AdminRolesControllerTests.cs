using AuthService.Api.Controllers.Admin;
using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.CQRS.Query.Role;
using AuthService.Application.DTOs.Response.Role;
using AuthService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace AuthService.UnitTests.Controllers;

public class AdminRolesControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private AdminRolesController NewCtrl() => new(_mediator.Object);

    [Fact]
    public async Task GetAll_PassesPaginationAndFilters()
    {
        _mediator.Setup(m => m.Send(It.IsAny<GetRolesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleListResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().GetAll(pageNumber: 3, pageSize: 50, keyword: "admin",
            status: RoleStatusEnum.Active, isSystemRole: true, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetRolesQuery>(q =>
                q.PageNumber == 3 && q.PageSize == 50 && q.Keyword == "admin"
                && q.Status == RoleStatusEnum.Active && q.IsSystemRole == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_BuildsQueryWithRouteId()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<GetRoleByIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().GetById(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<GetRoleByIdQuery>(q => q.Id == id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_PassesCommandThrough()
    {
        _mediator.Setup(m => m.Send(It.IsAny<CreateRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleActionResponse { IsSuccess = true, StatusCode = 201, Data = Guid.NewGuid() });

        var result = await NewCtrl().Create(new CreateRoleCommand { Name = "Inspector" }, CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task Update_AssignsIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<UpdateRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new UpdateRoleCommand { Name = "Renamed" };
        await NewCtrl().Update(id, cmd, CancellationToken.None);

        cmd.Id.Should().Be(id);
    }

    [Fact]
    public async Task ChangeStatus_AssignsIdFromRoute()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<ChangeRoleStatusCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleActionResponse { IsSuccess = true, StatusCode = 200 });

        var cmd = new ChangeRoleStatusCommand { Status = RoleStatusEnum.Deprecated };
        await NewCtrl().ChangeStatus(id, cmd, CancellationToken.None);

        cmd.Id.Should().Be(id);
    }

    [Fact]
    public async Task Delete_BuildsCommandWithRouteId()
    {
        var id = Guid.NewGuid();
        _mediator.Setup(m => m.Send(It.IsAny<DeleteRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleActionResponse { IsSuccess = true, StatusCode = 200 });

        await NewCtrl().Delete(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<DeleteRoleCommand>(c => c.Id == id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Delete_SystemRoleForbidden_PropagatesStatus()
    {
        _mediator.Setup(m => m.Send(It.IsAny<DeleteRoleCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoleActionResponse { IsSuccess = false, StatusCode = 403, Message = "System role" });

        var result = await NewCtrl().Delete(Guid.NewGuid(), CancellationToken.None) as ObjectResult;

        result!.StatusCode.Should().Be(403);
    }
}
