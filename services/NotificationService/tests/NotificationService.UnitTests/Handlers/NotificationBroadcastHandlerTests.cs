using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using GroupEntity = NotificationService.Domain.Entities.NotificationGroup;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// Sprint 6.4 NOTI4-07 — gửi hàng loạt. Đây là trái tim của sprint, và ba luật dưới đây nếu sai thì
/// hỏng theo kiểu <b>im lặng</b>, không có gì báo lỗi:
///
/// <list type="number">
/// <item><b>Gom trùng</b> — người ở hai nhóm cùng được nhắm nhận hai thông báo y hệt.</item>
/// <item><b>Lọc người còn hoạt động</b> — người đã nghỉ vẫn nhận thông báo nội bộ.</item>
/// <item><b>Tập rỗng phải báo lỗi</b> — báo "đã gửi" trong khi không ai nhận gì.</item>
/// </list>
///
/// <para>Dùng <see cref="RecipientResolver"/> THẬT (không mock) vì chính đoạn gom trùng nằm trong
/// đó — mock nó đi thì test này mất hết ý nghĩa.</para>
/// </summary>
public class NotificationBroadcastHandlerTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static AccountReadModel Account(string role = "Staff", bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Email = $"{Guid.NewGuid():N}@x.z",
        FullName = "Người Dùng",
        Role = role,
        IsActive = isActive,
        LastSyncedAtUtc = DateTime.UtcNow,
    };

    private static GroupEntity StaticGroup(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Kind = NotificationGroupKindEnum.Static,
        CreatedAt = DateTime.UtcNow,
    };

    private static GroupEntity RoleGroup(string role) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Toàn bộ {role}",
        NormalizedName = $"TOÀN BỘ {role}".ToUpperInvariant(),
        Kind = NotificationGroupKindEnum.Role,
        RoleFilter = role,
        IsSystem = true,
        CreatedAt = DateTime.UtcNow,
    };

    private static NotificationGroupMember Member(Guid groupId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        GroupId = groupId,
        UserId = userId,
    };

    private static NotificationBroadcastCommand Command(
        IEnumerable<Guid>? groupIds = null,
        IEnumerable<Guid>? userIds = null,
        params NotificationChannelEnum[] channels) => new()
        {
            Type = NotificationTypeEnum.TicketCreated,
            Channels = (channels.Length > 0 ? channels : new[] { NotificationChannelEnum.InApp }).ToList(),
            Title = "System maintenance",
            Body = "System maintenance 22:00-23:00.",
            GroupIds = groupIds?.ToList() ?? new List<Guid>(),
            UserIds = userIds?.ToList() ?? new List<Guid>(),
            ActorUserId = Actor,
        };

    // ── Luật 1: gom trùng ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HaiNhomGiaoNhau_NguoiChungChiNhanMotLan()
    {
        var shared = Account();
        var onlyA = Account();
        var onlyB = Account();

        var groupA = StaticGroup("Nhóm A");
        var groupB = StaticGroup("Nhóm B");

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { shared, onlyA, onlyB },
            groupSeed: new[] { groupA, groupB },
            groupMemberSeed: new[]
            {
                Member(groupA.Id, shared.Id), Member(groupA.Id, onlyA.Id),
                Member(groupB.Id, shared.Id), Member(groupB.Id, onlyB.Id),
            });

        var added = CaptureNotifications(notifications);

        var handler = NewHandler(uow);
        var resp = await handler.Handle(
            Command(groupIds: new[] { groupA.Id, groupB.Id },
                    channels: new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Push }),
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.RecipientCount.Should().Be(3, "hợp của 2 nhóm là 3 người, không phải 4");
        resp.Data.NotificationCount.Should().Be(6, "3 người × 2 kênh");

        added.Should().HaveCount(6);
        added.Where(n => n.UserId == shared.Id).Should().HaveCount(2,
            "người ở cả hai nhóm chỉ nhận 1 dòng MỖI KÊNH, không phải 2 dòng mỗi kênh");
        added.Select(n => (n.UserId, n.Channel)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task NguoiVuaTrongNhomVuaDuocChonDichDanh_ChiNhanMotLan()
    {
        var person = Account();
        var group = StaticGroup("Nhóm A");

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { person },
            groupSeed: new[] { group },
            groupMemberSeed: new[] { Member(group.Id, person.Id) });

        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(groupIds: new[] { group.Id }, userIds: new[] { person.Id }),
            CancellationToken.None);

        resp.Data!.RecipientCount.Should().Be(1);
        added.Should().HaveCount(1);
    }

    // ── Luật 2: chỉ người còn hoạt động ────────────────────────────────────────────────────────

    [Fact]
    public async Task NguoiDaNgungHoatDong_KhongNhan_KeCaKhiChonDichDanh()
    {
        // Không có cửa sau nào: chỉ định đích danh cũng phải qua đúng bộ lọc như thành viên nhóm.
        var inactive = Account(isActive: false);
        var active = Account();

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { inactive, active });

        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(userIds: new[] { inactive.Id, active.Id }), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.RecipientCount.Should().Be(1);
        resp.Data.SkippedUsers.Should().Be(1);
        resp.Message.Should().Contain("Skipped 1 invalid or inactive recipient(s).");
        added.Should().ContainSingle().Which.UserId.Should().Be(active.Id);
    }

    [Fact]
    public async Task NhomTheoVaiTro_SuyNguoiNhanTuReadModel()
    {
        var group = RoleGroup("Manager");
        var accounts = new[]
        {
            Account("Manager"), Account("manager"), Account("Manager", isActive: false), Account("Staff"),
        };

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: accounts, groupSeed: new[] { group });

        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(groupIds: new[] { group.Id }), CancellationToken.None);

        resp.Data!.RecipientCount.Should().Be(2, "khớp role không phân biệt hoa-thường, loại người đã ngừng");
        added.Should().HaveCount(2);
    }

    // ── Luật 3: tập rỗng phải báo lỗi ──────────────────────────────────────────────────────────

    [Fact]
    public async Task KhongConNguoiNhanHopLe_Tra400_VaKHONGTaoLanGuiMoCoi()
    {
        // Đây đúng là cách một lỗi nghiêm trọng từng ẩn mình suốt thời gian dài: nhánh "không có
        // người nhận → ghi log rồi lặng lẽ trả về" nhìn từ ngoài y hệt như đã gửi thành công.
        var inactive = Account(isActive: false);
        var group = StaticGroup("Nhóm chết");

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { inactive },
            groupSeed: new[] { group },
            groupMemberSeed: new[] { Member(group.Id, inactive.Id) });

        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(groupIds: new[] { group.Id }), CancellationToken.None);

        resp.IsSuccess.Should().BeFalse();
        resp.StatusCode.Should().Be(400);
        resp.Message.Should().Contain("No valid recipients");

        added.Should().BeEmpty();
        uow.Object.NotificationBatches.GetAllAsync().Should().BeEmpty("không được để lại lần gửi mồ côi");
        uow.Verify(u => u.BeginTransactionAsync(), Times.Never, "không cần mở transaction để trả lỗi");
    }

    [Fact]
    public async Task NhomKhongTonTai_BaoLyDoCuThe()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var resp = await NewHandler(uow).Handle(
            Command(groupIds: new[] { Guid.NewGuid() }), CancellationToken.None);

        resp.StatusCode.Should().Be(400);
        resp.Message.Should().Contain("group(s) not found or deleted",
            "lỗi trống trơn thì admin không biết sửa gì");
    }

    // ── Bản ghi lần gửi ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GuiThanhCong_GhiDuLanGui_MucTieu_VaGanBatchIdVaoTungDong()
    {
        var manager = Account("Manager");
        var extra = Account("Staff");
        var group = RoleGroup("Manager");

        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(
            accountSeed: new[] { manager, extra }, groupSeed: new[] { group });

        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(groupIds: new[] { group.Id }, userIds: new[] { extra.Id },
                    channels: new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Email }),
            CancellationToken.None);

        resp.StatusCode.Should().Be(201);

        var batch = uow.Object.NotificationBatches.GetAllAsync().Single();
        batch.Status.Should().Be(NotificationBatchStatusEnum.FannedOut);
        batch.Source.Should().Be(NotificationBatchSourceEnum.Manual);
        batch.RecipientCount.Should().Be(2);
        batch.NotificationCount.Should().Be(4);
        batch.Channels.Should().BeEquivalentTo(
            new[] { NotificationChannelEnum.InApp, NotificationChannelEnum.Email });

        var targets = uow.Object.NotificationBatchTargets.GetAllAsync().ToList();
        targets.Should().HaveCount(2);
        targets.Should().ContainSingle(t => t.TargetKind == NotificationBatchTargetKindEnum.Group
                                            && t.GroupId == group.Id && t.UserId == null);
        targets.Should().ContainSingle(t => t.TargetKind == NotificationBatchTargetKindEnum.User
                                            && t.UserId == extra.Id && t.GroupId == null);

        added.Should().HaveCount(4);
        added.Should().OnlyContain(n => n.BatchId == batch.Id, "mọi dòng phải gom được về lần gửi");
        added.Should().OnlyContain(n => n.Status == NotificationStatusEnum.Pending,
            "phải để worker giao, nhờ vậy vẫn tôn trọng tuỳ chọn nhận tin và khung giờ yên tĩnh");
    }

    [Fact]
    public async Task GuiThanhCong_KhongGoiUpdateAsyncTrenBatchVuaTao()
    {
        // Hồi quy một lỗi đã xảy ra thật trên môi trường chạy: handler từng gán Status rồi gọi
        // UpdateAsync trên chính entity vừa AddAsync. EF chuyển entity từ Added sang Modified ⇒ bỏ
        // hẳn lệnh INSERT batch và cố UPDATE một dòng chưa tồn tại, khiến target vi phạm khoá ngoại
        // và cả lần gửi hỏng với HTTP 500.
        //
        // Mock không mô phỏng EntityState nên không tái hiện được lỗi gốc; thứ khoá được ở tầng này
        // là HÀNH VI: không đụng UpdateAsync lên bản ghi vừa tạo.
        var person = Account();
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(accountSeed: new[] { person });
        CaptureNotifications(notifications);

        var batchRepo = Mock.Get(uow.Object.NotificationBatches);

        var resp = await NewHandler(uow).Handle(
            Command(userIds: new[] { person.Id }), CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        batchRepo.Verify(
            r => r.UpdateAsync(It.IsAny<NotificationBatch>()),
            Times.Never,
            "entity vừa AddAsync đang ở trạng thái Added — gọi UpdateAsync sẽ huỷ lệnh INSERT");
    }

    [Fact]
    public async Task TrungKenhTrongPayload_DuocGopLai()
    {
        var person = Account();
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build(accountSeed: new[] { person });
        var added = CaptureNotifications(notifications);

        var resp = await NewHandler(uow).Handle(
            Command(userIds: new[] { person.Id },
                    channels: new[]
                    {
                        NotificationChannelEnum.InApp,
                        NotificationChannelEnum.InApp,
                        NotificationChannelEnum.Push,
                    }),
            CancellationToken.None);

        resp.Data!.NotificationCount.Should().Be(2);
        added.Should().HaveCount(2);
    }

    // ── Xem trước phải khớp lần gửi thật ───────────────────────────────────────────────────────

    [Fact]
    public async Task XemTruoc_RaDungConSoCuaLanGuiThat()
    {
        // Nếu hai đường tự tính riêng thì sớm muộn sẽ lệch, mà lệch nghĩa là admin thấy "3 người"
        // rồi bấm gửi và chỉ 2 người nhận — không có gì báo lỗi.
        var shared = Account();
        var onlyA = Account();
        var groupA = StaticGroup("Nhóm A");
        var groupB = StaticGroup("Nhóm B");

        var seedAccounts = new[] { shared, onlyA };
        var seedGroups = new[] { groupA, groupB };
        var seedMembers = new[]
        {
            Member(groupA.Id, shared.Id), Member(groupA.Id, onlyA.Id), Member(groupB.Id, shared.Id),
        };

        var (uowPreview, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: seedAccounts, groupSeed: seedGroups, groupMemberSeed: seedMembers);

        var preview = await new NotificationBroadcastPreviewQueryHandler(
                uowPreview.Object, new RecipientResolver(uowPreview.Object))
            .Handle(
                new NotificationBroadcastPreviewQuery
                {
                    GroupIds = new List<Guid> { groupA.Id, groupB.Id },
                    Channels = new List<NotificationChannelEnum> { NotificationChannelEnum.InApp },
                },
                CancellationToken.None);

        var (uowSend, _, _) = MockNotificationUnitOfWork.Build(
            accountSeed: seedAccounts, groupSeed: seedGroups, groupMemberSeed: seedMembers);

        var sent = await NewHandler(uowSend).Handle(
            Command(groupIds: new[] { groupA.Id, groupB.Id }), CancellationToken.None);

        preview.Data!.RecipientCount.Should().Be(sent.Data!.RecipientCount);
        preview.Data.NotificationCount.Should().Be(sent.Data.NotificationCount);
        preview.Data.RecipientCount.Should().Be(2);
        preview.Data.RawCount.Should().Be(3, "cộng dồn từng nhóm là 3 — chênh lệch cho thấy nhóm giao nhau");
    }

    // ── Hạ tầng test ───────────────────────────────────────────────────────────────────────────

    private static NotificationBroadcastCommandHandler NewHandler(
        Mock<NotificationService.Application.Interfaces.Repositories.INotificationUnitOfWork> uow)
        => new(
            uow.Object,
            new RecipientResolver(uow.Object),
            NoopAuditWriter.Instance,
            NullLogger<NotificationBroadcastCommandHandler>.Instance);

    /// <summary>
    /// Mock <c>Notifications</c> dùng chung không ghi lại phần tử được thêm (cố ý — để không đổi
    /// hành vi của các test cũ), nên bắt riêng ở đây.
    /// </summary>
    private static List<NotificationEntity> CaptureNotifications(
        Mock<SharedKernels.Interfaces.IGenericRepository<NotificationEntity>> repo)
    {
        var captured = new List<NotificationEntity>();
        repo.Setup(r => r.AddAsync(It.IsAny<NotificationEntity>()))
            .Callback<NotificationEntity>(captured.Add)
            .Returns(Task.CompletedTask);
        return captured;
    }
}
