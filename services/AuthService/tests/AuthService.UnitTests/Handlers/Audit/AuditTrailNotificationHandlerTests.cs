using AuthService.Application.CQRS.Handler.Audit;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.UnitTests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SharedInfrastructure.Services;

namespace AuthService.UnitTests.Handlers.Audit;

public class AuditTrailNotificationHandlerTests
{
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private AuditTrailNotificationHandler CreateHandler(
        Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>? auditLogsMock = null,
        string? currentUserId = null)
    {
        var (uow, _, _, _, _) = MockUnitOfWork.Build();
        if (auditLogsMock != null)
            uow.SetupGet(u => u.AuditLogs).Returns(auditLogsMock.Object);

        _currentUser.SetupGet(c => c.UserId).Returns(currentUserId);

        return new AuditTrailNotificationHandler(
            uow.Object,
            _currentUser.Object,
            NullLogger<AuditTrailNotificationHandler>.Instance,
            httpContextAccessor: null);
    }

    [Fact]
    public async Task Handle_AppendsEntry_WithCoreFields()
    {
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs);
        var targetId = Guid.NewGuid();
        var notification = new AuditTrailNotification(
            AuditActionEnum.LoginSuccess,
            targetId,
            IsSuccess: true,
            TargetEmail: "user@example.com");

        await handler.Handle(notification, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Action.Should().Be(AuditActionEnum.LoginSuccess);
        captured.TargetAccountId.Should().Be(targetId);
        captured.TargetEmail.Should().Be("user@example.com");
        captured.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ResolvesActor_FromCurrentUserService_WhenNoOverride()
    {
        var actorId = Guid.NewGuid();
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs, currentUserId: actorId.ToString());
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.PasswordChanged, Guid.NewGuid(), IsSuccess: true),
            CancellationToken.None);

        captured!.ActorAccountId.Should().Be(actorId);
    }

    [Fact]
    public async Task Handle_OverrideActor_TakesPrecedence_OverCurrentUser()
    {
        var currentUserId = Guid.NewGuid();
        var overrideActor = Guid.NewGuid();
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs, currentUserId: currentUserId.ToString());
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.LoginSuccess, Guid.NewGuid(), IsSuccess: true,
            ActorAccountIdOverride: overrideActor),
            CancellationToken.None);

        captured!.ActorAccountId.Should().Be(overrideActor);
    }

    [Fact]
    public async Task Handle_AnonymousFlow_ActorIsNull_WhenCurrentUserIdInvalid()
    {
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs, currentUserId: null);
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.LoginFailedWrongPassword, TargetAccountId: null,
            IsSuccess: false, TargetEmail: "ghost@example.com"),
            CancellationToken.None);

        captured!.ActorAccountId.Should().BeNull();
        captured.TargetAccountId.Should().BeNull();
        captured.TargetEmail.Should().Be("ghost@example.com");
    }

    [Fact]
    public async Task Handle_SerializesMetadata_ToJson()
    {
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs);
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.AccountStatusChanged, Guid.NewGuid(), IsSuccess: true,
            Metadata: new Dictionary<string, object?>
            {
                ["oldStatus"] = 1,
                ["newStatus"] = 2,
                ["reason"] = "violation"
            }),
            CancellationToken.None);

        captured!.MetadataJson.Should().NotBeNullOrEmpty();
        captured.MetadataJson!.Should().Contain("\"oldStatus\":1");
        captured.MetadataJson.Should().Contain("\"newStatus\":2");
        captured.MetadataJson.Should().Contain("\"reason\":\"violation\"");
    }

    [Fact]
    public async Task Handle_TruncatesReason_To500Chars()
    {
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs);
        var longReason = new string('x', 1000);
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.LoginFailedWrongPassword, Guid.NewGuid(), IsSuccess: false,
            Reason: longReason),
            CancellationToken.None);

        captured!.Reason!.Length.Should().Be(500);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenRepositoryFails()
    {
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var handler = CreateHandler(auditLogs);

        // Audit failure KHÔNG được phá vỡ business flow → must not throw.
        var action = async () => await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.LoginSuccess, Guid.NewGuid(), IsSuccess: true),
            CancellationToken.None);

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_NullMetadata_DoesNotSerialize()
    {
        AuditLog? captured = null;
        var auditLogs = new Mock<SharedKernels.Interfaces.IGenericRepository<AuditLog>>();
        auditLogs.Setup(r => r.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(e => captured = e)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler(auditLogs);
        await handler.Handle(new AuditTrailNotification(
            AuditActionEnum.Logout, Guid.NewGuid(), IsSuccess: true),
            CancellationToken.None);

        captured!.MetadataJson.Should().BeNull();
    }
}
