using AuthService.Application.CQRS.Command.Permission;
using AuthService.Application.CQRS.Handler.Permission;
using AuthService.Application.CQRS.Query.Permission;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.PermissionTests;

public class SetRolePermissionsCommandHandlerTests
{
    private readonly Mock<MediatR.IPublisher> _publisher = MockPublisher.NoOp();

    private static Permission NewPerm(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Module = code.Split('.')[0],
        Description = code,
        IsSystemPermission = false,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Set_NewPermissions_AddsAll()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Tech", NormalizedName = "TECH", Status = RoleStatusEnum.Active, IsSystemRole = false };
        var perm1 = NewPerm("battery.view");
        var perm2 = NewPerm("ticket.view");
        var (uow, _, _, rolesMock) = MockUnitOfWork.Build(roleSeed: new[] { role });

        var permsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<Permission>>();
        permsMock.Setup(r => r.GetAllAsync()).Returns(new[] { perm1, perm2 }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Permissions).Returns(permsMock.Object);

        var rolePermsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<RolePermission>>();
        rolePermsMock.Setup(r => r.GetAllAsync()).Returns(Array.Empty<RolePermission>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermsMock.Object);

        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);
        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = role.Id,
            PermissionIds = new List<Guid> { perm1.Id, perm2.Id }
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        rolePermsMock.Verify(r => r.AddAsync(It.IsAny<RolePermission>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Set_RemovesPermissionNotInList()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Tech", NormalizedName = "TECH", Status = RoleStatusEnum.Active, IsSystemRole = false };
        var keepPerm = NewPerm("battery.view");
        var removePerm = NewPerm("ticket.delete");

        var existing = new[]
        {
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = keepPerm.Id },
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = removePerm.Id }
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var permsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<Permission>>();
        permsMock.Setup(r => r.GetAllAsync()).Returns(new[] { keepPerm, removePerm }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Permissions).Returns(permsMock.Object);

        var rolePermsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<RolePermission>>();
        rolePermsMock.Setup(r => r.GetAllAsync()).Returns(existing.AsQueryable().BuildMock());
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermsMock.Object);

        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);
        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = role.Id,
            PermissionIds = new List<Guid> { keepPerm.Id }
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        rolePermsMock.Verify(r => r.DeleteAsync(It.Is<RolePermission>(rp => rp.PermissionId == removePerm.Id)), Times.Once);
        rolePermsMock.Verify(r => r.AddAsync(It.IsAny<RolePermission>()), Times.Never); // keepPerm đã tồn tại
    }

    [Fact]
    public async Task Set_SystemRole_BlockedByDefault_Returns400()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN", Status = RoleStatusEnum.Active, IsSystemRole = true };
        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });

        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);
        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = role.Id,
            PermissionIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Set_SystemRole_AllowedWhenOverride()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Manager", NormalizedName = "MANAGER", Status = RoleStatusEnum.Active, IsSystemRole = true };
        var perm = NewPerm("user.view");
        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });

        var permsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<Permission>>();
        permsMock.Setup(r => r.GetAllAsync()).Returns(new[] { perm }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Permissions).Returns(permsMock.Object);

        var rolePermsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<RolePermission>>();
        rolePermsMock.Setup(r => r.GetAllAsync()).Returns(Array.Empty<RolePermission>().AsQueryable().BuildMock());
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermsMock.Object);

        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);
        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = role.Id,
            PermissionIds = new List<Guid> { perm.Id },
            AllowSystemRole = true
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Set_RoleNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);

        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = Guid.NewGuid(),
            PermissionIds = new List<Guid> { Guid.NewGuid() }
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Set_PermissionIdNotExist_Returns400()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "Tech", NormalizedName = "TECH", Status = RoleStatusEnum.Active, IsSystemRole = false };
        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });

        var handler = new SetRolePermissionsCommandHandler(uow.Object, _publisher.Object);
        var resp = await handler.Handle(new SetRolePermissionsCommand
        {
            RoleId = role.Id,
            PermissionIds = new List<Guid> { Guid.NewGuid() } // không tồn tại
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}

public class GetRolePermissionsQueryHandlerTests
{
    [Fact]
    public async Task GetByRole_ReturnsOnlyPermissionsForThatRole()
    {
        var role = new Role { Id = Guid.NewGuid(), Name = "X", NormalizedName = "X", Status = RoleStatusEnum.Active };
        var p1 = new Permission { Id = Guid.NewGuid(), Code = "a.view", Module = "A" };
        var p2 = new Permission { Id = Guid.NewGuid(), Code = "b.view", Module = "B" };
        var p3 = new Permission { Id = Guid.NewGuid(), Code = "c.view", Module = "C" };

        var otherRoleId = Guid.NewGuid();
        var rolePerms = new[]
        {
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p1.Id },
            new RolePermission { Id = Guid.NewGuid(), RoleId = role.Id, PermissionId = p2.Id },
            new RolePermission { Id = Guid.NewGuid(), RoleId = otherRoleId, PermissionId = p3.Id }
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var permsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<Permission>>();
        permsMock.Setup(r => r.GetAllAsync()).Returns(new[] { p1, p2, p3 }.AsQueryable().BuildMock());
        uow.SetupGet(u => u.Permissions).Returns(permsMock.Object);
        var rolePermsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<RolePermission>>();
        rolePermsMock.Setup(r => r.GetAllAsync()).Returns(rolePerms.AsQueryable().BuildMock());
        uow.SetupGet(u => u.RolePermissions).Returns(rolePermsMock.Object);

        var handler = new GetRolePermissionsQueryHandler(uow.Object);
        var resp = await handler.Handle(new GetRolePermissionsQuery { RoleId = role.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var permissions = resp.Data!;
        permissions.Should().HaveCount(2);
        permissions.Select(p => p.Code).Should().Contain(new[] { "a.view", "b.view" });
    }

    [Fact]
    public async Task GetByRole_RoleNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetRolePermissionsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolePermissionsQuery { RoleId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetByRole_EmptyId_Returns400()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetRolePermissionsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolePermissionsQuery { RoleId = Guid.Empty }, CancellationToken.None);

        resp.StatusCode.Should().Be(400);
    }
}
