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
    private readonly IFileAuthorizationService _fileAuthorizationService;

    public GetFileMetadataQueryHandler(
        IFileStorageUnitOfWork unitOfWork,
        IFileAuthorizationService fileAuthorizationService)
    {
        _unitOfWork = unitOfWork;
        _fileAuthorizationService = fileAuthorizationService;
    }

    public async Task<CommonResponse<FileMetadataResponse>> Handle(GetFileMetadataQuery request, CancellationToken cancellationToken)
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

        return new CommonResponse<FileMetadataResponse>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "File metadata retrieved successfully.",
            Data = FileMetadataMapper.ToResponse(file)
        };
    }

    private static CommonResponse<FileMetadataResponse> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "File not found."
    };

    private static CommonResponse<FileMetadataResponse> Forbidden() => new()
    {
        IsSuccess = false,
        StatusCode = 403,
        Message = "You do not have permission to access this file's metadata."
    };
}
