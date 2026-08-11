using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class RestoreSiteCommandHandler : IRequestHandler<RestoreSiteCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public RestoreSiteCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(RestoreSiteCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Sites
            .GetAllAsync()
            .FirstOrDefaultAsync(site => site.Id == request.Id && site.IsDeleted, cancellationToken);

        if (entity is null)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Deleted site not found."
            };
        }

        var duplicate = await _unitOfWork.Sites
            .GetAllAsync()
            .AnyAsync(site =>
                site.Id != request.Id &&
                !site.IsDeleted &&
                site.CustomerId == entity.CustomerId &&
                site.Name == entity.Name, cancellationToken);

        if (duplicate)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cannot restore because the site name is already used by this customer."
            };
        }

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        _unitOfWork.Sites.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Site restored successfully."
        };
    }
}
