namespace FileStorageService.Domain.Enums;

/// <summary>6 action audit của FileStorageService (Sprint audit #AUDIT-29). Enum bắt đầu từ 1.</summary>
public enum FileAuditActionEnum
{
    FileUploaded = 1,
    FileDownloaded = 2,
    FileDeleted = 3,
    AccessDenied = 4,
    PresignedUrlGenerated = 5,
    PresignedUrlRevoked = 6,
}
