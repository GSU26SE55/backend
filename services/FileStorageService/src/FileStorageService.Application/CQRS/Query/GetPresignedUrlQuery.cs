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
            response.Message = "ObjectKey là bắt buộc.";
            response.ListErrors.Add(new Errors { Field = nameof(ObjectKey), Detail = "ObjectKey là bắt buộc." });
        }

        if (ExpiresInMinutes is < 1 or > 1440)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "ExpiresInMinutes phải nằm trong khoảng 1 đến 1440.";
            response.ListErrors.Add(new Errors { Field = nameof(ExpiresInMinutes), Detail = "ExpiresInMinutes phải nằm trong khoảng 1 đến 1440." });
        }

        return Task.FromResult(response);
    }
}
