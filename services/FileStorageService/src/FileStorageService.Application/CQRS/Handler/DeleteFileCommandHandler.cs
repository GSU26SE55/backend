using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Notification.Audit;
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
    private readonly IPublisher _publisher;

    public DeleteFileCommandHandler(
        IObjectStorageService objectStorageService,
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService,
        IPublisher publisher)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
        _publisher = publisher;
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
        {
            // #46 QA solars.io.vn 2026-08-29 — hạ tầng FileAuditLog có đủ nhưng chưa handler nào
            // từng ghi vào đó.
            await _publisher.Publish(FileAuditTrailNotification.For(
                FileAuditActionEnum.AccessDenied, file.Id, file.OriginalFileName, isSuccess: false,
                reason: "Not authorized to delete this file"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Forbidden();
        }

        await _objectStorageService.DeleteAsync(objectKey, cancellationToken);

        file.Status = FileStatusEnum.Deleted;
        _unitOfWork.UploadedFiles.DeleteAsync(file);
        await _publisher.Publish(FileAuditTrailNotification.For(
            FileAuditActionEnum.FileDeleted, file.Id, file.OriginalFileName), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 204,
            Message = "File deleted successfully.",
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
        Message = "File not found."
    };

    private static CommonResponse<string> Forbidden() => new()
    {
        IsSuccess = false,
        StatusCode = 403,
        Message = "You do not have permission to delete this file."
    };
}
