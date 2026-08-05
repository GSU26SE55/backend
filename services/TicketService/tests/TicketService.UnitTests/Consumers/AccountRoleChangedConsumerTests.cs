using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable;
using SharedContracts.Events;
using SharedInfrastructure.Idempotency;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

/// <summary>
/// GH-769 — đổi role không đồng bộ xuống bản sao account.
///
/// <para>
/// TicketService giữ hai bảng tách biệt <c>StaffAccounts</c> / <c>CustomerAccounts</c>, và cả hai
/// chỉ được dựng ĐÚNG MỘT LẦN lúc account kích hoạt. Đổi Customer → Staff sau đó thì không có
/// <c>StaffAccount</c> nào được tạo ⇒ người vừa lên Staff không thể được giao ticket. Đổi ngược
/// lại thì <c>StaffAccount</c> cũ vẫn nằm đó và vẫn nhận việc.
/// </para>
/// </summary>
public class AccountRoleChangedConsumerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IInboxStore> _inbox = new();
    private readonly Mock<IGenericRepository<StaffAccount>> _staffRepo = new();
    private readonly Mock<IGenericRepository<CustomerAccount>> _customerRepo = new();

    public AccountRoleChangedConsumerTests()
    {
        _uow.SetupGet(u => u.StaffAccounts).Returns(_staffRepo.Object);
        _uow.SetupGet(u => u.CustomerAccounts).Returns(_customerRepo.Object);
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "gh764-test-token"));
        Seed();
    }

    private void Seed(StaffAccount? staff = null, CustomerAccount? customer = null)
    {
        _staffRepo.Setup(r => r.GetAllAsync())
            .Returns((staff is null ? Array.Empty<StaffAccount>() : new[] { staff }).AsQueryable().BuildMock());
        _customerRepo.Setup(r => r.GetAllAsync())
            .Returns((customer is null ? Array.Empty<CustomerAccount>() : new[] { customer }).AsQueryable().BuildMock());
    }

    private TicketAccountRoleChangedConsumer Consumer()
        => new(_uow.Object, _inbox.Object, NullLogger<TicketAccountRoleChangedConsumer>.Instance);

    private static ConsumeContext<AccountRoleChangedEvent> Ctx(string oldRole, string newRole, Guid accountId)
    {
        var msg = new AccountRoleChangedEvent(
            accountId, "user@example.com", "Nguyễn Văn A", "0901234567",
            oldRole, newRole, DateTime.UtcNow);
        var mock = new Mock<ConsumeContext<AccountRoleChangedEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        mock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    [Fact]
    public async Task CustomerToStaff_CreatesStaffMirror_AndDeactivatesCustomerMirror()
    {
        var accountId = Guid.NewGuid();
        var customer = new CustomerAccount
        {
            Id = Guid.NewGuid(), AccountId = accountId, Email = "user@example.com",
            FullName = "Nguyễn Văn A", Status = AccountStatusEnum.Active,
        };
        Seed(customer: customer);

        await Consumer().Consume(Ctx("Customer", "Staff", accountId));

        // Thiếu StaffAccount là người vừa lên Staff không bao giờ được giao ticket.
        _staffRepo.Verify(r => r.AddAsync(It.Is<StaffAccount>(s =>
            s.AccountId == accountId && s.Status == AccountStatusEnum.Active)), Times.Once);
        // Còn CustomerAccount đang Active là vẫn giữ quyền nghiệp vụ cũ.
        customer.Status.Should().Be(AccountStatusEnum.Inactive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaffToCustomer_CreatesCustomerMirror_AndDeactivatesStaffMirror()
    {
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = Guid.NewGuid(), AccountId = accountId, Email = "user@example.com",
            FullName = "Nguyễn Văn A", Status = AccountStatusEnum.Active,
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx("Staff", "Customer", accountId));

        _customerRepo.Verify(r => r.AddAsync(It.Is<CustomerAccount>(c =>
            c.AccountId == accountId && c.Status == AccountStatusEnum.Active)), Times.Once);
        staff.Status.Should().Be(AccountStatusEnum.Inactive);
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Admin")]
    [InlineData("staff")]
    public async Task InternalRoles_AllMapToStaffMirror(string role)
    {
        // Phải khớp ĐÚNG cách TicketAccountActivatedConsumer phân loại lúc kích hoạt. Lệch nhau là
        // một account nằm ở cả hai bảng, hoặc không bảng nào.
        var accountId = Guid.NewGuid();

        await Consumer().Consume(Ctx("Customer", role, accountId));

        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Once);
        _customerRepo.Verify(r => r.AddAsync(It.IsAny<CustomerAccount>()), Times.Never);
    }

    [Fact]
    public async Task ExistingStaffMirror_IsReactivated_NotDuplicated()
    {
        // Customer → Staff → Customer → Staff: lần cuối phải DÙNG LẠI bản ghi cũ, không tạo bản
        // thứ hai cho cùng một account.
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = Guid.NewGuid(), AccountId = accountId, Email = "old@example.com",
            FullName = "Tên Cũ", Status = AccountStatusEnum.Inactive,
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx("Customer", "Staff", accountId));

        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Never);
        staff.Status.Should().Be(AccountStatusEnum.Active);
        staff.Email.Should().Be("user@example.com");
        staff.FullName.Should().Be("Nguyễn Văn A");
    }

    [Fact]
    public async Task DuplicateMessage_DoesNothing()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        await Consumer().Consume(Ctx("Customer", "Staff", Guid.NewGuid()));

        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
