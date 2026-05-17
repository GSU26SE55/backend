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

    public GetFilePresignedUrlByIdQueryHandler(
        IObjectStorageService objectStorageService,
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
    }

    public async Task<CommonResponse<string>> Handle(GetFilePresignedUrlByIdQuery request, CancellationToken cancellationToken)
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

        if (!_fileAuthorizationService.CanRead(file))
            return Forbidden();

        if (file.Status == FileStatusEnum.Quarantined)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File đang bị cách ly và không thể tải."
            };
        }

        if (file.Status == FileStatusEnum.Processing)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File đang được xử lý, vui lòng thử lại sau."
            };
        }

        var result = await _objectStorageService.GetPresignedUrlAsync(
            file.ObjectKey,
            TimeSpan.FromMinutes(request.ExpiresInMinutes),
            cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Tạo presigned URL thành công.",
            Data = result
        };
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
        Message = "Không có quyền tạo presigned URL cho file này."
    };
}
