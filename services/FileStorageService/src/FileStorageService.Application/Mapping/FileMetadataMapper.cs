using FileStorageService.Application.DTOs;
using FileStorageService.Domain.Entities;

namespace FileStorageService.Application.Mapping;

public static class FileMetadataMapper
{
    public static FileMetadataResponse ToResponse(UploadedFile file)
    {
        return new FileMetadataResponse
        {
            FileId = file.Id,
            ObjectKey = file.ObjectKey,
            FileName = file.OriginalFileName,
            ContentType = file.ContentType,
            Size = file.Size,
            FolderName = file.FolderName,
            Purpose = file.Purpose,
            Status = file.Status,
            PublicUrl = file.PublicUrl,
            CreatedAt = file.CreatedAt,
            UpdatedAt = file.UpdatedAt
        };
    }
}
