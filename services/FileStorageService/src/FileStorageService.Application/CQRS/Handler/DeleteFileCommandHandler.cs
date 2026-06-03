using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand, CommonResponse<string>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;
    private readonly IFileAuthorizationService _fileAuthorizationService;

    public DeleteFileCommandHandler(
        IObjectStorageService objectStorageService,
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
    }

    public async Task<CommonResponse<string>> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var objectKey = NormalizeObjectKey(request.ObjectKey);
        var file = await _unitOfWork.UploadedFiles
            .GetAllAsync()
            .FirstOrDefaultAsync(f => f.ObjectKey == objectKey && !f.IsDeleted && f.Status != FileStatusEnum.Deleted, cancellationToken);

        if (file is null)
            return NotFound();

        if (!_fileAuthorizationService.CanDelete(file))
            return Forbidden();

        await _objectStorageService.DeleteAsync(objectKey, cancellationToken);

        file.Status = FileStatusEnum.Deleted;
        _unitOfWork.UploadedFiles.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 204,
            Message = "Xóa file thành công.",
            Data = objectKey
        };
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        return objectKey.Trim().TrimStart('/', '\\').Replace('\\', '/');
    }

    private static CommonResponse<string> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Không tìm thấy file."
    };

    private static CommonResponse<string> Forbidden() => new()
    {
        IsSuccess = false,
        StatusCode = 403,
        Message = "Không có quyền xóa file này."
    };
}
