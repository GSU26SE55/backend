using Microsoft.AspNetCore.Http;

namespace FileStorageService.Application.Validation;

/// <summary>
/// Nhận diện định dạng thật của file bằng magic bytes thay vì tin vào đuôi file và Content-Type do client khai.
/// </summary>
/// <remarks>
/// Lý do tồn tại: iOS Photos xuất ảnh HEIC nhưng đặt tên <c>IMG_xxxx.JPEG</c>. Khi chỉ kiểm tra đuôi file,
/// upload trả 201, object nằm trong bucket và URL trả HTTP 200 — nhưng body là HEIC còn header lại nói
/// <c>image/jpeg</c>. Chrome/Firefox/Android không decode được HEIC (và MinIO gửi kèm
/// <c>X-Content-Type-Options: nosniff</c> nên trình duyệt cũng không tự đoán lại), kết quả là ảnh vỡ.
///
/// Bảng chữ ký dưới đây chỉ liệt kê các định dạng nằm trong danh sách đuôi file được phép của
/// <see cref="Authorization.FileUploadPolicy"/>, cộng các định dạng hay bị nhận nhầm với chúng
/// (HEIC, AVIF). Đuôi file không có magic bytes ổn định — <c>.txt</c>, <c>.csv</c>, <c>.doc</c>,
/// <c>.xls</c>, <c>.bin</c>, <c>.hex</c>, <c>.fw</c> — cố tình không nằm trong bảng nên không bị kiểm tra nội dung.
/// </remarks>
public static class FileSignatureInspector
{
    /// <summary>
    /// Số byte đầu file cần đọc để nhận diện. 32 byte là đủ: chữ ký nằm xa nhất trong bảng là
    /// brand của ISO-BMFF ở byte 8..11.
    /// </summary>
    public const int HeaderLength = 32;

    private static readonly FileContentSignature Jpeg = new("JPEG", "image/jpeg", [".jpg", ".jpeg"]);
    private static readonly FileContentSignature Png = new("PNG", "image/png", [".png"]);
    private static readonly FileContentSignature Gif = new("GIF", "image/gif", [".gif"]);
    private static readonly FileContentSignature Webp = new("WebP", "image/webp", [".webp"]);
    private static readonly FileContentSignature Heif = new("HEIC/HEIF", "image/heic", [".heic", ".heif"]);
    private static readonly FileContentSignature Avif = new("AVIF", "image/avif", [".avif"]);
    private static readonly FileContentSignature Pdf = new("PDF", "application/pdf", [".pdf"]);
    private static readonly FileContentSignature Mp3 = new("MP3", "audio/mpeg", [".mp3"]);
    private static readonly FileContentSignature Wav = new("WAV", "audio/wav", [".wav"]);
    private static readonly FileContentSignature Ogg = new("OGG", "audio/ogg", [".ogg", ".oga"]);
    private static readonly FileContentSignature Flac = new("FLAC", "audio/flac", [".flac"]);

    private static readonly FileContentSignature Matroska = new(
        "WebM/Matroska",
        "video/webm",
        [".webm", ".mkv"],
        new Dictionary<string, string> { [".mkv"] = "video/x-matroska" });

    private static readonly FileContentSignature IsoMedia = new(
        "MP4/M4A/MOV",
        "video/mp4",
        [".mp4", ".m4v", ".m4a", ".mov"],
        new Dictionary<string, string> { [".m4a"] = "audio/mp4", [".mov"] = "video/quicktime" });

    private static readonly FileContentSignature Zip = new(
        "ZIP (Office Open XML)",
        "application/zip",
        [".zip", ".docx", ".xlsx", ".pptx"],
        new Dictionary<string, string>
        {
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        });

    /// <summary>Brand ISO-BMFF (byte 8..11) của họ HEIF — đây là thứ máy ảnh iPhone sinh ra.</summary>
    private static readonly string[] HeifBrands =
        ["heic", "heix", "heim", "heis", "hevc", "hevx", "hevm", "hevs", "heif", "mif1", "msf1"];

    private static readonly string[] AvifBrands = ["avif", "avis"];

    private static readonly FileContentSignature[] KnownSignatures =
        [Jpeg, Png, Gif, Webp, Heif, Avif, Pdf, Mp3, Wav, Ogg, Flac, Matroska, IsoMedia, Zip];

    private static readonly IReadOnlyDictionary<string, FileContentSignature> SignatureByExtension = BuildExtensionIndex();

    /// <summary>
    /// Đọc <see cref="HeaderLength"/> byte đầu của file. Trả về mảng ngắn hơn nếu file nhỏ hơn.
    /// Mở stream riêng nên không ảnh hưởng tới stream dùng để upload.
    /// </summary>
    public static async Task<byte[]> ReadHeaderAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        await using var stream = file.OpenReadStream();

