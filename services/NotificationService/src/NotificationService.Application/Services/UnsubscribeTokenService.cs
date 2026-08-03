using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.Services;

/// <summary>
/// Sprint 6.3 NOTI3-15 (#715) — token cho liên kết hủy đăng ký một chạm.
///
/// **Vì sao phải ký, không dùng id trần:** endpoint hủy đăng ký buộc phải mở công khai — Gmail/Yahoo
/// gửi <c>POST</c> tự động, không kèm cookie hay JWT. Nếu link chỉ chứa <c>userId</c> thì ai đoán ra
/// một GUID cũng tắt được thông báo của người khác. Token gắn chữ ký HMAC-SHA256 khiến chỉ hệ thống
/// mới tạo được link hợp lệ.
///
/// **Có hạn dùng:** email cũ nằm trong hộp thư nhiều năm; token vô hạn là một cánh cửa mở mãi.
/// </summary>
public class UnsubscribeTokenService
{
    private readonly IConfiguration _configuration;

    public UnsubscribeTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Hạn dùng của token, tính từ lúc gửi email.</summary>
    private TimeSpan Lifetime => TimeSpan.FromDays(
        int.TryParse(_configuration["Notification:Unsubscribe:TokenLifetimeDays"], out var d) && d > 0 ? d : 180);

    /// <summary>
    /// Khoá ký. **Không cấu hình ⇒ không phát hành được token** (trả <c>null</c>), thay vì rơi về
    /// một khoá mặc định — khoá mặc định nghĩa là bất kỳ ai đọc mã nguồn cũng ký được link hợp lệ.
    /// </summary>
    private string? Secret =>
        _configuration["Notification:Unsubscribe:Secret"] ?? _configuration["Notification__Unsubscribe__Secret"];

    /// <summary>Địa chỉ gốc của API công khai, để dựng URL tuyệt đối trong email.</summary>
    public string? PublicBaseUrl =>
        _configuration["Notification:Unsubscribe:PublicBaseUrl"] ?? _configuration["PublicBaseUrl"];

    /// <summary>Tạo token. Trả <c>null</c> khi chưa cấu hình khoá ký.</summary>
    public string? Create(Guid userId, NotificationCategoryEnum category, DateTime? nowUtc = null)
    {
        var secret = Secret;
        if (string.IsNullOrWhiteSpace(secret))
            return null;

        var expiresAt = (nowUtc ?? DateTime.UtcNow).Add(Lifetime);
        var payload = $"{userId:N}.{(int)category}.{expiresAt.ToUnixTimeSecondsUtc()}";
        var signature = Sign(payload, secret);

        return $"{Base64Url(Encoding.UTF8.GetBytes(payload))}.{signature}";
    }

    /// <summary>
    /// Kiểm tra token và lấy lại nội dung. Sai chữ ký, sai định dạng hay hết hạn đều trả <c>false</c>
    /// — không phân biệt lý do ra ngoài để không rò rỉ thông tin cho người dò.
    /// </summary>
    public bool TryValidate(string? token, out Guid userId, out NotificationCategoryEnum category, DateTime? nowUtc = null)
    {
        userId = Guid.Empty;
        category = NotificationCategoryEnum.Account;

        var secret = Secret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(token))
            return false;

        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Sign(payload, secret);

        // So sánh constant-time: so sánh chuỗi thường rò rỉ độ dài tiền tố khớp qua thời gian phản hồi.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(expected)))
        {
            return false;
        }

        var fields = payload.Split('.');
        if (fields.Length != 3)
            return false;

        if (!Guid.TryParseExact(fields[0], "N", out userId))
            return false;

        if (!int.TryParse(fields[1], out var categoryValue)
            || !Enum.IsDefined(typeof(NotificationCategoryEnum), categoryValue))
        {
            return false;
        }

        if (!long.TryParse(fields[2], out var expiresAtUnix))
            return false;

        if (DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix).UtcDateTime < (nowUtc ?? DateTime.UtcNow))
            return false;

        category = (NotificationCategoryEnum)categoryValue;
        return true;
    }

    private static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>Base64 an toàn cho URL — bản chuẩn có <c>+ / =</c> sẽ vỡ khi nằm trong query string.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

internal static class UnsubscribeTokenTimeExtensions
{
    public static long ToUnixTimeSecondsUtc(this DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
