using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class DownloadFileQueryHandler : IRequestHandler<DownloadFileQuery, CommonResponse<FileDownloadResponse>>
{
    private readonly IObjectStorageService _objectStorageService;

    public DownloadFileQueryHandler(IObjectStorageService objectStorageService)
    {
        _objectStorageService = objectStorageService;
    }

    public async Task<CommonResponse<FileDownloadResponse>> Handle(DownloadFileQuery request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var result = await _objectStorageService.DownloadAsync(request.ObjectKey, cancellationToken);

        return new CommonResponse<FileDownloadResponse>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Download file thành công.",
            Data = result
        };
    }
}
