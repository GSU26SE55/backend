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

    public string AccessKey { get; set; } = "minioadmin";

    public string SecretKey { get; set; } = "minioadmin";

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
