using Microsoft.EntityFrameworkCore;
using SmsService.Domain.Entities;
using SmsService.Infrastructure.Implements.Services;
using SmsService.IntegrationTests.Fixtures;

namespace SmsService.IntegrationTests.Outbox;

/// <summary>
/// GH-794 — hai relay chạy song song chỉ được publish mỗi dòng outbox đúng một lần.
/// </summary>
/// <remarks>
/// <para>
/// Relay trước đây chỉ lọc <c>ProcessedAt == null</c> rồi publish, mà <c>ProcessedAt</c> lại chỉ
/// được ghi SAU khi publish xong. Trong khoảng giữa hai việc đó, replica khác vẫn thấy dòng này
/// "chưa xử lý" và cùng publish. Với SMS, mỗi lần trùng là một tin nhắn tính phí gửi thêm cho
/// người dùng — không thu hồi được.
/// </para>
/// <para>
/// Chạy trên Postgres THẬT vì tính nguyên tử nằm ở câu <c>UPDATE … WHERE</c>: mock hay EF InMemory
/// không có trọng tài nào để chọn người thắng, nên chúng không thể phát hiện chính lỗi này.
/// </para>
/// </remarks>
[Collection(nameof(SmsDatabaseCollection))]
public class OutboxClaimConcurrencyTests : IAsyncLifetime
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly SmsPostgresFixture _db;

    public OutboxClaimConcurrencyTests(SmsPostgresFixture db) => _db = db;

    public Task InitializeAsync() => _db.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<OutboxMessage> SeedAsync(Action<OutboxMessage>? mutate = null)
    {
        var row = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = "SharedContracts.Events.SendSmsCommand, SharedContracts",
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            ProcessedAt = null,
            RetryCount = 0,
        };
        mutate?.Invoke(row);

        await using var db = _db.NewContext();
        db.OutboxMessages.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Mỗi "replica" một DbContext riêng — DbContext không dùng chung được giữa các luồng.</summary>
    private async Task<T> AsReplicaAsync<T>(Func<OutboxClaimService, Task<T>> action)
    {
        await using var db = _db.NewContext();
        return await action(new OutboxClaimService(db));
    }

    private async Task<OutboxMessage?> ReadAsync(Guid id)
    {
        await using var db = _db.NewContext();
        return await db.OutboxMessages.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
    }

    [Fact]
    public async Task EightRacingRelays_OnlyOneClaimsTheRow()
    {
        var row = await SeedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => AsReplicaAsync(c => c.TryClaimAsync(row.Id, $"relay-{i}", Lease))));

        results.Count(r => r is not null).Should().Be(1,
            "mỗi dòng outbox chỉ được đúng một relay gửi — SMS trùng là tin tính phí gửi thêm");
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
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkProcessedAsync(row.Id, "relay-b"))).Should().BeFalse();
        (await ReadAsync(row.Id))!.ProcessedAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkFailed_ByOwner_IncrementsRetry_AndReleasesTheRow()
    {
        var row = await SeedAsync();
        await AsReplicaAsync(c => c.TryClaimAsync(row.Id, "relay-a", Lease));

        (await AsReplicaAsync(c => c.MarkFailedAsync(row.Id, "relay-a", "gateway offline"))).Should().BeTrue();

        var saved = await ReadAsync(row.Id);
        saved!.RetryCount.Should().Be(1);
        saved.LastError.Should().Be("gateway offline");
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
}
