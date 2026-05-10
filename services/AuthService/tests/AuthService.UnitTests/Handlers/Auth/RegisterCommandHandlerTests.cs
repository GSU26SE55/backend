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

public class RegisterCommandHandlerTests
{
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    public RegisterCommandHandlerTests()
    {
        _hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("HASHED");
    }

    [Fact]
    public async Task Register_NewEmail_CreatesPendingAccount_AndPublishesEvent()
    {
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build();

        var handler = new RegisterCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<RegisterCommandHandler>.Instance);
        var cmd = new RegisterCommand
        {
            Email = "  Alice@Example.com ",
            Password = "Strong1Pass!",
            FullName = "  Alice  ",
            PhoneNumber = "0900111222",
            DateOfBirth = new DateTime(1990, 5, 1),
            Address = "Hanoi"
        };

        var response = await handler.Handle(cmd, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(201);
        response.Data!.Email.Should().Be("alice@example.com");
        response.Data.OtpExpiresInSeconds.Should().Be(300);

        accounts.Verify(r => r.AddAsync(It.Is<Account>(a =>
            a.Email == "alice@example.com" &&
            a.PasswordHash == "HASHED" &&
            a.FullName == "Alice" &&
            a.PhoneNumber == "0900111222" &&
            a.Status == AccountStatusEnum.PendingVerification &&
            a.EmailConfirmed == false &&
            a.OtpPurpose == OtpPurposeEnum.Register &&
            a.OtpCode!.Length == 6 &&
            a.OtpExpiredAt > DateTime.UtcNow.AddMinutes(4) &&
            a.GoogleId == null &&
            a.Provider == null
        )), Times.Once);

        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _producer.Verify(p => p.PublishAsync(
            It.Is<SendOtpRegisterEvent>(e => e.ToEmail == "alice@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Register_EmailAlreadyActive_Returns409()
    {
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "active@example.com",
            PasswordHash = "x",
            FullName = "Existing",
            Status = AccountStatusEnum.Active
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });

        var handler = new RegisterCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<RegisterCommandHandler>.Instance);
        var response = await handler.Handle(new RegisterCommand
        {
            Email = "active@example.com",
            Password = "Strong1Pass!",
            FullName = "X"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        response.ListErrors.Should().ContainSingle(e => e.Field == "Email");
        accounts.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never);
        _producer.Verify(p => p.PublishAsync(It.IsAny<SendOtpRegisterEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Register_EmailExistsAsPending_OverwritesOtp_IsIdempotent()
    {
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "pending@example.com",
            PasswordHash = "old-hash",
            FullName = "Old Name",
            Status = AccountStatusEnum.PendingVerification,
            OtpCode = "111111",
            OtpExpiredAt = DateTime.UtcNow.AddMinutes(2)
        };
        var (uow, accounts, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { existing });

        var handler = new RegisterCommandHandler(uow.Object, _hasher.Object, _producer.Object, NullLogger<RegisterCommandHandler>.Instance);
        var response = await handler.Handle(new RegisterCommand
        {
            Email = "pending@example.com",
            Password = "Strong1Pass!",
            FullName = "New Name"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        existing.FullName.Should().Be("New Name");
        existing.PasswordHash.Should().Be("HASHED");
        existing.OtpCode.Should().NotBe("111111");
        existing.OtpPurpose.Should().Be(OtpPurposeEnum.Register);
        accounts.Verify(r => r.UpdateAsync(existing), Times.Once);
        accounts.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never);
    }
}
