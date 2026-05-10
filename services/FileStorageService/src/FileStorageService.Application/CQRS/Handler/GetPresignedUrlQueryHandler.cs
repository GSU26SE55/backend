using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class GetPresignedUrlQueryHandler : IRequestHandler<GetPresignedUrlQuery, CommonResponse<string>>
{
    private readonly IObjectStorageService _objectStorageService;

    public GetPresignedUrlQueryHandler(IObjectStorageService objectStorageService)
    {
        _objectStorageService = objectStorageService;
    }

    public async Task<CommonResponse<string>> Handle(GetPresignedUrlQuery request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var result = await _objectStorageService.GetPresignedUrlAsync(
            request.ObjectKey,
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
}
