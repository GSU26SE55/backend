using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.CQRS.Handler.NotificationTemplate;
using NotificationService.Domain.Enums;
using TemplateEntity = NotificationService.Domain.Entities.NotificationTemplate;

namespace NotificationService.UnitTests.Handlers.NotificationTemplate;

/// <summary>
/// 02/08/2026 — soạn thảo template từ giao diện quản trị (tạo / sửa / quay lui / xoá).
///
/// Trước đó template CHỈ đến từ seeder, mà seeder idempotent theo cặp (Type × Channel) nên sửa
/// catalog rồi deploy lại cũng không ghi đè bản đã có ⇒ cả cơ chế phiên bản là code chết.
/// </summary>
public class NotificationTemplateCommandHandlersTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    // ──────────────────────────────── Create ────────────────────────────────

    private static NotificationTemplateCreateCommandHandler CreateHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object, h.Renderer, h.Audit.Object,
            TemplateHandlerHarness.Logger<NotificationTemplateCreateCommandHandler>());

    // 03/08/2026 — nội dung mẫu đổi sang biến CÓ THẬT của SlaWarning (percentage, ticketId). Bản cũ
    // dùng {{ticketCode}}/{{customerName}} — hai biến không hề tồn tại ở bất kỳ type nào — nên từ
    // khi TemplateVariableGuard được nối vào handler, mọi test dùng chúng đều trả 400. Chính là
    // bộ chặn làm đúng việc.
    private static NotificationTemplateCreateCommand CreateCommand(
        string title = "Tiêu đề {{percentage}}", string body = "Nội dung {{ticketId}}") => new()
        {
            Type = NotificationTypeEnum.SlaWarning,
            Channel = NotificationChannelEnum.Email,
            TitleTemplate = title,
            BodyTemplate = body,
            ActorUserId = Actor,
        };

    [Fact]
    public async Task Create_CapChuaCoTemplate_TaoV1VaBatLen()
    {
        var h = new TemplateHandlerHarness();

        var result = await CreateHandler(h).Handle(CreateCommand(), default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        h.Templates.Should().HaveCount(1);
        h.Templates[0].Version.Should().Be(1);
        h.Templates[0].IsActive.Should().BeTrue();
        h.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Create_CapDaCoTemplate_Tra409()
    {
        var existing = TemplateHandlerHarness.Template(
            NotificationTypeEnum.SlaWarning, NotificationChannelEnum.Email);
        var h = new TemplateHandlerHarness(existing);

        var result = await CreateHandler(h).Handle(CreateCommand(), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        h.Templates.Should().HaveCount(1, "không được thêm bản nào");
    }

    /// <summary>
    /// Index unique (type, channel, version) KHÔNG lọc is_deleted — dùng lại version của một bản đã
    /// xoá mềm sẽ vi phạm khoá, nên version mới phải tính trên cả bản đã xoá.
    /// </summary>
    [Fact]
    public async Task Create_CapChiConBanDaXoaMem_KhongDungLaiSoVersionCu()
    {
        var deleted = TemplateHandlerHarness.Template(
            NotificationTypeEnum.SlaWarning, NotificationChannelEnum.Email,
            version: 3, isActive: false, isDeleted: true);
        var h = new TemplateHandlerHarness(deleted);

        var result = await CreateHandler(h).Handle(CreateCommand(), default);

        result.IsSuccess.Should().BeTrue();
        h.Templates.Should().HaveCount(2);
        h.Templates.Single(t => !t.IsDeleted).Version.Should().Be(4);
    }

    [Fact]
    public async Task Create_CuPhapHandlebarsHong_Tra400VaKhongLuu()
    {
        var h = new TemplateHandlerHarness();

        var result = await CreateHandler(h).Handle(CreateCommand(body: "Hỏng {{#if x}} thiếu đóng"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("BodyTemplate");
        h.Templates.Should().BeEmpty();
    }

    /// <summary>
    /// 03/08/2026 — khoá hành vi: handler PHẢI gọi <c>TemplateVariableGuard</c>.
    ///
    /// <para>Bỏ dòng gọi guard trong <c>NotificationTemplateCreateCommandHandler</c> là test này đỏ.
    /// Cần khoá vì lỗi sai tên biến hoàn toàn im lặng: đúng cú pháp, lưu được, gửi được, chỉ có
    /// người nhận là đọc phải câu cụt.</para>
    /// </summary>
    [Fact]
    public async Task Create_BienKhongTonTai_Tra400VaKhongLuu()
    {
        var h = new TemplateHandlerHarness();

        // {{ticketCode}} — đúng cú pháp, nhưng SlaWarning không có khoá đó (consumer chỉ ghi
        // ticketId/staffId/percentage/warningAt/screen).
        var result = await CreateHandler(h).Handle(
            CreateCommand(title: "Cảnh báo SLA {{ticketCode}}"), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("ticketCode");
        h.Templates.Should().BeEmpty("template hỏng biến không được phép lọt vào DB");
    }

    [Fact]
    public async Task Create_ThongBaoLoi_GoiYTenBienDung()
    {
        var h = new TemplateHandlerHarness();

        var result = await CreateHandler(h).Handle(
            CreateCommand(body: "Còn {{percent}} phần trăm"), default);

        result.Message.Should().Contain("percentage",
            "báo sai thôi chưa đủ — phải chỉ luôn tên đúng thì người soạn mới sửa được ngay");
    }

    // ──────────────────────────────── Revise ────────────────────────────────

    private static NotificationTemplateReviseCommandHandler ReviseHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object, h.Renderer, h.Audit.Object,
            TemplateHandlerHarness.Logger<NotificationTemplateReviseCommandHandler>());

    [Fact]
    public async Task Revise_SinhPhienBanMoi_VaTatBanCu()
    {
        var current = TemplateHandlerHarness.Template(version: 1);
        var h = new TemplateHandlerHarness(current);

        var result = await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        {
            Id = current.Id,
            TitleTemplate = "Tiêu đề mới {{code}}",
            BodyTemplate = "Nội dung mới",
            ActorUserId = Actor,
        }, default);

        result.IsSuccess.Should().BeTrue();
        h.Templates.Should().HaveCount(2, "bản cũ được GIỮ LẠI để quay lui, không ghi đè");
        current.IsActive.Should().BeFalse();

        var revision = h.Templates.Single(t => t.Id != current.Id);
        revision.Version.Should().Be(2);
        revision.IsActive.Should().BeTrue();
        revision.TitleTemplate.Should().Be("Tiêu đề mới {{code}}");
        revision.Type.Should().Be(current.Type, "sửa không được đổi cặp (Type × Channel)");
        revision.Channel.Should().Be(current.Channel);
    }

    [Fact]
    public async Task Revise_KhongTimThayTemplate_Tra404()
    {
        var h = new TemplateHandlerHarness();

        var result = await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        {
            Id = Guid.NewGuid(),
            TitleTemplate = "x",
            BodyTemplate = "y",
            ActorUserId = Actor,
        }, default);

        result.StatusCode.Should().Be(404);
    }

    /// <summary>
    /// Bản gốc mang type <c>TicketCreated</c>, nên <c>{{percentage}}</c> (biến của SlaWarning) là
    /// biến lạ. Kiểm ở nhánh sửa phải lấy type từ bản gốc — người sửa không truyền type lên.
    /// </summary>
    [Fact]
    public async Task Revise_BienKhongTonTaiVoiTypeCuaBanGoc_Tra400()
    {
        var current = TemplateHandlerHarness.Template();
        var h = new TemplateHandlerHarness(current);

        var result = await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        {
            Id = current.Id,
            TitleTemplate = "Ticket {{percentage}}",
            BodyTemplate = "ok",
            ActorUserId = Actor,
        }, default);

        result.StatusCode.Should().Be(400);
        result.Message.Should().Contain("percentage");
        h.Templates.Should().HaveCount(1);
        current.IsActive.Should().BeTrue("bản đang dùng không được đụng tới khi lưu thất bại");
    }

    [Fact]
    public async Task Revise_CuPhapHong_Tra400VaKhongDungToiBanHienTai()
    {
        var current = TemplateHandlerHarness.Template();
        var h = new TemplateHandlerHarness(current);

        var result = await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        {
            Id = current.Id,
            TitleTemplate = "{{#each}} hỏng",
            BodyTemplate = "ok",
            ActorUserId = Actor,
        }, default);

        result.StatusCode.Should().Be(400);
        h.Templates.Should().HaveCount(1);
        current.IsActive.Should().BeTrue("bản đang dùng không được đụng tới khi lưu thất bại");
    }

    // ─────────────────────────────── Activate ───────────────────────────────

    private static NotificationTemplateActivateCommandHandler ActivateHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object, h.Audit.Object,
            TemplateHandlerHarness.Logger<NotificationTemplateActivateCommandHandler>());

    [Fact]
    public async Task Activate_QuayLuiBanCu_ChiConDungMotBanActive()
    {
        var old = TemplateHandlerHarness.Template(version: 1, isActive: false);
        var current = TemplateHandlerHarness.Template(version: 2, isActive: true);
        var h = new TemplateHandlerHarness(old, current);

        var result = await ActivateHandler(h).Handle(
            new NotificationTemplateActivateCommand { Id = old.Id, ActorUserId = Actor }, default);

        result.IsSuccess.Should().BeTrue();
        old.IsActive.Should().BeTrue();
        current.IsActive.Should().BeFalse();
        h.Templates.Count(t => t.IsActive && !t.IsDeleted).Should().Be(1);
    }

    [Fact]
    public async Task Activate_BanVonDaActive_Idempotent_KhongGhiAudit()
    {
        var current = TemplateHandlerHarness.Template(isActive: true);
        var h = new TemplateHandlerHarness(current);

        var result = await ActivateHandler(h).Handle(
            new NotificationTemplateActivateCommand { Id = current.Id, ActorUserId = Actor }, default);

        result.IsSuccess.Should().BeTrue();
        h.Audit.VerifyNoOtherCalls();
        h.Committed.Should().BeFalse("không có gì thay đổi thì không mở giao dịch");
    }

    [Fact]
    public async Task Activate_KhongTimThay_Tra404()
    {
        var h = new TemplateHandlerHarness();

        var result = await ActivateHandler(h).Handle(
            new NotificationTemplateActivateCommand { Id = Guid.NewGuid(), ActorUserId = Actor }, default);

        result.StatusCode.Should().Be(404);
    }

    // ──────────────────────────────── Delete ────────────────────────────────

    private static NotificationTemplateDeleteCommandHandler DeleteHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object, h.Audit.Object,
            TemplateHandlerHarness.Logger<NotificationTemplateDeleteCommandHandler>());

    [Fact]
    public async Task Delete_BanKhongActive_XoaMemThanhCong()
    {
        var old = TemplateHandlerHarness.Template(version: 1, isActive: false);
        var current = TemplateHandlerHarness.Template(version: 2, isActive: true);
        var h = new TemplateHandlerHarness(old, current);

        var result = await DeleteHandler(h).Handle(
            new NotificationTemplateDeleteCommand { Id = old.Id, ActorUserId = Actor }, default);

        result.IsSuccess.Should().BeTrue();
        old.IsDeleted.Should().BeTrue();
        current.IsDeleted.Should().BeFalse();
    }

    /// <summary>
    /// Cặp mất bản active ⇒ dispatcher lặng lẽ rơi về chuỗi hardcode trong consumer: thông báo vẫn
    /// gửi nhưng mất nội dung tuỳ biến và không ai hay. Phải chặn ngay tại đây.
    /// </summary>
    [Fact]
    public async Task Delete_BanDangDung_Tra409()
    {
        var current = TemplateHandlerHarness.Template(isActive: true);
        var h = new TemplateHandlerHarness(current);

        var result = await DeleteHandler(h).Handle(
            new NotificationTemplateDeleteCommand { Id = current.Id, ActorUserId = Actor }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        current.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_BanDaXoaTruocDo_Tra404()
    {
        var deleted = TemplateHandlerHarness.Template(isActive: false, isDeleted: true);
        var h = new TemplateHandlerHarness(deleted);

        var result = await DeleteHandler(h).Handle(
            new NotificationTemplateDeleteCommand { Id = deleted.Id, ActorUserId = Actor }, default);

        result.StatusCode.Should().Be(404);
    }

    // ─────────────────── Bất biến: mỗi cặp đúng 1 bản active ───────────────────

    /// <summary>
    /// Chuỗi thao tác thật của một admin: tạo → sửa 2 lần → quay lui → xoá bản thừa.
    /// Sau MỌI bước, cặp (Type × Channel) phải luôn có đúng một bản active — đúng ràng buộc của
    /// partial unique index `ux_notification_templates_active_per_key`.
    /// </summary>
    [Fact]
    public async Task ChuoiThaoTac_LuonGiuDungMotBanActive()
    {
        var h = new TemplateHandlerHarness();
        void AssertInvariant(string step) =>
            h.Templates.Count(t => t.IsActive && !t.IsDeleted)
                .Should().Be(1, $"sau bước '{step}' phải còn đúng 1 bản đang dùng");

        await CreateHandler(h).Handle(CreateCommand(), default);
        AssertInvariant("tạo");

        var v1 = h.Templates.Single();
        await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        { Id = v1.Id, TitleTemplate = "v2 {{percentage}}", BodyTemplate = "b", ActorUserId = Actor }, default);
        AssertInvariant("sửa lần 1");

        var v2 = h.Templates.Single(t => t.Version == 2);
        await ReviseHandler(h).Handle(new NotificationTemplateReviseCommand
        { Id = v2.Id, TitleTemplate = "v3 {{percentage}}", BodyTemplate = "b", ActorUserId = Actor }, default);
        AssertInvariant("sửa lần 2");

        await ActivateHandler(h).Handle(
            new NotificationTemplateActivateCommand { Id = v1.Id, ActorUserId = Actor }, default);
        AssertInvariant("quay lui về v1");

        var v3 = h.Templates.Single(t => t.Version == 3);
        await DeleteHandler(h).Handle(
            new NotificationTemplateDeleteCommand { Id = v3.Id, ActorUserId = Actor }, default);
        AssertInvariant("xoá v3");

        h.Templates.Where(t => !t.IsDeleted).Select(t => t.Version)
            .Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
