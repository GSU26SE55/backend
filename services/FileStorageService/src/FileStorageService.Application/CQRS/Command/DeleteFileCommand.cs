using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Command;

public class DeleteFileCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string ObjectKey { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
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
