using AuthService.Application.CQRS.Command.Role;
using AuthService.Application.CQRS.Handler.Role;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Roles;

public class CreateRoleCommandHandlerTests
{
    [Fact]
    public async Task Create_NewName_AddsRole_Returns201()
    {
        var (uow, _, _, roles) = MockUnitOfWork.Build();
        var handler = new CreateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new CreateRoleCommand { Name = "Inspector", Description = "Bảo trì" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(201);
        roles.Verify(r => r.AddAsync(It.Is<global::AuthService.Domain.Entities.Role>(role =>
            role.Name == "Inspector" &&
            role.NormalizedName == "INSPECTOR" &&
            role.Status == RoleStatusEnum.Active &&
            role.IsSystemRole == false
        )), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateName_Returns409()
    {
        var existing = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "X", NormalizedName = "INSPECTOR", Status = RoleStatusEnum.Active };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { existing });
        var handler = new CreateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new CreateRoleCommand { Name = "Inspector" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        roles.Verify(r => r.AddAsync(It.IsAny<global::AuthService.Domain.Entities.Role>()), Times.Never);
    }
}

public class UpdateRoleCommandHandlerTests
{
    [Fact]
    public async Task Update_CustomRole_UpdatesNameAndDescription()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Old",
            NormalizedName = "OLD",
            Description = "old desc",
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        roles.Setup(r => r.GetByIdAsync(role.Id)).ReturnsAsync(role);
        var handler = new UpdateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new UpdateRoleCommand { Id = role.Id, Name = "New", Description = "new desc" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        role.Name.Should().Be("New");
        role.NormalizedName.Should().Be("NEW");
        role.Description.Should().Be("new desc");
    }

    [Fact]
    public async Task Update_SystemRole_Returns403()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            Status = RoleStatusEnum.Active,
            IsSystemRole = true
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new UpdateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new UpdateRoleCommand { Id = role.Id, Name = "X" }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Update_DuplicateName_Returns409()
    {
        var role = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Me", NormalizedName = "ME", Status = RoleStatusEnum.Active, IsSystemRole = false };
        var other = new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Other", NormalizedName = "OTHER", Status = RoleStatusEnum.Active };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role, other });
        roles.Setup(r => r.GetByIdAsync(role.Id)).ReturnsAsync(role);
        var handler = new UpdateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new UpdateRoleCommand { Id = role.Id, Name = "Other" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var (uow, _, _, roles) = MockUnitOfWork.Build();
        roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Role?)null);
        var handler = new UpdateRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new UpdateRoleCommand { Id = Guid.NewGuid(), Name = "X" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class ChangeRoleStatusCommandHandlerTests
{
    [Fact]
    public async Task Change_CustomRole_DiffStatus_Updates()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "X",
            NormalizedName = "X",
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new ChangeRoleStatusCommandHandler(uow.Object);

        var resp = await handler.Handle(new ChangeRoleStatusCommand { Id = role.Id, Status = RoleStatusEnum.Inactive }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        role.Status.Should().Be(RoleStatusEnum.Inactive);
    }

    [Fact]
    public async Task Change_SystemRole_Returns403()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            Status = RoleStatusEnum.Active,
            IsSystemRole = true
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new ChangeRoleStatusCommandHandler(uow.Object);

        var resp = await handler.Handle(new ChangeRoleStatusCommand { Id = role.Id, Status = RoleStatusEnum.Inactive }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Change_SameStatus_NoOp()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "X",
            NormalizedName = "X",
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new ChangeRoleStatusCommandHandler(uow.Object);

        var resp = await handler.Handle(new ChangeRoleStatusCommand { Id = role.Id, Status = RoleStatusEnum.Active }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Message.Should().Contain("không thay đổi");
    }
}

public class DeleteRoleCommandHandlerTests
{
    [Fact]
    public async Task Delete_CustomRole_NotInUse_DeletesSoft()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "X",
            NormalizedName = "X",
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new DeleteRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteRoleCommand { Id = role.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        roles.Verify(r => r.DeleteAsync(role), Times.Once);
    }

    [Fact]
    public async Task Delete_SystemRole_Returns403()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            Status = RoleStatusEnum.Active,
            IsSystemRole = true
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new DeleteRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteRoleCommand { Id = role.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Delete_RoleInUse_Returns409()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "X",
            NormalizedName = "X",
            Status = RoleStatusEnum.Active,
            IsSystemRole = false
        };
        // 1-N refactor: account dùng role này được tính qua Account.RoleId thay vì AccountRole join table.
        var accountUsingRole = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = "x",
            FullName = "User",
            RoleId = role.Id
        };
        var (uow, _, _, roles) = MockUnitOfWork.Build(roleSeed: new[] { role }, accountSeed: new[] { accountUsingRole });
        var handler = new DeleteRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteRoleCommand { Id = role.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Delete_NotFound_Returns404()
    {
        var (uow, _, _, roles) = MockUnitOfWork.Build();
        roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Role?)null);
        var handler = new DeleteRoleCommandHandler(uow.Object);

        var resp = await handler.Handle(new DeleteRoleCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
