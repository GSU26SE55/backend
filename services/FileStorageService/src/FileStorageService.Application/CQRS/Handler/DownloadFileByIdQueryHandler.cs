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

    public DownloadFileByIdQueryHandler(IObjectStorageService objectStorageService, IFileStorageUnitOfWork unitOfWork)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<FileDownloadResponse>> Handle(DownloadFileByIdQuery request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var file = await _unitOfWork.UploadedFiles
            .GetAllAsync()
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.Status != FileStatusEnum.Deleted, cancellationToken);

        if (file is null)
            return NotFound();

        if (file.Status == FileStatusEnum.Quarantined)
        {
            return new CommonResponse<FileDownloadResponse>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File đang bị cách ly và không thể tải."
            };
        }

        var result = await _objectStorageService.DownloadAsync(file.ObjectKey, cancellationToken);

        return new CommonResponse<FileDownloadResponse>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Download file thành công.",
            Data = result
        };
    }

    private static CommonResponse<FileDownloadResponse> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Không tìm thấy file."
    };
}
