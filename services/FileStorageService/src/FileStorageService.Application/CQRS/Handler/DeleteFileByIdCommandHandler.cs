using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace FileStorageService.Application.CQRS.Handler;

public class DeleteFileByIdCommandHandler : IRequestHandler<DeleteFileByIdCommand, CommonResponse<string>>
{
    private readonly IObjectStorageService _objectStorageService;
    private readonly IFileStorageUnitOfWork _unitOfWork;

    public DeleteFileByIdCommandHandler(IObjectStorageService objectStorageService, IFileStorageUnitOfWork unitOfWork)
    {
        _objectStorageService = objectStorageService;
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<string>> Handle(DeleteFileByIdCommand request, CancellationToken cancellationToken)
    {
        var validation = await request.ValidateAsync();
        if (!validation.IsSuccess)
            return validation;

        var file = await _unitOfWork.UploadedFiles
            .GetAllAsync()
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.Status != FileStatusEnum.Deleted, cancellationToken);

        if (file is null)
            return NotFound();

        await _objectStorageService.DeleteAsync(file.ObjectKey, cancellationToken);

        file.Status = FileStatusEnum.Deleted;
        _unitOfWork.UploadedFiles.DeleteAsync(file);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<string>
        {
            IsSuccess = true,
            StatusCode = 204,
            Message = "Xóa file thành công.",
            Data = request.Id.ToString()
        };
    }

    private static CommonResponse<string> NotFound() => new()
    {
        IsSuccess = false,
        StatusCode = 404,
        Message = "Không tìm thấy file."
    };
}
