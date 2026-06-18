using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

public class SendPhoneOtpCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    private static global::AuthService.Domain.Entities.Account WithPhone(string? phone = "+84900111", bool confirmed = false) => new()
    {
        Id = Guid.NewGuid(),
        Email = "u@example.com",
        PasswordHash = "x",
        FullName = "U",
        Status = AccountStatusEnum.Active,
        PhoneNumber = phone,
        PhoneConfirmed = confirmed
    };

    [Fact]
    public async Task Send_NoPhone_Returns400()
    {
        var account = WithPhone(phone: null);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new SendPhoneOtpCommandHandler(uow.Object, _producer.Object, NullLogger<SendPhoneOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new SendPhoneOtpCommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task Send_AlreadyConfirmed_Returns400()
    {
        var account = WithPhone(confirmed: true);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new SendPhoneOtpCommandHandler(uow.Object, _producer.Object, NullLogger<SendPhoneOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new SendPhoneOtpCommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Send_FreshAccount_GeneratesOtp_PublishesEvent()
    {
        var account = WithPhone();
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new SendPhoneOtpCommandHandler(uow.Object, _producer.Object, NullLogger<SendPhoneOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new SendPhoneOtpCommand { AccountId = account.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.OtpCode!.Length.Should().Be(6);
        account.OtpPurpose.Should().Be(OtpPurposeEnum.PhoneVerify);
        // Sprint SMS Phase 9 atomic switch (#SMS-38): publish SendSmsCommand qua SmsService gateway
        // thay vì SendPhoneOtpEvent trực tiếp. Body chứa OTP code, source="auth", category="otp".
        _producer.Verify(p => p.PublishAsync(It.Is<SendSmsCommand>(c =>
            c.PhoneNumber == "+84900111" &&
            c.Message.Contains(account.OtpCode!) &&
            c.SourceService == "auth" &&
            c.Category == "otp"),
            It.IsAny<CancellationToken>()), Times.Once);
        // KHÔNG còn publish SendPhoneOtpEvent — atomic switch yêu cầu xoá hẳn trong cùng commit.
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPhoneOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Send_TooFastResend_Returns429()
    {
        var account = WithPhone();
        account.OtpCode = "111111";
        account.OtpPurpose = OtpPurposeEnum.PhoneVerify;
        account.OtpExpiredAt = DateTime.UtcNow.AddMinutes(4).AddSeconds(45);
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new SendPhoneOtpCommandHandler(uow.Object, _producer.Object, NullLogger<SendPhoneOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new SendPhoneOtpCommand { AccountId = account.Id }, CancellationToken.None);

        resp.StatusCode.Should().Be(429);
    }
}

public class VerifyPhoneOtpCommandHandlerTests
{
    private static global::AuthService.Domain.Entities.Account WithPhoneOtp(string otp = "123456", DateTime? expired = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "u@example.com",
        PasswordHash = "x",
        FullName = "U",
        Status = AccountStatusEnum.Active,
        PhoneNumber = "0900111",
        PhoneConfirmed = false,
        OtpCode = otp,
        OtpExpiredAt = expired ?? DateTime.UtcNow.AddMinutes(3),
        OtpPurpose = OtpPurposeEnum.PhoneVerify
    };

    [Fact]
    public async Task Verify_CorrectOtp_SetsPhoneConfirmedTrue()
    {
        var account = WithPhoneOtp();
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyPhoneOtpCommandHandler(uow.Object);

        var resp = await handler.Handle(new VerifyPhoneOtpCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.PhoneConfirmed.Should().BeTrue();
        account.OtpCode.Should().BeNull();
        account.OtpPurpose.Should().BeNull();
    }

    [Fact]
    public async Task Verify_AlreadyConfirmed_Returns400()
    {
        var account = WithPhoneOtp();
        account.PhoneConfirmed = true;
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyPhoneOtpCommandHandler(uow.Object);

        var resp = await handler.Handle(new VerifyPhoneOtpCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Verify_WrongOtp_IncrementsCounter()
    {
        var account = WithPhoneOtp(otp: "999999");
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyPhoneOtpCommandHandler(uow.Object);

        var resp = await handler.Handle(new VerifyPhoneOtpCommand { AccountId = account.Id, Otp = "111111" }, CancellationToken.None);

        resp.StatusCode.Should().Be(401);
        account.FailedLoginAttempts.Should().Be(1);
        account.PhoneConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_OtpExpired_Returns422()
    {
        var account = WithPhoneOtp(expired: DateTime.UtcNow.AddMinutes(-1));
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new VerifyPhoneOtpCommandHandler(uow.Object);

        var resp = await handler.Handle(new VerifyPhoneOtpCommand { AccountId = account.Id, Otp = "123456" }, CancellationToken.None);

        resp.StatusCode.Should().Be(422);
    }
}
