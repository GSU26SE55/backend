using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.Application.CQRS.Handler.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-07 — nở người nhận rồi sinh thông báo cho một lần gửi hàng loạt.
///
/// <para><b>Ba chi tiết bắt buộc ở bước nở, thiếu cái nào cũng thành lỗi thật:</b>
/// <list type="number">
/// <item><b>Gom trùng</b> — người ở hai nhóm cùng được nhắm chỉ nhận MỘT lần. Lớp thứ nhất là
/// <c>HashSet</c> ở đây; lớp thứ hai là unique index <c>ux_notifications_batch_user_channel</c>.</item>
/// <item><b>Lọc người còn hoạt động</b> — bằng JOIN sang read-model tài khoản. Chỉ đúng được nhờ
/// bản vá NOTI4-00; trước 02/08/2026 cột <c>is_active</c> không bao giờ được cập nhật.</item>
/// <item><b>Tập rỗng thì trả 400 tường minh</b>, KHÔNG tạo lần gửi mồ côi và KHÔNG ghi log rồi lặng
/// lẽ trả về — đó đúng là cách lỗi NOTI4-00 ẩn mình suốt một thời gian dài.</item>
/// </list></para>
///
/// <para><b>Nở đồng bộ trong handler</b> (§17.6.4.5 fork 1). Quy mô hiện tại là hàng chục tài khoản
/// ⇒ vài chục dòng, một transaction là xong. Ngưỡng chuyển sang chạy nền: ~2.000 dòng hoặc phản hồi
/// vượt 2 giây; cột <c>Status</c>/<c>RecipientCount</c> đã chừa sẵn cho đúng bước đó nên chuyển
/// không phải đổi schema.</para>
/// </summary>
public class NotificationBroadcastCommandHandler
    : IRequestHandler<NotificationBroadcastCommand, NotificationBroadcastResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;
    private readonly INotificationAuditWriter _auditWriter;
    private readonly ILogger<NotificationBroadcastCommandHandler> _logger;

    public NotificationBroadcastCommandHandler(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        INotificationAuditWriter auditWriter,
        ILogger<NotificationBroadcastCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
        _auditWriter = auditWriter;
        _logger = logger;
    }

    public async Task<NotificationBroadcastResponse> Handle(
        NotificationBroadcastCommand request, CancellationToken cancellationToken)
    {
        var plan = await BroadcastPlanner.BuildAsync(
            _unitOfWork, _recipientResolver, request.GroupIds, request.UserIds, cancellationToken);

        if (plan.Recipients.Count == 0)
        {
            // KHÔNG tạo batch, KHÔNG mở transaction. Trả lỗi nói rõ vì sao rỗng để admin sửa được,
            // thay vì báo "đã gửi" rồi không ai nhận gì.
            _logger.LogWarning(
                "Gửi hàng loạt bị từ chối: không có người nhận hợp lệ. groups={Groups} users={Users} actor={Actor}",
                request.GroupIds.Count, request.UserIds.Count, request.ActorUserId);

            return new NotificationBroadcastResponse
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = plan.BuildEmptyReason(),
            };
        }

        var channels = request.Channels.Distinct().ToArray();
        var title = request.Title.Trim();
        var body = request.Body.Trim();

        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = request.Type,
            Title = title,
            Body = body,
            PayloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? null : request.PayloadJson,
            EntityType = string.IsNullOrWhiteSpace(request.EntityType) ? null : request.EntityType.Trim(),
            EntityId = request.EntityId,
            Channels = channels,
            Source = NotificationBatchSourceEnum.Manual,
            // 03/08/2026 — quyết định "có render qua mẫu không" ghi ngay lên lần gửi, vì dispatcher
            // xử lý từng dòng một và không nhìn thấy ý định của admin ở đâu khác.
            UseTemplate = request.UseTemplate,
            Status = NotificationBatchStatusEnum.Pending,
            RecipientCount = plan.Recipients.Count,
            NotificationCount = plan.Recipients.Count * channels.Length,
        };

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.NotificationBatches.AddAsync(batch);

            foreach (var groupId in plan.ResolvedGroupIds)
            {
                await _unitOfWork.NotificationBatchTargets.AddAsync(new NotificationBatchTarget
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    TargetKind = NotificationBatchTargetKindEnum.Group,
                    GroupId = groupId,
                });
            }

            foreach (var userId in plan.ResolvedUserIds)
            {
                await _unitOfWork.NotificationBatchTargets.AddAsync(new NotificationBatchTarget
                {
                    Id = Guid.NewGuid(),
                    BatchId = batch.Id,
                    TargetKind = NotificationBatchTargetKindEnum.User,
                    UserId = userId,
                });
            }

            // Một dòng cho mỗi (người nhận × kênh). Trạng thái Pending để
            // NotificationDispatchBackgroundService nhặt và giao xuống kênh — broadcast đi qua đúng
            // đường giao của mọi notification khác, nên vẫn tôn trọng tuỳ chọn người nhận và quiet
            // hours (§17.6.4.6 câu hỏi 5).
            foreach (var userId in plan.Recipients)
            {
                foreach (var channel in channels)
                {
                    await _unitOfWork.Notifications.AddAsync(new NotificationEntity
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        BatchId = batch.Id,
                        Type = batch.Type,
                        Channel = channel,
                        Status = NotificationStatusEnum.Pending,
                        Title = batch.Title,
                        Body = batch.Body,
                        PayloadJson = batch.PayloadJson,
                        EntityType = batch.EntityType,
                        EntityId = batch.EntityId,
                    });
                }
            }

            // Gán thẳng, KHÔNG gọi UpdateAsync: entity đang ở trạng thái Added, mà UpdateAsync sẽ
            // chuyển nó sang Modified ⇒ EF bỏ hẳn lệnh INSERT và cố UPDATE một dòng chưa tồn tại,
            // kéo theo target vi phạm khoá ngoại batch_id. EF đã theo dõi entity nên thay đổi thuộc
            // tính ở đây tự đi vào chính lệnh INSERT đó.
            //
            // Trạng thái Pending vì thế không bao giờ chạm DB ở đường đồng bộ — nó tồn tại sẵn cho
            // lúc chuyển fan-out sang chạy nền (§17.6.4.5 fork 1).
            batch.Status = NotificationBatchStatusEnum.FannedOut;

            await _auditWriter.WriteAsync(
                NotificationAuditActionEnum.BroadcastSent,
                batch.Id,
                request.ActorUserId,
                isSuccess: true,
                reason: "Send bulk notification",
                metadata: new Dictionary<string, object?>
                {
                    ["type"] = batch.Type.ToString(),
                    ["channels"] = channels.Select(c => c.ToString()).ToArray(),
                    ["groups"] = plan.ResolvedGroupIds.Count,
                    ["recipients"] = plan.Recipients.Count,
                    ["notifications"] = batch.NotificationCount,
                    ["skippedUsers"] = plan.SkippedUsers,
                },
                ct: cancellationToken);

            await _unitOfWork.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(
                ex, "Gửi hàng loạt thất bại (batch {BatchId}, {Recipients} người nhận).",
                batch.Id, plan.Recipients.Count);

            return new NotificationBroadcastResponse
            {
                IsSuccess = false,
                StatusCode = 500,
                Message = "Failed to send the notification.",
            };
        }

        _logger.LogInformation(
            "Đã gửi hàng loạt batch {BatchId}: {Recipients} người × {Channels} kênh = {Rows} dòng.",
            batch.Id, plan.Recipients.Count, channels.Length, batch.NotificationCount);

        return new NotificationBroadcastResponse
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = BuildSuccessMessage(plan.Recipients.Count, channels.Length, plan.SkippedUsers),
            Data = new NotificationBroadcastDto
            {
                BatchId = batch.Id,
                RecipientCount = batch.RecipientCount,
                NotificationCount = batch.NotificationCount,
                GroupCount = plan.ResolvedGroupIds.Count,
                SkippedUsers = plan.SkippedUsers,
            },
        };
    }

    private static string BuildSuccessMessage(int recipients, int channels, int skipped)
    {
        var message = $"Sent to {recipients} recipient(s) across {channels} channel(s).";
        if (skipped > 0)
            message += $" Skipped {skipped} invalid or inactive recipient(s).";
        return message;
    }
}

