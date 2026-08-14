using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Application.Validation;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class UploadFileCommandHandler : IRequestHandler<UploadFileCommand, CommonResponse<FileUploadResponse>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;
    private readonly IFileAuthorizationService _fileAuthorizationService;

    public UploadFileCommandHandler(
        IObjectStorageService objectStorageService,
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
    }

    public async Task<CommonResponse<FileUploadResponse>> Handle(UploadFileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        if (!_fileAuthorizationService.CanUpload(request.Purpose))
        {
            return new CommonResponse<FileUploadResponse>
            {
                IsSuccess = false,
                StatusCode = 403,
                Message = "You do not have permission to upload a file with this purpose."
            };
        }

        // Content-Type suy từ nội dung thật, không lấy nguyên giá trị client khai: object storage phục vụ
        // file trực tiếp cho trình duyệt qua publicUrl, khai sai header là ảnh/audio không mở được.
        var contentHeader = await FileSignatureInspector.ReadHeaderAsync(request.File!, cancellationToken);
        var contentType = FileSignatureInspector.ResolveContentType(
            contentHeader,
            request.File!.FileName,
            request.File.ContentType);

        await using var stream = request.File.OpenReadStream();
        var result = await _objectStorageService.UploadAsync(
            stream,
            request.File.FileName,
            contentType,
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
            PublicUrl = result.PublicUrl,
            CreatedBy = _fileAuthorizationService.CurrentUserId
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
            Message = "File uploaded successfully.",
            Data = result
        };
    }
}
