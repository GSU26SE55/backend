using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class UpdateSiteCommandHandler : IRequestHandler<UpdateSiteCommand, CommonResponse<SiteDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public UpdateSiteCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDto>> Handle(UpdateSiteCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Sites
            .GetAllAsync()
            .Include(site => site.BatteryGroups)
            .Include(site => site.BatteryAssets)
            .FirstOrDefaultAsync(site => site.Id == request.Id && !site.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound();

        if (request.CustomerId != Guid.Empty && request.CustomerId != entity.CustomerId)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Không thể đổi chủ sở hữu site qua endpoint cập nhật."
            };
        }

        var name = request.Name.Trim();
        var duplicate = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site =>
                site.Id != request.Id &&
                !site.IsDeleted &&
                site.CustomerId == entity.CustomerId &&
                site.Name.ToLower() == name.ToLower(), cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Tên site đã tồn tại cho khách hàng này.",
                ListErrors = { new Errors { Field = nameof(request.Name), Detail = "Tên site đã tồn tại cho khách hàng này." } }
            };
        }

        entity.Name = name;
        entity.Address = request.Address?.Trim();
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.CapacityKw = request.CapacityKw;
        entity.InstallDate = ToUtc(request.InstallDate);
        entity.Status = request.Status;
        entity.ContactPersonName = request.ContactPersonName?.Trim();
        entity.ContactPersonPhone = request.ContactPersonPhone?.Trim();

        _unitOfWork.Sites.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<SiteDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Cập nhật site thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }

    private static CommonResponse<SiteDto> NotFound()
    {
        return new CommonResponse<SiteDto>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = "Không tìm thấy site."
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
