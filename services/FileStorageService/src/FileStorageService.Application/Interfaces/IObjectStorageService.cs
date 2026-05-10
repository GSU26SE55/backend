using FileStorageService.Application.DTOs;

namespace FileStorageService.Application.Interfaces;

public interface IObjectStorageService
{
    Task<FileUploadResponse> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        long size,
        string folderName,
        CancellationToken cancellationToken = default);

    Task<FileDownloadResponse> DownloadAsync(string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<string> GetPresignedUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default);
}
