namespace FileStorageService.Application.DTOs;

public class FileDownloadResponse
{
    public Stream Stream { get; set; } = Stream.Null;

    public string ContentType { get; set; } = "application/octet-stream";

    public string FileName { get; set; } = string.Empty;
}
