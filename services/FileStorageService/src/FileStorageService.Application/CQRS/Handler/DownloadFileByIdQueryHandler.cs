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

    public DownloadFileByIdQueryHandler(
        IObjectStorageService objectStorageService,
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
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
            return Forbidden();

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
