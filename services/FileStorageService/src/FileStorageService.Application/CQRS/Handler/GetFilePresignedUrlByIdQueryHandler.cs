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

    public GetFilePresignedUrlByIdQueryHandler(IObjectStorageService objectStorageService, IFileStorageUnitOfWork unitOfWork)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
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

        if (file.Status == FileStatusEnum.Quarantined)
        {
            return new CommonResponse<string>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "File đang bị cách ly và không thể tải."
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
}
