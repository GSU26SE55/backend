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
        => SeedMany(
            staff is null ? Array.Empty<StaffAccount>() : new[] { staff },
            customer is null ? Array.Empty<CustomerAccount>() : new[] { customer });

    private void SeedMany(
        IEnumerable<StaffAccount>? staffs = null,
        IEnumerable<CustomerAccount>? customers = null)
    {
        _staffRepo.Setup(r => r.GetAllAsync())
            .Returns((staffs ?? Array.Empty<StaffAccount>()).AsQueryable().BuildMock());
        _customerRepo.Setup(r => r.GetAllAsync())
            .Returns((customers ?? Array.Empty<CustomerAccount>()).AsQueryable().BuildMock());
    }

    private TicketAccountSyncSnapshotConsumer Consumer()
        => new(_uow.Object, _inbox.Object, NullLogger<TicketAccountSyncSnapshotConsumer>.Instance);

    private static ConsumeContext<AccountSyncSnapshotEvent> Ctx(
        Guid accountId,
        string role,
        bool isActive = true,
        bool isDeleted = false,
        DateTime? snapshotAtUtc = null,
        int accountStatus = 1,
        bool hasStaffProfile = false,
        string? employeeCode = null,
        int maxConcurrentTickets = 3,
        bool isAvailable = true,
        int skillTier = 0,
        List<string>? skillCodes = null,
        bool hasAvatarSnapshot = false,
        string? avatarUrl = null)
    {
        var msg = new AccountSyncSnapshotEvent(
            accountId, "user@example.com", "Nguyễn Văn A", "0901234567",
            role, isActive, isDeleted, snapshotAtUtc ?? DateTime.UtcNow, "Resync",
            accountStatus, hasStaffProfile, employeeCode, maxConcurrentTickets,
            isAvailable, skillTier, skillCodes, hasAvatarSnapshot, avatarUrl);
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
            LastSourceEventAtUtc = DateTime.UtcNow,
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
    public async Task CustomerRole_WithNoExistingMirror_CreatesCustomerProjection()
    {
        // Đây là repair path cho event Activated bị lỡ hoặc database Ticket được dựng lại riêng.
        var accountId = Guid.NewGuid();

        await Consumer().Consume(Ctx(accountId, "Customer"));

        _customerRepo.Verify(r => r.AddAsync(It.Is<CustomerAccount>(c =>
            c.AccountId == accountId && c.Status == AccountStatusEnum.Active)), Times.Once);
        _staffRepo.Verify(r => r.AddAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Staff")]
    [InlineData("Customer")]
    public async Task AuthoritativeAvatarSnapshot_RepairsProjection(string role)
    {
        var accountId = Guid.NewGuid();
        var staff = role == "Staff"
            ? new StaffAccount { Id = accountId, AccountId = accountId, AvatarUrl = "drifted" }
            : null;
        var customer = role == "Customer"
            ? new CustomerAccount { Id = accountId, AccountId = accountId, AvatarUrl = "drifted" }
            : null;
        Seed(staff, customer);

        await Consumer().Consume(Ctx(
            accountId,
            role,
            hasAvatarSnapshot: true,
            avatarUrl: " https://cdn.example.com/avatar.png "));

        (staff?.AvatarUrl ?? customer?.AvatarUrl)
            .Should().Be("https://cdn.example.com/avatar.png");
    }

    [Fact]
    public async Task LifecycleSnapshotWithoutAvatar_DoesNotClearProjection()
    {
        var accountId = Guid.NewGuid();
        var customer = new CustomerAccount
        {
            Id = accountId,
            AccountId = accountId,
            AvatarUrl = "https://cdn.example.com/existing.png"
        };
        Seed(customer: customer);

        await Consumer().Consume(Ctx(accountId, "Customer"));

        customer.AvatarUrl.Should().Be("https://cdn.example.com/existing.png");
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
    public async Task FullStaffSnapshot_RepairsEveryStaffProjectionField()
    {
        var accountId = Guid.NewGuid();
        var staff = new StaffAccount
        {
            Id = accountId,
            AccountId = accountId,
            Email = "drift@example.com",
            FullName = "Drifted",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            EmployeeCode = "WRONG",
            MaxConcurrentTickets = 99,
            IsAvailable = false,
            SkillTier = StaffSkillTierEnum.SeniorSpecialist,
            SkillCodes = new List<string> { "WRONG" }
        };
        Seed(staff: staff);

        await Consumer().Consume(Ctx(
            accountId,
            "Staff",
            hasStaffProfile: true,
            employeeCode: "EMP-001",
            maxConcurrentTickets: 5,
            isAvailable: true,
            skillTier: (int)StaffSkillTierEnum.ModuleSpecialist,
            skillCodes: new List<string> { " inverter ", "battery", "battery" }));

        staff.EmployeeCode.Should().Be("EMP-001");
        staff.MaxConcurrentTickets.Should().Be(5);
        staff.IsAvailable.Should().BeTrue();
        staff.SkillTier.Should().Be(StaffSkillTierEnum.ModuleSpecialist);
        staff.SkillCodes.Should().Equal("battery", "inverter");
    }

    [Fact]
    public async Task FullStaffSnapshot_WithMissingProjection_KeepsNewEntityInAddedState()
    {
        var accountId = Guid.NewGuid();

        await Consumer().Consume(Ctx(
            accountId,
            "Staff",
            hasStaffProfile: true,
            employeeCode: "EMP-NEW",
            maxConcurrentTickets: 6,
            isAvailable: false,
            skillTier: (int)StaffSkillTierEnum.SeniorSpecialist,
            skillCodes: new List<string> { "SOLAR" }));

        _staffRepo.Verify(r => r.AddAsync(It.Is<StaffAccount>(staff =>
            staff.AccountId == accountId
            && staff.EmployeeCode == "EMP-NEW"
            && staff.MaxConcurrentTickets == 6
            && !staff.IsAvailable
            && staff.SkillTier == StaffSkillTierEnum.SeniorSpecialist
            && staff.SkillCodes.SequenceEqual(new[] { "SOLAR" }))), Times.Once);
        _staffRepo.Verify(r => r.UpdateAsync(It.IsAny<StaffAccount>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LegacyEmployeeCodeAlias_IsDeactivatedAndReleasedBeforeCanonicalStaffInsert()
    {
        var canonicalAccountId = Guid.NewGuid();
        var legacy = new StaffAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Legacy seed",
            EmployeeCode = "EMP-LEGACY",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            LastSyncedAt = DateTime.UtcNow.AddDays(-30),
        };
        SeedMany(staffs: new[] { legacy });

        await Consumer().Consume(Ctx(
            canonicalAccountId,
            "Staff",
            hasStaffProfile: true,
            employeeCode: "EMP-LEGACY"));

        legacy.Status.Should().Be(AccountStatusEnum.Inactive);
        legacy.IsAvailable.Should().BeFalse();
        legacy.EmployeeCode.Should().BeNull();
        _staffRepo.Verify(r => r.AddAsync(It.Is<StaffAccount>(staff =>
            staff.AccountId == canonicalAccountId
            && staff.EmployeeCode == "EMP-LEGACY")), Times.Once);
        // Flush alias UPDATE trước canonical INSERT để unique employee_code không còn xung đột.
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CanonicalStaffSnapshot_DeactivatesLegacyEmailAlias()
    {
        var canonicalAccountId = Guid.NewGuid();
        var canonical = new StaffAccount
        {
            Id = canonicalAccountId,
            AccountId = canonicalAccountId,
            Email = "user@example.com",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            LastSyncedAt = DateTime.UtcNow.AddDays(-1),
        };
        var legacy = new StaffAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Email = "user@example.com",
            Role = "Staff",
            Status = AccountStatusEnum.Active,
            IsAvailable = true,
            LastSyncedAt = DateTime.UtcNow.AddDays(-30),
        };
        SeedMany(staffs: new[] { canonical, legacy });

        await Consumer().Consume(Ctx(canonicalAccountId, "Manager"));

        canonical.Role.Should().Be("Manager");
        legacy.Status.Should().Be(AccountStatusEnum.Inactive);
        legacy.IsAvailable.Should().BeFalse();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CustomerSnapshot_DeactivatesLegacyEmailAliasAndCreatesCanonicalRow()
    {
        var canonicalAccountId = Guid.NewGuid();
        var legacy = new CustomerAccount
        {
            Id = Guid.NewGuid(),
            AccountId = Guid.NewGuid(),
            Email = "user@example.com",
            FullName = "Legacy seed",
            Status = AccountStatusEnum.Active,
            LastSyncedAt = DateTime.UtcNow.AddDays(-30),
        };
        SeedMany(customers: new[] { legacy });

        await Consumer().Consume(Ctx(canonicalAccountId, "Customer"));

        legacy.Status.Should().Be(AccountStatusEnum.Inactive);
        _customerRepo.Verify(r => r.AddAsync(It.Is<CustomerAccount>(customer =>
            customer.AccountId == canonicalAccountId)), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
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
