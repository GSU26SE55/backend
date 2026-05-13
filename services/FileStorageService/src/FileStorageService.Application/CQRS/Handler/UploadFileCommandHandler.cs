using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, CommonResponse<FileUploadResponse>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;

    public UploadFileCommandHandler(IObjectStorageService objectStorageService, IFileStorageUnitOfWork unitOfWork)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
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

        var uploadedFile = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = result.ObjectKey,
            OriginalFileName = result.FileName,
            ContentType = result.ContentType,
            Size = result.Size,
            FolderName = string.IsNullOrWhiteSpace(request.FolderName) ? "default" : request.FolderName.Trim(),
            Purpose = request.Purpose,
            Status = FileStatusEnum.Ready,
            PublicUrl = result.PublicUrl
        };

        try
        {
            await _unitOfWork.UploadedFiles.AddAsync(uploadedFile);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await _objectStorageService.DeleteAsync(result.ObjectKey, cancellationToken);
            }
            catch
            {
                // Preserve the metadata persistence failure; orphan cleanup can be retried operationally.
            }

            throw;
        }

        result.FileId = uploadedFile.Id;

        return new CommonResponse<FileUploadResponse>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Upload file thành công.",
            Data = result
        };
    }
}
