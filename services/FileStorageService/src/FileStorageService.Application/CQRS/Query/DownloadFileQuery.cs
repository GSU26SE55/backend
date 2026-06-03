using FileStorageService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class DownloadFileQuery : IRequest<CommonResponse<FileDownloadResponse>>, IValidatable<CommonResponse<FileDownloadResponse>>
{
    public string ObjectKey { get; set; } = string.Empty;

    public Task<CommonResponse<FileDownloadResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileDownloadResponse>();
        if (string.IsNullOrWhiteSpace(ObjectKey))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "ObjectKey là bắt buộc.";
            response.ListErrors.Add(new Errors { Field = nameof(ObjectKey), Detail = "ObjectKey là bắt buộc." });
        }
        else if (ObjectKey.Contains("..", StringComparison.Ordinal))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "ObjectKey không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(ObjectKey), Detail = "ObjectKey không được chứa '..'." });
        }

        return Task.FromResult(response);
    }
}
