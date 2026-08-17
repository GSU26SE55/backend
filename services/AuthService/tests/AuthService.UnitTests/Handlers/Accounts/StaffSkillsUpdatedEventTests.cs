using AuthService.Application.CQRS.Command.Account;
using AuthService.Application.CQRS.Handler.Account;
using AuthService.Domain.Entities;
using AuthService.UnitTests.Helpers;
using FluentAssertions;
using SharedContracts.Events;
using SharedContracts.Interfaces;

namespace AuthService.UnitTests.Handlers.Accounts;

/// <summary>
/// GH-770 — thêm/xoá kỹ năng Staff không bao giờ được đồng bộ.
///
/// <para>
/// TicketService có sẵn <c>TicketStaffSkillsUpdatedConsumer</c> cho
/// <c>StaffSkillsUpdatedEvent</c>, nhưng tìm khắp AuthService không có chỗ nào phát event đó —
/// consumer mồ côi. Kết quả: định tuyến và giao việc theo kỹ năng chạy trên dữ liệu chụp lúc kích
/// hoạt, và không bao giờ đổi nữa.
/// </para>
/// <para>
/// Quyết định thiết kế được ghim ở đây: phát TOÀN BỘ tập kỹ năng sau thay đổi, không phát "vừa
/// thêm/xoá mã X". Một event rơi giữa chừng thì tập đầy đủ ở lần sau vẫn tự chữa được; danh sách
/// gia giảm thì lệch mãi.
/// </para>
/// </summary>
public class StaffSkillsUpdatedEventTests
{
    private sealed class CapturingProducer : IMessageProducerService
    {
        public List<StaffSkillsUpdatedEvent> Events { get; } = new();

        public Task PublishAsync<T>(T message, CancellationToken cancellationToken = default)
            where T : SharedContracts.Events.Root.IntegrationEvent
        {
            if (message is StaffSkillsUpdatedEvent e)
                Events.Add(e);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Chỉ account role Staff mới được cấp kỹ năng — handler tự tạo StaffProfile nếu chưa có,
    /// nên cấp skill cho Manager là đưa họ vào danh sách phân công ticket.
    /// </summary>
    private static Account StaffAccountFixture(Guid staffId, string roleName = "Staff")
    {
        var roleId = Guid.NewGuid();
        return new Account
        {
            Id = staffId,
            Email = "s@example.com",
            FullName = "Kỹ Thuật Viên",
            RoleId = roleId,
            Role = new Role { Id = roleId, Name = roleName, NormalizedName = roleName.ToUpperInvariant() }
        };
    }

    private static StaffSkill Skill(Guid staffId, string code, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        StaffAccountId = staffId,
        SkillCode = code,
        IsDeleted = deleted,
    };

    [Fact]
    public async Task AddSkill_PublishesFullSkillSet_IncludingTheNewOne()
    {
        var staffId = Guid.NewGuid();
        var account = StaffAccountFixture(staffId);
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            staffProfileSeed: new[] { new StaffProfile { Id = Guid.NewGuid(), AccountId = staffId } },
            staffSkillSeed: new[] { Skill(staffId, "SKL1"), Skill(staffId, "SKL2") });
        var producer = new CapturingProducer();
        var handler = new AddStaffSkillCommandHandler(uow.Object, producer);

        var resp = await handler.Handle(
            new AddStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL3", SkillLevel = 3 },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.Events.Should().ContainSingle().Subject;
        evt.AccountId.Should().Be(staffId);
        evt.SkillCodes.Should().BeEquivalentTo("SKL1", "SKL2", "SKL3");
    }

    [Fact]
    public async Task AddSkill_ThatAlreadyExists_DoesNotDuplicateItInTheSet()
    {
        // Cập nhật cấp độ của một kỹ năng đã có: tập không được sinh ra mã trùng, kẻo consumer
        // ghi vào danh sách kỹ năng hai bản giống nhau.
        var staffId = Guid.NewGuid();
        var account = StaffAccountFixture(staffId);
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            staffProfileSeed: new[] { new StaffProfile { Id = Guid.NewGuid(), AccountId = staffId } },
            staffSkillSeed: new[] { Skill(staffId, "SKL1") });
        var producer = new CapturingProducer();
        var handler = new AddStaffSkillCommandHandler(uow.Object, producer);

        await handler.Handle(
            new AddStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL1", SkillLevel = 5 },
            CancellationToken.None);

        producer.Events.Should().ContainSingle().Which.SkillCodes.Should().BeEquivalentTo("SKL1");
    }

