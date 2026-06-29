namespace TicketService.Application.Interfaces.Services;

public interface IFileUploadClient
{
    /// <summary>
    /// Upload file lên FileStorageService, forward Authorization header từ original request.
    /// Returns (FileId, DownloadUrl) từ FileStorageService response.
    /// </summary>
    Task<(Guid FileId, string DownloadUrl)> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        long sizeBytes,
        string authorizationHeader,
        CancellationToken ct = default);
}
