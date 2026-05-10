using System.Text.Json;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Implements.Services;
using AuthService.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events;
using SharedInfrastructure.Persistence.Interceptors;

namespace AuthService.UnitTests.Infrastructure.Outbox;

public class OutboxMessagePublisherTests
{
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"outbox-test-{Guid.NewGuid()}")
            .Options;
        var interceptor = new AuditableEntityInterceptor(new NullUserService());
        return new ApplicationDbContext(options, interceptor);
    }

    [Fact]
    public async Task PublishAsync_AddsOutboxMessage_ToDbContext()
    {
        var db = CreateInMemoryDb();
        var publisher = new OutboxMessagePublisher(db);

        var evt = new SendOtpRegisterEvent("user@example.com", "123456");

        await publisher.PublishAsync(evt);

        // Chưa SaveChanges nhưng entity đã được tracked
        db.ChangeTracker.Entries<OutboxMessage>().Should().HaveCount(1);

        await db.SaveChangesAsync();

        var saved = await db.OutboxMessages.SingleAsync();
        saved.EventType.Should().Contain("SendOtpRegisterEvent");
        saved.Payload.Should().Contain("user@example.com");
        saved.Payload.Should().Contain("123456");
        saved.ProcessedAt.Should().BeNull();
        saved.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_DoesNotCommit_SaveChangesAsync_NotCalledByPublisher()
    {
        var db = CreateInMemoryDb();
        var publisher = new OutboxMessagePublisher(db);

        var evt = new SendOtpRegisterEvent("user@example.com", "123456");

        await publisher.PublishAsync(evt);

        // Verify chưa commit — entity vẫn ở Added state
        var entry = db.ChangeTracker.Entries<OutboxMessage>().Single();
        entry.State.Should().Be(EntityState.Added);
    }

    [Fact]
    public async Task PublishAsync_PayloadDeserializableBackToOriginalEvent()
    {
        var db = CreateInMemoryDb();
        var publisher = new OutboxMessagePublisher(db);

        var original = new SendOtpRegisterEvent("test@a.com", "999000");
        await publisher.PublishAsync(original);
        await db.SaveChangesAsync();

        var saved = await db.OutboxMessages.SingleAsync();
        var deserialized = JsonSerializer.Deserialize<SendOtpRegisterEvent>(saved.Payload);

        deserialized.Should().NotBeNull();
        deserialized!.ToEmail.Should().Be("test@a.com");
        deserialized.Otp.Should().Be("999000");
    }

    [Fact]
    public async Task PublishAsync_OccurredAt_TakenFromEvent_NotNow()
    {
        var db = CreateInMemoryDb();
        var publisher = new OutboxMessagePublisher(db);

        var evt = new SendOtpRegisterEvent("a@b.c", "111111");
        var eventTime = evt.OccurredAt;

        await publisher.PublishAsync(evt);
        await db.SaveChangesAsync();

        var saved = await db.OutboxMessages.SingleAsync();
        saved.OccurredAt.Should().Be(eventTime);
    }

    [Fact]
    public async Task PublishAsync_MultipleEvents_AllAdded()
    {
        var db = CreateInMemoryDb();
        var publisher = new OutboxMessagePublisher(db);

        await publisher.PublishAsync(new SendOtpRegisterEvent("a@b.c", "111"));
        await publisher.PublishAsync(new SendPasswordResetOtpEvent("c@d.e", "222"));
        await publisher.PublishAsync(new SendPhoneOtpEvent("0901234567", "333"));
        await db.SaveChangesAsync();

        var rows = await db.OutboxMessages.OrderBy(o => o.OccurredAt).ToListAsync();
        rows.Should().HaveCount(3);
        rows[0].EventType.Should().Contain("SendOtpRegisterEvent");
        rows[1].EventType.Should().Contain("SendPasswordResetOtpEvent");
        rows[2].EventType.Should().Contain("SendPhoneOtpEvent");
    }
}
