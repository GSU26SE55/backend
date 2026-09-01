using AuthService.Application.CQRS.Command.Admin;
using AuthService.Application.CQRS.Handler.Admin;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Admin;

/// <summary>
/// #50 QA solars.io.vn 2026-08-29 — không có chốt chặn "Admin cuối cùng". Gộp (tombstone) account
/// Admin duy nhất còn lại vào account khác là khoá cửa vĩnh viễn (không ai còn quyền quản trị).
/// </summary>
public class MergeAccountCommandHandlerTests
{
    [Fact]
    public async Task Merge_SecondaryIsLastAdmin_Returns409_DoesNotMerge()
    {
        var adminRole = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Admin",
            NormalizedName = "ADMIN",
            Status = RoleStatusEnum.Active
        };
        var customerRole = new global::AuthService.Domain.Entities.Role
        {
            Id = Guid.NewGuid(),
            Name = "Customer",
            NormalizedName = "CUSTOMER",
            Status = RoleStatusEnum.Active
        };
        var primary = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "primary@e.com",
            PasswordHash = "x",
            FullName = "Primary",
            RoleId = customerRole.Id
        };
        var lastAdmin = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "admin@e.com",
            PasswordHash = "x",
            FullName = "Admin",
            RoleId = adminRole.Id
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { primary, lastAdmin }, roleSeed: new[] { adminRole, customerRole });
        var handler = new MergeAccountCommandHandler(
            uow.Object, MockPublisher.NoOp().Object,
            new Mock<AuthService.Application.Interfaces.Services.ITokenRevocationStore>().Object,
            new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new MergeAccountCommand
        {
            PrimaryAccountId = primary.Id,
            SecondaryAccountId = lastAdmin.Id,
            Reason = "Duplicate account",
            PerformedBy = Guid.NewGuid()
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(409);
        lastAdmin.IsDeleted.Should().BeFalse("Admin cuối cùng không được tombstone");
        accounts.Verify(r => r.UpdateAsync(It.IsAny<global::AuthService.Domain.Entities.Account>()), Times.Never);
    }
}
