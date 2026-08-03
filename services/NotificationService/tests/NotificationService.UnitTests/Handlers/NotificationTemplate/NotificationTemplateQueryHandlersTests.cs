using System.Text.Json;
using NotificationService.Application.CQRS.Handler.NotificationTemplate;
using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.NotificationTemplate;

public class NotificationTemplateQueryHandlersTests
{
    // ──────────────────────────────── GetList ────────────────────────────────

    private static NotificationTemplateGetListQueryHandler ListHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object);

    [Fact]
    public async Task GetList_TraCaLichSuPhienBan_MoiNhatTruoc()
    {
        var h = new TemplateHandlerHarness(
            TemplateHandlerHarness.Template(version: 1, isActive: false),
            TemplateHandlerHarness.Template(version: 2, isActive: true));

        var result = await ListHandler(h).Handle(new NotificationTemplateGetListQuery(), default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(2);
        result.Data.Items.Select(i => i.Version).Should().Equal(2, 1);
    }

    [Fact]
    public async Task GetList_ActiveOnly_ChiTraBanDangDung()
    {
        var h = new TemplateHandlerHarness(
            TemplateHandlerHarness.Template(version: 1, isActive: false),
            TemplateHandlerHarness.Template(version: 2, isActive: true));

        var result = await ListHandler(h).Handle(
            new NotificationTemplateGetListQuery { ActiveOnly = true }, default);

        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items.Single().Version.Should().Be(2);
    }

    [Fact]
    public async Task GetList_BoQuaBanDaXoaMem()
    {
        var h = new TemplateHandlerHarness(
            TemplateHandlerHarness.Template(version: 1, isActive: false, isDeleted: true),
            TemplateHandlerHarness.Template(version: 2, isActive: true));

        var result = await ListHandler(h).Handle(new NotificationTemplateGetListQuery(), default);

        result.Data!.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetList_LocTheoTypeVaChannel()
    {
        var h = new TemplateHandlerHarness(
            TemplateHandlerHarness.Template(NotificationTypeEnum.SlaBreached, NotificationChannelEnum.Email),
            TemplateHandlerHarness.Template(NotificationTypeEnum.SlaBreached, NotificationChannelEnum.Sms),
            TemplateHandlerHarness.Template(NotificationTypeEnum.TicketClosed, NotificationChannelEnum.Email));

        var result = await ListHandler(h).Handle(new NotificationTemplateGetListQuery
        {
            Type = NotificationTypeEnum.SlaBreached,
            Channel = NotificationChannelEnum.Email,
        }, default);

        result.Data!.TotalItems.Should().Be(1);
        result.Data.Items.Single().Channel.Should().Be(NotificationChannelEnum.Email);
    }

    /// <summary>Trang vượt quá dữ liệu trả 200 + rỗng, không phải lỗi (xem ToPagedEntityListAsync).</summary>
    [Fact]
    public async Task GetList_TrangVuotQuaDuLieu_TraRong()
    {
        var h = new TemplateHandlerHarness(TemplateHandlerHarness.Template());

        var result = await ListHandler(h).Handle(
            new NotificationTemplateGetListQuery { PageNumber = 99 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().BeEmpty();
        result.Data.TotalItems.Should().Be(1);
    }

    // ─────────────────────────────── GetById ───────────────────────────────

    [Fact]
    public async Task GetById_TraDungBanGhi()
    {
        var target = TemplateHandlerHarness.Template(version: 7, isActive: false);
        var h = new TemplateHandlerHarness(target, TemplateHandlerHarness.Template(version: 8));

        var result = await new NotificationTemplateGetByIdQueryHandler(h.Uow.Object)
            .Handle(new NotificationTemplateGetByIdQuery { Id = target.Id }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Version.Should().Be(7);
        result.Data.IsActive.Should().BeFalse("bản không active vẫn xem lại được");
    }

    [Fact]
    public async Task GetById_KhongTonTai_Tra404()
    {
        var h = new TemplateHandlerHarness();

        var result = await new NotificationTemplateGetByIdQueryHandler(h.Uow.Object)
            .Handle(new NotificationTemplateGetByIdQuery { Id = Guid.NewGuid() }, default);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_BanDaXoaMem_Tra404()
    {
        var deleted = TemplateHandlerHarness.Template(isDeleted: true);
        var h = new TemplateHandlerHarness(deleted);

        var result = await new NotificationTemplateGetByIdQueryHandler(h.Uow.Object)
            .Handle(new NotificationTemplateGetByIdQuery { Id = deleted.Id }, default);

        result.StatusCode.Should().Be(404);
    }

    // ─────────────────────────────── Preview ───────────────────────────────

    private static NotificationTemplatePreviewQueryHandler PreviewHandler(TemplateHandlerHarness h) =>
        new(h.Uow.Object, h.Renderer,
            TemplateHandlerHarness.Logger<NotificationTemplatePreviewQueryHandler>());

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public async Task Preview_DienDuBien_RenderRaNoiDungDay()
    {
        var t = TemplateHandlerHarness.Template(
            title: "Ticket {{ticketCode}}", body: "Khách {{customerName}} vừa tạo.");
        var h = new TemplateHandlerHarness(t);

        var result = await PreviewHandler(h).Handle(new NotificationTemplatePreviewQuery
        {
            Id = t.Id,
            SampleData = Json("""{"ticketCode":"TK-001","customerName":"Nguyễn Văn An"}"""),
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Title.Should().Be("Ticket TK-001");
        result.Data.Body.Should().Be("Khách Nguyễn Văn An vừa tạo.");
    }

    /// <summary>Không truyền dữ liệu ⇒ placeholder ra rỗng — đó là cách phát hiện gọi sai tên biến.</summary>
    [Fact]
    public async Task Preview_KhongCoDuLieuMau_PlaceholderRaRong()
    {
        var t = TemplateHandlerHarness.Template(title: "Ticket {{ticketCode}}", body: "x");
        var h = new TemplateHandlerHarness(t);

        var result = await PreviewHandler(h).Handle(
            new NotificationTemplatePreviewQuery { Id = t.Id }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Title.Should().Be("Ticket ");
    }

    [Fact]
    public async Task Preview_KhoaKhongPhanBietHoaThuong()
    {
        var t = TemplateHandlerHarness.Template(title: "{{TicketCode}}", body: "x");
        var h = new TemplateHandlerHarness(t);

        var result = await PreviewHandler(h).Handle(new NotificationTemplatePreviewQuery
        {
            Id = t.Id,
            SampleData = Json("""{"ticketCode":"TK-9"}"""),
        }, default);

        result.Data!.Title.Should().Be("TK-9");
    }

    [Fact]
    public async Task Preview_KhongTimThay_Tra404()
    {
        var h = new TemplateHandlerHarness();

        var result = await PreviewHandler(h).Handle(
            new NotificationTemplatePreviewQuery { Id = Guid.NewGuid() }, default);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Preview_TemplateHongCuPhap_Tra400ChuKhongNem500()
    {
        var t = TemplateHandlerHarness.Template(title: "ok", body: "{{#if x}} thiếu đóng");
        var h = new TemplateHandlerHarness(t);

        var result = await PreviewHandler(h).Handle(
            new NotificationTemplatePreviewQuery { Id = t.Id }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
