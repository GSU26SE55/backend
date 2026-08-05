namespace BatteryService.Application.Mqtt;

/// <summary>Một dòng credential trong file passwd của Mosquitto.</summary>
/// <param name="Username">Username (đã chuẩn hoá chữ thường).</param>
/// <param name="PasswordHash">Hash dạng <c>$7$&lt;iter&gt;$&lt;salt&gt;$&lt;hash&gt;</c>.</param>
public readonly record struct MosquittoCredential(string Username, string PasswordHash);

/// <summary>
/// GH-784 — soạn nội dung file <c>passwd</c> của Mosquitto từ danh sách thiết bị.
/// </summary>
/// <remarks>
/// <para>
/// Hàm THUẦN, không đụng đĩa ⇒ test được trực tiếp. Phần I/O nằm ở background service.
/// </para>
/// <para>
/// Điểm sống còn: file hiện tại chứa <c>backend-bridge</c> — chính tài khoản mà BatteryService dùng
/// để nối vào broker. Ghi đè cả file bằng danh sách thiết bị sẽ khiến cầu nối tự khoá mình ra ngoài,
/// và toàn bộ telemetry MQTT chết theo.
/// </para>
/// <para>
/// Giải pháp: một VÙNG CÓ MỐC do service quản lý. Ngoài mốc giữ nguyên từng ký tự (bridge, ghi chú,
/// user vận hành thêm tay); trong mốc dựng lại toàn bộ mỗi lần đồng bộ — nhờ vậy thiết bị bị thu
/// hồi thực sự MẤT quyền, thay vì nằm lại mãi vì "không thuộc danh sách hiện tại".
/// </para>
/// </remarks>
public static class MosquittoPasswordFile
{
    /// <summary>Mốc mở vùng do BatteryService quản lý.</summary>
    public const string BeginMarker = "# >>> BatteryService managed devices (GH-784) — KHÔNG sửa tay";

    /// <summary>Mốc đóng vùng.</summary>
    public const string EndMarker = "# <<< BatteryService managed devices";

    /// <summary>
    /// Soạn nội dung mới: mọi thứ NGOÀI vùng có mốc được giữ nguyên từng ký tự; vùng trong mốc bị
    /// dựng lại hoàn toàn từ <paramref name="devices"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Vì sao dùng vùng có mốc thay vì "giữ dòng nào không thuộc danh sách hiện tại": cách sau
    /// KHÔNG xoá được thiết bị đã thu hồi — dòng của nó cũng "không thuộc danh sách hiện tại" nên
    /// được giữ lại, và revoke chỉ có tác dụng trên giấy tờ trong khi thiết bị vẫn publish được.
    /// </para>
    /// <para>
    /// Ngược lại, xoá sạch rồi ghi lại toàn bộ sẽ cuốn theo <c>backend-bridge</c> — chính tài khoản
    /// BatteryService dùng để nối vào broker. Vùng có mốc tách bạch được hai nhu cầu đó.
    /// </para>
    /// </remarks>
    public static string Compose(string? existingContent, IReadOnlyCollection<MosquittoCredential> devices)
    {
        var managedNames = devices
            .Select(d => d.Username)
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToHashSet(StringComparer.Ordinal);

        var preserved = new List<string>();
        var insideManagedRegion = false;

        foreach (var raw in SplitLines(existingContent))
        {
            var line = raw.TrimEnd();

            if (line == BeginMarker) { insideManagedRegion = true; continue; }
            if (line == EndMarker) { insideManagedRegion = false; continue; }

            // Trong vùng quản lý ⇒ bỏ, sẽ dựng lại bên dưới (đây là chỗ thiết bị thu hồi biến mất).
            if (insideManagedRegion) continue;

            if (line.Length == 0)
                continue;

            // Dòng NGOÀI vùng mốc trùng username với một thiết bị đang quản lý ⇒ bỏ. Giữ lại sẽ
            // thành hai bản ghi cùng username; Mosquitto lấy bản ĐẦU TIÊN, nên khoá vừa xoay xong
            // lại không có tác dụng. Xảy ra khi file có dòng thiết bị từ trước khi có vùng mốc.
            var sep = line.IndexOf(':');
            if (sep > 0 && !line.StartsWith('#') && managedNames.Contains(line[..sep]))
                continue;

            preserved.Add(line);
        }

        var managed = devices
            .Where(d => !string.IsNullOrWhiteSpace(d.Username) && !string.IsNullOrWhiteSpace(d.PasswordHash))
            // Thứ tự ổn định: ghi lại file kéo theo một lần broker nạp lại, nên nội dung không đổi
            // thì byte cũng phải không đổi.
            .OrderBy(d => d.Username, StringComparer.Ordinal)
            .Select(d => $"{d.Username}:{d.PasswordHash}")
            .ToList();

        var lines = new List<string>(preserved);
        if (managed.Count > 0)
        {
            lines.Add(BeginMarker);
            lines.AddRange(managed);
            lines.Add(EndMarker);
        }

        // Mosquitto đọc theo dòng và cần newline cuối; thiếu nó thì dòng chót có thể bị bỏ qua.
        return lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";
    }

    private static IEnumerable<string> SplitLines(string? content)
        => string.IsNullOrEmpty(content)
            ? Array.Empty<string>()
            : content.Split('\n', StringSplitOptions.None);
}
