using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using SharedContracts.Events.Root;

namespace NotificationService.Application.Consumers;

/// <summary>
/// GH-107 — Helper ghi Notification record TRỰC TIẾP qua <see cref="INotificationUnitOfWork"/>,
/// bỏ qua <c>CreateNotificationCommand</c>/ValidationBehavior (vốn reject recipient placeholder
/// <see cref="System.Guid.Empty"/>). Consumer tạo notification cho recipient chưa-resolve (broadcast,
/// dispatcher fan-out Sprint 6) hoặc recipient thật (StaffId/CustomerId/AccountId) đều dùng helper này.
///
/// Lỗi DB sẽ propagate → MassTransit auto-retry (KHÔNG nuốt exception).
/// </summary>
internal static class NotificationWriter
{
    /// <summary>Channel mặc định cho notification hướng user (in-app list + push device).</summary>
    public static readonly NotificationChannelEnum[] InAppPush =
    {
        NotificationChannelEnum.InApp,
        NotificationChannelEnum.Push
    };

    /// <summary>Chỉ in-app (vd welcome, không cần push).</summary>
    public static readonly NotificationChannelEnum[] InAppOnly =
    {
        NotificationChannelEnum.InApp
    };

    /// <summary>
    /// Sprint Bonus NS-14 (#658) — cảnh báo an toàn cấp cao (vd cascade risk): in-app + push + email.
    /// </summary>
    public static readonly NotificationChannelEnum[] InAppPushEmail =
    {
        NotificationChannelEnum.InApp,
        NotificationChannelEnum.Push,
        NotificationChannelEnum.Email
    };

    /// <summary>
    /// Sprint 6.2 NOTI-08 (#679) — cảnh báo Critical hướng Customer: đủ 4 kênh theo spec §3.4 T#13.
    /// Preference/quiet hours lọc lại ở <c>NotificationDispatcher</c>, nên bật đủ kênh ở đây là
    /// "ý định gửi" chứ không phải ép gửi.
    /// </summary>
    public static readonly NotificationChannelEnum[] AllChannels =
    {
        NotificationChannelEnum.InApp,
        NotificationChannelEnum.Push,
        NotificationChannelEnum.Email,
        NotificationChannelEnum.Sms
    };

    /// <summary>
    /// Ghi notification cho từng (người nhận × kênh).
    ///
    /// <para><b>Sprint 6.4 NOTI4-08 — <paramref name="batchId"/> là tham số TUỲ CHỌN.</b> Để tuỳ
    /// chọn chứ không bắt buộc là có chủ đích: 20 lời gọi ở 13 file consumer vẫn hợp lệ nguyên vẹn,
    /// nên chuyển sang mô hình "lần gửi" làm được từng consumer một thay vì phải sửa cả 13 file
    /// trong một PR rồi không ai review nổi.</para>
    ///
    /// <para>Truyền <paramref name="batchId"/> thì các dòng sinh ra gom được thành một lần gửi và
    /// thống kê được; để trống thì hành vi y hệt trước đây.</para>
    /// </summary>
    public static async Task WriteAsync(
        INotificationUnitOfWork unitOfWork,
        IReadOnlyCollection<Guid> recipientIds,
        NotificationTypeEnum type,
        IReadOnlyCollection<NotificationChannelEnum> channels,
        string title,
        string body,
        string? payloadJson,
        string entityType,
        Guid? entityId,
        CancellationToken cancellationToken,
        Guid? batchId = null)
    {
        foreach (var userId in recipientIds)
        {
            foreach (var channel in channels)
            {
                await unitOfWork.Notifications.AddAsync(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BatchId = batchId,
                    Type = type,
                    Channel = channel,
                    Status = NotificationStatusEnum.Pending,
                    Title = title,
                    Body = body,
                    PayloadJson = payloadJson,
                    EntityType = entityType,
                    EntityId = entityId
                });
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes notification rows with deterministic primary keys. Existing rows, including
    /// soft-deleted rows, count as already processed so a delayed broker redelivery cannot
    /// recreate a notification the user removed.
    /// </summary>
    public static async Task WriteIdempotentAsync(
        INotificationUnitOfWork unitOfWork,
        IReadOnlyCollection<Guid> recipientIds,
        NotificationTypeEnum type,
        IReadOnlyCollection<NotificationChannelEnum> channels,
        string title,
        string body,
        string? payloadJson,
        string entityType,
        Guid entityId,
        string businessKey,
        CancellationToken cancellationToken)
    {
        var added = false;
        foreach (var userId in recipientIds.Distinct())
        {
            foreach (var channel in channels.Distinct())
            {
                var notificationId = DeterministicEventId.From(
                    entityId,
                    $"notification:{businessKey}:{userId:N}:{(int)channel}");

                // GetById intentionally includes soft-deleted rows: the deterministic primary
                // key is a delivery receipt, not only an active-row lookup.
                if (await unitOfWork.Notifications.GetByIdAsync(notificationId) is not null)
                    continue;

                await unitOfWork.Notifications.AddAsync(new Notification
                {
                    Id = notificationId,
                    UserId = userId,
                    Type = type,
                    Channel = channel,
                    Status = NotificationStatusEnum.Pending,
                    Title = title,
                    Body = body,
                    PayloadJson = payloadJson,
                    EntityType = entityType,
                    EntityId = entityId
                });
                added = true;
            }
        }

        if (added)
            await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Sprint 6.4 NOTI4-08 — tạo bản ghi "lần gửi" cho một fan-out do sự kiện sinh ra, rồi ghi
    /// notification kèm khoá gom.
    ///
    /// <para>Dùng thay cho <see cref="WriteAsync"/> ở consumer nào muốn thống kê được. Không đổi
    /// hành vi gửi: vẫn là mỗi (người nhận × kênh) một dòng <c>Pending</c>, vẫn đi qua đúng đường
    /// giao cũ.</para>
    /// </summary>
    public static async Task WriteBatchedAsync(
        INotificationUnitOfWork unitOfWork,
        IReadOnlyCollection<Guid> recipientIds,
        NotificationTypeEnum type,
        IReadOnlyCollection<NotificationChannelEnum> channels,
        string title,
        string body,
        string? payloadJson,
        string entityType,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var distinctRecipients = recipientIds.Distinct().ToList();
        var distinctChannels = channels.Distinct().ToList();

        var batch = new NotificationBatch
        {
            Id = Guid.NewGuid(),
            Type = type,
            Title = title,
            Body = body,
            PayloadJson = payloadJson,
            EntityType = entityType,
            EntityId = entityId,
            Channels = distinctChannels.ToArray(),
            Source = NotificationBatchSourceEnum.Event,
            Status = NotificationBatchStatusEnum.FannedOut,
            RecipientCount = distinctRecipients.Count,
            NotificationCount = distinctRecipients.Count * distinctChannels.Count,
        };

        await unitOfWork.NotificationBatches.AddAsync(batch);

        await WriteAsync(
            unitOfWork, distinctRecipients, type, distinctChannels,
            title, body, payloadJson, entityType, entityId, cancellationToken, batch.Id);
    }
}
