using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.Authorization;

public static class FileUploadPolicy
{
    public const long MaxFileSizeBytes = 20 * 1024 * 1024;

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

    public static void Validate<T>(IFormFile? file, FilePurposeEnum purpose, CommonResponse<T> response)
    {
        if (file is null)
        {
            AddError(response, 400, "File là bắt buộc.", nameof(file), "File là bắt buộc.");
            return;
        }

        if (file.Length <= 0)
            AddError(response, 400, "File rỗng.", nameof(file), "File rỗng.");

        if (file.Length > MaxFileSizeBytes)
            AddError(response, 413, "File vượt quá giới hạn 20 MB.", nameof(file), "Kích thước file tối đa là 20 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(extension))
        {
            AddError(response, 400, "File phải có phần mở rộng hợp lệ.", nameof(file), "File phải có phần mở rộng hợp lệ.");
            return;
        }

        var allowedExtensions = GetAllowedExtensions(purpose);
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            AddError(
                response,
                400,
                $"Định dạng file không hợp lệ cho purpose {purpose}.",
                nameof(file),
                $"Chỉ chấp nhận: {string.Join(", ", allowedExtensions)}.");
        }
    }

    private static void AddError<T>(CommonResponse<T> response, int statusCode, string message, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = statusCode == 413 || response.StatusCode == 0 ? statusCode : response.StatusCode;
        response.Message = string.IsNullOrWhiteSpace(response.Message) ? message : response.Message;
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