        var buffer = new byte[HeaderLength];
        var read = await stream.ReadAtLeastAsync(buffer, HeaderLength, throwOnEndOfStream: false, cancellationToken);

        return read == HeaderLength ? buffer : buffer[..read];
    }

    /// <summary>
    /// Định dạng mà đuôi file <paramref name="extension"/> bắt buộc phải khớp, hoặc <c>null</c> nếu
    /// đuôi này không có magic bytes ổn định để kiểm tra.
    /// </summary>
    public static FileContentSignature? FindByExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return SignatureByExtension.TryGetValue(extension.Trim().ToLowerInvariant(), out var signature)
            ? signature
            : null;
    }

    /// <summary>Định dạng thật của nội dung, hoặc <c>null</c> nếu không khớp chữ ký nào trong bảng.</summary>
    public static FileContentSignature? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return null;

        if (StartsWith(header, [0xFF, 0xD8, 0xFF]))
            return Jpeg;

        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return Png;

        if (MatchesAscii(header, 0, "GIF87a") || MatchesAscii(header, 0, "GIF89a"))
            return Gif;

        if (MatchesAscii(header, 0, "RIFF"))
        {
            if (MatchesAscii(header, 8, "WEBP"))
                return Webp;

            return MatchesAscii(header, 8, "WAVE") ? Wav : null;
        }

        if (MatchesAscii(header, 0, "%PDF-"))
            return Pdf;

        // ISO Base Media (ftyp box): HEIC, AVIF, MP4, M4A, MOV đều dùng chung container này.
        if (MatchesAscii(header, 4, "ftyp"))
            return DetectIsoBaseMedia(header);

        if (MatchesAscii(header, 0, "OggS"))
            return Ogg;

        if (MatchesAscii(header, 0, "fLaC"))
            return Flac;

        if (MatchesAscii(header, 0, "ID3"))
            return Mp3;

        if (StartsWith(header, [0x1A, 0x45, 0xDF, 0xA3]))
            return Matroska;

        // PK\x03\x04 (local file header), PK\x05\x06 (empty archive), PK\x07\x08 (spanned archive).
        if (header[0] == 0x50 && header[1] == 0x4B && header[2] is 0x03 or 0x05 or 0x07)
            return Zip;

        // MP3 không có tag ID3 thì bắt đầu bằng MPEG frame sync (11 bit 1).
        if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
            return Mp3;

        return null;
    }

    /// <summary>
    /// Content-Type để ghi lên object storage. Ưu tiên MIME suy ra từ nội dung thật; chỉ dùng giá trị
    /// client khai khi nội dung không nhận diện được hoặc không khớp đuôi file (trường hợp này đã bị
    /// <see cref="Authorization.FileUploadPolicy"/> chặn từ trước với các đuôi kiểm tra được).
    /// </summary>
    public static string ResolveContentType(ReadOnlySpan<byte> header, string fileName, string? declaredContentType)
    {
        var extension = Path.GetExtension(fileName);
        var actual = Detect(header);

        if (actual is not null && actual.Matches(extension))
            return actual.MimeFor(extension);

        // Trả rỗng để tầng storage tự suy Content-Type từ đuôi file như hành vi sẵn có.
        return string.IsNullOrWhiteSpace(declaredContentType) ? string.Empty : declaredContentType;
    }

    private static FileContentSignature? DetectIsoBaseMedia(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12)
            return null;

        Span<char> brandChars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
            brandChars[i] = (char)header[8 + i];

        var brand = new string(brandChars).ToLowerInvariant();

        if (HeifBrands.Contains(brand))
            return Heif;

        if (AvifBrands.Contains(brand))
            return Avif;

        return IsoMedia;
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> magic)
    {
        return header.Length >= magic.Length && header[..magic.Length].SequenceEqual(magic);
    }

    private static bool MatchesAscii(ReadOnlySpan<byte> header, int offset, string value)
    {
        if (header.Length < offset + value.Length)
            return false;

        for (var i = 0; i < value.Length; i++)
        {
            if (header[offset + i] != (byte)value[i])
                return false;
        }

        return true;
    }

    private static IReadOnlyDictionary<string, FileContentSignature> BuildExtensionIndex()
    {
        var index = new Dictionary<string, FileContentSignature>(StringComparer.OrdinalIgnoreCase);

        foreach (var signature in KnownSignatures)
        {
            foreach (var extension in signature.Extensions)
                index[extension] = signature;
        }

        return index;
    }
}
