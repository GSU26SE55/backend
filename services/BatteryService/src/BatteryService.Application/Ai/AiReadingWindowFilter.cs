using System.Globalization;
using BatteryService.Application.Common.Models;

namespace BatteryService.Application.Ai;

/// <summary>
/// GH-762 — loại các reading mà AI chắc chắn từ chối, TRƯỚC khi gửi cả cửa sổ đi.
/// </summary>
/// <remarks>
/// <para>
/// Lỗi gốc: job gom đúng 30 reading gần nhất rồi gửi thẳng. AI duyệt TỪNG dòng và ném lỗi ở
/// dòng lệch dải ĐẦU TIÊN, nên chỉ một số đo bất khả thi là hỏng cả cửa sổ; job nhận null rồi
/// <c>continue</c>. Pin đó không có prediction nào cho tới khi outlier tự rơi khỏi 30 mẫu —
/// với chu kỳ đo hiện tại là hàng giờ, mà suốt thời gian ấy không ai được báo gì.
/// </para>
/// <para>
/// Vì sao lọc ở đây chứ không chặn ở ingest: một số đo bất khả thi vẫn là bằng chứng CÓ THẬT
/// của sự cố (sai mapping cảm biến, đứt dây, gán nhầm pack 48V vào asset 12V). Vứt nó ngay ở
/// ingest là mất đúng thứ cần để truy nguyên. Nên: vẫn lưu, nhưng không cho nó bịt miệng model.
/// </para>
/// <para>
/// Vì sao ngưỡng nằm ở đây mà không phải một bộ ngưỡng "hợp lý" tự nghĩ ra: đây là bản SAO
/// CHÉP hợp đồng đầu vào của AI (<c>ai-module/src/schemas/predict.py::_check_ranges</c> +
/// <c>src/core/config.py</c>). Lọc rộng hơn AI thì mất dữ liệu tốt; lọc hẹp hơn thì vẫn bị
/// từ chối cả cửa sổ — tức là không sửa được gì. Bằng đúng AI thì mọi dòng còn lại chắc chắn
/// qua được. <see cref="AiInputContract"/> có test ghim từng con số.
/// </para>
/// </remarks>
public static class AiReadingWindowFilter
{
    /// <summary>Kết quả lọc một cửa sổ.</summary>
    /// <param name="AcceptedIndices">
    /// Vị trí các dòng được nhận trong danh sách đầu vào, tăng dần.
    /// <para>
    /// Trả về CHỈ SỐ chứ không phải bản sao dòng, vì người gọi còn phải lần ngược ra thực thể
    /// <c>SensorReading</c> tương ứng để lấy mốc thời gian cửa sổ — và quan trọng hơn, cột
    /// <c>time</c> gửi cho AI là GIÂY TƯƠNG ĐỐI so với mẫu đầu cửa sổ. Nếu lọc sau khi đã dựng
    /// dòng, gốc thời gian sẽ là một mẫu đã bị loại, khiến cột <c>time</c> không còn bắt đầu từ 0
    /// và phân phối đầu vào model lệch đi trong im lặng.
    /// </para>
    /// </param>
    /// <param name="RejectedCount">Số dòng bị loại.</param>
    /// <param name="FirstRejectionReason">
    /// Mô tả dòng bị loại đầu tiên — để log nói được CHUYỆN GÌ xảy ra thay vì chỉ một con số.
    /// </param>
    public sealed record FilterResult(
        IReadOnlyList<int> AcceptedIndices,
        int RejectedCount,
        string? FirstRejectionReason)
    {
        public int AcceptedCount => AcceptedIndices.Count;
    }

    /// <summary>
    /// Lọc các dòng <c>[voltage, current, temperature, …]</c> theo dải AI chấp nhận.
    /// Chỉ ba cột đầu được dùng để quyết định; các cột sau chỉ bị kiểm tính hữu hạn.
    /// </summary>
    public static FilterResult Filter(IReadOnlyList<double[]> readings, AiPackConfig? packConfig)
    {
        var nSeries = packConfig?.NSeries ?? 1;
        if (nSeries < 1) nSeries = 1;

        // AI: `i_scale = NOMINAL_CAPACITY_AH / capacity_ah if capacity_ah else 1.0`.
        // Trong Python, 0.0 là falsy ⇒ capacity 0 rơi về 1.0 chứ KHÔNG chia cho 0. Phải sao y,
        // vì lệch chỗ này là hai bên bất đồng về chính cái dòng đang tranh cãi.
        var capacity = packConfig?.CapacityAh;
        var iScale = capacity.HasValue && capacity.Value != 0.0
            ? AiInputContract.NominalCapacityAh / capacity.Value
            : 1.0;

        var acceptedIndices = new List<int>(readings.Count);
        var rejected = 0;
        string? firstReason = null;

        for (var i = 0; i < readings.Count; i++)
        {
            var reason = Reject(readings[i], nSeries, iScale, i);
            if (reason is null)
            {
                acceptedIndices.Add(i);
                continue;
            }

            rejected++;
            firstReason ??= reason;
        }

        return new FilterResult(acceptedIndices, rejected, firstReason);
    }

