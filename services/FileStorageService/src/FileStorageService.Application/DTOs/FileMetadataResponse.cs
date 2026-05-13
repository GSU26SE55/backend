using FileStorageService.Domain.Enums;

namespace FileStorageService.Application.DTOs;

public class FileMetadataResponse
{
    public Guid FileId { get; set; }

    public string ObjectKey { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public string FolderName { get; set; } = string.Empty;

    public FilePurposeEnum Purpose { get; set; }

    public FileStatusEnum Status { get; set; }

    public string? PublicUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
