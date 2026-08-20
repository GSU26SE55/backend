using System.Text;

namespace BatteryService.Application.Import;

/// <summary>
/// Đưa mã seri và mã tham chiếu của bên thứ ba về dạng mà hệ thống chấp nhận.
/// </summary>
/// <remarks>
/// <para>
/// Ràng buộc hiện có cho mã seri pin là chỉ gồm chữ in hoa, chữ số và dấu gạch nối. Mã seri trong
/// thực tế thì hiếm khi sạch như vậy: nhà sản xuất dùng dấu gạch chéo, gạch dưới, khoảng trắng, và
/// người nhập liệu thì gõ chữ thường. Không chuẩn hoá thì phần lớn file của đối tác bị loại vì một
/// lý do thuần hình thức.
/// </para>
/// <para>
/// Giá trị gốc luôn được giữ lại ở nơi gọi, vì khi đối chiếu ngược với đối tác thì thứ họ tra được
/// là mã trong hệ thống của họ, không phải mã đã bị mình viết lại.
/// </para>
/// </remarks>
public static class ImportSerialNormalizer
{
    /// <summary>Chuẩn hoá mã seri pin về dạng <c>[A-Z0-9-]</c>.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            if (ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                // Mọi ký tự lạ thành một dấu gạch nối, và không cho hai gạch nối liền nhau.
                builder.Append('-');
            }
        }

        // Gạch nối thừa ở cuối xuất hiện khi chuỗi gốc kết thúc bằng ký tự lạ.
        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.ToString();
    }

    /// <summary>
    /// Chuẩn hoá mã tham chiếu của đối tác. Nới hơn mã seri: giữ thêm dấu gạch dưới và dấu chấm vì
    /// đây là mã nội bộ của họ, mình chỉ cần một dạng ổn định để tra, không cần ép về khuôn của mình.
    /// </summary>
    public static string NormalizeReference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            if (ch is >= 'A' and <= 'Z' || ch is >= '0' and <= '9' || ch is '-' or '_' or '.')
                builder.Append(ch);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        while (builder.Length > 0 && builder[^1] == '-')
            builder.Length--;

        return builder.ToString();
    }
}
