using FileStorageService.Application.CQRS.Notification.Audit;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class DownloadFileByIdQueryHandler : IRequestHandler<DownloadFileByIdQuery, CommonResponse<FileDownloadResponse>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;
    private readonly IFileAuthorizationService _fileAuthorizationService;
    private readonly IPublisher _publisher;

    public DownloadFileByIdQueryHandler(
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

    public async Task<CommonResponse<FileDownloadResponse>> Handle(DownloadFileByIdQuery request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var file = await _unitOfWork.UploadedFiles
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.Id && !f.IsDeleted && f.Status != FileStatusEnum.Deleted, cancellationToken);

        if (file is null)
            return NotFound();

        if (!_fileAuthorizationService.CanRead(file))
        {
            // #46 QA solars.io.vn 2026-08-29 — hạ tầng FileAuditLog có đủ nhưng chưa handler nào
            // từng ghi vào đó. Đây chính là action "data leak investigation" cần theo dõi nhất.
            await _publisher.Publish(FileAuditTrailNotification.For(
                FileAuditActionEnum.AccessDenied, file.Id, file.OriginalFileName, isSuccess: false,
                reason: "Not authorized to download this file"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Forbidden();
        }

        if (file.Status == FileStatusEnum.Quarantined)
        {
            return new CommonResponse<FileDownloadResponse>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File is quarantined and cannot be downloaded."
            };
        }

        if (file.Status == FileStatusEnum.Processing)
        {
            return new CommonResponse<FileDownloadResponse>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File is being processed, please try again later."
            };
        }

        var result = await _objectStorageService.DownloadAsync(file.ObjectKey, cancellationToken);

        await _publisher.Publish(FileAuditTrailNotification.For(
            FileAuditActionEnum.FileDownloaded, file.Id, file.OriginalFileName), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<FileDownloadResponse>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "File downloaded successfully.",
            Data = result
        };
    }

    private static CommonResponse<FileDownloadResponse> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "File not found."
    };

    private static CommonResponse<FileDownloadResponse> Forbidden() => new()
    {
        IsSuccess = false,
        StatusCode = 403,
        Message = "You do not have permission to download this file."
    };
}
