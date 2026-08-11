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
    [InlineData("Overheat", "Overheating")]
    [InlineData("SohDegradation", "Battery health degradation")]
    [InlineData("CellImbalance", "Cell imbalance")]
    [InlineData("Undertemp", "Low temperature")]
    public void TenEnum_QuyVeTiengAnh(string name, string expected)
    {
        BatteryAnomalyLabels.AnomalyType(name, 0).Should().Be(expected);
    }

    [Theory]
    [InlineData("Info", "Info")]
    [InlineData("Warning", "Warning")]
    [InlineData("Critical", "Critical")]
    public void MucDoNghiemTrong_QuyVeTiengAnh(string name, string expected)
    {
        BatteryAnomalyLabels.Severity(name, 0).Should().Be(expected);
    }

    [Fact]
    public void KhongPhanBietHoaThuong()
    {
        BatteryAnomalyLabels.AnomalyType("overheat", 0).Should().Be("Overheating");
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
