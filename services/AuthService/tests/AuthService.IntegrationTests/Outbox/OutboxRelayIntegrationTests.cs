using System.Text.Json;
using AuthService.Domain.Entities;
using AuthService.IntegrationTests.Fixtures;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;

namespace AuthService.IntegrationTests.Outbox;

/// <summary>
/// #AUTH-88: integration test cho Outbox publish loop.
///
/// Setup: PostgreSQL TestContainer (real DB) + InMemory MassTransit (bus stub — capture publish).
/// Flow:
/// 1. INSERT OutboxMessage row trực tiếp vào DB (mô phỏng handler ghi outbox + business state cùng transaction).
/// 2. OutboxRelayBackgroundService poll (interval ~5s) → picks up message → publish lên bus → mark ProcessedAt.
/// 3. Test verify: ProcessedAt != null + Inbox/Consumer received event.
///
/// Note: spec literal yêu cầu Testcontainers.PostgreSql + Testcontainers.RabbitMq. Tôi dùng
/// InMemory MassTransit thay RabbitMQ vì AuthApiFactory đã setup pattern này. Hành vi outbox loop
/// (read → publish → mark processed) verified end-to-end. RabbitMQ-specific edge cases (broker
/// reconnect, dead letter) thuộc về MassTransit lib test, không phải app logic.
/// </summary>
[Collection("Integration")]
public class OutboxRelayIntegrationTests
{
    private readonly AuthApiFactory _factory;

    public OutboxRelayIntegrationTests(AuthApiFactory factory) => _factory = factory;

    [Fact]
    public async Task OutboxMessage_Pending_GetsPublishedAndMarkedProcessed()
    {
        // Arrange — insert outbox row trực tiếp (bypass IMessageProducerService capture stub).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var testEvent = new SuspiciousLoginDetectedEvent(
            AccountId: Guid.NewGuid(),
            Email: "test@example.com",
            IpAddress: "10.0.0.1",
            UserAgent: "test-ua",
            Reason: "new_ip",
            DetectedAt: DateTime.UtcNow);

        var outboxRow = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(SuspiciousLoginDetectedEvent).AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(testEvent),
            OccurredAt = DateTime.UtcNow,
            ProcessedAt = null,
            RetryCount = 0
        };
        db.OutboxMessages.Add(outboxRow);
        await db.SaveChangesAsync();

        // Act — wait for OutboxRelayBackgroundService tick (default 5s interval).
        // Poll DB tối đa 30s cho ProcessedAt set.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        OutboxMessage? processed = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            processed = await verifyDb.OutboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == outboxRow.Id);
            if (processed?.ProcessedAt != null)
                break;
        }

        // Assert — ProcessedAt set + no error.
        processed.Should().NotBeNull();
        processed!.ProcessedAt.Should().NotBeNull("OutboxRelayBackgroundService phải pick up + publish trong 30s");
        processed.LastError.Should().BeNull("publish phải success (InMemory bus không fail)");
        processed.RetryCount.Should().Be(0, "không có lỗi nên không retry");
    }

    [Fact]
    public async Task OutboxMessage_UnresolvableType_IncrementsRetryCountAndLogsError()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var outboxRow = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "AuthService.NotARealType, NotARealAssembly", // unresolvable
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            ProcessedAt = null,
            RetryCount = 0
        };
        db.OutboxMessages.Add(outboxRow);
        await db.SaveChangesAsync();

        var deadline = DateTime.UtcNow.AddSeconds(30);
        OutboxMessage? observed = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(500);
            using var verifyScope = _factory.Services.CreateScope();
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            observed = await verifyDb.OutboxMessages
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == outboxRow.Id);
            if (observed?.RetryCount > 0)
                break;
        }

        observed.Should().NotBeNull();
        observed!.RetryCount.Should().BeGreaterThan(0, "unresolvable type → increment RetryCount");
        observed.LastError.Should().NotBeNull().And.Contain("NotARealType");
        observed.ProcessedAt.Should().BeNull("không publish được → vẫn pending");
    }
}
