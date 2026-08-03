using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupGetByIdQueryHandler
    : IRequestHandler<NotificationGroupGetByIdQuery, NotificationGroupResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationGroupGetByIdQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationGroupResponse> Handle(
        NotificationGroupGetByIdQuery request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm.",
            };
        }

        var memberCount = await NotificationGroupMembership.CountRecipientsAsync(
            _unitOfWork, group, cancellationToken);

        return new NotificationGroupResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new NotificationGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                Kind = group.Kind,
                RoleFilter = group.RoleFilter,
                IsSystem = group.IsSystem,
                MemberCount = memberCount,
                CreatedAt = group.CreatedAt,
                UpdatedAt = group.UpdatedAt,
            },
        };
    }
}
