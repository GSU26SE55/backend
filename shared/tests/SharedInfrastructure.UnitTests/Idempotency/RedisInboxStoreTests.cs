using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedInfrastructure.Idempotency;
using StackExchange.Redis;

namespace SharedInfrastructure.UnitTests.Idempotency;

/// <summary>
/// GH-764 — Inbox chuyển từ "đánh dấu một phát" sang vòng đời ba bước: giữ chỗ có hạn → chốt khi
/// side effect xong → nhả khi lỗi. Các test dưới đây ghim đúng các lệnh Redis phát ra, vì chính
/// chi tiết "TTL nào cho bước nào" mới là thứ quyết định lỗi tạm thời có làm mất message hay không.
/// </summary>
public class RedisInboxStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();

    public RedisInboxStoreTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                  .Returns(_dbMock.Object);
    }

    private RedisInboxStore CreateStore(InboxOptions? options = null) =>
        new(_redisMock.Object,
            Options.Create(options ?? new InboxOptions { TtlDays = 7, LeaseSeconds = 300, FailOpenWhenRedisDown = false }),
            NullLogger<RedisInboxStore>.Instance);

    /// <summary>
    /// <c>IDatabase.StringSetAsync</c> có nhiều overload; compiler chọn bản 4 tham số
    /// <c>(RedisKey, RedisValue, TimeSpan?, When)</c> theo lời gọi trong production.
    /// </summary>
    private void SetupClaim(bool acquired) =>
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(acquired);

    private void SetupExistingValue(string? value) =>
        _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(value is null ? RedisValue.Null : value);

    [Fact]
    public async Task TryBegin_FirstCall_ClaimsWithLeaseTtl_NotTheLongTtl()
    {
        // Chỗ giữ phải dùng TTL NGẮN. Nếu giữ chỗ bằng TTL 7 ngày như bản cũ thì một tiến trình
        // chết giữa chừng sẽ khoá message suốt 7 ngày — đúng kiểu mất message mà GH-764 mô tả.
        SetupClaim(true);
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");

        claim.Status.Should().Be(InboxClaimStatus.Claimed);
        claim.Token.Should().NotBeNullOrEmpty();
        _dbMock.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => ((string)k!).StartsWith("inbox:MyConsumer:")),
            It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t == TimeSpan.FromSeconds(300)),
            When.NotExists), Times.Once);
    }

    [Fact]
    public async Task TryBegin_WhenAlreadyCompleted_ReportsCompleted()
    {
        SetupClaim(false);
        SetupExistingValue("d");
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");

        claim.Status.Should().Be(InboxClaimStatus.AlreadyCompleted);
    }

    [Fact]
    public async Task TryBegin_WhenSomeoneElseIsStillWorking_ReportsInProgress_NotCompleted()
    {
        // ĐÂY là điểm mấu chốt của GH-764: "đang chạy" KHÁC "đã xong". Gộp hai cái làm một chính
        // là cách message bị ACK trong khi side effect chưa từng chạy.
        SetupClaim(false);
        SetupExistingValue("p:someone-else");
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");

        claim.Status.Should().Be(InboxClaimStatus.InProgressElsewhere);
    }

    [Fact]
    public async Task TryBegin_WhenKeyVanishesBetweenSetAndGet_RetriesClaimOnce()
    {
        // Chỗ giữ vừa hết hạn ngay giữa hai lệnh: lần SET đầu trượt, GET không thấy gì, lần SET
        // thứ hai thành công. Không thử lại thì message bị hoãn vô cớ một vòng.
        _dbMock.SetupSequence(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ReturnsAsync(false)
            .ReturnsAsync(true);
        SetupExistingValue(null);
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");

        claim.Status.Should().Be(InboxClaimStatus.Claimed);
    }

    [Fact]
    public async Task Complete_UsesTheLongTtl_SoDuplicatesStayDeduped()
    {
        SetupClaim(true);
        _dbMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");
        await store.CompleteAsync(Guid.NewGuid(), "MyConsumer", claim.Token);

        _dbMock.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.IsAny<RedisKey[]>(),
            It.Is<RedisValue[]>(v => v.Length == 3 && (long)v[2] == (long)TimeSpan.FromDays(7).TotalSeconds),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Complete_ComparesOwnershipToken_SoAnExpiredLeaseCannotOverwriteAnother()
    {
        SetupClaim(true);
        RedisValue[]? args = null;
        _dbMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, a, _) => args = a)
            .ReturnsAsync(RedisResult.Create(0));
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");
        await store.CompleteAsync(Guid.NewGuid(), "MyConsumer", claim.Token);

        args.Should().NotBeNull();
        ((string)args![0]!).Should().Be(claim.Token);
    }

    [Fact]
    public async Task Release_DeletesOnlyOurOwnLease()
    {
        SetupClaim(true);
        RedisValue[]? args = null;
        _dbMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .Callback<string, RedisKey[], RedisValue[], CommandFlags>((_, _, a, _) => args = a)
            .ReturnsAsync(RedisResult.Create(1));
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");
        await store.ReleaseAsync(Guid.NewGuid(), "MyConsumer", claim.Token);

        args.Should().NotBeNull();
        ((string)args![0]!).Should().Be(claim.Token);
    }

    [Fact]
    public async Task CompleteAndRelease_WithEmptyToken_TouchNothing()
    {
        // Dấu rỗng = chế độ fail-open (Redis sập lúc xin chỗ). Không giữ chỗ nào thì cũng không
        // được đụng vào khoá của ai.
        var store = CreateStore();

        await store.CompleteAsync(Guid.NewGuid(), "C", string.Empty);
        await store.ReleaseAsync(Guid.NewGuid(), "C", string.Empty);

        _dbMock.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task Complete_WhenRedisIsDown_DoesNotThrow()
    {
        // Side effect ĐÃ thành công rồi. Ném lỗi ở bước chốt sẽ biến nó thành thất bại và kéo
        // theo một lần gửi lại — tức là gửi email/SMS hai lần vì lý do hoàn toàn không đáng.
        SetupClaim(true);
        _dbMock.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));
        var store = CreateStore();

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "MyConsumer");
        var act = async () => await store.CompleteAsync(Guid.NewGuid(), "MyConsumer", claim.Token);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RedisDown_FailOpenTrue_ClaimsWithEmptyToken_AndDoesNotThrow()
    {
        SetupClaim(true);
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var store = CreateStore(new InboxOptions { TtlDays = 7, FailOpenWhenRedisDown = true });

        var claim = await store.TryBeginAsync(Guid.NewGuid(), "C");

        claim.Status.Should().Be(InboxClaimStatus.Claimed);
        claim.Token.Should().BeEmpty("không giữ được chỗ nào thì chốt/nhả sau đó phải vô hiệu");
    }

    [Fact]
    public async Task RedisDown_FailOpenFalse_Throws()
    {
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var store = CreateStore(new InboxOptions { TtlDays = 7, FailOpenWhenRedisDown = false });

        var act = async () => await store.TryBeginAsync(Guid.NewGuid(), "C");

        await act.Should().ThrowAsync<RedisConnectionException>();
    }

    [Fact]
    public async Task KeyFormat_Contains_ConsumerName_And_MessageId()
    {
        RedisKey capturedKey = default;
        _dbMock.Setup(d => d.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), When.NotExists))
            .Callback<RedisKey, RedisValue, TimeSpan?, When>((k, _, _, _) => capturedKey = k)
            .ReturnsAsync(true);

        var store = CreateStore();
        var msgId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await store.TryBeginAsync(msgId, "SendOtp");

        ((string)capturedKey!).Should().Be($"inbox:SendOtp:{msgId:N}");
    }
}
