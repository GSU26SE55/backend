using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupAddMembersCommandHandler
    : IRequestHandler<NotificationGroupAddMembersCommand, NotificationGroupAddMembersResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationGroupAddMembersCommandHandler> _logger;

    public NotificationGroupAddMembersCommandHandler(
        INotificationUnitOfWork unitOfWork,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationGroupAddMembersCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationGroupAddMembersResponse> Handle(
        NotificationGroupAddMembersCommand request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .FirstOrDefaultAsync(g => g.Id == request.GroupId && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupAddMembersResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Không tìm thấy nhóm.",
            };
        }

        if (group.Kind == NotificationGroupKindEnum.Role)
        {
            return new NotificationGroupAddMembersResponse
            {
                IsSuccess = false,
                StatusCode = 409,
                Message = "Nhóm theo vai trò tự suy ra thành viên — không thêm tay được.",
            };
        }

        // Gộp id trùng NGAY TRONG payload: client gửi cùng một người hai lần thì hai dòng INSERT sẽ
        // đụng ux_notification_group_members_pair và làm hỏng cả transaction.
        var requested = request.UserIds.Distinct().ToList();

        // Chỉ nhận id có thật trong read-model. Không lọc IsActive ở đây: thêm người đang tạm khoá
        // vào nhóm là hợp lệ, họ chỉ không được tính lúc gửi.
        var known = await _unitOfWork.Accounts.GetAllAsync(false)
            .Where(a => !a.IsDeleted && requested.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var knownSet = known.ToHashSet();

        // Dòng thành viên hiện có, KỂ CẢ đã xoá mềm: unique index lọc is_deleted nên dòng đã xoá
        // không chặn INSERT mới, nhưng hồi sinh dòng cũ rẻ hơn và giữ được lịch sử CreatedAt.
        var existing = await _unitOfWork.NotificationGroupMembers.GetAllAsync()
            .Where(m => m.GroupId == group.Id && requested.Contains(m.UserId))
            .ToListAsync(cancellationToken);

        var existingActive = existing.Where(m => !m.IsDeleted).Select(m => m.UserId).ToHashSet();
        var revivable = existing.Where(m => m.IsDeleted).ToDictionary(m => m.UserId);

        var toAdd = new List<Guid>();
        foreach (var userId in requested)
        {
            if (!knownSet.Contains(userId)) continue;
            if (existingActive.Contains(userId)) continue;
            toAdd.Add(userId);
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var userId in toAdd)
            {
                if (revivable.TryGetValue(userId, out var previous))
                {
                    previous.IsDeleted = false;
                    previous.DeletedAt = null;
                    previous.AddedBy = request.ActorUserId;
                    _unitOfWork.NotificationGroupMembers.UpdateAsync(previous);  // VOID — không await
                }
                else
                {
                    await _unitOfWork.NotificationGroupMembers.AddAsync(new NotificationGroupMember
                    {
                        Id = Guid.NewGuid(),
                        GroupId = group.Id,
                        UserId = userId,
                        AddedBy = request.ActorUserId,
                    });
                }
            }

            if (toAdd.Count > 0)
            {
                await _auditWriter.WriteAsync(
                    NotificationAuditActionEnum.GroupMembersAdded,
                    group.Id,
                    request.ActorUserId,
                    isSuccess: true,
                    reason: "Thêm thành viên vào nhóm",
                    metadata: new Dictionary<string, object?>
                    {
                        ["groupName"] = group.Name,
                        ["added"] = toAdd.Count,
                        ["userIds"] = toAdd.Select(id => id.ToString()).ToArray(),
                    },
                    ct: cancellationToken);
            }

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Thêm thành viên vào nhóm {Id} thất bại.", group.Id);

            return new NotificationGroupAddMembersResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Không thêm được thành viên.",
            };
        }

        var memberCount = await NotificationGroupMembership.CountRecipientsAsync(
            _unitOfWork, group, cancellationToken);

        var alreadyMembers = requested.Count(id => existingActive.Contains(id));
        var unknown = requested.Count - knownSet.Count;

        return new NotificationGroupAddMembersResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = BuildMessage(toAdd.Count, alreadyMembers, unknown),
            Data = new NotificationGroupAddMembersDto
            {
                Added = toAdd.Count,
                AlreadyMembers = alreadyMembers,
                UnknownAccounts = unknown,
                MemberCount = memberCount,
            },
        };
    }

    /// <summary>
    /// Nói rõ cái gì bị bỏ qua. Im lặng báo "đã thêm" trong khi bỏ qua 5 người là cách nhanh nhất để
    /// admin tưởng nhóm đã đủ người rồi gửi thiếu.
    /// </summary>
    private static string BuildMessage(int added, int alreadyMembers, int unknown)
    {
        var parts = new List<string> { $"Đã thêm {added} người" };
        if (alreadyMembers > 0) parts.Add($"{alreadyMembers} người đã có sẵn trong nhóm");
        if (unknown > 0) parts.Add($"{unknown} tài khoản không tồn tại");
        return string.Join(", ", parts) + ".";
    }
}
