using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Query.Account;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

public class GetAccountStatsQueryHandlerTests
{
    private static Role MakeRole(string name)
        => new() { Id = Guid.NewGuid(), Name = name, NormalizedName = name.ToUpperInvariant(), Status = RoleStatusEnum.Active };

    private static Account MakeAccount(Role? role = null, bool isDeleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@e.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            RoleId = role?.Id,
            Role = role!,
            IsDeleted = isDeleted
        };

    [Fact]
    public async Task Handle_Empty_ReturnsZeroStats()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new GetAccountStatsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountStatsQuery(), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Total.Should().Be(0);
        resp.Data.CountByRole.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_CountsByRole_ZeroFillsAllRoles()
    {
        var admin = MakeRole("Admin");
        var customer = MakeRole("Customer");
        var staff = MakeRole("Staff"); // role tồn tại nhưng chưa có account → zero-fill

        var accounts = new[]
        {
            MakeAccount(admin),
            MakeAccount(customer),
            MakeAccount(customer),
            MakeAccount(role: null),          // chưa gán role — chỉ tính vào Total
            MakeAccount(customer, isDeleted: true) // đã xóa — loại hoàn toàn
        };

        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: accounts,
            roleSeed: new[] { admin, customer, staff });
        var handler = new GetAccountStatsQueryHandler(uow.Object);

        var resp = await handler.Handle(new GetAccountStatsQuery(), CancellationToken.None);

        resp.Data!.Total.Should().Be(4);
        resp.Data.CountByRole["Admin"].Should().Be(1);
        resp.Data.CountByRole["Customer"].Should().Be(2);
        resp.Data.CountByRole["Staff"].Should().Be(0);
        resp.Data.CountByRole.Values.Sum().Should().Be(3); // account chưa gán role không nằm trong CountByRole
    }
}
