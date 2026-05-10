using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;

namespace AuthService.UnitTests.Handlers.Auth;

public class VerifyOtpCommandHandlerTests
{
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IJwtHelper> _jwt = new();

    public VerifyOtpCommandHandlerTests()
    {
        _jwt.Setup(j => j.GenerateAccessToken(It.IsAny<Account>(), It.IsAny<IEnumerable<string>>())).ReturnsAsync("access-token");
        _jwt.Setup(j => j.GenerateRefreshToken()).Returns("refresh-token-value");
    }

    private static global::AuthService.Domain.Entities.Account PendingAccount(string otp = "123456", DateTime? otpExpired = null)
    {
        return new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            PasswordHash = "x",
            FullName = "Pending",
            Status = AccountStatusEnum.PendingVerification,
            OtpCode = otp,
            OtpExpiredAt = otpExpired ?? DateTime.UtcNow.AddMinutes(3),
            OtpPurpose = OtpPurposeEnum.Register,
            AccountRoles = new List<AccountRole>()
        };
    }

    [Fact]
    public async Task Verify_CorrectOtp_ActivatesAccount_AssignsCustomerRole_IssuesTokens()
    {
        var account = PendingAccount();
        var (uow, accounts, refreshTokens, _, accountRoles) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Data!.AccessToken.Should().Be("access-token");
        response.Data.RefreshToken.Should().Be("refresh-token-value");

        account.Status.Should().Be(AccountStatusEnum.Active);
        account.EmailConfirmed.Should().BeTrue();
        account.OtpCode.Should().BeNull();
        account.OtpPurpose.Should().BeNull();
        account.LastLoginAt.Should().NotBeNull();

        accountRoles.Verify(r => r.AddAsync(It.Is<AccountRole>(ar => ar.RoleId == CustomerRoleId && ar.IsActive)), Times.Once);
        refreshTokens.Verify(r => r.AddAsync(It.Is<RefreshToken>(rt =>
            rt.AccountId == account.Id &&
            rt.Token == "refresh-token-value" &&
            rt.Status == RefreshTokenStatus.Active
        )), Times.Once);
    }

    [Fact]
    public async Task Verify_WrongOtp_IncrementsFailedAttempts_Returns401()
    {
        var account = PendingAccount(otp: "999999");
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "111111"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        account.FailedLoginAttempts.Should().Be(1);
        account.Status.Should().Be(AccountStatusEnum.PendingVerification);
        accounts.Verify(r => r.UpdateAsync(account), Times.Once);
    }

    [Fact]
    public async Task Verify_WrongOtp_FifthAttempt_LocksAccount15Minutes()
    {
        var account = PendingAccount(otp: "999999");
        account.FailedLoginAttempts = 4;
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "111111"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(423);
        account.FailedLoginAttempts.Should().Be(5);
        account.LockoutEndAt.Should().NotBeNull();
        account.LockoutEndAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Verify_OtpExpired_Returns400()
    {
        var account = PendingAccount(otpExpired: DateTime.UtcNow.AddMinutes(-1));
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(400);
        response.Message.Should().Contain("hết hạn");
    }

    [Fact]
    public async Task Verify_AlreadyActive_Returns400()
    {
        var account = PendingAccount();
        account.Status = AccountStatusEnum.Active;
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(400);
        response.Message.Should().Contain("xác thực");
    }

    [Fact]
    public async Task Verify_EmailNotFound_Returns404()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();

        var handler = new VerifyOtpCommandHandler(uow.Object, _jwt.Object);
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "ghost@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(404);
    }
}