/// <summary>
/// Nở danh sách người nhận từ nhóm + cá nhân. Tách riêng vì <b>xem trước</b> và <b>gửi thật</b> bắt
/// buộc phải cho ra cùng một con số — nếu hai đường tự tính riêng thì admin sẽ thấy "12 người" rồi
/// gửi đi 9 người nhận, mà không có gì báo lỗi.
/// </summary>
internal sealed class BroadcastPlan
{
    public required IReadOnlyList<Guid> Recipients { get; init; }

    /// <summary>Nhóm được nhắm tới và thực sự tồn tại (đã loại nhóm đã xoá).</summary>
    public required IReadOnlyList<Guid> ResolvedGroupIds { get; init; }

    /// <summary>Cá nhân được nhắm tới và thực sự nhận được thông báo.</summary>
    public required IReadOnlyList<Guid> ResolvedUserIds { get; init; }

    /// <summary>Số nhóm được yêu cầu nhưng không tìm thấy.</summary>
    public required int MissingGroups { get; init; }

    /// <summary>Số cá nhân bị bỏ qua vì không tồn tại hoặc đang ngừng hoạt động.</summary>
    public required int SkippedUsers { get; init; }

    /// <summary>Tổng số người nếu cộng dồn từng nhóm mà không gom trùng.</summary>
    public required int RawCount { get; init; }

