using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupRemoveMemberCommandHandler
    : IRequestHandler<NotificationGroupRemoveMemberCommand, NotificationGroupActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationGroupRemoveMemberCommandHandler> _logger;

    public NotificationGroupRemoveMemberCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationGroupRemoveMemberCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationGroupActionResponse> Handle(
        NotificationGroupRemoveMemberCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .FirstOrDefaultAsync(g => g.Id == request.GroupId && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Group not found.",
            };
        }

        if (group.Kind == NotificationGroupKindEnum.Role)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Role-based group membership is inferred automatically — members cannot be removed manually.",
            };
        }

        var member = await _unitOfWork.NotificationGroupMembers.GetAllAsync()
            .FirstOrDefaultAsync(
                m => m.GroupId == group.Id && m.UserId == request.UserId && !m.IsDeleted,
                cancellationToken);

        if (member is null)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "This user is not in the group.",
            };
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            _unitOfWork.NotificationGroupMembers.DeleteAsync(member);   // VOID — không await

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.GroupMemberRemoved,
                group.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Remove member from group",
                metadata: new Dictionary<string, object?>
                {
                    ["groupName"] = group.Name,
                    ["userId"] = request.UserId.ToString(),
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Bỏ thành viên {UserId} khỏi nhóm {Id} thất bại.", request.UserId, group.Id);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Failed to remove the member.",
            };
        }

        return new NotificationGroupActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Member removed from the group.",
            Data = group.Id,
        };
    }
}
