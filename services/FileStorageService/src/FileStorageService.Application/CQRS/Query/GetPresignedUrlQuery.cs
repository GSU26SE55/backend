using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class GetPresignedUrlQuery : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public string ObjectKey { get; set; } = string.Empty;

    public int ExpiresInMinutes { get; set; } = 15;

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

        if (ExpiresInMinutes is < 1 or > 1440)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "ExpiresInMinutes must be between 1 and 1440.";
            response.ListErrors.Add(new Errors { Field = nameof(ExpiresInMinutes), Detail = "ExpiresInMinutes must be between 1 and 1440." });
        }

        return Task.FromResult(response);
    }
}
