using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Entities;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Services;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Audit;
using SharedKernels.Interfaces;

namespace NotificationService.UnitTests.Services;

/// <summary>
/// Sprint 6.2 NOTI-13 (#684) — trước sprint này bảng <c>notification_audit_logs</c> /
/// <c>notification_audit_outbox</c> chưa từng có một dòng nào, relay poll bảng rỗng 2s/lần.
/// </summary>
public class NotificationAuditWriterTests
{
    private static (NotificationAuditWriter sut,
                    List<NotificationAuditLog> logs,
                    List<NotificationAuditOutbox> outboxes,
                    Mock<INotificationUnitOfWork> uow) Build()
    {
        var logs = new List<NotificationAuditLog>();
        var outboxes = new List<NotificationAuditOutbox>();

        var (uow, _, _) = MockNotificationUnitOfWork.Build();

        var logRepo = new Mock<IGenericRepository<NotificationAuditLog>>();
        logRepo.Setup(r => r.AddAsync(It.IsAny<NotificationAuditLog>()))
               .Callback<NotificationAuditLog>(logs.Add)
               .Returns(Task.CompletedTask);

        var outboxRepo = new Mock<IGenericRepository<NotificationAuditOutbox>>();
        outboxRepo.Setup(r => r.AddAsync(It.IsAny<NotificationAuditOutbox>()))
                  .Callback<NotificationAuditOutbox>(outboxes.Add)
                  .Returns(Task.CompletedTask);

        uow.SetupGet(u => u.NotificationAuditLogs).Returns(logRepo.Object);
        uow.SetupGet(u => u.NotificationAuditOutboxes).Returns(outboxRepo.Object);

        var sut = new NotificationAuditWriter(uow.Object, NullLogger<NotificationAuditWriter>.Instance);
        return (sut, logs, outboxes, uow);
    }

    [Fact]
    public async Task WriteAsync_PushSent_WritesLogAndOutbox_WithSharedEventId()
    {
        var (sut, logs, outboxes, uow) = Build();
        var notificationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await sut.WriteAsync(
            NotificationAuditActionEnum.PushSent, notificationId, userId, isSuccess: true,
            metadata: new Dictionary<string, object?> { ["channel"] = "Push" });

        logs.Should().ContainSingle();
        outboxes.Should().ContainSingle();

        var log = logs[0];
        log.ActionCode.Should().Be(ActionCodes.Notification.PushSent);
        log.ActionCategory.Should().Be(AuditCategories.Communication);
        log.Severity.Should().Be(Severities.Info);
        log.TargetType.Should().Be(TargetTypes.Notification);
        log.TargetId.Should().Be(notificationId);
        log.ActorAccountId.Should().Be(userId);
        log.IsSuccess.Should().BeTrue();
        log.ErrorCode.Should().BeNull();
        log.MetadataJson.Should().Contain("Push");

        // Cùng EventId để aggregator dedup được giữa log và outbox.
        outboxes[0].EventId.Should().Be(log.EventId);
        outboxes[0].Status.Should().Be(AuditOutboxStatusEnum.Pending);
        outboxes[0].Payload.Should().Contain(notificationId.ToString());

        // KHÔNG tự SaveChanges — caller commit atomic cùng thay đổi nghiệp vụ.
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteAsync_PushFailed_MarksWarningSeverity_AndErrorCode()
    {
        var (sut, logs, _, _) = Build();

        await sut.WriteAsync(
            NotificationAuditActionEnum.PushFailed, Guid.NewGuid(), Guid.NewGuid(),
            isSuccess: false, reason: "DeviceNotRegistered");

        logs[0].Severity.Should().Be(Severities.Warning);
        logs[0].IsSuccess.Should().BeFalse();
        logs[0].ErrorCode.Should().Be(ActionCodes.Notification.PushFailed);
        logs[0].Reason.Should().Be("DeviceNotRegistered");
    }

    [Fact]
    public async Task WriteAsync_InAppRead_MapsToInAppReadActionCode()
    {
        var (sut, logs, _, _) = Build();

        await sut.WriteAsync(NotificationAuditActionEnum.InAppRead, Guid.NewGuid(), Guid.NewGuid(), true);

        logs[0].ActionCode.Should().Be(ActionCodes.Notification.InAppRead);
    }

    [Fact]
    public async Task WriteAsync_EmptyUserId_LeavesActorNull()
    {
        var (sut, logs, _, _) = Build();

        await sut.WriteAsync(NotificationAuditActionEnum.InAppCreated, Guid.NewGuid(), Guid.Empty, true);

        logs[0].ActorAccountId.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_LongReason_IsTruncated()
    {
        var (sut, logs, _, _) = Build();

        await sut.WriteAsync(
            NotificationAuditActionEnum.PushFailed, Guid.NewGuid(), Guid.NewGuid(),
            isSuccess: false, reason: new string('x', 900));

        logs[0].Reason!.Length.Should().Be(500);
    }

    /// <summary>Lỗi ghi audit KHÔNG được ném ra ngoài — audit hỏng không được chặn việc gửi notification.</summary>
    [Fact]
    public async Task WriteAsync_WhenRepositoryThrows_DoesNotPropagate()
    {
        var (uow, _, _) = MockNotificationUnitOfWork.Build();
        var logRepo = new Mock<IGenericRepository<NotificationAuditLog>>();
        logRepo.Setup(r => r.AddAsync(It.IsAny<NotificationAuditLog>()))
               .ThrowsAsync(new InvalidOperationException("db down"));
        uow.SetupGet(u => u.NotificationAuditLogs).Returns(logRepo.Object);

        var sut = new NotificationAuditWriter(uow.Object, NullLogger<NotificationAuditWriter>.Instance);

        var act = async () => await sut.WriteAsync(
            NotificationAuditActionEnum.PushSent, Guid.NewGuid(), Guid.NewGuid(), true);

        await act.Should().NotThrowAsync();
    }
}
