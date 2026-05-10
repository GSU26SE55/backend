using FileStorageService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Command;

public class UploadFileCommand : IRequest<CommonResponse<FileUploadResponse>>, IValidatable<CommonResponse<FileUploadResponse>>
{
    public IFormFile? File { get; set; }

    public string FolderName { get; set; } = "default";

    public Task<CommonResponse<FileUploadResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileUploadResponse>();
        if (File is null)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "File là bắt buộc.";
            response.ListErrors.Add(new Errors { Field = nameof(File), Detail = "File là bắt buộc." });
        }

        return Task.FromResult(response);
    }
}