    [Fact]
    public async Task AddSkill_ReactivatingASoftDeletedOne_IncludesItAgain()
    {
        // Kỹ năng từng bị gỡ rồi cấp lại: bản ghi cũ được khôi phục chứ không tạo mới, nên truy
        // vấn "chưa xoá" trên DB CHƯA thấy nó — phải tự hợp nhất vào tập.
        var staffId = Guid.NewGuid();
        var account = StaffAccountFixture(staffId);
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { account },
            staffProfileSeed: new[] { new StaffProfile { Id = Guid.NewGuid(), AccountId = staffId } },
            staffSkillSeed: new[] { Skill(staffId, "SKL1"), Skill(staffId, "SKL9", deleted: true) });
        var producer = new CapturingProducer();
        var handler = new AddStaffSkillCommandHandler(uow.Object, producer);

        await handler.Handle(
            new AddStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL9", SkillLevel = 2 },
            CancellationToken.None);

        producer.Events.Should().ContainSingle().Which.SkillCodes.Should().BeEquivalentTo("SKL1", "SKL9");
    }

    [Theory]
    [InlineData("Manager")]
    [InlineData("Admin")]
    public async Task AddSkill_ToNonStaffAccount_IsRejected(string roleName)
    {
        // Handler TỰ TẠO StaffProfile nếu chưa có. Cấp skill cho một Manager là vô tình đưa họ vào
        // "danh sách kỹ thuật viên" mà màn hình phân công ticket đọc, và không có gì gỡ ra sau đó.
        var accountId = Guid.NewGuid();
        var (uow, _, _, _) = MockUnitOfWork.Build(
            accountSeed: new[] { StaffAccountFixture(accountId, roleName) });
        var producer = new CapturingProducer();
        var handler = new AddStaffSkillCommandHandler(uow.Object, producer);

        var resp = await handler.Handle(
            new AddStaffSkillCommand { StaffAccountId = accountId, SkillCode = "SKL1", SkillLevel = 3 },
            CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(409);
        producer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSkill_PublishesRemainingSet_WithoutTheRemovedOne()
    {
        var staffId = Guid.NewGuid();
        var (uow, _, _, _) = MockUnitOfWork.Build(
            staffSkillSeed: new[] { Skill(staffId, "SKL1"), Skill(staffId, "SKL2"), Skill(staffId, "SKL3") });
        var producer = new CapturingProducer();
        var handler = new DeleteStaffSkillCommandHandler(uow.Object, producer);

        var resp = await handler.Handle(
            new DeleteStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL2" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        var evt = producer.Events.Should().ContainSingle().Subject;
        evt.SkillCodes.Should().BeEquivalentTo("SKL1", "SKL3");
    }

    [Fact]
    public async Task DeleteLastSkill_PublishesEmptySet_NotNothing()
    {
        // Gỡ kỹ năng cuối cùng phải phát tập RỖNG. Im lặng ở đây là consumer giữ nguyên kỹ năng
        // cuối và vẫn giao đúng loại việc đó cho người đã không còn làm được.
        var staffId = Guid.NewGuid();
        var (uow, _, _, _) = MockUnitOfWork.Build(staffSkillSeed: new[] { Skill(staffId, "SKL1") });
        var producer = new CapturingProducer();
        var handler = new DeleteStaffSkillCommandHandler(uow.Object, producer);

        await handler.Handle(
            new DeleteStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL1" },
            CancellationToken.None);

        producer.Events.Should().ContainSingle().Which.SkillCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSkill_NotFound_PublishesNothing()
    {
        var staffId = Guid.NewGuid();
        var (uow, _, _, _) = MockUnitOfWork.Build(staffSkillSeed: new[] { Skill(staffId, "SKL1") });
        var producer = new CapturingProducer();
        var handler = new DeleteStaffSkillCommandHandler(uow.Object, producer);

        var resp = await handler.Handle(
            new DeleteStaffSkillCommand { StaffAccountId = staffId, SkillCode = "KHONG-CO" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(404);
        producer.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteSkill_OnlyTouchesTheTargetStaff()
    {
        // Cùng mã kỹ năng ở nhiều nhân sự: tập phát ra phải là của ĐÚNG người bị sửa.
        var staffId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var (uow, _, _, _) = MockUnitOfWork.Build(staffSkillSeed: new[]
        {
            Skill(staffId, "SKL1"), Skill(staffId, "SKL2"),
            Skill(otherStaffId, "SKL1"), Skill(otherStaffId, "SKL2"),
        });
        var producer = new CapturingProducer();
        var handler = new DeleteStaffSkillCommandHandler(uow.Object, producer);

        await handler.Handle(
            new DeleteStaffSkillCommand { StaffAccountId = staffId, SkillCode = "SKL1" },
            CancellationToken.None);

        var evt = producer.Events.Should().ContainSingle().Subject;
        evt.AccountId.Should().Be(staffId);
        evt.SkillCodes.Should().BeEquivalentTo("SKL2");
    }
}
