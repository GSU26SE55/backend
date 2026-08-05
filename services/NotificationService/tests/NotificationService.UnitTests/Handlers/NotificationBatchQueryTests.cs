using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using GroupEntity = NotificationService.Domain.Entities.NotificationGroup;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// Sprint 6.4 NOTI4-09 — chi tiết một lần gửi.
///
/// <para>Trọng tâm là luật <b>lịch sử không đổi theo việc xoá nhóm</b>: nhóm bị xoá mềm vẫn phải
/// xuất hiện trong danh sách mục tiêu <b>kèm đúng tên</b> nó mang lúc được gửi. Lọc bỏ dòng đã xoá
/// nghe có vẻ "đúng quy ước" (dự án luôn lọc <c>!IsDeleted</c>) nên rất dễ có người sửa lại — test
/// này tồn tại để chặn đúng việc đó.</para>
/// </summary>
public class NotificationBatchQueryTests
{
    private static GroupEntity Group(string name, bool deleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        Kind = NotificationGroupKindEnum.Static,
        IsDeleted = deleted,
        DeletedAt = deleted ? DateTime.UtcNow : null,
        CreatedAt = DateTime.UtcNow,
    };

    private static NotificationBatch Batch() => new()
    {
        Id = Guid.NewGuid(),
        Type = NotificationTypeEnum.System,
        Title = "Bảo trì hệ thống",
        Body = "Nội dung",
        Channels = new[] { NotificationChannelEnum.InApp },
        Source = NotificationBatchSourceEnum.Manual,
        Status = NotificationBatchStatusEnum.FannedOut,
        RecipientCount = 1,
        NotificationCount = 1,
        CreatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ChiTiet_NhomDaXoaMem_VanHienDUNGTEN()
    {
        var deleted = Group("Trực sự cố cuối tuần", deleted: true);
        var alive = Group("Toàn bộ Quản lý");
        var batch = Batch();

        var targets = new[]
        {
            new NotificationBatchTarget
            {
                Id = Guid.NewGuid(), BatchId = batch.Id,
                TargetKind = NotificationBatchTargetKindEnum.Group, GroupId = deleted.Id,
            },
            new NotificationBatchTarget
            {
                Id = Guid.NewGuid(), BatchId = batch.Id,
                TargetKind = NotificationBatchTargetKindEnum.Group, GroupId = alive.Id,
            },
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            groupSeed: new[] { deleted, alive },
            batchSeed: new[] { batch },
            batchTargetSeed: targets);

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = batch.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.Targets.Should().HaveCount(2, "xoá nhóm không làm mất mục tiêu khỏi lịch sử");
        resp.Data.Targets.Should().Contain(
            t => t.GroupId == deleted.Id && t.GroupName == "Trực sự cố cuối tuần",
            "nhóm đã xoá mềm phải giữ ĐÚNG TÊN — mất tên thì người xem chỉ còn thấy 'một nhóm nào đó'");
        resp.Data.Targets.Should().Contain(t => t.GroupId == alive.Id && t.GroupName == "Toàn bộ Quản lý");
    }

    [Fact]
    public async Task ChiTiet_MucTieuCaNhan_KhongCoTenNhom()
    {
        var batch = Batch();
        var userId = Guid.NewGuid();
        var target = new NotificationBatchTarget
        {
            Id = Guid.NewGuid(), BatchId = batch.Id,
            TargetKind = NotificationBatchTargetKindEnum.User, UserId = userId,
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            batchSeed: new[] { batch }, batchTargetSeed: new[] { target });

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = batch.Id }, CancellationToken.None);