    /// <summary>Trả lý do loại, hoặc null nếu dòng dùng được.</summary>
    private static string? Reject(double[] row, int nSeries, double iScale, int index)
    {
        // Thông báo chẩn đoán phải ĐỌC GIỐNG NHAU trên mọi máy. Theo locale mặc định,
        // dấu thập phân thành dấu phẩy và dải "[2.0, 4.5]" hiện ra là "[2, 4,5]" — nhìn
        // như ba con số. Người đọc log lúc sự cố không nên phải đoán.
        var c = CultureInfo.InvariantCulture;
        // Dòng thiếu cột thì không thể kiểm — coi như không dùng được, thay vì đọc lố mảng.
        if (row.Length < 3)
            return string.Format(c, "readings[{0}]: thiếu cột (có {1}, cần ≥ 3)", index, row.Length);

        // Non-finite lọt vào scaler sẽ ra SOH vô nghĩa mà confidence vẫn trông bình thường.
        for (var col = 0; col < row.Length; col++)
        {
            if (double.IsNaN(row[col]) || double.IsInfinity(row[col]))
                return string.Format(c, "readings[{0}][{1}]: giá trị không hữu hạn ({2})", index, col, row[col]);
        }

        var vCell = row[0] / nSeries;
        if (vCell < AiInputContract.VoltageCellMin || vCell > AiInputContract.VoltageCellMax)
        {
            return string.Format(c,
                "readings[{0}].voltage: {1:F3} V/cell (pack {2:F2} V ÷ {3}S) ngoài dải [{4}, {5}] V",
                index, vCell, row[0], nSeries,
                AiInputContract.VoltageCellMin, AiInputContract.VoltageCellMax);
        }

        var iEquiv = row[1] * iScale;
        if (iEquiv < AiInputContract.CurrentMin || iEquiv > AiInputContract.CurrentMax)
        {
            return string.Format(c,
                "readings[{0}].current: {1:F3} A quy đổi ngoài dải [{2}, {3}] A",
                index, iEquiv, AiInputContract.CurrentMin, AiInputContract.CurrentMax);
        }

        if (row[2] < AiInputContract.TemperatureMin || row[2] > AiInputContract.TemperatureMax)
        {
            return string.Format(c,
                "readings[{0}].temperature: {1:F1} °C ngoài dải [{2}, {3}] °C",
                index, row[2], AiInputContract.TemperatureMin, AiInputContract.TemperatureMax);
        }

        // GH-777 — cửa sổ 6 cột có thêm cycle_count (chỉ số 4) và soc_percent (chỉ số 5).
        // AI kiểm soc bằng `if len(row) >= 6 and not s_lo <= row[5] <= s_hi` nên KHÔNG kiểm ở đây
        // là để lọt một SOC xấu và bị từ chối NGUYÊN CỬA SỔ — đúng cái GH-762 vừa gỡ.
        // cycle_count cố ý không kiểm dải: AI cũng không kiểm (chỉ clip khi chuẩn hoá).
        if (row.Length >= 6
            && (row[5] < AiInputContract.SocMin || row[5] > AiInputContract.SocMax))
        {
            return string.Format(c,
                "readings[{0}].soc_percent: {1:F1} ngoài dải [{2}, {3}]",
                index, row[5], AiInputContract.SocMin, AiInputContract.SocMax);
        }

        return null;
    }
}

/// <summary>
/// GH-762 — bản sao các hằng trong hợp đồng đầu vào của AI module.
/// </summary>
/// <remarks>
/// Nguồn sự thật: <c>ai-module/src/core/config.py</c>. Hai kho khác nhau nên không tham chiếu
/// trực tiếp được; đổi bên kia mà quên bên này thì cửa sổ lại bị từ chối nguyên khối như cũ.
/// Vì vậy các con số ở đây được ghim bằng test và ghi rõ tên hằng bên Python để dò lại.
/// </remarks>
public static class AiInputContract
{
    /// <summary>config.py: <c>VOLTAGE_CELL_RANGE = (2.0, 4.5)</c> — kiểm SAU khi chia n_series.</summary>
    public const double VoltageCellMin = 2.0;
    public const double VoltageCellMax = 4.5;

    /// <summary>config.py: <c>CURRENT_RANGE = (-5.0, 5.0)</c> — kiểm trên trị quy đổi C-rate.</summary>
    public const double CurrentMin = -5.0;
    public const double CurrentMax = 5.0;

    /// <summary>config.py: <c>TEMPERATURE_RANGE = (-10.0, 60.0)</c>.</summary>
    public const double TemperatureMin = -10.0;
    public const double TemperatureMax = 60.0;

    /// <summary>config.py: <c>SOC_RANGE = (0.0, 100.0)</c> — chỉ áp cho cửa sổ 6 cột (GH-777).</summary>
    public const double SocMin = 0.0;
    public const double SocMax = 100.0;

    /// <summary>config.py: <c>NOMINAL_CAPACITY_AH = 2.0</c> — tử số khi quy đổi dòng.</summary>
    public const double NominalCapacityAh = 2.0;
}
