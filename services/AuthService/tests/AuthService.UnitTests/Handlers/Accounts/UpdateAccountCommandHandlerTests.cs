using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Accounts;

public class UpdateAccountCommandHandlerTests
{
    [Fact]
    public async Task Update_ChangePhone_ResetsPhoneConfirmedToFalse()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "Old",
            PhoneNumber = "0900111",
            PhoneConfirmed = true,
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new UpdateAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new UpdateAccountCommand
        {
            Id = account.Id,
            FullName = "New",
            PhoneNumber = "0900222"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.FullName.Should().Be("New");
        account.PhoneNumber.Should().Be("0900222");
        account.PhoneConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Update_SamePhone_DoesNotResetConfirmed()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "Old",
            PhoneNumber = "0900111",
            PhoneConfirmed = true,
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new UpdateAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new UpdateAccountCommand
        {
            Id = account.Id,
            FullName = "New",
            PhoneNumber = "0900111"
        }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PhoneConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Update_DuplicatePhone_Returns409()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "Me",
            Status = AccountStatusEnum.Active
        };
        var other = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "other@example.com",
            PasswordHash = "x",
            FullName = "Other",
            PhoneNumber = "0900222",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account, other });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);
        var handler = new UpdateAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new UpdateAccountCommand
        {
            Id = account.Id,
            FullName = "Me",
            PhoneNumber = "0900222"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Update_NotFound_Returns404()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((global::AuthService.Domain.Entities.Account?)null);
        var handler = new UpdateAccountCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object);

        var resp = await handler.Handle(new UpdateAccountCommand
        {
            Id = Guid.NewGuid(),
            FullName = "X"
        }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