        var only = resp.Data!.Targets.Should().ContainSingle().Subject;
        only.TargetKind.Should().Be(NotificationBatchTargetKindEnum.User);
        only.UserId.Should().Be(userId);
        only.GroupName.Should().BeNull("mục tiêu cá nhân không gắn với nhóm nào");
    }

    [Fact]
    public async Task ChiTiet_ThongKeGomDungTungTrangThai()
    {
        var batch = Batch();
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();

        NotificationEntity Row(Guid user, NotificationChannelEnum ch, NotificationStatusEnum st, DateTime? read = null)
            => new()
            {
                Id = Guid.NewGuid(), UserId = user, BatchId = batch.Id,
                Type = batch.Type, Channel = ch, Status = st, ReadAt = read,
                Title = batch.Title, Body = batch.Body,
            };

        var rows = new[]
        {
            Row(u1, NotificationChannelEnum.InApp, NotificationStatusEnum.Sent, DateTime.UtcNow),
            Row(u1, NotificationChannelEnum.Email, NotificationStatusEnum.Failed),
            Row(u2, NotificationChannelEnum.InApp, NotificationStatusEnum.Sent),
            Row(u2, NotificationChannelEnum.Email, NotificationStatusEnum.Pending),
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: rows, batchSeed: new[] { batch });

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = batch.Id }, CancellationToken.None);

        var d = resp.Data!;
        d.TotalRows.Should().Be(4);
        d.DistinctRecipients.Should().Be(2, "4 dòng nhưng chỉ 2 người");
        d.SentCount.Should().Be(2);
        d.ReadCount.Should().Be(1);
        d.FailedCount.Should().Be(1);
        d.PendingCount.Should().Be(1);
        (d.SentCount + d.FailedCount + d.PendingCount).Should().Be(d.TotalRows);
    }

    /// <summary>
    /// GH-792 — dòng đang được gửi (<c>Processing</c>) phải nằm trong ô "đang chờ".
    /// </summary>
    /// <remarks>
    /// Bất biến <c>Sent + Failed + Pending == TotalRows</c> đã được khẳng định từ trước, nhưng
    /// không dữ liệu test nào có dòng <c>Processing</c> nên nó vẫn xanh sau khi trạng thái này
    /// ra đời. Người dùng nhìn thấy hậu quả ở màn hình chi tiết batch của Admin: giữa chừng một
    /// lần gửi lớn, tổng ba ô đếm nhỏ hơn tổng số dòng — trông như bản ghi bốc hơi.
    /// </remarks>
    [Fact]
    public async Task ChiTiet_DongDangGui_VanNamTrongODangCho()
    {
        var batch = Batch();
        var user = Guid.NewGuid();

        NotificationEntity Row(NotificationChannelEnum ch, NotificationStatusEnum st)
            => new()
            {
                Id = Guid.NewGuid(), UserId = user, BatchId = batch.Id,
                Type = batch.Type, Channel = ch, Status = st,
                Title = batch.Title, Body = batch.Body,
            };

        var rows = new[]
        {
            Row(NotificationChannelEnum.InApp, NotificationStatusEnum.Sent),
            Row(NotificationChannelEnum.Email, NotificationStatusEnum.Pending),
            Row(NotificationChannelEnum.Push, NotificationStatusEnum.Processing),
        };

        var (uow, _, _) = MockNotificationUnitOfWork.Build(
            notificationSeed: rows, batchSeed: new[] { batch });

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = batch.Id }, CancellationToken.None);

        var d = resp.Data!;
        d.TotalRows.Should().Be(3);
        d.PendingCount.Should().Be(2, "Pending và Processing đều là phần chưa xong");
        (d.SentCount + d.FailedCount + d.PendingCount).Should().Be(d.TotalRows,
            "không dòng nào được rơi ra ngoài cả ba ô đếm");
    }

    [Fact]
    public async Task ChiTiet_LanGuiChuaSinhDongNao_TraVeSoKhongChuKhongVoi()
    {
        // Batch ở trạng thái Pending (đường chạy nền sau này) — truy vấn thống kê không có dòng nào
        // để gom, phải trả 0 chứ không được ném NullReference.
        var batch = Batch();
        batch.Status = NotificationBatchStatusEnum.Pending;

        var (uow, _, _) = MockNotificationUnitOfWork.Build(batchSeed: new[] { batch });

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = batch.Id }, CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.Data!.TotalRows.Should().Be(0);
        resp.Data.DistinctRecipients.Should().Be(0);
    }

    [Fact]
    public async Task ChiTiet_KhongTimThay_Tra404()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var resp = await new NotificationBatchGetByIdQueryHandler(uow.Object)
            .Handle(new NotificationBatchGetByIdQuery { Id = Guid.NewGuid() }, CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }
}
