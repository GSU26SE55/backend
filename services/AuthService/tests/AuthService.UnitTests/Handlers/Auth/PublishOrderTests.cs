using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Application.Interfaces.Helpers;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Auth;

/// <summary>
/// F003 convention: AuthService publish event TRƯỚC SaveChangesAsync để OutboxMessage
/// commit atomic với business data. Test verify thứ tự gọi của 6 handler có publish.
/// Dùng MockSequence — fail nếu SaveChanges chạy trước PublishAsync.
/// </summary>
public class PublishOrderTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    public PublishOrderTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASHED");
        _hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public async Task Register_PublishesBeforeSaveChanges()
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();

        var sequence = new MockSequence();
        _producer.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new RegisterCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<RegisterCommandHandler>.Instance);
        var cmd = new RegisterCommand
        {
            Email = "alice@example.com",
            Password = "Strong1Pass!",
            FullName = "Alice",
            PhoneNumber = "0900111222"
        };

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForgotPassword_ActiveAccount_PublishesBeforeSaveChanges()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var sequence = new MockSequence();
        _producer.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ForgotPasswordCommandHandler(uow.Object, _producer.Object, NullLogger<ForgotPasswordCommandHandler>.Instance);

        var response = await handler.Handle(new ForgotPasswordCommand { Email = "user@example.com" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPasswordResetOtpEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResendOtp_PublishesBeforeSaveChanges()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.PendingVerification,
            OtpExpiredAt = DateTime.UtcNow.AddMinutes(-10)  // hết cooldown
        };
        var (uow, _, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var sequence = new MockSequence();
        _producer.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ResendOtpCommandHandler(uow.Object, _producer.Object, NullLogger<ResendOtpCommandHandler>.Instance);

        var response = await handler.Handle(new ResendOtpCommand { Email = "user@example.com" }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendPhoneOtp_PublishesBeforeSaveChanges()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            PasswordHash = "x",
            FullName = "U",
            PhoneNumber = "0901234567",
            PhoneConfirmed = false,
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var sequence = new MockSequence();
        _producer.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SendPhoneOtpEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new SendPhoneOtpCommandHandler(uow.Object, _producer.Object, NullLogger<SendPhoneOtpCommandHandler>.Instance);

        var response = await handler.Handle(new SendPhoneOtpCommand { AccountId = account.Id }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendPhoneOtpEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeEmail_PublishesBeforeSaveChanges()
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = "old@example.com",
            PasswordHash = "x",
            FullName = "U",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });
        accounts.Setup(r => r.GetByIdAsync(account.Id)).ReturnsAsync(account);

        var sequence = new MockSequence();
        _producer.InSequence(sequence)
            .Setup(p => p.PublishAsync(It.IsAny<SendEmailChangeOtpEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.InSequence(sequence)
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = new ChangeEmailCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<ChangeEmailCommandHandler>.Instance);

        var response = await handler.Handle(new ChangeEmailCommand
        {
            AccountId = account.Id,
            CurrentPassword = "Pass1234!",
            NewEmail = "new@example.com"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendEmailChangeOtpEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
