using System.Globalization;
using BatteryService.Application.Ai;
using BatteryService.Application.Common.Models;
using FluentAssertions;
using Xunit;

namespace BatteryService.UnitTests.Ai;

/// <summary>
/// GH-762 — một số đo ngoài dải làm hỏng cả cửa sổ dự đoán 30 mẫu.
///
/// Bằng chứng runtime trong issue: asset BAT-2026-001 (LiFePO4, danh định 12 V) có một số đo
/// primary 52.40 V. Job dựng pack_config n_series=4, AI tính 13.10 V/cell và từ chối cả payload
/// với lỗi dải [2.0, 4.5]. Client nuốt lỗi thành null, job `continue` ⇒ pin không có prediction
/// nào cho tới khi số đo đó rơi khỏi 30 mẫu.
///
/// Rủi ro của bản sửa là lọc SAI ngưỡng: lọc rộng hơn AI thì vứt mất dữ liệu tốt, lọc hẹp hơn
/// thì cửa sổ vẫn bị AI từ chối nguyên khối — tức là chẳng sửa được gì. Nên các test dưới đây
/// ghim từng con số theo `ai-module/src/core/config.py`.
/// </summary>
public class AiReadingWindowFilterTests
{
    /// <summary>Pack LiFePO4 12 V ⇒ 4S; dung lượng 100 Ah như asset trong bằng chứng.</summary>
    private static AiPackConfig LfP12V => new(NSeries: 4, Chemistry: "LFP", CapacityAh: 100);

    /// <summary>Một dòng hợp lệ: 12.8 V pack (3.2 V/cell), dòng nhỏ, 25 °C.</summary>
    private static double[] Good(double voltage = 12.8) => new[] { voltage, 1.5, 25.0 };

    private static List<double[]> Window(int count, double voltage = 12.8)
        => Enumerable.Range(0, count).Select(_ => Good(voltage)).ToList();

    [Fact]
    public void Filter_CleanWindow_KeepsEverything()
    {
        var result = AiReadingWindowFilter.Filter(Window(30), LfP12V);

        result.RejectedCount.Should().Be(0);
        result.AcceptedCount.Should().Be(30);
        result.FirstRejectionReason.Should().BeNull();
    }

    [Fact]
    public void Filter_SingleOutlier_RemovesOnlyThatRow()
    {
        // ĐÂY là ca của GH-762, dựng đúng theo bằng chứng runtime.
        var rows = Window(30);
        rows[17] = new[] { 52.40, 1.5, 25.0 };

        var result = AiReadingWindowFilter.Filter(rows, LfP12V);

        result.RejectedCount.Should().Be(1);
        result.AcceptedCount.Should().Be(29);
        result.AcceptedIndices.Should().NotContain(17);
        // Thông báo phải nêu được số liệu thật, không chỉ "invalid" — người trực cần biết
        // 13.1 V/cell để đoán ra là gán nhầm pack 48 V vào asset 12 V.
        result.FirstRejectionReason.Should().Contain("13.100");
        result.FirstRejectionReason.Should().Contain("52.40");
    }

    [Fact]
    public void Filter_KeepsInputOrder_SoNewestStaysLast()
    {
        // Người gọi lấy phần ĐUÔI làm cửa sổ mới nhất — thứ tự đảo là lấy nhầm mẫu cũ.
        var rows = Window(5);
        rows[0] = new[] { 99.0, 1.5, 25.0 };

        var result = AiReadingWindowFilter.Filter(rows, LfP12V);

        result.AcceptedIndices.Should().Equal(1, 2, 3, 4);
    }

