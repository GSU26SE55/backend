using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Command;

public class DeleteFileByIdCommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    public Guid Id { get; set; }

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

        return Task.FromResult(response);
    }
}
