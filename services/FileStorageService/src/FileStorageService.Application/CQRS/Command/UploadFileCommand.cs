using FileStorageService.Application.Authorization;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Validation;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Command;

public class UploadFileCommand : IRequest<CommonResponse<FileUploadResponse>>, IValidatable<CommonResponse<FileUploadResponse>>
{
    public IFormFile? File { get; set; }

    public string FolderName { get; set; } = "default";

    public FilePurposeEnum Purpose { get; set; } = FilePurposeEnum.Other;

    public async Task<CommonResponse<FileUploadResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileUploadResponse>();

        // Đọc magic bytes để đối chiếu nội dung thật với đuôi file — xem FileSignatureInspector.
        var contentHeader = File is null ? null : await FileSignatureInspector.ReadHeaderAsync(File);
        FileUploadPolicy.Validate(File, Purpose, response, contentHeader);

        return response;
    }
}
