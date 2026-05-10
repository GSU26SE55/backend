using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, CommonResponse<FileUploadResponse>>
{
    private readonly IObjectStorageService _objectStorageService;

    public UploadFileCommandHandler(IObjectStorageService objectStorageService)
    {
        _objectStorageService = objectStorageService;
    }

    public async Task<CommonResponse<FileUploadResponse>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        await using var stream = request.File!.OpenReadStream();
        var result = await _objectStorageService.UploadAsync(
            stream,
            request.File.FileName,
            request.File.ContentType,
            request.File.Length,
            request.FolderName,
            cancellationToken);

        return new CommonResponse<FileUploadResponse>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Upload file thành công.",
            Data = result
        };
    }
}
