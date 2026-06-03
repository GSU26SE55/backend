using FileStorageService.Application.Authorization;
using FileStorageService.Application.DTOs;
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

    public Task<CommonResponse<FileUploadResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileUploadResponse>();
        FileUploadPolicy.Validate(File, Purpose, response);

        return Task.FromResult(response);
    }
}
