using Microsoft.AspNetCore.Http;

namespace SharedContracts.Interfaces;

public interface IFileStorageService
{
    Task<string> UploadAsync(IFormFile file, string folderName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);
}
