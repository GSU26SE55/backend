using AuthService.Application.CQRS.Notification.Audit;
using MediatR;

namespace AuthService.UnitTests.Helpers;

/// <summary>
/// Helper tạo <see cref="IPublisher"/> mock đã setup để các call .Publish trả Task.CompletedTask
/// (Moq default cho Task return là null → NullReferenceException khi await).
///
/// Verify ví dụ:
/// <code>
/// publisher.Verify(p =&gt; p.Publish(
///     It.Is&lt;AuditTrailNotification&gt;(n =&gt; n.Action == AuditActionEnum.LoginSuccess),
///     It.IsAny&lt;CancellationToken&gt;()), Times.Once);
/// </code>
/// </summary>
public static class MockPublisher
{
    public static Mock<IPublisher> NoOp()
    {
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(p => p.Publish(It.IsAny<AuditTrailNotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher
            .Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return publisher;
    }
}
