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
            response.Message = "ObjectKey is required.";
            response.ListErrors.Add(new Errors { Field = nameof(ObjectKey), Detail = "ObjectKey is required." });
        }
        else if (ObjectKey.Contains("..", StringComparison.Ordinal))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid ObjectKey.";
            response.ListErrors.Add(new Errors { Field = nameof(ObjectKey), Detail = "ObjectKey must not contain '..'." });
        }

        return Task.FromResult(response);
    }
}
