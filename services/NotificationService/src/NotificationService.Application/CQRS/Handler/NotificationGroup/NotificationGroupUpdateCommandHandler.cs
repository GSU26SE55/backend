using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupUpdateCommandHandler
    : IRequestHandler<NotificationGroupUpdateCommand, NotificationGroupActionResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationGroupUpdateCommandHandler> _logger;

    public NotificationGroupUpdateCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationGroupUpdateCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationGroupActionResponse> Handle(
        NotificationGroupUpdateCommand request, CancellationToken cancellationToken)
    {
        // Tracking BẬT: lấy để sửa rồi lưu.
        var group = await _unitOfWork.NotificationGroups.GetAllAsync()
            .FirstOrDefaultAsync(g => g.Id == request.Id && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm.",
            };
        }

        if (group.IsSystem)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Nhóm hệ thống không sửa được.",
            };
        }

        var name = request.Name.Trim();
        var normalized = NotificationGroupRules.Normalize(name);
        var previousName = group.Name;

        // Loại trừ chính nó: đổi hoa-thường của tên hiện tại ("nhóm a" → "Nhóm A") phải được.
        var duplicated = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .AnyAsync(g => !g.IsDeleted && g.Id != group.Id && g.NormalizedName == normalized, cancellationToken);

        if (duplicated)
        {
            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Đã có nhóm khác trùng tên.",
            };
        }

        group.Name = name;
        group.NormalizedName = normalized;
        group.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            // UpdateAsync là VOID — không await.
            _unitOfWork.NotificationGroups.UpdateAsync(group);

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.GroupUpdated,
                group.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Sửa nhóm người nhận",
                metadata: new Dictionary<string, object?>
                {
                    ["previousName"] = previousName,
                    ["newName"] = group.Name,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (DbUpdateException ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogWarning(ex, "Sửa nhóm {Id} thất bại — nhiều khả năng trùng tên do race.", group.Id);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Đã có nhóm khác trùng tên.",
            };
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Sửa nhóm {Id} thất bại.", group.Id);

            return new NotificationGroupActionResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Không sửa được nhóm.",
            };
        }

        return new NotificationGroupActionResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã cập nhật nhóm.",
            Data = group.Id,
        };
    }
}
