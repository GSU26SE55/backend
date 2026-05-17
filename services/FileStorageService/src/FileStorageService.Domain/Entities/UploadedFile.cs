using FileStorageService.Domain.Enums;
using SharedKernels.Domain;

namespace FileStorageService.Domain.Entities;

public class UploadedFile : AuditableEntity
{
    public string ObjectKey { get; set; } = null!;

    public string OriginalFileName { get; set; } = null!;

    public string ContentType { get; set; } = null!;

    public long Size { get; set; }

    public string FolderName { get; set; } = null!;

    public FilePurposeEnum Purpose { get; set; } = FilePurposeEnum.Other;

    public FileStatusEnum Status { get; set; } = FileStatusEnum.Uploaded;

    public string? PublicUrl { get; set; }
}
