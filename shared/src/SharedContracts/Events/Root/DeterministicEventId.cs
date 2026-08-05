using System.Security.Cryptography;
using System.Text;

namespace SharedContracts.Events.Root;

/// <summary>
/// GH-792 — sinh <see cref="IntegrationEvent.Id"/> theo dữ liệu nghiệp vụ, để lần phát lại của cùng
/// một việc mang đúng ID cũ.
/// </summary>
/// <remarks>
/// <para>
/// Consumer chống trùng bằng <c>ProcessOnceAsync</c>, khoá theo <see cref="IntegrationEvent.Id"/>.
/// Nếu mỗi lần publish sinh ID ngẫu nhiên thì một lần gửi lại — sau khi tiến trình chết đúng vào
/// khoảng giữa "provider đã nhận" và "DB đã ghi Sent" — trông y hệt một việc hoàn toàn mới, và người
/// dùng nhận email/SMS lần thứ hai.
/// </para>
/// <para>
/// Sinh theo tên (name-based) giống UUID v5 nhưng dùng SHA-256 thay SHA-1 và đánh version 8
/// ("custom" theo RFC 9562): SHA-1 không còn được khuyến nghị và hay bị công cụ quét bảo mật gắn cờ,
/// trong khi ở đây không cần tính chất mật mã nào ngoài "cùng đầu vào ra cùng kết quả, khác đầu vào
/// thì gần như chắc chắn khác kết quả".
/// </para>
/// </remarks>
public static class DeterministicEventId
{
    /// <summary>
    /// Không gian tên cố định của hệ thống — ghim cứng, KHÔNG được đổi.
    /// </summary>
    /// <remarks>
    /// Đổi giá trị này là đổi toàn bộ ID sinh ra, và mọi bản ghi inbox đã lưu trở nên vô dụng: đợt
    /// gửi lại đầu tiên sau khi đổi sẽ trùng lặp hàng loạt.
    /// </remarks>
    private static readonly byte[] NamespaceBytes =
        new Guid("6f2a1c1e-9d3b-4a57-8f10-2b7c4e51d9a3").ToByteArray();

    /// <summary>Sinh ID từ một chuỗi tên.</summary>
    public static Guid From(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var buffer = new byte[NamespaceBytes.Length + nameBytes.Length];
        NamespaceBytes.CopyTo(buffer, 0);
        nameBytes.CopyTo(buffer, NamespaceBytes.Length);

        var hash = SHA256.HashData(buffer);
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        // Version 8 (custom) + variant RFC 4122 — để ID này đọc ra là "sinh theo quy tắc", không bị
        // nhầm với GUID ngẫu nhiên khi ai đó điều tra sự cố.
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x80);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Sinh ID từ một định danh nghiệp vụ và một nhãn phân biệt.
    /// </summary>
    /// <param name="scope">Định danh nghiệp vụ, ví dụ <c>NotificationId</c>.</param>
    /// <param name="discriminator">
    /// Nhãn phân biệt mục đích, ví dụ <c>"email"</c>/<c>"sms"</c>. Cùng một bản ghi nghiệp vụ có thể
    /// sinh ra nhiều message khác nhau; thiếu nhãn này thì chúng đè lên nhau và message thứ hai bị
    /// coi là trùng rồi bỏ đi.
    /// </param>
    public static Guid From(Guid scope, string discriminator)
        => From($"{scope:N}:{discriminator}");
}
