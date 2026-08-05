namespace FileStorageService.Infrastructure.Options;

public class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";

    public string Provider { get; set; } = "Minio";

    /// <summary>URL Minio internal (containers gọi qua đây). Vd: http://minio:9000 trong docker.</summary>
    public string ServiceUrl { get; set; } = "http://localhost:9000";

    /// <summary>
    /// URL Minio public (browser/external truy cập). Dùng để generate presigned URL với hostname mà browser resolve được.
    /// Nếu không set, fallback dùng ServiceUrl. Vd: http://localhost:9090 trong docker dev.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    public string BucketName { get; set; } = "solar-battery-files";

    /// <summary>
    /// GH-788 — KHÔNG có giá trị mặc định. Trước đây mặc định là <c>minioadmin</c>, nên một triển
    /// khai quên cấu hình vẫn chạy được bằng credential ai cũng đoán ra, mà không lỗi cũng không
    /// cảnh báo. Thiếu giá trị thì <see cref="ObjectStorageCredentialGuard"/> chặn ngay lúc khởi động.
    /// </summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <inheritdoc cref="AccessKey"/>
    public string SecretKey { get; set; } = string.Empty;

    public string Region { get; set; } = "auto";

    public bool ForcePathStyle { get; set; } = true;

    public bool CreateBucketIfNotExists { get; set; } = true;

    public string? PublicBaseUrl { get; set; }

    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public string[] AllowedExtensions { get; set; } =
    [
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".pdf",
        ".doc",
        ".docx",
        ".xls",
        ".xlsx",
        ".txt",
        ".csv",
        ".bin",
        ".hex",
        ".fw"
    ];
}
