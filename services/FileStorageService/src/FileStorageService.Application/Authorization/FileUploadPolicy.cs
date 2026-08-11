using FileStorageService.Application.Validation;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.Authorization;

public static class FileUploadPolicy
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;

    /// <summary>Tên field trong <c>listErrors</c> — khớp với tên form-data mà client gửi lên.</summary>
    private const string FileField = "file";

    private static readonly IReadOnlyDictionary<FilePurposeEnum, string[]> ExtensionsByPurpose =
        new Dictionary<FilePurposeEnum, string[]>
        {
            [FilePurposeEnum.Other] =
            [
                ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv"
            ],
            [FilePurposeEnum.Avatar] = [".jpg", ".jpeg", ".png", ".webp"],
            [FilePurposeEnum.TicketAttachment] = [".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx", ".mp3", ".wav", ".ogg", ".webm", ".m4a", ".flac"],
            [FilePurposeEnum.MaintenancePhoto] = [".jpg", ".jpeg", ".png"],
            [FilePurposeEnum.KbImage] = [".jpg", ".jpeg", ".png", ".webp", ".gif"],
            [FilePurposeEnum.Firmware] = [".bin", ".hex", ".fw"]
        };

    public static IReadOnlyCollection<string> GetAllowedExtensions(FilePurposeEnum purpose)
    {
        return ExtensionsByPurpose.TryGetValue(purpose, out var extensions)
            ? extensions
            : ExtensionsByPurpose[FilePurposeEnum.Other];
    }

    /// <param name="contentHeader">
    /// <see cref="FileSignatureInspector.HeaderLength"/> byte đầu của file, dùng để kiểm tra nội dung thật
    /// có khớp đuôi file hay không. Truyền <c>null</c> thì bỏ qua bước này và chỉ kiểm tra theo đuôi file.
    /// </param>
    public static void Validate<T>(
        IFormFile? file,
        FilePurposeEnum purpose,
        CommonResponse<T> response,
        byte[]? contentHeader = null)
    {
        if (file is null)
        {
            AddError(response, 400, "File is required.", nameof(file), "File is required.");
            return;
        }

        if (file.Length <= 0)
            AddError(response, 400, "File is empty.", nameof(file), "File is empty.");

        if (file.Length > MaxFileSizeBytes)
            AddError(response, 413, "File exceeds the 20 MB limit.", nameof(file), "Maximum file size is 20 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            AddError(response, 400, "File must have a valid extension.", nameof(file), "File must have a valid extension.");
            return;
        }

        var allowedExtensions = GetAllowedExtensions(purpose);
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AddError(
                response,
                400,
                $"Invalid file format for purpose {purpose}.",
                nameof(file),
                $"Only accepted: {string.Join(", ", allowedExtensions)}.");
            return;
        }

        // Chỉ kiểm tra nội dung khi file đã qua được kiểm tra kích thước + đuôi file: bước này so nội dung
        // với đuôi file nên chỉ có nghĩa khi đuôi file đã hợp lệ, và giữ nguyên status 413 cho file quá lớn.
        if (response.ListErrors.Count > 0 || contentHeader is null)
            return;

        ValidateContentSignature(extension, contentHeader, response);
    }

    /// <summary>
    /// Chặn file khai đuôi một đằng, nội dung một nẻo. Không có bước này thì HEIC đặt tên <c>.JPEG</c>
    /// (mặc định của ảnh chụp iPhone) vẫn upload được, được lưu kèm Content-Type <c>image/jpeg</c>,
    /// và chỉ vỡ ở trình duyệt khi FE render — lúc đó API đã trả 201 nên rất khó lần ra nguyên nhân.
    /// </summary>
    private static void ValidateContentSignature<T>(string extension, byte[] contentHeader, CommonResponse<T> response)
    {
        var expected = FileSignatureInspector.FindByExtension(extension);
        if (expected is null)
            return;

        var actual = FileSignatureInspector.Detect(contentHeader);
        if (actual is not null && actual.Matches(extension))
            return;

        var detail = actual is null
            ? $"File content is not a valid {expected.DisplayName}."
            : $"File has extension {extension} but its actual content is {actual.DisplayName}.{GetConversionHint(actual)}";

        AddError(
            response,
            400,
            $"File content does not match extension {extension}.",
            FileField,
            detail);
    }

    private static string GetConversionHint(FileContentSignature actual)
    {
        return actual.Mime is "image/heic" or "image/avif"
            ? " Photos taken with an iPhone default to HEIC even when the file name is .JPG/.JPEG, and browsers cannot display this format."
              + " Please convert the image to JPEG or PNG before uploading (iPhone: Settings → Camera → Formats → Most Compatible)."
            : string.Empty;
    }

    private static void AddError<T>(CommonResponse<T> response, int statusCode, string message, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = statusCode == 413 || response.StatusCode == 0 ? statusCode : response.StatusCode;
        response.Message = string.IsNullOrWhiteSpace(response.Message) ? message : response.Message;
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