    /// <summary>Nói RÕ vì sao rỗng — "không có người nhận" trống trơn thì admin không biết sửa gì.</summary>
    public string BuildEmptyReason()
    {
        var reasons = new List<string>();
        if (MissingGroups > 0)
            reasons.Add($"{MissingGroups} group(s) not found or deleted");
        if (SkippedUsers > 0)
            reasons.Add($"{SkippedUsers} selected user(s) are inactive or do not exist");
        if (reasons.Count == 0)
            reasons.Add("the selected groups have no active members");

        return "No valid recipients: " + string.Join("; ", reasons) + ".";
    }
}

internal static class BroadcastPlanner
{
    public static async Task<BroadcastPlan> BuildAsync(
        INotificationUnitOfWork unitOfWork,
        IRecipientResolver recipientResolver,
        IReadOnlyCollection<Guid> groupIds,
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var requestedGroups = groupIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var requestedUsers = userIds.Where(id => id != Guid.Empty).Distinct().ToList();

        var existingGroups = requestedGroups.Count == 0
            ? new List<Guid>()
            : await unitOfWork.NotificationGroups.GetAllAsync(false)
                .Where(g => !g.IsDeleted && requestedGroups.Contains(g.Id))
                .Select(g => g.Id)
                .ToListAsync(cancellationToken);

        var fromGroups = await recipientResolver.GetGroupRecipientsAsync(existingGroups, cancellationToken);

        // Cá nhân chỉ định đích danh cũng phải qua đúng bộ lọc như thành viên nhóm — không có cửa
        // sau nào gửi được cho tài khoản đã nghỉ.
        var fromUsers = requestedUsers.Count == 0
            ? new List<Guid>()
            : await NotificationGroupMembership.NotifiableAccounts(unitOfWork)
                .Where(a => requestedUsers.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

        // RawCount tính TRƯỚC khi gom trùng, để giao diện giải thích được vì sao con số cuối nhỏ hơn
        // tổng mà người dùng nhẩm ra từ số thành viên từng nhóm.
        var rawCount = 0;
        if (existingGroups.Count > 0)
        {
            foreach (var groupId in existingGroups)
            {
                var single = await recipientResolver.GetGroupRecipientsAsync(
                    new[] { groupId }, cancellationToken);
                rawCount += single.Count;
            }
        }
        rawCount += fromUsers.Count;

        var recipients = new HashSet<Guid>(fromGroups);
        foreach (var id in fromUsers)
            recipients.Add(id);

        return new BroadcastPlan
        {
            Recipients = recipients.ToList(),
            ResolvedGroupIds = existingGroups,
            ResolvedUserIds = fromUsers,
            MissingGroups = requestedGroups.Count - existingGroups.Count,
            SkippedUsers = requestedUsers.Count - fromUsers.Count,
            RawCount = rawCount,
        };
    }
}
