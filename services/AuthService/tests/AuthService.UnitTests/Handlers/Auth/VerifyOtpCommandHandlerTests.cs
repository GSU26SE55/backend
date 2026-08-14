using AuthService.Application.CQRS.Command.Auth;
using AuthService.Application.CQRS.Handler.Auth;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using SharedContracts.Events;

namespace AuthService.UnitTests.Handlers.Auth;

public class VerifyOtpCommandHandlerTests
{
    private static readonly Guid CustomerRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static global::AuthService.Domain.Entities.Account PendingAccount(string otp = "123456", DateTime? otpExpired = null, Guid? roleId = null)
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
            RoleId = roleId ?? Guid.Empty
        };
    }

    [Fact]
    public async Task Verify_CorrectOtp_ActivatesAccount_AssignsCustomerRole_PublishesEvent_NoTokenIssued()
    {
        // Account self-register chưa có role → handler phải resolve "CUSTOMER" và gán RoleId.
        var account = PendingAccount();
        var customerRole = new Role { Id = CustomerRoleId, Name = "Customer", NormalizedName = "CUSTOMER", Status = RoleStatusEnum.Active };
        var (uow, accounts, refreshTokens, _) = MockUnitOfWork.Build(accountSeed: new[] { account }, roleSeed: new[] { customerRole });
        var producer = new Mock<IMessageProducerService>();

        var handler = new VerifyOtpCommandHandler(uow.Object, producer.Object, Moq.Mock.Of<MediatR.IPublisher>());
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.StatusCode.Should().Be(200);
        response.Message.Should().Contain("active");

        account.Status.Should().Be(AccountStatusEnum.Active);
        account.EmailConfirmed.Should().BeTrue();
        account.OtpCode.Should().BeNull();
        account.OtpPurpose.Should().BeNull();
        account.LastLoginAt.Should().BeNull("verify-otp should NOT log a login session");

        // 1-N refactor: account chỉ có 1 role → handler set RoleId trực tiếp thay vì add AccountRole.
        account.RoleId.Should().Be(CustomerRoleId);
        account.RoleAssignedAt.Should().NotBeNull();
        refreshTokens.Verify(r => r.AddAsync(It.IsAny<RefreshToken>()), Times.Never);
        producer.Verify(p => p.PublishAsync(It.IsAny<AccountActivatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Verify_CorrectOtp_AccountAlreadyHasRole_KeepsExistingRole()
    {
        // Account invited bởi admin với role Staff → handler KHÔNG được ghi đè sang Customer.
        var staffRoleId = Guid.NewGuid();
        var account = PendingAccount(roleId: staffRoleId);
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        account.RoleId.Should().Be(staffRoleId);
    }

    [Fact]
    public async Task Verify_WrongOtp_IncrementsFailedAttempts_Returns401()
    {
        var account = PendingAccount(otp: "999999");
        var (uow, accounts, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
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
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
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
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(401);
        response.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task Verify_AlreadyActive_Returns400()
    {
        var account = PendingAccount();
        account.Status = AccountStatusEnum.Active;
        var (uow, _, _, _) = MockUnitOfWork.Build(accountSeed: new[] { account });

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "pending@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(409);
        response.Message.Should().Contain("verified");
    }

    [Fact]
    public async Task Verify_EmailNotFound_Returns404()
    {
        var (uow, _, _, _) = MockUnitOfWork.Build();

        var handler = new VerifyOtpCommandHandler(uow.Object, new Mock<IMessageProducerService>().Object, Moq.Mock.Of<MediatR.IPublisher>());
        var response = await handler.Handle(new VerifyOtpCommand
        {
            Email = "ghost@example.com",
            Otp = "123456"
        }, CancellationToken.None);

        response.StatusCode.Should().Be(404);
    }
}
