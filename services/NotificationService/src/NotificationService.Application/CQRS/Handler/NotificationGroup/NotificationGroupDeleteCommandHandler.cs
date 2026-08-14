using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupDeleteCommandHandler
    : IRequestHandler<NotificationGroupDeleteCommand, NotificationGroupActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationGroupDeleteCommandHandler> _logger;

    public NotificationGroupDeleteCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationGroupDeleteCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationGroupActionResponse> Handle(
        NotificationGroupDeleteCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.NotificationGroups.GetAllAsync()
            .FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Group not found.",
            };
        }

        if (group.IsSystem)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "System groups cannot be deleted.",
            };
        }

        // Xoá mềm thành viên TRƯỚC rồi mới tới nhóm — quy ước cascade của dự án (§7 rules).
        // Khoá ngoại ON DELETE CASCADE ở DB chỉ chạy khi xoá CỨNG; ở đây là soft delete nên phải tự
        // đánh dấu, nếu không thì thêm lại đúng người vào nhóm mới sẽ đụng ux_..._pair của dòng cũ.
        var members = await _unitOfWork.NotificationGroupMembers.GetAllAsync()
            .Where(m => m.GroupId == group.Id && !m.IsDeleted)
            .ToListAsync(cancellationToken);

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var member in members)
                _unitOfWork.NotificationGroupMembers.DeleteAsync(member);   // VOID — không await

            _unitOfWork.NotificationGroups.DeleteAsync(group);              // VOID — không await

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.GroupDeleted,
                group.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Delete recipient group",
                metadata: new Dictionary<string, object?>
                {
                    ["name"] = group.Name,
                    ["removedMembers"] = members.Count,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Xoá nhóm {Id} thất bại.", group.Id);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Failed to delete the group.",
            };
        }

        _logger.LogInformation(
            "Đã xoá nhóm '{Name}' (id {Id}) cùng {Count} thành viên.", group.Name, group.Id, members.Count);

        return new NotificationGroupActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = $"Deleted the group and {members.Count} member(s).",
            Data = group.Id,
        };
    }
}
