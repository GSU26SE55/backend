using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.Interfaces;
using MediatR;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class DeleteFileCommandHandler : IRequestHandler<DeleteFileCommand, CommonResponse<string>>
{
    private readonly IObjectStorageService _objectStorageService;

    public DeleteFileCommandHandler(IObjectStorageService objectStorageService)
    {
        _objectStorageService = objectStorageService;
    }

    public async Task<CommonResponse<string>> Handle(DeleteFileCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        await _objectStorageService.DeleteAsync(request.ObjectKey, cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 204,
            Message = "Xóa file thành công.",
            Data = request.ObjectKey
        };
    }
}
