using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

public class ForgotPasswordCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task Forgot_NonExistentEmail_Returns200_NoLeak()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new ForgotPasswordCommandHandler(uow.Object, _producer.Object, NullLogger<ForgotPasswordCommandHandler>.Instance);

        var resp = await handler.Handle(new ForgotPasswordCommand { Email = "ghost@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Forgot_ActiveAccount_GeneratesOtp_PublishesEvent()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ForgotPasswordCommandHandler(uow.Object, _producer.Object, NullLogger<ForgotPasswordCommandHandler>.Instance);

        var resp = await handler.Handle(new ForgotPasswordCommand { Email = "user@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.OtpCode.Should().NotBeNull().And.HaveLength(6);
        account.OtpPurpose.Should().Be(OtpPurposeEnum.PasswordReset);
        account.OtpExpiredAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(5));
        _producer.Verify(p => p.PublishAsync(It.Is<SendPasswordResetOtpEvent>(e => e.ToEmail == "user@example.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Forgot_PendingAccount_NoOtpGenerated()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            PasswordHash = "x",
            FullName = "P",
            Status = AccountStatusEnum.PendingVerification
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ForgotPasswordCommandHandler(uow.Object, _producer.Object, NullLogger<ForgotPasswordCommandHandler>.Instance);

        var resp = await handler.Handle(new ForgotPasswordCommand { Email = "pending@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.OtpCode.Should().BeNull();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class VerifyResetOtpCommandHandlerTests
{
    private readonly Mock<IJwtHelper> _jwt = new();

    public VerifyResetOtpCommandHandlerTests()
    {
        _jwt.Setup(j => j.GenerateResetToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>())).Returns("reset-jwt");
    }

    private static global::AuthService.Domain.Entities.Account WithResetOtp(string otp = "123456", DateTime? expired = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "u@example.com",
        PasswordHash = "x",
        FullName = "U",
        Status = AccountStatusEnum.Active,
        OtpCode = otp,
        OtpExpiredAt = expired ?? DateTime.UtcNow.AddMinutes(8),
        OtpPurpose = OtpPurposeEnum.PasswordReset
    };

    [Fact]
    public async Task VerifyReset_CorrectOtp_ReturnsResetToken()
    {
        var account = WithResetOtp();
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyResetOtpCommandHandler(uow.Object, _jwt.Object);

        var resp = await handler.Handle(new VerifyResetOtpCommand { Email = "u@example.com", Otp = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.ResetToken.Should().Be("reset-jwt");
        resp.Data.ExpiresInSeconds.Should().Be(900);
        account.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public async Task VerifyReset_WrongOtp_IncrementsCounter_Returns401()
    {
        var account = WithResetOtp(otp: "999999");
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyResetOtpCommandHandler(uow.Object, _jwt.Object);

        var resp = await handler.Handle(new VerifyResetOtpCommand { Email = "u@example.com", Otp = "111111" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        account.FailedLoginAttempts.Should().Be(1);
    }

    [Fact]
    public async Task VerifyReset_OtpPurposeMismatch_Returns400()
    {
        var account = WithResetOtp();
        account.OtpPurpose = OtpPurposeEnum.Register;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyResetOtpCommandHandler(uow.Object, _jwt.Object);

        var resp = await handler.Handle(new VerifyResetOtpCommand { Email = "u@example.com", Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task VerifyReset_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new VerifyResetOtpCommandHandler(uow.Object, _jwt.Object);

        var resp = await handler.Handle(new VerifyResetOtpCommand { Email = "ghost@example.com", Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}

public class ResetPasswordCommandHandlerTests
{
    private readonly Mock<IJwtHelper> _jwt = new();
    private readonly Mock<IPasswordHasher> _hasher = new();

    public ResetPasswordCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("NEW-HASH");
    }

    [Fact]
    public async Task Reset_ValidToken_UpdatesPassword_RevokesAllSessions()
    {
        var accountId = Guid.NewGuid();
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = accountId,
            Email = "u@example.com",
            PasswordHash = "old",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            OtpCode = "111111",
            OtpPurpose = OtpPurposeEnum.PasswordReset
        };
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            Token = "rt",
            Status = RefreshTokenStatus.Active,
            IssuedAt = DateTime.UtcNow,
            ExpiredAt = DateTime.UtcNow.AddDays(7)
        };
        var (uow, accounts, refreshTokens, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, tokenSeed: new[] { token });
        accounts.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync(account);
        _jwt.Setup(j => j.ValidateResetToken("good-token")).Returns((accountId, (string?)null));

        var handler = new ResetPasswordCommandHandler(uow.Object, _hasher.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var resp = await handler.Handle(new ResetPasswordCommand { ResetToken = "good-token", NewPassword = "Strong1Pass!" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PasswordHash.Should().Be("NEW-HASH");
        account.OtpCode.Should().BeNull();
        account.OtpPurpose.Should().BeNull();
        token.Status.Should().Be(RefreshTokenStatus.Revoked);
        token.RevokedReason.Should().Be("Password reset");
    }

    [Fact]
    public async Task Reset_InvalidToken_Returns401()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        _jwt.Setup(j => j.ValidateResetToken(It.IsAny<string>())).Returns(((Guid?)null, "invalid"));

        var handler = new ResetPasswordCommandHandler(uow.Object, _hasher.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var resp = await handler.Handle(new ResetPasswordCommand { ResetToken = "bad", NewPassword = "Strong1Pass!" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Reset_AccountDeleted_Returns404()
    {
        var accountId = Guid.NewGuid();
        var (uow, accounts, _, _) = MockUnitOfWork.Build();
        accounts.Setup(r => r.GetByIdAsync(accountId)).ReturnsAsync((Account?)null);
        _jwt.Setup(j => j.ValidateResetToken("good")).Returns((accountId, (string?)null));

        var handler = new ResetPasswordCommandHandler(uow.Object, _hasher.Object, _jwt.Object, MockPublisher.NoOp().Object);
        var resp = await handler.Handle(new ResetPasswordCommand { ResetToken = "good", NewPassword = "Strong1Pass!" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
