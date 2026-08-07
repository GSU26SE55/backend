namespace FileStorageService.Application.Validation;

/// <summary>
/// Một họ định dạng file nhận diện được bằng magic bytes (chuỗi byte đặc trưng ở đầu file).
/// </summary>
/// <param name="DisplayName">Tên hiển thị trong thông báo lỗi, ví dụ <c>HEIC/HEIF</c>.</param>
/// <param name="Mime">MIME type thật của định dạng — dùng làm Content-Type khi ghi object lên storage.</param>
/// <param name="Extensions">Các phần mở rộng hợp lệ tương ứng với định dạng này.</param>
/// <param name="MimeByExtension">
/// MIME riêng cho từng phần mở rộng khi một container dùng chung magic bytes nhưng khác MIME,
/// ví dụ ISO-BMFF: <c>.m4a</c> là <c>audio/mp4</c> còn <c>.mp4</c> là <c>video/mp4</c>.
/// </param>
public sealed record FileContentSignature(
    string DisplayName,
    string Mime,
    string[] Extensions,
    IReadOnlyDictionary<string, string>? MimeByExtension = null)
{
    /// <summary>Phần mở rộng <paramref name="extension"/> có thuộc định dạng này không.</summary>
    public bool Matches(string extension)
    {
        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>MIME chính xác cho <paramref name="extension"/>, fallback về <see cref="Mime"/>.</summary>
    public string MimeFor(string extension)
    {
        if (MimeByExtension is not null && MimeByExtension.TryGetValue(extension.ToLowerInvariant(), out var mime))
            return mime;

        return Mime;
    }
}
