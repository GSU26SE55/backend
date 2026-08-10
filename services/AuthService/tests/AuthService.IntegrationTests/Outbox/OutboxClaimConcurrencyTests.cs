using System.Text.Json;
using AuthService.Application.Interfaces.Services;
using AuthService.Domain.Entities;
using AuthService.Infrastructure.Persistence;
using AuthService.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedContracts.Events;

namespace AuthService.IntegrationTests.Outbox;

/// <summary>
/// GH-794 — hai relay chạy song song chỉ được publish mỗi dòng outbox đúng một lần.
/// </summary>
/// <remarks>
/// <para>
/// Relay trước đây chỉ lọc <c>ProcessedAt == null</c> rồi publish, mà <c>ProcessedAt</c> lại chỉ
/// được ghi SAU khi publish xong. Trong khoảng giữa hai việc đó, mọi replica khác vẫn thấy dòng này
/// "chưa xử lý" và cùng publish — người dùng nhận email/SMS hai lần, và bản ghi outbox không lưu
/// dấu vết nào của lần thứ hai.
/// </para>
/// <para>
/// Chạy trên Postgres THẬT vì tính nguyên tử nằm ở câu <c>UPDATE … WHERE</c>: mock hay EF InMemory
/// đều không có trọng tài nào để chọn người thắng, nên chúng không thể phát hiện chính lỗi này.
/// </para>
/// </remarks>
[Collection("Integration")]
public class OutboxClaimConcurrencyTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly AuthApiFactory _factory;

    public OutboxClaimConcurrencyTests(AuthApiFactory factory) => _factory = factory;

    private static OutboxMessage Row() => new()
    {
        Id = Guid.NewGuid(),
        EventType = typeof(SuspiciousLoginDetectedEvent).AssemblyQualifiedName!,
        Payload = JsonSerializer.Serialize(new SuspiciousLoginDetectedEvent(
            AccountId: Guid.NewGuid(),
            Email: "gh794@example.com",
            IpAddress: "10.0.0.1",
            UserAgent: "test-ua",
            Reason: "gh794",
            DetectedAt: DateTime.UtcNow)),
        OccurredAt = DateTime.UtcNow,
        ProcessedAt = null,
        RetryCount = 0,
    };

    private async Task<OutboxMessage> SeedAsync(Action<OutboxMessage>? mutate = null)
    {
        var row = Row();
        mutate?.Invoke(row);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OutboxMessages.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Mỗi "replica" phải có scope riêng — DbContext không dùng chung được giữa các luồng.</summary>
    private async Task<T> AsReplicaAsync<T>(Func<IOutboxClaimService, Task<T>> action)
    {
        using var scope = _factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IOutboxClaimService>());
    }

    private async Task<OutboxMessage?> ReadAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .OutboxMessages.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    }

    [Fact]
    public async Task EightRacingRelays_OnlyOneClaimsTheRow()
    {
        // Khẳng định trung tâm của issue.
        var row = await SeedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => AsReplicaAsync(c => c.TryClaimAsync(row.Id, $"relay-{i}", Lease))));

        results.Count(r => r is not null).Should().Be(1,
            "mỗi dòng outbox chỉ được đúng một relay publish");
    }

    [Fact]
    public async Task ClaimedRow_IsInvisibleToOtherRelays_UntilTheLeaseExpires()
    {
        var row = await SeedAsync();

        (await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease))).Should().NotBeNull();
        (await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-b", Lease))).Should().BeNull();
    }

    [Fact]
    public async Task ExpiredLease_IsReclaimable_SoACrashedRelayDoesNotBlockTheRowForever()
    {
        // Không có phần này thì một instance chết giữa chừng sẽ khoá vĩnh viễn một message chưa ai
        // gửi — im lặng, không lỗi, và người dùng đơn giản là không nhận được gì.
        var row = await SeedAsync(r =>
        {
            r.LeaseOwner = "relay-da-chet";
            r.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        });

        (await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-moi", Lease))).Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessedRow_CannotBeClaimedAgain()
    {
        var row = await SeedAsync(r => r.ProcessedAt = DateTime.UtcNow);

        (await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease))).Should().BeNull();
    }

    [Fact]
    public async Task MarkProcessed_IsRefusedToNonOwners()
    {
        // Một instance đã treo quá lâu và mất quyền không được đánh dấu "xong" thay cho chủ đang giữ:
        // làm vậy là ghi nhận một lần publish có thể chưa từng xảy ra, và message biến mất khỏi hàng đợi.
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkProcessedAsync(row.Id, "relay-b"))).Should().BeFalse();
        (await ReadAsync(row.Id))!.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkProcessed_ByOwner_ClearsTheLease()
    {
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkProcessedAsync(row.Id, "relay-a"))).Should().BeTrue();

        var saved = await ReadAsync(row.Id);
        saved!.ProcessedAt.Should().NotBeNull();
        saved.LeaseOwner.Should().BeNull("xong việc thì phải nhả quyền, không giữ đến hết hạn");
        saved.LeaseUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailed_ByOwner_IncrementsRetry_AndReleasesTheRow()
    {
        // Thất bại phải trả dòng về hàng đợi ngay, không đợi hết hạn quyền — nếu không, mỗi lần lỗi
        // là message nằm im thêm trọn thời hạn lease.
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkFailedAsync(row.Id, "relay-a", "broker down"))).Should().BeTrue();

        var saved = await ReadAsync(row.Id);
        saved!.RetryCount.Should().Be(1);
        saved.LastError.Should().Be("broker down");
        saved.ProcessedAt.Should().BeNull();
        saved.LeaseOwner.Should().BeNull();
        (await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-b", Lease))).Should().NotBeNull();
    }

    [Fact]
    public async Task MarkFailed_IsRefusedToNonOwners()
    {
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkFailedAsync(row.Id, "relay-b", "khong phai viec cua toi")))
            .Should().BeFalse();
        (await ReadAsync(row.Id))!.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RowHeldByAnotherRelay_IsNotPublishedByTheRunningRelay()
    {
        // Đây là phép kiểm ở tầng relay THẬT (nền đang chạy trong factory), không phải chỉ tầng
        // claim service: dòng đang được người khác giữ phải nằm yên.
        var row = await SeedAsync(r =>
        {
            r.LeaseOwner = "relay-khac";
            r.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(5);
        });

        // Relay poll mỗi ~5s; chờ đủ vài nhịp.
        await Task.Delay(TimeSpan.FromSeconds(12));

        var saved = await ReadAsync(row.Id);
        saved!.ProcessedAt.Should().BeNull("dòng đang có chủ thì relay này phải bỏ qua");
        saved.LeaseOwner.Should().Be("relay-khac", "không được cướp quyền của relay khác");
    }
}
