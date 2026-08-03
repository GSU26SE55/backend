using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Events.Root;
using SmsService.Infrastructure.Implements.Services;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.Services;

/// <summary>
/// <see cref="OutboxMessagePublisher"/> — nửa đầu của Outbox Pattern (nửa sau là
/// <c>OutboxRelayBackgroundService</c>). Trước bộ test này phủ 0%.
///
/// <para><b>Điểm mấu chốt:</b> publisher <b>KHÔNG</b> gọi <c>SaveChangesAsync</c>. Nó chỉ
/// <c>Add</c> vào change-tracker rồi để handler lưu chung một lượt với dữ liệu nghiệp vụ. Chính
/// điều đó làm cho "ghi dữ liệu" và "phát event" trở thành một giao dịch duy nhất. Nếu ai đó thêm
/// <c>SaveChanges</c> vào đây thì event sẽ được lưu ngay cả khi giao dịch nghiệp vụ sau đó bị huỷ —
/// tức là phát ra event cho một việc chưa từng xảy ra. Test cuối cùng chốt đúng điều này.</para>
/// </summary>
[Collection(nameof(SmsDatabaseCollection))]
public class OutboxMessagePublisherTests : IAsyncLifetime
{
    private readonly SmsPostgresFixture _db;
    public OutboxMessagePublisherTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task PublishAsync_WritesRowWithResolvableTypeAndPayload()
    {
        var evt = new SampleSmsEvent { Note = "xin chao" };

        await using (var db = _db.NewContext())
        {
            await new OutboxMessagePublisher(db).PublishAsync(evt);
            await db.SaveChangesAsync();
        }

        await using var verify = _db.NewContext();
        var row = await verify.OutboxMessages.SingleAsync();

        // Relay phân giải kiểu bằng Type.GetType(EventType) — chuỗi lưu xuống PHẢI phân giải được,
        // nếu không event sẽ nằm chết trong bảng và chỉ lộ ra khi ai đó đọc log.
        Type.GetType(row.EventType).Should().Be(typeof(SampleSmsEvent),
            "EventType phải là AssemblyQualifiedName mà relay phân giải lại được");

        JsonSerializer.Deserialize<SampleSmsEvent>(row.Payload)!.Note.Should().Be("xin chao");
        row.ProcessedAt.Should().BeNull("vừa ghi thì chưa gửi");
        row.RetryCount.Should().Be(0);
        row.LastError.Should().BeNull();
        row.OccurredAt.Should().BeCloseTo(evt.OccurredAt, TimeSpan.FromSeconds(1),
            "mốc thời gian phải lấy từ chính event, không phải lúc ghi bảng");
    }

    [Fact]
    public async Task PublishAsync_MultipleEvents_WritesOneRowEach()
    {
        await using (var db = _db.NewContext())
        {
            var publisher = new OutboxMessagePublisher(db);
            await publisher.PublishAsync(new SampleSmsEvent { Note = "mot" });
            await publisher.PublishAsync(new SampleSmsEvent { Note = "hai" });
            await publisher.PublishAsync(new SampleSmsEvent { Note = "ba" });
            await db.SaveChangesAsync();
        }

        await using var verify = _db.NewContext();
        (await verify.OutboxMessages.CountAsync()).Should().Be(3);
    }

    /// <summary>
    /// Đây là lý do tồn tại của Outbox Pattern: giao dịch nghiệp vụ bị huỷ thì event cũng phải biến
    /// mất. Nếu publisher tự lưu, event vẫn còn và hệ thống sẽ thông báo về một việc chưa từng xảy ra.
    /// </summary>
    [Fact]
    public async Task PublishAsync_DoesNotSaveByItself_SoRollbackAlsoDiscardsTheEvent()
    {
        await using (var db = _db.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            await new OutboxMessagePublisher(db).PublishAsync(new SampleSmsEvent { Note = "se bi huy" });
            await db.SaveChangesAsync();

            await tx.RollbackAsync();
        }

        await using var verify = _db.NewContext();
        (await verify.OutboxMessages.CountAsync()).Should().Be(0,
            "huỷ giao dịch nghiệp vụ phải huỷ luôn event — đó chính là toàn bộ mục đích của Outbox Pattern");
    }

    [Fact]
    public async Task PublishAsync_BeforeSaveChanges_NothingIsPersistedYet()
    {
        await using var db = _db.NewContext();
        await new OutboxMessagePublisher(db).PublishAsync(new SampleSmsEvent { Note = "chua luu" });

        // Đọc bằng một kết nối khác: chưa SaveChanges thì bảng phải còn rỗng.
        await using var other = _db.NewContext();
        (await other.OutboxMessages.CountAsync()).Should().Be(0,
            "publisher chỉ được Add vào change-tracker, việc lưu là của handler");
    }
}

/// <summary>Event tối giản, phải là kiểu THẬT để <c>Type.GetType</c> phân giải lại được.</summary>
public record SampleSmsEvent : IntegrationEvent
{
    public string Note { get; set; } = string.Empty;
}
