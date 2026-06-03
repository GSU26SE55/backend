using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class GetFilePresignedUrlByIdQuery : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public Guid Id { get; set; }

    public int ExpiresInMinutes { get; set; } = 15;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();
        if (Id == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "FileId không hợp lệ.";
            response.ListErrors.Add(new Errors { Field = nameof(Id), Detail = "FileId không hợp lệ." });
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
