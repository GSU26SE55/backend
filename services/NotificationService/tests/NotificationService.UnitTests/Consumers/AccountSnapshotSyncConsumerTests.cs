using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using NotificationService.Application.Consumers;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using SharedContracts.Events;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Consumers;

/// <summary>
/// 02/08/2026 — <see cref="AccountSnapshotSyncConsumer"/>: nguồn đồng bộ read-model account thứ tư,
/// và là nguồn DUY NHẤT cập nhật được <c>Role</c> và <c>IsActive</c> sau khi account đã tồn tại.
///
/// Ba nhóm hồi quy được khoá lại ở đây, đều là lỗi đã đo được trên môi trường thật:
/// <list type="number">
/// <item>Đổi role không tới read-model ⇒ thông báo theo nhóm gửi sai người.</item>
/// <item>Khoá/vô hiệu hoá tài khoản không tới read-model ⇒ người đã nghỉ vẫn nhận thông báo.</item>
/// <item>Account seed không có mặt trong read-model ⇒ nhóm Admin rỗng, thông báo bị bỏ qua im lặng.</item>
/// </list>
/// </summary>
public class AccountSnapshotSyncConsumerTests
{
    private static readonly DateTime T0 = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc);

    private sealed class RepoCapture
    {
        public List<AccountReadModel> Added { get; } = new();
        public List<AccountReadModel> Updated { get; } = new();
        public List<AccountReadModel> Deleted { get; } = new();
        public Mock<INotificationUnitOfWork> Uow { get; init; } = null!;
    }

    private static RepoCapture BuildUow(IEnumerable<AccountReadModel> seed)
    {
        var capture = new RepoCapture { Uow = new Mock<INotificationUnitOfWork>() };

        var repo = new Mock<IGenericRepository<AccountReadModel>>();
        repo.Setup(r => r.GetAllAsync()).Returns(seed.AsQueryable().BuildMock());
        repo.Setup(r => r.AddAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Added.Add).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Updated.Add);
        repo.Setup(r => r.DeleteAsync(It.IsAny<AccountReadModel>()))
            .Callback<AccountReadModel>(capture.Deleted.Add);

        capture.Uow.SetupGet(u => u.Accounts).Returns(repo.Object);
        capture.Uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        capture.Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return capture;
    }

    private static ConsumeContext<T> Ctx<T>(T message) where T : class
    {
        var ctx = new Mock<ConsumeContext<T>>();
        ctx.SetupGet(c => c.Message).Returns(message);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static AccountSnapshotSyncConsumer Sut(RepoCapture cap) =>
        new(cap.Uow.Object, NullLogger<AccountSnapshotSyncConsumer>.Instance);

    private static AccountSyncSnapshotEvent Snapshot(
        Guid id,
        string role = "Manager",
        bool isActive = true,
        bool isDeleted = false,
        DateTime? at = null,
        string reason = "Resync") =>
        new(id, "U@X.Z", " User ", " 0901234567 ", role, isActive, isDeleted, at ?? T1, reason);

    private static AccountReadModel Existing(
        Guid id,
        string role = "Staff",
        bool isActive = true,
        bool isDeleted = false,
        DateTime? lastSnapshotAt = null) => new()
        {
            Id = id,
            Email = "old@x.z",
            FullName = "Old Name",
            Role = role,
            IsActive = isActive,
            IsDeleted = isDeleted,
            LastSyncedAtUtc = T0,
            LastSnapshotAtUtc = lastSnapshotAt
        };

    // ── Tạo mới ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_AccountChuaCo_ThiTaoMoi_ChuanHoaEmailVaTrim()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());
        var evt = Snapshot(Guid.NewGuid(), role: "Admin", reason: "Seed");

        await Sut(cap).Consume(Ctx(evt));

        cap.Added.Should().ContainSingle();
        var added = cap.Added[0];
        added.Id.Should().Be(evt.AccountId);
        added.Role.Should().Be("Admin");
        added.IsActive.Should().BeTrue();
        added.Email.Should().Be("u@x.z", "email phải hạ về chữ thường");
        added.FullName.Should().Be("User", "phải trim");
        added.PhoneNumber.Should().Be("0901234567", "phải trim");
        added.LastSnapshotAtUtc.Should().Be(T1, "mốc chống-về-trễ phải lấy từ event, không phải lúc consume");
    }

    [Fact]
    public async Task Snapshot_AccountChuaActive_VanTaoDong_NhungIsActiveFalse()
    {
        // Account chờ xác thực / bị đình chỉ: vẫn mirror để lần kích hoạt sau chỉ là UPDATE,
        // nhưng RecipientResolver lọc IsActive nên không lọt vào danh sách gửi.
        var cap = BuildUow(Array.Empty<AccountReadModel>());

        await Sut(cap).Consume(Ctx(Snapshot(Guid.NewGuid(), isActive: false)));

        cap.Added.Should().ContainSingle();
        cap.Added[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_AccountDaXoaVaChuaCoDong_ThiBoQua()
    {
        var cap = BuildUow(Array.Empty<AccountReadModel>());

        await Sut(cap).Consume(Ctx(Snapshot(Guid.NewGuid(), isActive: false, isDeleted: true)));

        cap.Added.Should().BeEmpty("không có gì để mirror");
        cap.Updated.Should().BeEmpty();
        cap.Deleted.Should().BeEmpty();
    }

    // ── Hồi quy chính: Role và IsActive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_DoiRole_ThiReadModelDoiTheo()
    {
        // Đây là lỗi số 1: trước bản vá không event nào mang role mới, read-model kẹt role cũ.
        var id = Guid.NewGuid();
        var existing = Existing(id, role: "Staff");
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, role: "Manager", reason: "RoleChanged")));

        cap.Added.Should().BeEmpty();
        cap.Updated.Should().ContainSingle();
        existing.Role.Should().Be("Manager");
        existing.LastSnapshotAtUtc.Should().Be(T1);
    }

    [Fact]
    public async Task Snapshot_KhoaTaiKhoan_ThiIsActiveVeFalse()
    {
        // Lỗi số 2: khoá/đình chỉ tài khoản trước đây không tới read-model.
        var id = Guid.NewGuid();
        var existing = Existing(id, isActive: true);
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, isActive: false, reason: "StatusChanged")));

        existing.IsActive.Should().BeFalse();
        cap.Updated.Should().ContainSingle();
    }

    // ── Chống snapshot về ngược thứ tự ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_VeTre_ThiBoQua_KhongDeReadModelLuiTrangThai()
    {
        var id = Guid.NewGuid();
        // Dòng đã áp snapshot ở T1; giờ có bản cũ hơn (T0) về sau.
        var existing = Existing(id, role: "Manager", lastSnapshotAt: T1);
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, role: "Staff", at: T0)));

        cap.Updated.Should().BeEmpty();
        existing.Role.Should().Be("Manager", "bản cũ không được ghi đè bản mới");
    }

    [Fact]
    public async Task Snapshot_TrungMocThoiGian_ThiBoQua()
    {
        var id = Guid.NewGuid();
        var existing = Existing(id, role: "Manager", lastSnapshotAt: T1);
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, role: "Staff", at: T1)));

        cap.Updated.Should().BeEmpty();
        existing.Role.Should().Be("Manager");
    }

    [Fact]
    public async Task Snapshot_DongChuaTungCoSnapshot_ThiVanApDung()
    {
        // Dòng do 3 consumer vòng đời cũ tạo ra có LastSnapshotAtUtc = null — không được coi là
        // "mới hơn" mà chặn mất snapshot đầu tiên.
        var id = Guid.NewGuid();
        var existing = Existing(id, role: "Staff", lastSnapshotAt: null);
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, role: "Admin", at: T0)));

        cap.Updated.Should().ContainSingle();
        existing.Role.Should().Be("Admin");
        existing.LastSnapshotAtUtc.Should().Be(T0);
    }

    // ── Xoá và khôi phục ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Snapshot_BaoDaXoa_ThiSoftDeleteDong()
    {
        var id = Guid.NewGuid();
        var existing = Existing(id);
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, isActive: false, isDeleted: true)));

        cap.Deleted.Should().ContainSingle("interceptor sẽ chuyển DeleteAsync thành soft delete");
        cap.Updated.Should().BeEmpty();
        existing.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Snapshot_KhoiPhucTaiKhoan_ThiBoCoXoaMem()
    {
        // ReactivateVerify hồi sinh account đã xoá mềm — read-model phải sống lại theo, nếu không
        // thì tài khoản khôi phục xong vĩnh viễn không nhận được thông báo nào nữa.
        var id = Guid.NewGuid();
        var existing = Existing(id, isActive: false, isDeleted: true);
        existing.DeletedAt = T0;
        var cap = BuildUow(new[] { existing });

        await Sut(cap).Consume(Ctx(Snapshot(id, isActive: true, isDeleted: false, reason: "Reactivated")));

        cap.Updated.Should().ContainSingle();
        cap.Deleted.Should().BeEmpty();
        existing.IsDeleted.Should().BeFalse();
        existing.DeletedAt.Should().BeNull();
        existing.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Snapshot_ChayLaiNhieuLan_ThiKhongDoiKetQua()
    {
        // Idempotent: resync gọi bao nhiêu lần cũng được. Lần 2 trùng mốc nên bị bỏ qua, và kết quả
        // sau lần 1 vẫn đúng.
        var id = Guid.NewGuid();
        var existing = Existing(id, role: "Staff");
        var cap = BuildUow(new[] { existing });
        var evt = Snapshot(id, role: "Manager");

        await Sut(cap).Consume(Ctx(evt));
        await Sut(cap).Consume(Ctx(evt));

        cap.Updated.Should().ContainSingle("lần 2 trùng mốc → bỏ qua");
        existing.Role.Should().Be("Manager");
    }
}
