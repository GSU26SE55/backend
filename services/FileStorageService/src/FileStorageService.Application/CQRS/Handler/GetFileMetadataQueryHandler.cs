using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Application.Mapping;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class GetFileMetadataQueryHandler : IRequestHandler<GetFileMetadataQuery, CommonResponse<FileMetadataResponse>>
{
    private readonly IFileStorageUnitOfWork _unitOfWork;

    public GetFileMetadataQueryHandler(IFileStorageUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<FileMetadataResponse>> Handle(GetFileMetadataQuery request, CancellationToken cancellationToken)
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

        return new CommonResponse<FileMetadataResponse>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Lấy metadata file thành công.",
            Data = FileMetadataMapper.ToResponse(file)
        };
    }

    private static CommonResponse<FileMetadataResponse> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Không tìm thấy file."
    };
}
