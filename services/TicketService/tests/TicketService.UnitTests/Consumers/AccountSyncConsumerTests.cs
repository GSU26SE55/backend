using MassTransit;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

public class AccountSyncConsumerTests
{
    private readonly Mock<ITicketUnitOfWork> _uowMock;
    private readonly Mock<IInboxStore> _inboxMock;
    private readonly Mock<IGenericRepository<StaffAccount>> _staffRepoMock;
    private readonly Mock<IGenericRepository<CustomerAccount>> _customerRepoMock;

    public AccountSyncConsumerTests()
    {
        _uowMock = new Mock<ITicketUnitOfWork>();
        _inboxMock = new Mock<IInboxStore>();
        _staffRepoMock = new Mock<IGenericRepository<StaffAccount>>();
        _customerRepoMock = new Mock<IGenericRepository<CustomerAccount>>();

        _uowMock.SetupGet(u => u.StaffAccounts).Returns(_staffRepoMock.Object);
        _uowMock.SetupGet(u => u.CustomerAccounts).Returns(_customerRepoMock.Object);
        _staffRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<StaffAccount>().AsQueryable().BuildMock());
        _customerRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<CustomerAccount>().AsQueryable().BuildMock());
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
    }

    #region TicketAccountActivatedConsumer Tests

    [Fact]
    public async Task TicketAccountActivatedConsumer_DuplicateMessage_ShouldReturnEarly()
    {
        // Arrange
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var consumer = new TicketAccountActivatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountActivatedEvent(Guid.NewGuid(), "staff@test.com", "Staff Name", "12345", "Staff", "Register");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketAccountActivatedConsumer_NewStaff_ShouldAddStaffAccount()
    {
        // Arrange
        var staffList = new List<StaffAccount>().AsQueryable().BuildMock();
        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(staffList);

        var consumer = new TicketAccountActivatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountActivatedEvent(Guid.NewGuid(), "staff@test.com", "Staff Name", "12345", "Staff", "Register");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        _staffRepoMock.Verify(r => r.AddAsync(It.Is<StaffAccount>(s =>
            s.Id == message.AccountId &&
            s.AccountId == message.AccountId &&
            s.Email == message.Email &&
            s.FullName == message.FullName &&
            s.Status == AccountStatusEnum.Active
        )), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketAccountActivatedConsumer_ExistingStaff_ShouldUpdateStaffAccount()
    {
        // Arrange
        var existingStaff = new StaffAccount { AccountId = Guid.NewGuid(), Email = "old@test.com" };
        var staffList = new List<StaffAccount> { existingStaff }.AsQueryable().BuildMock();
        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(staffList);

        var consumer = new TicketAccountActivatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountActivatedEvent(existingStaff.AccountId, "new@test.com", "New Manager Name", "12345", "Manager", "Register");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        _staffRepoMock.Verify(r => r.UpdateAsync(It.Is<StaffAccount>(s =>
            s.AccountId == message.AccountId &&
            s.Email == message.Email &&
            s.FullName == message.FullName &&
            s.Status == AccountStatusEnum.Active
        )), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketAccountActivatedConsumer_NewCustomer_ShouldAddCustomerAccount()
    {
        // Arrange
        var customerList = new List<CustomerAccount>().AsQueryable().BuildMock();
        _customerRepoMock.Setup(r => r.GetAllAsync()).Returns(customerList);

        var consumer = new TicketAccountActivatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountActivatedEvent(Guid.NewGuid(), "customer@test.com", "Customer Name", "12345", "Customer", "Register");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        _customerRepoMock.Verify(r => r.AddAsync(It.Is<CustomerAccount>(c =>
            c.Id == message.AccountId &&
            c.AccountId == message.AccountId &&
            c.Email == message.Email &&
            c.FullName == message.FullName &&
            c.PhoneNumber == message.PhoneNumber &&
            c.Status == AccountStatusEnum.Active
        )), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketAccountActivatedConsumer_ExistingCustomer_ShouldUpdateCustomerAccount()
    {
        // Arrange
        var existingCustomer = new CustomerAccount { AccountId = Guid.NewGuid(), Email = "old@test.com" };
        var customerList = new List<CustomerAccount> { existingCustomer }.AsQueryable().BuildMock();
        _customerRepoMock.Setup(r => r.GetAllAsync()).Returns(customerList);

        var consumer = new TicketAccountActivatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountActivatedEvent(existingCustomer.AccountId, "new@test.com", "New Customer Name", "54321", "Customer", "Register");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        _customerRepoMock.Verify(r => r.UpdateAsync(It.Is<CustomerAccount>(c =>
            c.AccountId == message.AccountId &&
            c.Email == message.Email &&
            c.FullName == message.FullName &&
            c.PhoneNumber == message.PhoneNumber &&
            c.Status == AccountStatusEnum.Active
        )), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TicketAccountStatusChangedConsumer Tests

    [Fact]
    public async Task TicketAccountStatusChangedConsumer_DuplicateMessage_ShouldReturnEarly()
    {
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var consumer = new TicketAccountStatusChangedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountStatusChangedEvent(Guid.NewGuid(), "staff@test.com", 1, 5, "Reason");
        var context = MockConsumeContext(message);

        await consumer.Consume(context);

        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketAccountStatusChangedConsumer_StaffAndCustomer_ShouldUpdateStatus()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            AccountId = accountId,
            Status = AccountStatusEnum.Active,
            AvatarUrl = "https://cdn.example.com/staff.png"
        };
        var customer = new CustomerAccount
        {
            AccountId = accountId,
            Status = AccountStatusEnum.Active,
            AvatarUrl = "https://cdn.example.com/customer.png"
        };

        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());
        _customerRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<CustomerAccount> { customer }.AsQueryable().BuildMock());

        var consumer = new TicketAccountStatusChangedConsumer(_uowMock.Object, _inboxMock.Object);
        // Số trong event là của enum AuthService: Active=1 → Suspended=4. KHÔNG phải số của enum
        // TicketService (bên này Suspended=5) — hai enum lệch nhau một bậc, đọc nhầm nguồn chính là
        // nguyên nhân của lỗi mà AuthAccountStatusMapper sinh ra để chặn.
        var message = new AccountStatusChangedEvent(accountId, "staff@test.com", 1, 4, "Reason");
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        staff.Status.Should().Be(AccountStatusEnum.Suspended);
        customer.Status.Should().Be(AccountStatusEnum.Suspended);
        // Contract cũ không gửi AvatarUrl. Null phải giữ nguyên, không được hiểu là xoá avatar.
        staff.AvatarUrl.Should().Be("https://cdn.example.com/staff.png");
        customer.AvatarUrl.Should().Be("https://cdn.example.com/customer.png");
        _staffRepoMock.Verify(r => r.UpdateAsync(staff), Times.Once);
        _customerRepoMock.Verify(r => r.UpdateAsync(customer), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketAccountStatusChangedConsumer_ExplicitAvatar_ShouldUpdateBothHistoricalProjections()
    {
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount { AccountId = accountId, AvatarUrl = "old-staff" };
        var customer = new CustomerAccount { AccountId = accountId, AvatarUrl = "old-customer" };
        _staffRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());
        _customerRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<CustomerAccount> { customer }.AsQueryable().BuildMock());

        var consumer = new TicketAccountStatusChangedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountStatusChangedEvent(
            accountId, "staff@test.com", 1, 1, "Reason",
            Role: "Staff", AvatarUrl: " https://cdn.example.com/new.png ");

        await consumer.Consume(MockConsumeContext(message));

        staff.AvatarUrl.Should().Be("https://cdn.example.com/new.png");
        customer.AvatarUrl.Should().Be("https://cdn.example.com/new.png");
    }

    #endregion

    #region TicketAccountProfileUpdatedConsumer Tests

    [Fact]
    public async Task TicketAccountProfileUpdatedConsumer_DuplicateMessage_ShouldReturnEarly()
    {
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var consumer = new TicketAccountProfileUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountProfileUpdatedEvent(Guid.NewGuid(), "test@test.com", "New Name", "98765", null);
        var context = MockConsumeContext(message);

        await consumer.Consume(context);

        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketAccountProfileUpdatedConsumer_StaffAndCustomer_ShouldUpdateProfile()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount { AccountId = accountId, FullName = "Old Name" };
        var customer = new CustomerAccount { AccountId = accountId, FullName = "Old Name", PhoneNumber = "00000" };

        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());
        _customerRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<CustomerAccount> { customer }.AsQueryable().BuildMock());

        var consumer = new TicketAccountProfileUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountProfileUpdatedEvent(accountId, "test@test.com", "New Name", "98765", null);
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        staff.FullName.Should().Be("New Name");
        customer.FullName.Should().Be("New Name");
        customer.PhoneNumber.Should().Be("98765");
        _staffRepoMock.Verify(r => r.UpdateAsync(staff), Times.Once);
        _customerRepoMock.Verify(r => r.UpdateAsync(customer), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TicketStaffProfileUpdatedConsumer Tests

    [Fact]
    public async Task TicketStaffProfileUpdatedConsumer_DuplicateMessage_ShouldReturnEarly()
    {
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var consumer = new TicketStaffProfileUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new StaffProfileUpdatedEvent(Guid.NewGuid(), "STF01", 3, true, 2);
        var context = MockConsumeContext(message);

        await consumer.Consume(context);

        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketStaffProfileUpdatedConsumer_ExistingStaff_ShouldUpdateFields()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount { AccountId = accountId, EmployeeCode = "OLD" };
        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());

        var consumer = new TicketStaffProfileUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new StaffProfileUpdatedEvent(accountId, "STF01", 5, true, 3); // 3: SeniorSpecialist
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        staff.EmployeeCode.Should().Be("STF01");
        staff.MaxConcurrentTickets.Should().Be(5);
        staff.IsAvailable.Should().BeTrue();
        staff.SkillTier.Should().Be(StaffSkillTierEnum.SeniorSpecialist);
        _staffRepoMock.Verify(r => r.UpdateAsync(staff), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TicketStaffSkillsUpdatedConsumer Tests

    [Fact]
    public async Task TicketStaffSkillsUpdatedConsumer_DuplicateMessage_ShouldReturnEarly()
    {
        _inboxMock.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        var consumer = new TicketStaffSkillsUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new StaffSkillsUpdatedEvent(Guid.NewGuid(), new List<string> { "SKL1" });
        var context = MockConsumeContext(message);

        await consumer.Consume(context);

        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TicketStaffSkillsUpdatedConsumer_ExistingStaff_ShouldUpdateSkills()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount { AccountId = accountId, SkillCodes = new List<string> { "OLD" } };
        _staffRepoMock.Setup(r => r.GetAllAsync()).Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());

        var consumer = new TicketStaffSkillsUpdatedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new StaffSkillsUpdatedEvent(accountId, new List<string> { "SKL1", "SKL2" });
        var context = MockConsumeContext(message);

        // Act
        await consumer.Consume(context);

        // Assert
        staff.SkillCodes.Should().BeEquivalentTo("SKL1", "SKL2");
        _staffRepoMock.Verify(r => r.UpdateAsync(staff), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TicketAccountDeletedConsumer Tests

    [Fact]
    public async Task TicketAccountDeletedConsumer_ExistingHistoricalProjections_ShouldSoftDeleteBoth()
    {
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            AccountId = accountId,
            Status = AccountStatusEnum.Active
        };
        var customer = new CustomerAccount
        {
            AccountId = accountId,
            Status = AccountStatusEnum.Active
        };

        _staffRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());
        _customerRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<CustomerAccount> { customer }.AsQueryable().BuildMock());

        var consumer = new TicketAccountDeletedConsumer(_uowMock.Object, _inboxMock.Object);
        var message = new AccountDeletedEvent(accountId, "deleted@test.com", "AccountDeleted");

        await consumer.Consume(MockConsumeContext(message));

        staff.Status.Should().Be(AccountStatusEnum.Inactive);
        staff.LastSourceEventAtUtc.Should().Be(message.OccurredAt);
        customer.Status.Should().Be(AccountStatusEnum.Inactive);
        customer.LastSourceEventAtUtc.Should().Be(message.OccurredAt);
        _staffRepoMock.Verify(r => r.DeleteAsync(staff), Times.Once);
        _customerRepoMock.Verify(r => r.DeleteAsync(customer), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TicketAccountDeletedConsumer_StaleEvent_ShouldNotOverwriteNewerProjection()
    {
        var accountId = Guid.NewGuid();
        var message = new AccountDeletedEvent(accountId, "deleted@test.com", "AccountDeleted");
        var newerTimestamp = message.OccurredAt.AddMinutes(1);
        var staff = new StaffAccount
        {
            AccountId = accountId,
            Status = AccountStatusEnum.Active,
            LastSourceEventAtUtc = newerTimestamp
        };
        _staffRepoMock.Setup(r => r.GetAllAsync())
            .Returns(new List<StaffAccount> { staff }.AsQueryable().BuildMock());

        var consumer = new TicketAccountDeletedConsumer(_uowMock.Object, _inboxMock.Object);

        await consumer.Consume(MockConsumeContext(message));

        staff.Status.Should().Be(AccountStatusEnum.Active);
        _staffRepoMock.Verify(r => r.DeleteAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    private static ConsumeContext<T> MockConsumeContext<T>(T message) where T : class
    {
        var contextMock = new Mock<ConsumeContext<T>>();
        contextMock.SetupGet(c => c.Message).Returns(message);
        contextMock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        contextMock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return contextMock.Object;
    }
}
