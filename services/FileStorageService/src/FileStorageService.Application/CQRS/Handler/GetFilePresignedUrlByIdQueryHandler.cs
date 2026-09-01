using FileStorageService.Application.CQRS.Notification.Audit;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class GetFilePresignedUrlByIdQueryHandler : IRequestHandler<GetFilePresignedUrlByIdQuery, CommonResponse<string>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;
    private readonly IFileAuthorizationService _fileAuthorizationService;
    private readonly IPublisher _publisher;

    public GetFilePresignedUrlByIdQueryHandler(
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

    public async Task<CommonResponse<string>> Handle(GetFilePresignedUrlByIdQuery request, CancellationToken cancellationToken)
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
            // từng ghi vào đó.
            await _publisher.Publish(FileAuditTrailNotification.For(
                FileAuditActionEnum.AccessDenied, file.Id, file.OriginalFileName, isSuccess: false,
                reason: "Not authorized to create a presigned URL for this file"), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Forbidden();
        }

        if (file.Status == FileStatusEnum.Quarantined)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File is quarantined and cannot be downloaded."
            };
        }

        if (file.Status == FileStatusEnum.Processing)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File is being processed, please try again later."
            };
        }

        var result = await _objectStorageService.GetPresignedUrlAsync(
            file.ObjectKey,
            TimeSpan.FromMinutes(request.ExpiresInMinutes),
            cancellationToken);

        await _publisher.Publish(FileAuditTrailNotification.For(
            FileAuditActionEnum.PresignedUrlGenerated, file.Id, file.OriginalFileName), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Presigned URL created successfully.",
            Data = result
        };
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
        Message = "You do not have permission to create a presigned URL for this file."
    };
}
