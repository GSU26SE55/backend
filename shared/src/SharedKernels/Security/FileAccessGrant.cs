using System.Security.Cryptography;
using System.Text;

namespace SharedKernels.Security;

/// <summary>
/// GH-723 — "giấy phép" đọc file, ký HMAC, ngắn hạn.
///
/// Vấn đề gốc: TicketService BIẾT ai được xem attachment của ticket (nó có
/// <c>TicketQueryHelper.CanAccessTicket</c>), còn FileStorageService thì KHÔNG — bảng
/// <c>UploadedFile</c> không hề có liên kết tới ticket, và không có kênh gRPC theo chiều
/// FileStorage → Ticket. Vì thế URL mà TicketService trả về trước đây không mang theo
/// quyết định phân quyền, và mọi user đã đăng nhập chỉ cần biết fileId là tải được.
///
/// Cách sửa: TicketService sau khi kiểm quyền thì ký một grant gắn chặt vào
/// (fileId, userId, hạn dùng). FileStorageService chỉ cần xác minh chữ ký — không cần
/// biết gì về ticket, không phát sinh phụ thuộc runtime giữa hai service.
///
/// Khoá ký dùng chung <c>JwtSettings:SecretKey</c> (một biến môi trường duy nhất cấp cho
/// cả 9 service), nên không cần thêm hạ tầng khoá mới.
/// </summary>
public static class FileAccessGrant
{
    /// <summary>Tên query string mang grant.</summary>
    public const string QueryParameterName = "grant";

    /// <summary>Hạn mặc định — đủ để client bấm tải, đủ ngắn để lộ link không thành quyền lâu dài.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Sinh grant cho <paramref name="userId"/> đọc <paramref name="fileId"/>.
    /// </summary>
    public static string Issue(string secretKey, Guid fileId, Guid userId, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("secretKey must not be empty.", nameof(secretKey));

        var expiry = expiresAt.ToUnixTimeSeconds();
        var signature = Sign(secretKey, fileId, userId, expiry);
        return $"{expiry}.{signature}";
    }

    /// <summary>
    /// Xác minh grant. Trả false cho MỌI trường hợp không chắc chắn (thiếu, sai định dạng,
    /// hết hạn, sai chữ ký) — fail closed.
    /// </summary>
    public static bool Validate(string secretKey, string? token, Guid fileId, Guid userId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(token))
            return false;

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1)
            return false;

        if (!long.TryParse(token.AsSpan(0, separator), out var expiry))
            return false;

        // Hết hạn thì thôi — kiểm TRƯỚC khi so chữ ký cho rẻ.
        if (DateTimeOffset.FromUnixTimeSeconds(expiry) < now)
            return false;

        var expected = Sign(secretKey, fileId, userId, expiry);
        var actual = token[(separator + 1)..];

        // So sánh thời gian hằng định — tránh timing attack dò dần chữ ký.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }

    private static string Sign(string secretKey, Guid fileId, Guid userId, long expiryUnixSeconds)
    {
        // Dùng ký tự phân tách không xuất hiện trong Guid/số ⇒ không thể "trượt" trường
        // (fileId nối userId của cặp khác không tạo ra cùng chuỗi).
        var payload = $"{fileId:D}|{userId:D}|{expiryUnixSeconds}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
