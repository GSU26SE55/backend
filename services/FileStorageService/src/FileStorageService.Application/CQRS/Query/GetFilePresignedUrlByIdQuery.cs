using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class GetFilePresignedUrlByIdQuery : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    public int ExpiresInMinutes { get; set; } = 15;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid FileId.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "Invalid FileId." });
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
