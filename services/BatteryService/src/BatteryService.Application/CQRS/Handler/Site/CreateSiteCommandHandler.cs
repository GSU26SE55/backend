using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using BatteryService.Application.Mapping;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SiteEntity = BatteryService.Domain.Entities.Site;

namespace BatteryService.Application.CQRS.Handler.Site;

public class CreateSiteCommandHandler : IRequestHandler<CreateSiteCommand, CommonResponse<SiteDto>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public CreateSiteCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<SiteDto>> Handle(CreateSiteCommand request, CancellationToken cancellationToken)
    {
        var customerExists = await _unitOfWork.CustomerAccounts
            .GetAllAsync()
            .AnyAsync(account =>
                account.Id == request.CustomerId &&
                account.IsActive &&
                !account.IsDeleted, cancellationToken);

        if (!customerExists)
        {
            return new CommonResponse<SiteDto>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy khách hàng đang hoạt động.",
                ListErrors = { new Errors { Field = nameof(request.CustomerId), Detail = "Khách hàng không tồn tại hoặc đã bị khóa." } }
            };
        }

        var name = request.Name.Trim();
        var duplicate = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site =>
                !site.IsDeleted &&
                site.CustomerId == request.CustomerId &&
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

        var entity = new SiteEntity
        {
            Id = Guid.NewGuid(),
            Name = name,
            CustomerId = request.CustomerId,
            Address = request.Address?.Trim(),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            CapacityKw = request.CapacityKw,
            InstallDate = ToUtc(request.InstallDate),
            Status = request.Status,
            ContactPersonName = request.ContactPersonName?.Trim(),
            ContactPersonPhone = request.ContactPersonPhone?.Trim()
        };

        await _unitOfWork.Sites.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<SiteDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Tạo site thành công.",
            Data = BatteryMapper.ToDto(entity)
        };
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
