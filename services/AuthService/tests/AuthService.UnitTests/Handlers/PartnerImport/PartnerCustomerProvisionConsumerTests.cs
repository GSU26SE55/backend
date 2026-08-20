using AuthService.Application.Interfaces.Helpers;
using AuthService.Application.Validation;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Consumers;
using AuthService.Infrastructure.Implements.Helpers;
using AuthService.UnitTests.Helpers;
using MassTransit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using SharedContracts.Common.Responses;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedInfrastructure.Idempotency;

namespace AuthService.UnitTests.Handlers.PartnerImport;

/// <summary>
/// I8 — cấp tài khoản khách hàng từ dữ liệu nhập của bên thứ ba.
/// </summary>
public class PartnerCustomerProvisionConsumerTests
{
    private static readonly Guid CustomerRoleId = Guid.NewGuid();

    private static Role CustomerRole() => new()
    {
        Id = CustomerRoleId,
        Name = "Customer",
        NormalizedName = "CUSTOMER",
        Status = RoleStatusEnum.Active
    };

    private static PartnerCustomerProvisionRequestedEvent Request(
        string email = "khach@example.com", string? phone = "0901234567") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "KH-001", email, "Cong ty Mat Troi", phone);

    private static (Mock<ConsumeContext<PartnerCustomerProvisionRequestedEvent>> Ctx,
                    Mock<IInboxStore> Inbox)
        BuildContext(PartnerCustomerProvisionRequestedEvent evt)
    {
        var ctx = new Mock<ConsumeContext<PartnerCustomerProvisionRequestedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        var inbox = new Mock<IInboxStore>();
        inbox.Setup(store => store.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "token"));
        inbox.Setup(store => store.CompleteAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return (ctx, inbox);
    }

    [Fact]
    public async Task Consume_NewEmail_CreatesAnActiveAccountSoBatteryServiceCanMirrorIt()
    {
        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: Array.Empty<Account>(), roleSeed: new[] { CustomerRole() });

        Account? created = null;
        accounts.Setup(r => r.AddAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account)
            .Returns(Task.CompletedTask);

        var published = new List<object>();
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<AccountActivatedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountActivatedEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);
        producer.Setup(p => p.PublishAsync(It.IsAny<PartnerCustomerProvisionedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerCustomerProvisionedEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);
        producer.Setup(p => p.PublishAsync(It.IsAny<SendPartnerImportWelcomeEvent>(), It.IsAny<CancellationToken>()))
            .Callback<SendPartnerImportWelcomeEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);

        var evt = Request();
        var (ctx, inbox) = BuildContext(evt);

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            uow.Object, new PasswordHasher(), producer.Object, Mock.Of<IPublisher>(),
            inbox.Object, NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(ctx.Object);

        created.Should().NotBeNull();
        // Trạng thái hoạt động là bắt buộc: bản sao khách hàng bên BatteryService chỉ được tạo khi
        // AuthService phát AccountActivatedEvent, và đường mời không phát sự kiện đó.
        created!.Status.Should().Be(AccountStatusEnum.Active);
        created.RoleId.Should().Be(CustomerRoleId);
        created.Email.Should().Be("khach@example.com");

        published.OfType<AccountActivatedEvent>().Should().ContainSingle()
            .Which.CreationSource.Should().Be("PartnerImport");
        published.OfType<PartnerCustomerProvisionedEvent>().Should().ContainSingle()
            .Which.WasExisting.Should().BeFalse();
        published.OfType<SendPartnerImportWelcomeEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_GeneratedPasswordSatisfiesThePasswordPolicy()
    {
        // Chỗ giữ chỗ của luồng mời dùng một chuỗi chỉ có chữ thường và số — ở đó vô hại vì tài
        // khoản chưa hoạt động. Ở đây tài khoản hoạt động ngay, nên mật khẩu phải hợp lệ thật.
        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: Array.Empty<Account>(), roleSeed: new[] { CustomerRole() });

        Account? created = null;
        accounts.Setup(r => r.AddAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account).Returns(Task.CompletedTask);

        var hasher = new PasswordHasher();
        var capturedPlaintext = new List<string>();
        var spyHasher = new Mock<IPasswordHasher>();
        spyHasher.Setup(h => h.Hash(It.IsAny<string>()))
            .Returns<string>(password => { capturedPlaintext.Add(password); return hasher.Hash(password); });

        var (ctx, inbox) = BuildContext(Request());

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            uow.Object, spyHasher.Object, Mock.Of<IMessageProducerService>(), Mock.Of<IPublisher>(),
            inbox.Object, NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(ctx.Object);

        created.Should().NotBeNull();
        capturedPlaintext.Should().ContainSingle();

        var errors = new List<Errors>();
        PasswordPolicy.AddStrongPasswordErrors(errors, capturedPlaintext[0], "Password", "Password");
        errors.Should().BeEmpty("the generated password must be usable if the customer ever needs it");
    }

    [Fact]
    public async Task Consume_ExistingEmail_LinksInsteadOfFailing()
    {
        // Đối tác nào bàn giao dữ liệu cũng có sẵn một phần khách đã là khách của mình.
        var existing = new Account
        {
            Id = Guid.NewGuid(),
            Email = "khach@example.com",
            FullName = "Khach cu",
            PasswordHash = "hash",
            Status = AccountStatusEnum.Active,
            RoleId = CustomerRoleId
        };

        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { existing }, roleSeed: new[] { CustomerRole() });

        var published = new List<object>();
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<PartnerCustomerProvisionedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerCustomerProvisionedEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);
        producer.Setup(p => p.PublishAsync(It.IsAny<AccountSyncSnapshotEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountSyncSnapshotEvent, CancellationToken>((e, _) => published.Add(e)).Returns(Task.CompletedTask);

        var (ctx, inbox) = BuildContext(Request());

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            uow.Object, new PasswordHasher(), producer.Object, Mock.Of<IPublisher>(),
            inbox.Object, NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(ctx.Object);

        accounts.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never);

        var provisioned = published.OfType<PartnerCustomerProvisionedEvent>().Should().ContainSingle().Subject;
        provisioned.WasExisting.Should().BeTrue();
        provisioned.AccountId.Should().Be(existing.Id);
        provisioned.FailureReason.Should().BeNull();

        // Bản sao bên BatteryService có thể chưa từng được dựng cho tài khoản cũ này.
        published.OfType<AccountSyncSnapshotEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Consume_PhoneAlreadyTakenByAnotherAccount_KeepsTheCustomerAndDropsThePhone()
    {
        // Đường tạo thủ công trả 409 khi số trùng. Với nhập hàng loạt thì đó là sai: một số trùng
        // sẽ làm mất luôn khách hàng, site và pin của dòng đó, trong khi số điện thoại là phụ.
        var other = new Account
        {
            Id = Guid.NewGuid(),
            Email = "nguoikhac@example.com",
            FullName = "Nguoi khac",
            PasswordHash = "hash",
            PhoneNumber = "+84901234567",
            Status = AccountStatusEnum.Active,
            RoleId = CustomerRoleId
        };

        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { other }, roleSeed: new[] { CustomerRole() });

        Account? created = null;
        accounts.Setup(r => r.AddAsync(It.IsAny<Account>()))
            .Callback<Account>(account => created = account).Returns(Task.CompletedTask);

        var (ctx, inbox) = BuildContext(Request(email: "moi@example.com", phone: "0901234567"));

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            uow.Object, new PasswordHasher(), Mock.Of<IMessageProducerService>(), Mock.Of<IPublisher>(),
            inbox.Object, NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(ctx.Object);

        created.Should().NotBeNull();
        created!.PhoneNumber.Should().BeNull();
        created.Email.Should().Be("moi@example.com");
    }

    [Fact]
    public async Task Consume_CustomerRoleMissing_ReportsFailureInsteadOfLeavingTheRowWaiting()
    {
        // Chờ tiếp cũng vô ích: không có vai trò thì không bao giờ cấp được tài khoản. Báo hỏng
        // ngay để dòng import dừng lại kèm lý do đọc được.
        var (uow, accounts, _, _) = MockUnitOfWork.Build(
            accountSeed: Array.Empty<Account>(), roleSeed: Array.Empty<Role>());

        var published = new List<PartnerCustomerProvisionedEvent>();
        var producer = new Mock<IMessageProducerService>();
        producer.Setup(p => p.PublishAsync(It.IsAny<PartnerCustomerProvisionedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PartnerCustomerProvisionedEvent, CancellationToken>((e, _) => published.Add(e))
            .Returns(Task.CompletedTask);

        var (ctx, inbox) = BuildContext(Request());

        var consumer = new PartnerCustomerProvisionRequestedConsumer(
            uow.Object, new PasswordHasher(), producer.Object, Mock.Of<IPublisher>(),
            inbox.Object, NullLogger<PartnerCustomerProvisionRequestedConsumer>.Instance);

        await consumer.Consume(ctx.Object);

        accounts.Verify(r => r.AddAsync(It.IsAny<Account>()), Times.Never);
        published.Should().ContainSingle();
        published[0].FailureReason.Should().NotBeNull();
        published[0].AccountId.Should().Be(Guid.Empty);
    }
}
