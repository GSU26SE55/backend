using Amazon.S3;
using Amazon.S3.Model;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace FileStorageService.Infrastructure.Services;

public class S3CompatibleFileStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly IAmazonS3 _publicS3Client;
    private readonly ObjectStorageOptions _options;

    public S3CompatibleFileStorageService(
        IAmazonS3 s3Client,
        [Microsoft.Extensions.DependencyInjection.FromKeyedServices("public")] IAmazonS3 publicS3Client,
        IOptions<ObjectStorageOptions> options)
    {
        _s3Client = s3Client;
        _publicS3Client = publicS3Client;
        _options = options.Value;
    }

    public async Task<FileUploadResponse> UploadAsync(
        Stream stream,
        string originalFileName,
        string contentType,
        long size,
        string folderName,
        CancellationToken cancellationToken = default)
    {
        ValidateFile(originalFileName, size);

        if (_options.CreateBucketIfNotExists)
            await EnsureBucketExistsAsync(cancellationToken);

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var safeFolder = SanitizePathSegment(folderName);
        var objectKey = $"{safeFolder}/{Guid.NewGuid():N}{extension}";
        var normalizedContentType = string.IsNullOrWhiteSpace(contentType)
            ? GetContentType(extension)
            : contentType;

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = normalizedContentType,
            AutoCloseStream = false
        };

        request.Metadata.Add("original-file-name", System.Uri.EscapeDataString(originalFileName));

        await _s3Client.PutObjectAsync(request, cancellationToken);

        return new FileUploadResponse
        {
            ObjectKey = objectKey,
            FileName = originalFileName,
            ContentType = normalizedContentType,
            Size = size,
            PublicUrl = BuildPublicUrl(objectKey)
        };
    }

    public async Task<FileDownloadResponse> DownloadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKey);
        using var response = await _s3Client.GetObjectAsync(_options.BucketName, safeObjectKey, cancellationToken);

        var memoryStream = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;

        return new FileDownloadResponse
        {
            Stream = memoryStream,
            ContentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? GetContentType(Path.GetExtension(safeObjectKey))
                : response.Headers.ContentType,
            FileName = Path.GetFileName(safeObjectKey)
        };
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKey);
        await _s3Client.DeleteObjectAsync(_options.BucketName, safeObjectKey, cancellationToken);
    }

    public Task<string> GetPresignedUrlAsync(string objectKey, TimeSpan expiresIn, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKey);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = safeObjectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiresIn)
        };

        // Dùng public S3 client → URL signed với hostname mà browser resolve được (vd localhost:9090).
        return Task.FromResult(_publicS3Client.GetPreSignedURL(request));
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var buckets = await _s3Client.ListBucketsAsync(cancellationToken);
        if (buckets.Buckets.Any(bucket => bucket.BucketName == _options.BucketName))
            return;

        await _s3Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = _options.BucketName
        }, cancellationToken);
    }

    private void ValidateFile(string originalFileName, long size)
    {
        if (size <= 0)
            throw new ArgumentException("Uploaded file is empty.", nameof(size));

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException("File extension is required.");
    }

    private string? BuildPublicUrl(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            return null;

        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{objectKey}";
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        var normalized = objectKey.Trim().TrimStart('/', '\\').Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("..", StringComparison.Ordinal))
            throw new InvalidOperationException("ObjectKey không hợp lệ.");

        return normalized;
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
        return cleaned.Replace("..", "-", StringComparison.Ordinal).Trim('/', '\\');
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".bin" or ".hex" or ".fw" => "application/octet-stream",
            _ => "application/octet-stream"
        };
    }
}
