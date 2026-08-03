using NotificationService.Application.Templates;

namespace NotificationService.UnitTests.Templates;

/// <summary>
/// 03/08/2026 — nhãn tiếng Việt cho hai enum của BatteryService.
///
/// <para>Trước thay đổi này, thông báo pin gửi cho khách ghi <b>"Loại: 4 — Mức độ: 3"</b>: hai enum
/// đó thuộc <c>BatteryService.Domain</c> nên NotificationService không tham chiếu được, event chỉ
/// mang con số. Nay bên phát gửi kèm tên enum và bảng này quy về tiếng Việt.</para>
/// </summary>
public class BatteryAnomalyLabelsTests
{
    [Theory]
    [InlineData("Overheat", "Quá nhiệt")]
    [InlineData("SohDegradation", "Suy giảm tuổi thọ pin")]
    [InlineData("CellImbalance", "Lệch cân bằng cell")]
    [InlineData("Undertemp", "Nhiệt độ quá thấp")]
    public void TenEnum_QuyVeTiengViet(string name, string expected)
    {
        BatteryAnomalyLabels.AnomalyType(name, 0).Should().Be(expected);
    }

    [Theory]
    [InlineData("Info", "Thông tin")]
    [InlineData("Warning", "Cảnh báo")]
    [InlineData("Critical", "Nghiêm trọng")]
    public void MucDoNghiemTrong_QuyVeTiengViet(string name, string expected)
    {
        BatteryAnomalyLabels.Severity(name, 0).Should().Be(expected);
    }

    [Fact]
    public void KhongPhanBietHoaThuong()
    {
        BatteryAnomalyLabels.AnomalyType("overheat", 0).Should().Be("Quá nhiệt");
    }

    /// <summary>
    /// BatteryService thêm loại bất thường mới mà chưa ai bổ sung vào bảng ⇒ hiện chính tên enum.
    /// Tiếng Anh nhưng người đọc vẫn hiểu — xuống cấp nhẹ, khác hẳn việc trơ ra một con số.
    /// </summary>
    [Fact]
    public void TenLa_LuiVeChinhTenDo_KhongPhaiSo()
    {
        BatteryAnomalyLabels.AnomalyType("LoaiBatThuongMoiNaoDo", 99)
            .Should().Be("LoaiBatThuongMoiNaoDo");
    }

    /// <summary>
    /// Event CŨ còn nằm trong Outbox/hàng đợi không có trường tên (thêm 03/08/2026, nullable) ⇒
    /// lùi về số để câu vẫn có thông tin, thay vì để trống hẳn.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ThieuTen_LuiVeSo(string? name)
    {
        BatteryAnomalyLabels.AnomalyType(name, 4).Should().Be("4");
        BatteryAnomalyLabels.Severity(name, 3).Should().Be("3");
    }
}
