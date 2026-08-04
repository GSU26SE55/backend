namespace NotificationService.Application.Templates;

/// <summary>
/// Nhãn tiếng Việt cho hai enum của BatteryService (<c>AnomalyTypeEnum</c>, <c>AlertSeverityEnum</c>),
/// tra theo <b>TÊN</b> enum chứ không theo số.
///
/// <para><b>Vì sao tra theo tên.</b> Event chỉ mang con số (<c>anomalyType = 4</c>) vì hai enum đó
/// thuộc <c>BatteryService.Domain</c>, service khác không tham chiếu được. Từ 03/08/2026 bên phát
/// gửi kèm <c>AnomalyTypeName</c>/<c>SeverityName</c> — đúng khuôn <c>OldStatusName</c> của
/// <c>TicketStatusChangedEvent</c>. Tra theo tên thì việc BatteryService chèn thêm một giá trị vào
/// giữa enum (đánh số lại các giá trị sau) KHÔNG làm nhãn ở đây dịch sai — đúng loại tai nạn đã xảy
/// ra với <c>NotificationTypeEnum</c> khi module Blog chiếm mất số 25/26.</para>
///
/// <para><b>Lùi an toàn.</b> Tên lạ (BatteryService thêm loại mới mà chưa ai bổ sung vào đây) thì
/// trả về chính tên đó — người đọc thấy "Loại: CellImbalance", tiếng Anh nhưng vẫn hiểu được. Đó là
/// xuống cấp nhẹ, khác hẳn với việc hiện ra "Loại: 13" như trước.</para>
/// </summary>
public static class BatteryAnomalyLabels
{
    /// <summary>Khớp <c>BatteryService.Domain.Enums.AnomalyTypeEnum</c> (16 giá trị, 03/08/2026).</summary>
    private static readonly IReadOnlyDictionary<string, string> AnomalyTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Overheat"] = "Quá nhiệt",
            ["Overvoltage"] = "Quá áp",
            ["Undervoltage"] = "Sụt áp",
            ["LowSoc"] = "Dung lượng thấp",
            ["RapidDischarge"] = "Xả nhanh bất thường",
            ["AbnormalCharging"] = "Sạc bất thường",
            ["DeviceOffline"] = "Thiết bị mất kết nối",
            ["SohDegradation"] = "Suy giảm tuổi thọ pin",
            ["HighAmbientTemp"] = "Nhiệt độ môi trường cao",
            ["HighHumidity"] = "Độ ẩm cao",
            ["HighTempHumidityCombo"] = "Nhiệt độ và độ ẩm cùng cao",
            ["HighInternalResistance"] = "Điện trở trong cao",
            ["CellImbalance"] = "Lệch cân bằng cell",
            ["EnvironmentalIncident"] = "Sự cố môi trường",
            ["SensorMismatch"] = "Cảm biến sai lệch",
            ["Undertemp"] = "Nhiệt độ quá thấp",
        };

    /// <summary>Khớp <c>BatteryService.Domain.Enums.AlertSeverityEnum</c> (3 giá trị).</summary>
    private static readonly IReadOnlyDictionary<string, string> Severities =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Info"] = "Thông tin",
            ["Warning"] = "Cảnh báo",
            ["Critical"] = "Nghiêm trọng",
        };

    /// <summary>
    /// Nhãn loại bất thường. <paramref name="name"/> rỗng (event cũ trong hàng đợi chưa có trường
    /// này) ⇒ lùi về số để câu vẫn có thông tin, thay vì để trống hẳn.
    /// </summary>
    public static string AnomalyType(string? name, int rawValue) => Resolve(AnomalyTypes, name, rawValue);

    /// <summary>Nhãn mức độ nghiêm trọng.</summary>
    public static string Severity(string? name, int rawValue) => Resolve(Severities, name, rawValue);

    private static string Resolve(IReadOnlyDictionary<string, string> map, string? name, int rawValue)
    {
        if (string.IsNullOrWhiteSpace(name))
            return rawValue.ToString();

        return map.TryGetValue(name, out var label) ? label : name;
    }
}
