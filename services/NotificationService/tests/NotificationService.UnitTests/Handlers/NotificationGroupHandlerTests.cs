using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.CQRS.Handler.NotificationGroup;
using NotificationService.Application.CQRS.Query.NotificationGroup;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using GroupEntity = NotificationService.Domain.Entities.NotificationGroup;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// Sprint 6.4 NOTI4-02/03 — vòng đời nhóm người nhận.
///
/// <para>Nhóm quyết định AI nhận được thông báo nội bộ, nên các luật ở đây không phải chuyện hình
/// thức: xoá nhầm nhóm hệ thống là mọi thông báo tự động mất người nhận; thêm hàng loạt mà im lặng
/// bỏ qua là admin tưởng nhóm đã đủ người rồi gửi thiếu.</para>
/// </summary>
public class NotificationGroupHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static GroupEntity Group(
        string name,
        NotificationGroupKindEnum kind = NotificationGroupKindEnum.Static,
        string? roleFilter = null,
        bool isSystem = false) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Kind = kind,
            RoleFilter = roleFilter,
            IsSystem = isSystem,
            CreatedAt = DateTime.UtcNow,
        };

    private static AccountReadModel Account(string role = "Staff", bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Email = $"{Guid.NewGuid():N}@x.z",
        FullName = "Người Dùng",
        Role = role,
        IsActive = isActive,
        LastSyncedAtUtc = DateTime.UtcNow,
    };

    // ── Tạo nhóm ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tao_TrungTenKhongPhanBietHoaThuong_Tra409()
    {
        var existing = Group("Trực sự cố");
        var (uow, _, _) = MockNotificationUnitOfWork.Build(groupSeed: new[] { existing });

        var handler = new NotificationGroupCreateCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupCreateCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupCreateCommand { Name = "  trực sự cố  ", ActorUserId = Actor },
            CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        resp.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Tao_ThanhCong_LuonLaNhomStatic_VaChuanHoaTen()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var handler = new NotificationGroupCreateCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupCreateCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupCreateCommand { Name = "  Trực cuối tuần  ", ActorUserId = Actor },
            CancellationToken.None);

        resp.StatusCode.Should().Be(201);

        var created = uow.Object.NotificationGroups.GetAllAsync().Single();
        created.Name.Should().Be("Trực cuối tuần", "phải trim");
        created.NormalizedName.Should().Be("TRỰC CUỐI TUẦN");
        created.Kind.Should().Be(NotificationGroupKindEnum.Static, "API không tạo nhóm theo vai trò");
        created.IsSystem.Should().BeFalse();
    }

    // ── Nhóm hệ thống ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Sua_NhomHeThong_Tra409()
    {
        var system = Group("Toàn bộ Quản lý", NotificationGroupKindEnum.Role, "Manager", isSystem: true);
        var (uow, _, _) = MockNotificationUnitOfWork.Build(groupSeed: new[] { system });

        var handler = new NotificationGroupUpdateCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupUpdateCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupUpdateCommand { Id = system.Id, Name = "Đổi tên", ActorUserId = Actor },
            CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        system.Name.Should().Be("Toàn bộ Quản lý", "không được đổi");
    }

    [Fact]
    public async Task Xoa_NhomHeThong_Tra409()
    {
        // Xoá được nhóm hệ thống nghĩa là mọi thông báo tự động cho vai trò đó mất người nhận —
        // im lặng, không có gì báo lỗi. Đây là lý do luật này tồn tại.
        var system = Group("Toàn bộ Quản trị viên", NotificationGroupKindEnum.Role, "Admin", isSystem: true);
        var (uow, _, _) = MockNotificationUnitOfWork.Build(groupSeed: new[] { system });

        var handler = new NotificationGroupDeleteCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupDeleteCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupDeleteCommand { Id = system.Id, ActorUserId = Actor },
            CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        system.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Xoa_NhomThuong_XoaMemCaThanhVien()
    {
        var group = Group("Trực sự cố");
        var members = new[]
        {
            new NotificationGroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = Guid.NewGuid() },
            new NotificationGroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = Guid.NewGuid() },
        };
        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            groupSeed: new[] { group }, groupMemberSeed: members);

        var handler = new NotificationGroupDeleteCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupDeleteCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupDeleteCommand { Id = group.Id, ActorUserId = Actor },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        group.IsDeleted.Should().BeTrue();
        // Phải tự đánh dấu: ON DELETE CASCADE ở DB chỉ chạy khi xoá CỨNG, còn đây là xoá mềm. Bỏ sót
        // thì thêm lại đúng người vào nhóm mới sẽ đụng unique index của dòng cũ.
        members.Should().OnlyContain(m => m.IsDeleted, "thành viên phải bị xoá mềm theo");
    }

    // ── Thêm thành viên ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ThemThanhVien_VaoNhomTheoVaiTro_Tra409()
    {
        var role = Group("Toàn bộ Quản lý", NotificationGroupKindEnum.Role, "Manager", isSystem: true);
        var (uow, _, _) = MockNotificationUnitOfWork.Build(groupSeed: new[] { role });

        var handler = new NotificationGroupAddMembersCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupAddMembersCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupAddMembersCommand
            {
                GroupId = role.Id,
                UserIds = new List<Guid> { Guid.NewGuid() },
                ActorUserId = Actor,
            },
            CancellationToken.None);

        resp.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ThemThanhVien_BoQuaNguoiDaCoVaIdKhongTonTai_DemRieng()
    {
        // Bỏ qua chứ KHÔNG làm hỏng cả lô — chọn 30 người rồi bị từ chối toàn bộ vì 1 người trùng là
        // hành vi khó chịu. Nhưng phải ĐẾM RIÊNG, im lặng bỏ qua thì admin tưởng đã thêm đủ.
        var group = Group("Trực sự cố");
        var already = Account();
        var fresh = Account();
        var ghost = Guid.NewGuid();   // không có trong read-model

        var existingMember = new NotificationGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = already.Id,
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { already, fresh },
            groupSeed: new[] { group },
            groupMemberSeed: new[] { existingMember });

        var handler = new NotificationGroupAddMembersCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupAddMembersCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupAddMembersCommand
            {
                GroupId = group.Id,
                UserIds = new List<Guid> { already.Id, fresh.Id, ghost, fresh.Id },
                ActorUserId = Actor,
            },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Added.Should().Be(1, "chỉ có fresh là mới; id lặp trong payload phải được gộp");
        resp.Data.AlreadyMembers.Should().Be(1);
        resp.Data.UnknownAccounts.Should().Be(1);
        resp.Message.Should().Contain("đã có sẵn").And.Contain("không tồn tại");
    }

    [Fact]
    public async Task ThemThanhVien_HoiSinhDongDaXoaMem_ThayViTaoDongMoi()
    {
        // Unique index lọc is_deleted nên tạo dòng mới KHÔNG vi phạm khoá, nhưng để lại rác và mất
        // mốc thời gian gốc. Hồi sinh dòng cũ rẻ hơn.
        var group = Group("Trực sự cố");
        var account = Account();
        var removed = new NotificationGroupMember
        {
            Id = Guid.NewGuid(),
            GroupId = group.Id,
            UserId = account.Id,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow.AddDays(-1),
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { account },
            groupSeed: new[] { group },
            groupMemberSeed: new[] { removed });

        var handler = new NotificationGroupAddMembersCommandHandler(
            uow.Object, new NoopAuditWriter(), NullLogger<NotificationGroupAddMembersCommandHandler>.Instance);

        var resp = await handler.Handle(
            new NotificationGroupAddMembersCommand
            {
                GroupId = group.Id,
                UserIds = new List<Guid> { account.Id },
                ActorUserId = Actor,
            },
            CancellationToken.None);

        resp.Data!.Added.Should().Be(1);
        removed.IsDeleted.Should().BeFalse("phải hồi sinh dòng cũ");
        removed.DeletedAt.Should().BeNull();
        uow.Object.NotificationGroupMembers.GetAllAsync().Should().HaveCount(1, "không tạo dòng thứ hai");
    }

    // ── Đếm người nhận ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DemThanhVien_ChiTinhNguoiConHoatDong()
    {
        // Thành viên trỏ tới tài khoản đã nghỉ vẫn nằm trong bảng nhưng KHÔNG được tính — nếu tính
        // thì admin thấy "3 người" rồi gửi đi chỉ 2 người nhận, không có gì báo lỗi.
        var group = Group("Trực sự cố");
        var active = Account();
        var inactive = Account(isActive: false);
        var members = new[]
        {
            new NotificationGroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = active.Id },
            new NotificationGroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = inactive.Id },
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { active, inactive },
            groupSeed: new[] { group },
            groupMemberSeed: members);

        var handler = new NotificationGroupGetByIdQueryHandler(uow.Object);
        var resp = await handler.Handle(
            new NotificationGroupGetByIdQuery { Id = group.Id }, CancellationToken.None);

        resp.Data!.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task DemThanhVien_NhomTheoVaiTro_SuyTuReadModel_KhongDocBangThanhVien()
    {
        var group = Group("Toàn bộ Quản lý", NotificationGroupKindEnum.Role, "Manager", isSystem: true);
        var accounts = new[]
        {
            Account("Manager"), Account("Manager"),
            Account("manager"),               // khác hoa-thường — vẫn phải khớp
            Account("Manager", isActive: false),
            Account("Staff"),
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: accounts, groupSeed: new[] { group });

        var handler = new NotificationGroupGetByIdQueryHandler(uow.Object);
        var resp = await handler.Handle(
            new NotificationGroupGetByIdQuery { Id = group.Id }, CancellationToken.None);

        resp.Data!.MemberCount.Should().Be(3, "2 Manager + 1 manager viết thường, loại người đã ngừng");
    }

    [Fact]
    public async Task DanhSachNhom_DemNguoiNhanDungChoCaHaiLoaiNhom()
    {
        var staticGroup = Group("Trực sự cố");
        var roleGroup = Group("Toàn bộ Quản lý", NotificationGroupKindEnum.Role, "Manager", isSystem: true);
        var manager = Account("Manager");
        var staff = Account("Staff");

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { manager, staff },
            groupSeed: new[] { staticGroup, roleGroup },
            groupMemberSeed: new[]
            {
                new NotificationGroupMember { Id = Guid.NewGuid(), GroupId = staticGroup.Id, UserId = staff.Id },
            });

        var handler = new NotificationGroupGetListQueryHandler(uow.Object);
        var resp = await handler.Handle(new NotificationGroupGetListQuery(), CancellationToken.None);

        resp.Data!.Items.Should().HaveCount(2);
        resp.Data.Items.Single(g => g.Id == staticGroup.Id).MemberCount.Should().Be(1);
        resp.Data.Items.Single(g => g.Id == roleGroup.Id).MemberCount.Should().Be(1);
        resp.Data.Items[0].IsSystem.Should().BeTrue("nhóm hệ thống phải lên đầu");
    }
}
