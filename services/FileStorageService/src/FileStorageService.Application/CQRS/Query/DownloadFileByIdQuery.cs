using System.Text.Json.Serialization;
using FileStorageService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class DownloadFileByIdQuery : IRequest<CommonResponse<FileDownloadResponse>>, IValidatable<CommonResponse<FileDownloadResponse>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    public Task<CommonResponse<FileDownloadResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileDownloadResponse>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "FileId không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "FileId không hợp lệ." });
        }

        return Task.FromResult(response);
    }
}
