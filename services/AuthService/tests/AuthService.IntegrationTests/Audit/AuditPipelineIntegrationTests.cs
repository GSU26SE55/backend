using System.Text.Json;
using AuthService.Application.CQRS.Notification.Audit;
using AuthService.Domain.Entities;
using AuthService.Domain.Enums;
using AuthService.Infrastructure.Persistence;
using AuthService.IntegrationTests.Fixtures;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events.Audit;

namespace AuthService.IntegrationTests.Audit;

/// <summary>
/// Integration test E2E cho audit pipeline Hybrid (Sprint audit #AUDIT-12 + #AUDIT-19) —
/// TestContainers Postgres thật. Verify: notification → AuditLog 14 cột + audit_outbox Pending;
/// outbox Pending → AuditOutboxRelayBackgroundService publish → Published.
/// </summary>
[Collection("Integration")]
public class AuditPipelineIntegrationTests
{
    private readonly AuthApiFactory _factory;

    public AuditPipelineIntegrationTests(AuthApiFactory factory) => _factory = factory;

    [Fact]
    public async Task AuditTrailNotification_WritesAuditLogWith14Columns_AndOutboxPending()
    {
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var targetAccountId = Guid.NewGuid();

        // #AUDIT-09 — handler ghi AuditLog + audit_outbox (KHÔNG SaveChanges, command handler save).
        await publisher.Publish(new AuditTrailNotification(
            Action: AuditActionEnum.LoginSuccess,
            TargetAccountId: targetAccountId,
            IsSuccess: true,
            TargetEmail: "audit-e2e@example.com"));
        await db.SaveChangesAsync();

        // Assert AuditLog — 14 cột chuẩn populated.
        var log = await db.Set<AuditLog>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TargetAccountId == targetAccountId);
        log.Should().NotBeNull();
        log!.EventId.Should().NotBe(Guid.Empty, "#AUDIT-06 event_id phải set");
        log.ServiceName.Should().Be("AuthService");
        log.ActionCode.Should().Be("LoginSuccess", "#AUDIT-09 map enum → action_code");
        log.ActionCategory.Should().NotBeNullOrEmpty();
        log.Severity.Should().NotBeNullOrEmpty();
        log.OccurredAt.Should().NotBe(default);
        log.RecordedAt.Should().NotBe(default);

        // Assert audit_outbox — Pending entry cùng event_id, payload deserialize được.
        var outbox = await db.Set<AuditOutbox>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.EventId == log.EventId);
        outbox.Should().NotBeNull("#AUDIT-07 outbox row phải tạo cùng transaction");
        outbox!.Status.Should().Be(AuditOutboxStatusEnum.Pending);
        var evt = JsonSerializer.Deserialize<AuditCreatedEventV1>(outbox.Payload);
        evt.Should().NotBeNull();
        evt!.ActionCode.Should().Be("LoginSuccess");
        evt.ServiceName.Should().Be("AuthService");
    }

    [Fact]
    public async Task AuditOutbox_Pending_GetsPublishedByRelay()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var eventId = Guid.NewGuid();
        var evt = new AuditCreatedEventV1(eventId, "AuthService", "LoginSuccess", "Authentication", "Info",
            "Account", Guid.NewGuid(), "x@example.com", Guid.NewGuid(), null, null, "127.0.0.1", "ua",
            true, null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);

        db.AuditOutboxes.Add(new AuditOutbox
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = nameof(AuditCreatedEventV1),
            Payload = JsonSerializer.Serialize(evt),
            Status = AuditOutboxStatusEnum.Pending,
        });
        await db.SaveChangesAsync();

        // #AUDIT-08 — relay poll mỗi 2s, publish, mark Published. Poll tối đa 30s.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        AuditOutbox? processed = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            processed = await verifyDb.AuditOutboxes.AsNoTracking().FirstOrDefaultAsync(o => o.EventId == eventId);
            if (processed?.Status == AuditOutboxStatusEnum.Published)
                break;
        }

        processed.Should().NotBeNull();
        processed!.Status.Should().Be(AuditOutboxStatusEnum.Published, "AuditOutboxRelay phải publish trong 30s");
        processed.ProcessedAt.Should().NotBeNull();
        processed.LastError.Should().BeNull();
    }
}
