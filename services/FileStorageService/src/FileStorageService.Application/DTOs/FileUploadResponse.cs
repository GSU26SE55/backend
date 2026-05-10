namespace FileStorageService.Application.DTOs;

public class FileUploadResponse
{
    public string ObjectKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public string? PublicUrl { get; set; }
}
