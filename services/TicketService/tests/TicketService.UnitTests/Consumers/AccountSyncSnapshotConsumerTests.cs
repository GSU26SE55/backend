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
/// Đối soát bản sao account của TicketService từ snapshot AuthService.
///
/// <para>
/// Bối cảnh: migration <c>AddTicketAiSuggestionAndStaffRole</c> thêm cột <c>role</c> với
/// <c>defaultValue: "Staff"</c>, nên mọi bản ghi có trước đó — gồm cả Manager/Admin — bị đóng dấu
/// "Staff". Panel gợi ý phân công lọc <c>Role == "Staff"</c> nên đề xuất luôn cả Manager.
/// TicketService lại là read-model DUY NHẤT không nghe <c>AccountSyncSnapshotEvent</c>, nên không
/// có đường nào sửa: mỗi service một database.
/// </para>
/// </summary>
public class AccountSyncSnapshotConsumerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IInboxStore> _inbox = new();
    private readonly Mock<IGenericRepository<StaffAccount>> _staffRepo = new();
    private readonly Mock<IGenericRepository<CustomerAccount>> _customerRepo = new();

    public AccountSyncSnapshotConsumerTests()
    {
        _uow.SetupGet(u => u.StaffAccounts).Returns(_staffRepo.Object);
        _uow.SetupGet(u => u.CustomerAccounts).Returns(_customerRepo.Object);
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxClaim(InboxClaimStatus.Claimed, "snapshot-test-token"));
        Seed();
    }

    private void Seed(StaffAccount? staff = null, CustomerAccount? customer = null)
    {
        _staffRepo.Setup(r => r.GetAllAsync())
            .Returns((staff is null ? Array.Empty<StaffAccount>() : new[] { staff }).AsQueryable().BuildMock());
        _customerRepo.Setup(r => r.GetAllAsync())
            .Returns((customer is null ? Array.Empty<CustomerAccount>() : new[] { customer }).AsQueryable().BuildMock());
    }

    private TicketAccountSyncSnapshotConsumer Consumer()
        => new(_uow.Object, _inbox.Object, NullLogger<TicketAccountSyncSnapshotConsumer>.Instance);

    private static ConsumeContext<AccountSyncSnapshotEvent> Ctx(
        Guid accountId,
        string role,
        bool isActive = true,
        bool isDeleted = false,
        DateTime? snapshotAtUtc = null)
    {
        var msg = new AccountSyncSnapshotEvent(
            accountId, "user@example.com", "Nguyễn Văn A", "0901234567",
            role, isActive, isDeleted, snapshotAtUtc ?? DateTime.UtcNow, "Resync");
        var mock = new Mock<ConsumeContext<AccountSyncSnapshotEvent>>();
        mock.SetupGet(c => c.Message).Returns(msg);
        mock.SetupGet(c => c.MessageId).Returns(Guid.NewGuid());
        mock.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    [Fact]
    public async Task Resync_OverwritesRoleWronglyBackfilledAsStaff()
    {
        // Đây chính là ca lỗi quan sát được trên UI: Demo Manager hiện trong danh sách
        // "AI-suggested staff" vì bản ghi mang role = "Staff" do migration gán mặc định.
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = accountId,
            AccountId = accountId,
            Email = "manager@example.com",
            FullName = "Demo Manager",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow.AddDays(-30),
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx(accountId, "Manager"));

        staff.Role.Should().Be("Manager");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StaleSnapshot_DoesNotRollBackMirror()
    {
        // Snapshot tới muộn không được đè lên trạng thái mới hơn do consumer vòng đời ghi.
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = accountId,
            AccountId = accountId,
            Role = "Manager",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow,
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx(accountId, "Staff", snapshotAtUtc: DateTime.UtcNow.AddDays(-1)));

        staff.Role.Should().Be("Manager");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task InternalRoles_CreateStaffMirror_WithActualRole(string role)
    {
        var accountId = Guid.NewGuid();

        await Consumer().Consume(Ctx(accountId, role));

        _staffRepo.Verify(r => r.AddAsync(It.Is<StaffAccount>(s =>
            s.AccountId == accountId && s.Role == role)), Times.Once);
        _customerRepo.Verify(r => r.AddAsync(It.IsAny<CustomerAccount>()), Times.Never);
    }

    [Fact]
    public async Task CustomerRole_WithNoExistingMirror_CreatesNothing()
    {
        // Đối soát KHÔNG phải là nơi dựng bản sao cho Customer chưa từng dính tới ticket:
        // TicketAccountActivatedConsumer lo việc đó. Ở đây chỉ sửa những bản sao đã tồn tại,
        // nếu không mỗi lần resync toàn hệ thống lại đổ toàn bộ Customer vào ticket_db.
        var accountId = Guid.NewGuid();

        await Consumer().Consume(Ctx(accountId, "Customer"));

        _customerRepo.Verify(r => r.AddAsync(It.IsAny<CustomerAccount>()), Times.Never);
        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CustomerRole_WithExistingStaffMirror_UpdatesCustomerMirror()
    {
        // Có bản sao staff cũ nghĩa là người này từng dính tới ticket — lúc này bản sao customer
        // phải được dựng/cập nhật để giữ đúng thông tin hiển thị trong lịch sử.
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = accountId,
            AccountId = accountId,
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx(accountId, "Customer"));

        _customerRepo.Verify(r => r.AddAsync(It.Is<CustomerAccount>(c => c.AccountId == accountId)), Times.Once);
        staff.Status.Should().Be(AccountStatusEnum.Inactive);
    }

    [Fact]
    public async Task RoleLeavingInternal_SuspendsStaffMirror_ButKeepsHistory()
    {
        // Đình chỉ chứ không xoá: ticket lịch sử còn tham chiếu tới bản ghi này.
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = accountId,
            AccountId = accountId,
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx(accountId, "Customer"));

        staff.Status.Should().Be(AccountStatusEnum.Inactive);
        _staffRepo.Verify(r => r.DeleteAsync(It.IsAny<StaffAccount>()), Times.Never);
    }

    [Fact]
    public async Task DeletedAccount_WithNoExistingMirror_DoesNothing()
    {
        await Consumer().Consume(Ctx(Guid.NewGuid(), "Staff", isActive: false, isDeleted: true));

        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DuplicateMessage_DoesNothing()
    {
        _inbox.Setup(i => i.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InboxClaim.Completed);

        await Consumer().Consume(Ctx(Guid.NewGuid(), "Manager"));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
