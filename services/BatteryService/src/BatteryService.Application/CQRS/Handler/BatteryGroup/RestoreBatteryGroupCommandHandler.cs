using BatteryService.Application.CQRS.Command.BatteryGroup;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.BatteryGroup;

public class RestoreBatteryGroupCommandHandler : IRequestHandler<RestoreBatteryGroupCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public RestoreBatteryGroupCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(RestoreBatteryGroupCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .FirstOrDefaultAsync(group => group.Id == request.Id && group.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm pin đã xóa."
            };
        }

        var siteExists = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site => site.Id == entity.SiteId && !site.IsDeleted, cancellationToken);

        if (!siteExists)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể khôi phục vì site của nhóm đã bị xóa."
            };
        }

        var duplicate = await _unitOfWork.BatteryGroups
            .GetAllAsync()
            .AnyAsync(group =>
                group.Id != request.Id &&
                !group.IsDeleted &&
                group.SiteId == entity.SiteId &&
                group.Name == entity.Name, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể khôi phục vì tên nhóm pin đã được sử dụng trong site."
            };
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        _unitOfWork.BatteryGroups.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Khôi phục nhóm pin thành công."
        };
    }
}