    [Theory]
    // Biên per-cell [2.0, 4.5] × 4S ⇒ pack [8.0, 18.0] V. Biên phải LỌT, không được chặn nhầm.
    [InlineData(8.0, true)]
    [InlineData(18.0, true)]
    [InlineData(7.99, false)]
    [InlineData(18.01, false)]
    [InlineData(52.40, false)]  // bằng chứng runtime
    [InlineData(0.0, false)]    // cảm biến chết
    public void Filter_VoltageBoundaries_MatchAiContract(double packVoltage, bool accepted)
    {
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { packVoltage, 1.5, 25.0 } }, LfP12V);

        result.AcceptedCount.Should().Be(accepted ? 1 : 0);
    }

    [Theory]
    // AI quy đổi dòng về C-rate của cell NASA 2 Ah: i_equiv = i × 2.0 / capacity_ah.
    // Pack 100 Ah ⇒ hệ số 0.02 ⇒ dải pack là ±250 A.
    [InlineData(250.0, true)]
    [InlineData(-250.0, true)]
    [InlineData(250.1, false)]
    [InlineData(-250.1, false)]
    public void Filter_CurrentBoundaries_UseCRateEquivalent(double current, bool accepted)
    {
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, current, 25.0 } }, LfP12V);

        result.AcceptedCount.Should().Be(accepted ? 1 : 0);
    }

    [Fact]
    public void Filter_ZeroCapacity_FallsBackToScaleOne_LikePython()
    {
        // Python: `i_scale = NOMINAL_CAPACITY_AH / capacity_ah if capacity_ah else 1.0`.
        // 0.0 là falsy trong Python ⇒ KHÔNG chia cho 0, mà rơi về hệ số 1. Lệch chỗ này là hai
        // bên bất đồng đúng về cái dòng đang tranh cãi (và bên C# còn có thể ra Infinity).
        var pack = new AiPackConfig(NSeries: 4, Chemistry: "LFP", CapacityAh: 0);

        var within = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, 5.0, 25.0 } }, pack);
        var beyond = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, 5.01, 25.0 } }, pack);

        within.AcceptedCount.Should().Be(1);
        beyond.AcceptedCount.Should().Be(0);
        beyond.FirstRejectionReason.Should().NotContain("∞").And.NotContain("Infinity");
    }

    [Fact]
    public void Filter_NullPackConfig_TreatsPackAsSingleCell()
    {
        // Không có pack_config thì AI lấy n_series = 1 ⇒ 12.8 V bị coi là 12.8 V/cell và bị từ chối.
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, 1.5, 25.0 } }, packConfig: null);

        result.AcceptedCount.Should().Be(0);
    }

    [Theory]
    [InlineData(-10.0, true)]
    [InlineData(60.0, true)]
    [InlineData(-10.01, false)]
    [InlineData(60.01, false)]
    public void Filter_TemperatureBoundaries_MatchAiContract(double temperature, bool accepted)
    {
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, 1.5, temperature } }, LfP12V);

        result.AcceptedCount.Should().Be(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Filter_NonFiniteValues_AreRejected(double bad)
    {
        // Lọt vào scaler thì ra SOH vô nghĩa mà confidence vẫn trông bình thường — tệ hơn cả bị
        // từ chối, vì không ai nghi ngờ gì.
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, bad, 25.0 } }, LfP12V);

        result.AcceptedCount.Should().Be(0);
        result.FirstRejectionReason.Should().Contain("không hữu hạn");
    }

    [Fact]
    public void Filter_ShortRow_IsRejectedInsteadOfReadingPastEnd()
    {
        var result = AiReadingWindowFilter.Filter(
            new List<double[]> { new[] { 12.8, 1.5 } }, LfP12V);

        result.AcceptedCount.Should().Be(0);
        result.FirstRejectionReason.Should().Contain("thiếu cột");
    }

    [Fact]
    public void Filter_EmptyWindow_IsNotAnError()
    {
        var result = AiReadingWindowFilter.Filter(new List<double[]>(), LfP12V);

        result.AcceptedCount.Should().Be(0);
        result.RejectedCount.Should().Be(0);
        result.FirstRejectionReason.Should().BeNull();
    }

    [Fact]
    public void Filter_ReportsFirstReasonOnly_NotTheLast()
    {
        var rows = new List<double[]>
        {
            new[] { 12.8, 1.5, 25.0 },
            new[] { 52.40, 1.5, 25.0 },   // hỏng trước
            new[] { 12.8, 1.5, 999.0 },   // hỏng sau
        };

        var result = AiReadingWindowFilter.Filter(rows, LfP12V);

        result.RejectedCount.Should().Be(2);
        result.FirstRejectionReason.Should().Contain("voltage");
        result.FirstRejectionReason.Should().NotContain("temperature");
    }

    [Fact]
    public void Filter_RejectionReason_ReadsTheSameUnderAnyCulture()
    {
        // Máy đặt locale dùng dấu phẩy thập phân sẽ in dải [2.0, 4.5] thành "[2, 4,5]" — trông
        // như ba con số. Log sự cố không được đổi nghĩa theo máy ai chạy.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var result = AiReadingWindowFilter.Filter(
                new List<double[]> { new[] { 52.40, 1.5, 25.0 } }, LfP12V);

            result.FirstRejectionReason.Should().Contain("13.100 V/cell");
            result.FirstRejectionReason.Should().Contain("[2, 4.5] V");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Ghim các hằng theo <c>ai-module/src/core/config.py</c>. Hai kho khác nhau nên không tham
    /// chiếu chéo được; test này là chỗ duy nhất phát hiện việc một bên đổi số mà bên kia không.
    /// Đổi số ở đây mà không mở file config.py đối chiếu là làm hỏng chính mục đích của nó.
    /// </summary>
    [Fact]
    public void AiInputContract_MatchesAiModuleConfig()
    {
        AiInputContract.VoltageCellMin.Should().Be(2.0);      // VOLTAGE_CELL_RANGE
        AiInputContract.VoltageCellMax.Should().Be(4.5);
        AiInputContract.CurrentMin.Should().Be(-5.0);         // CURRENT_RANGE
        AiInputContract.CurrentMax.Should().Be(5.0);
        AiInputContract.TemperatureMin.Should().Be(-10.0);    // TEMPERATURE_RANGE
        AiInputContract.TemperatureMax.Should().Be(60.0);
        AiInputContract.NominalCapacityAh.Should().Be(2.0);   // NOMINAL_CAPACITY_AH
    }
}
