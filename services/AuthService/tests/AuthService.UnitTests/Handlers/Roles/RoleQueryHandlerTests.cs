using AuthService.Application.CQRS.Handler.Role;
using AuthService.Application.CQRS.Query.Role;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Roles;

public class GetRoleByIdQueryHandlerTests
{
    [Fact]
    public async Task Found_ReturnsRoleDto()
    {
        var role = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            Description = "X",
            Status = RoleStatusEnum.Active,
            IsSystemRole = true
        };
        var (uow, _, _, roles, _) = MockUnitOfWork.Build(roleSeed: new[] { role });
        var handler = new GetRoleByIdQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRoleByIdQuery { Id = role.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Name.Should().Be("Admin");
        resp.Data.IsSystemRole.Should().BeTrue();
    }

    [Fact]
    public async Task NotFound_Returns404()
    {
        var (uow, _, _, roles, _) = MockUnitOfWork.Build();
        roles.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Role?)null);
        var handler = new GetRoleByIdQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRoleByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class GetRolesQueryHandlerTests
{
    private static IEnumerable<global::AuthService.Domain.Entities.Role> Seed() => new[]
    {
        new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Admin", NormalizedName = "ADMIN", Status = RoleStatusEnum.Active, IsSystemRole = true, CreatedAt = DateTime.UtcNow.AddDays(-3) },
        new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active, IsSystemRole = true, CreatedAt = DateTime.UtcNow.AddDays(-2) },
        new global::AuthService.Domain.Entities.Role { Id = Guid.NewGuid(), Name = "Supervisor", NormalizedName = "SUPERVISOR", Status = RoleStatusEnum.Inactive, IsSystemRole = false, CreatedAt = DateTime.UtcNow.AddDays(-1) }
    };

    [Fact]
    public async Task NoFilter_ReturnsAllPaged()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(roleSeed: Seed());
        var handler = new GetRolesQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolesQuery { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalItems.Should().Be(3);
        resp.Data.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task FilterByStatus_ReturnsOnlyMatching()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(roleSeed: Seed());
        var handler = new GetRolesQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolesQuery { PageNumber = 1, PageSize = 10, Status = RoleStatusEnum.Inactive }, CancellationToken.None);

        resp.Data!.TotalItems.Should().Be(1);
        resp.Data.Items[0].Name.Should().Be("Supervisor");
    }

    [Fact]
    public async Task FilterByKeyword_CaseInsensitive()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(roleSeed: Seed());
        var handler = new GetRolesQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolesQuery { PageNumber = 1, PageSize = 10, Keyword = "ADM" }, CancellationToken.None);

        resp.Data!.Items.Should().Contain(r => r.Name == "Admin");
    }

    [Fact]
    public async Task FilterByIsSystemRole_False()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build(roleSeed: Seed());
        var handler = new GetRolesQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetRolesQuery { PageNumber = 1, PageSize = 10, IsSystemRole = false }, CancellationToken.None);

        resp.Data!.Items.Should().OnlyContain(r => !r.IsSystemRole);
    }
}
