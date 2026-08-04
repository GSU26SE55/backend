using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// 03/08/2026 — bộ kiểm tên biến template.
///
/// <para>Bối cảnh: <c>TemplateSyntaxGuard</c> đã bắt được template hỏng cú pháp, nhưng lỗi thực sự
/// gây thiệt hại lại là loại <b>đúng cú pháp mà sai tên biến</b>. <c>{{ticketCode}}</c> compile hoàn
/// hảo; nó chỉ sai ở chỗ consumer ghi khoá <c>code</c>. Handlebars render biến lạ ra chuỗi rỗng chứ
/// không ném, nên người nhận đọc phải "Ticket mới " suốt nhiều tháng mà không log nào ghi lại.</para>
/// </summary>
public class TemplateVariableGuardTests
{
    // ── Bóc tên biến ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BocBien_CumDonGian()
    {
        TemplateVariableGuard.ExtractVariables("Ticket {{code}} — ưu tiên {{priority}}")
            .Should().BeEquivalentTo(["code", "priority"]);
    }

    [Fact]
    public void BocBien_BaNgoacKhongEscape_VanTinhLaBien()
    {
        TemplateVariableGuard.ExtractVariables("{{{rawHtml}}}")
            .Should().ContainSingle().Which.Should().Be("rawHtml");
    }

    [Fact]
    public void BocBien_BoQuaChuThichThoDongVaPartial()
    {
        // Ba dạng này không chứa biến của người soạn — tính nhầm là chặn oan template hợp lệ.
        TemplateVariableGuard.ExtractVariables("{{! ghi chú }}{{/if}}{{> phanChung}}")
            .Should().BeEmpty();
    }

    [Fact]
    public void BocBien_TrongBlock_HelperKhongPhaiBien()
    {
        // {{#if code}} → "if" là helper, chỉ "code" mới là biến. Tính cả "if" thì mọi template dùng
        // điều kiện đều bị chặn oan.
        TemplateVariableGuard.ExtractVariables("{{#if code}}có{{else}}không{{/if}}")
            .Should().BeEquivalentTo(["code"]);
    }

    [Fact]
    public void BocBien_SectionMotToken_VanLaBien()
    {
        // {{#items}} là dạng section lấy thẳng biến, không có helper để cắt.
        TemplateVariableGuard.ExtractVariables("{{#items}}x{{/items}}")
            .Should().BeEquivalentTo(["items"]);
    }

    [Fact]
    public void BocBien_DuongDanLongNhau_ChiLayGoc()
    {
        // Model là từ điển phẳng nên chỉ gốc mới tra được.
        TemplateVariableGuard.ExtractVariables("{{ticket.code}}")
            .Should().BeEquivalentTo(["ticket"]);
    }

    [Fact]
    public void BocBien_BoQuaTuKhoaVaHangSo()
    {
        TemplateVariableGuard.ExtractVariables("{{this}} {{else}} {{formatDate \"dd/MM\"}} {{.}}")
            .Should().BeEmpty();
    }

    // ── Phát hiện biến lạ ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void BienHopLe_KhongBaoLoi()
    {
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.TicketCreated,
                "Ticket mới {{code}}",
                "Mức ưu tiên {{priority}}.")
            .Should().BeNull();
    }

    [Fact]
    public void BienBuiltin_LuonHopLe_OMoiType()
    {
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.EnvironmentalIncidentResolved,   // type KHÔNG có payload nào
                "{{Title}}",
                "{{Body}} — {{CreatedAt}}")
            .Should().BeNull("sáu biến builtin do dispatcher nạp, không phụ thuộc payload");
    }

    /// <summary>
    /// Đây chính là lỗi đã sống trong production: consumer ghi khoá <c>code</c>, template gọi
    /// <c>{{ticketCode}}</c>. Bỏ dòng gọi guard trong handler là test này đỏ.
    /// </summary>
    [Fact]
    public void TicketCode_LaBienLa_ViConsumerGhiKhoaCode()
    {
        var error = TemplateVariableGuard.FindUnknownVariables(
            NotificationTypeEnum.TicketCreated, "Ticket mới {{ticketCode}}", "Nội dung");

        error.Should().NotBeNull();
        error.Should().Contain("ticketCode");
        error.Should().Contain("code", "phải gợi ý đúng tên khoá mà consumer thật sự ghi");
    }

    /// <summary>Lỗi thứ hai đã sống trong production, trên 1229 dòng thông báo pin.</summary>
    [Fact]
    public void SerialNumber_LaBienLa_ViConsumerGhiAssetSerialNumber()
    {
        var error = TemplateVariableGuard.FindUnknownVariables(
            NotificationTypeEnum.BatteryAnomalyDetected, "Bất thường pin {{serialNumber}}", "x");

        error.Should().NotBeNull();
        error.Should().Contain("assetSerialNumber", "gợi ý phải chỉ đúng khoá thật");
    }

    [Fact]
    public void Threshold_GoiY_ThresholdValue()
    {
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.BatteryAnomalyDetected, "x", "ngưỡng {{threshold}}")
            .Should().Contain("thresholdValue");
    }

    [Fact]
    public void GoLoiChinhTa_VanBiChan()
    {
        // Dạng người dùng gõ nhầm khi thử chức năng sửa template — từng lọt vào DB và đang active.
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.TicketCreated, "Ticket mới {{ticketCodeeeeeee}}", "x")
            .Should().NotBeNull();
    }

    [Fact]
    public void BienLaTrongThan_CungBiBat_KhongChiTieuDe()
    {
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.TicketCreated, "Ticket {{code}}", "Khách {{customerName}}")
            .Should().NotBeNull("biến lạ nằm ở thân cũng render ra rỗng y như ở tiêu đề");
    }

    [Fact]
    public void ThongBaoLoi_LietKeBienHopLe_DeNguoiSoanBietChonGi()
    {
        var error = TemplateVariableGuard.FindUnknownVariables(
            NotificationTypeEnum.TicketCreated, "{{saiBet}}", "x");

        error.Should().Contain("priority").And.Contain("ticketId");
    }

    [Fact]
    public void KhongPhanBietHoaThuong()
    {
        // Model dispatcher dựng bằng StringComparer.OrdinalIgnoreCase nên {{Code}} vẫn tra được.
        TemplateVariableGuard.FindUnknownVariables(
                NotificationTypeEnum.TicketCreated, "Ticket {{Code}}", "x")
            .Should().BeNull();
    }
}
