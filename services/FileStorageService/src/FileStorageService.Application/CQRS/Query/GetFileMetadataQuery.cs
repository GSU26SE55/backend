using FileStorageService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace FileStorageService.Application.CQRS.Query;

public class GetFileMetadataQuery : IRequest<CommonResponse<FileMetadataResponse>>, IValidatable<CommonResponse<FileMetadataResponse>>
{
    public Guid Id { get; set; }

    public Task<CommonResponse<FileMetadataResponse>> ValidateAsync()
    {
        var response = new CommonResponse<FileMetadataResponse>();
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
