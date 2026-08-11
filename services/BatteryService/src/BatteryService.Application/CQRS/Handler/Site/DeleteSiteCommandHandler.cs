using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Site;

public class DeleteSiteCommandHandler : IRequestHandler<DeleteSiteCommand, CommonResponse<object>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;

    public DeleteSiteCommandHandler(IBatteryUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<object>> Handle(DeleteSiteCommand request, CancellationToken cancellationToken)
    {
        var entity = await _unitOfWork.Sites
            .GetAllAsync()
            .FirstOrDefaultAsync(site => site.Id == request.Id && !site.IsDeleted, cancellationToken);

        if (entity is null)
            return NotFound("Site not found.");

        var hasAssets = await _unitOfWork.BatteryAssets
            .GetAllAsync()
            .AnyAsync(asset => asset.SiteId == request.Id && !asset.IsDeleted, cancellationToken);

        if (hasAssets)
        {
            return new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Cannot delete site because it still has battery assets."
            };
        }

        _unitOfWork.Sites.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CommonResponse<object>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Site deleted successfully."
        };
    }

    private static CommonResponse<object> NotFound(string message)
    {
        return new CommonResponse<object>
        {
            IsSuccess = false,
            StatusCode = 404,
            Message = message
        };
    }
}
