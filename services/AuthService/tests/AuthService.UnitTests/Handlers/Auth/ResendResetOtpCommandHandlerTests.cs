using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

public class ResendResetOtpCommandHandlerTests
{
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task Resend_NonExistentEmail_Returns200_NoLeak_NoPublish()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();
        var handler = new ResendResetOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendResetOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendResetOtpCommand { Email = "ghost@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resend_NotInPasswordResetFlow_NoOtpSent()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            OtpPurpose = OtpPurposeEnum.Register
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendResetOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendResetOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendResetOtpCommand { Email = "u@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resend_InResetFlow_AfterCooldown_PublishesEvent()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            OtpCode = "111111",
            OtpExpiredAt = DateTime.UtcNow.AddMinutes(2),
            OtpPurpose = OtpPurposeEnum.PasswordReset
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendResetOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendResetOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendResetOtpCommand { Email = "u@example.com" }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        account.OtpCode.Should().NotBe("111111");
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resend_TooFast_Returns429()
    {
        var account = new global::AuthService.Domain.Entities.Account
        {
            Id = Guid.NewGuid(),
            Email = "u@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active,
            OtpCode = "111111",
            OtpExpiredAt = DateTime.UtcNow.AddMinutes(9).AddSeconds(45),
            OtpPurpose = OtpPurposeEnum.PasswordReset
        };
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        var handler = new ResendResetOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendResetOtpCommandHandler>.Instance);

        var resp = await handler.Handle(new ResendResetOtpCommand { Email = "u@example.com" }, CancellationToken.None);

        resp.StatusCode.Should().Be(429);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
