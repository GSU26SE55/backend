using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Configuration;

namespace AuthService.UnitTests.Handlers.Auth;

public class Enable2FACommandHandlerTests
{
    private static IConfiguration Cfg() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Issuer"] = "TestApp" }).Build();

    [Fact]
    public async Task Enable_ActiveAccount_GeneratesSecret_BuildsOtpAuthUri()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new Enable2FACommandHandler(uow.Object, Cfg());

        var resp = await handler.Handle(new Enable2FACommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Secret.Should().NotBeNullOrEmpty();
        resp.Data.OtpAuthUri.Should().StartWith("otpauth://totp/");
        resp.Data.OtpAuthUri.Should().Contain("issuer=TestApp");
        account.TwoFactorEnabled.Should().BeTrue();
        account.TwoFactorSecret.Should().Be(resp.Data.Secret);
    }

    [Fact]
    public async Task Enable_AlreadyEnabled_Returns400()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            TwoFactorEnabled = true,
            TwoFactorSecret = "EXISTING"
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new Enable2FACommandHandler(uow.Object, Cfg());

        var resp = await handler.Handle(new Enable2FACommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Enable_NotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Account?)null);
        var handler = new Enable2FACommandHandler(uow.Object, Cfg());

        var resp = await handler.Handle(new Enable2FACommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class Disable2FACommandHandlerTests
{
    [Fact]
    public async Task Disable_2FAEnabled_ClearsSecret()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            TwoFactorEnabled = true,
            TwoFactorSecret = "SECRET"
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new Disable2FACommandHandler(uow.Object);

        var resp = await handler.Handle(new Disable2FACommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.TwoFactorEnabled.Should().BeFalse();
        account.TwoFactorSecret.Should().BeNull();
    }

    [Fact]
    public async Task Disable_AlreadyOff_NoOp_Returns200()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            TwoFactorEnabled = false
        };
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new Disable2FACommandHandler(uow.Object);

        var resp = await handler.Handle(new Disable2FACommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Disable_NotFound_Returns404()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((Account?)null);
        var handler = new Disable2FACommandHandler(uow.Object);

        var resp = await handler.Handle(new Disable2FACommand { AccountId = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
