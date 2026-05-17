using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;

namespace FileStorageService.Application.Interfaces;

public interface IFileAuthorizationService
{
    Guid? CurrentUserId { get; }

    bool CanUpload(FilePurposeEnum purpose);

    bool CanRead(UploadedFile file);

    bool CanDelete(UploadedFile file);
}
