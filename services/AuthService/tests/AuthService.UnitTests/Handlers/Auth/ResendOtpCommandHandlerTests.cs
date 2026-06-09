using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

public class ResendOtpCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    private static global::AuthService.Domain.Entities.Account Pending(DateTime? otpExpired = null) => new()
    {
        Id = Guid.NewGuid(),
        Email = "p@example.com",
        PasswordHash = "x",
        FullName = "P",
        Status = AccountStatusEnum.PendingVerification,
        OtpCode = "111111",
        OtpExpiredAt = otpExpired,
        OtpPurpose = OtpPurposeEnum.Register
    };

    [Fact]
    public async Task Resend_NonPendingAccount_Returns400()
    {
        var account = Pending();
        account.Status = AccountStatusEnum.Active;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendOtpCommand { Email = "p@example.com" }, CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Resend_NotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new ResendOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendOtpCommand { Email = "ghost@example.com" }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Resend_TooFast_Returns429()
    {
        // Vừa gửi cách đây <60s → OtpExpiredAt còn > 4 phút
        var account = Pending(otpExpired: DateTime.UtcNow.AddMinutes(4).AddSeconds(30));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendOtpCommand { Email = "p@example.com" }, CancellationToken.None);

        resp.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Resend_AfterCooldown_GeneratesNewOtp_AndPublishes()
    {
        var account = Pending(otpExpired: DateTime.UtcNow.AddMinutes(2));
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendOtpCommand { Email = "p@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.OtpCode.Should().NotBe("111111");
        account.OtpPurpose.Should().Be(OtpPurposeEnum.Register);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
