using SharedInfrastructure.Leasing;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace SharedInfrastructure.IntegrationTests.Leasing;

/// <summary>
/// GH-793 — quyền chạy độc quyền phải NGUYÊN TỬ, và phải đối chiếu chủ sở hữu.
/// </summary>
/// <remarks>
/// <para>
/// Khuôn cũ rải khắp các job nền là <c>GET</c> rồi <c>SET</c>: đọc thấy khoá trống thì ghi tên mình
/// vào. Hai replica cùng đọc thấy trống trong cùng khoảnh khắc thì cả hai đều tự coi là chủ và cùng
/// gửi một thông báo.
/// </para>
/// <para>
/// Chạy trên Redis THẬT vì đúng chỗ này là chỗ mock vô dụng: mock chỉ trả lại thứ ta bảo nó trả,
/// nên nó không thể phát hiện khe hở giữa hai lệnh — mà khe hở đó chính là lỗi.
/// </para>
/// </remarks>
public sealed class RedisDistributedLeaseTests : IAsyncLifetime
{
    private RedisContainer _redis = null!;
    private IConnectionMultiplexer _connection = null!;
    private RedisDistributedLease _lease = null!;

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    [Obsolete]
    public async Task InitializeAsync()
    {
        _redis = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await _redis.StartAsync();

        _connection = await ConnectionMultiplexer.ConnectAsync(
            $"{_redis.GetConnectionString()},abortConnect=false");
        _lease = new RedisDistributedLease(_connection);
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        if (_redis is not null)
            await _redis.DisposeAsync();
    }

    /// <summary>Khoá riêng cho từng test — chạy song song không giẫm lên nhau.</summary>
    private static string NewKey() => $"lease:{Guid.NewGuid():N}";

    [Fact]
    public async Task FreeLease_IsGrantedToTheFirstCaller()
    {
        var key = NewKey();

        (await _lease.TryAcquireAsync(key, "instance-a", Ttl)).Should().BeTrue();
    }

    [Fact]
    public async Task HeldLease_IsRefusedToEveryoneElse()
    {
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", Ttl);

        (await _lease.TryAcquireAsync(key, "instance-b", Ttl)).Should().BeFalse();
    }

    [Fact]
    public async Task OwnerCanReacquire_WhichIsHowEachTickExtendsItsHold()
    {
        // Job nền gọi lại mỗi nhịp; nếu chủ hiện tại bị từ chối thì quyền sẽ nhảy qua nhảy lại giữa
        // các instance sau mỗi lần hết hạn.
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", Ttl);

        (await _lease.TryAcquireAsync(key, "instance-a", Ttl)).Should().BeTrue();
    }

    [Fact]
    public async Task OnlyTheOwnerCanRenew()
    {
        // Không đối chiếu chủ sở hữu thì một instance đã mất quyền vẫn gia hạn được, và giữ mãi một
        // quyền mà nó không còn sở hữu.
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", Ttl);

        (await _lease.TryRenewAsync(key, "instance-a", Ttl)).Should().BeTrue();
        (await _lease.TryRenewAsync(key, "instance-b", Ttl)).Should().BeFalse();
    }

    [Fact]
    public async Task RenewingALeaseNobodyHolds_Fails()
    {
        // Gia hạn phải là "kéo dài cái đang có", không phải cửa sau để giành quyền mà bỏ qua bước
        // giành hợp lệ.
        (await _lease.TryRenewAsync(NewKey(), "instance-a", Ttl)).Should().BeFalse();
    }

    [Fact]
    public async Task OnlyTheOwnerCanRelease()
    {
        // ĐÂY là bẫy nguy hiểm nhất: instance treo lâu quá, tỉnh lại và nhả quyền — nhưng quyền lúc
        // đó đã thuộc về người khác, và cú nhả nhầm mở đường cho một instance thứ ba chen vào giữa
        // lúc chủ hợp lệ vẫn đang chạy.
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", Ttl);

        await _lease.ReleaseAsync(key, "instance-b");

        (await _lease.TryAcquireAsync(key, "instance-c", Ttl))
            .Should().BeFalse("quyền vẫn phải thuộc về instance-a");
    }

    [Fact]
    public async Task OwnerRelease_FreesTheLeaseForOthers()
    {
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", Ttl);

        await _lease.ReleaseAsync(key, "instance-a");

        (await _lease.TryAcquireAsync(key, "instance-b", Ttl)).Should().BeTrue();
    }

    [Fact]
    public async Task ExpiredLease_IsGrantedToTheNextCaller()
    {
        // Instance chết mà không kịp nhả thì công việc phải tiếp tục được — nếu không, một lần chết
        // là dừng hẳn job đó cho tới khi có người can thiệp.
        var key = NewKey();
        await _lease.TryAcquireAsync(key, "instance-a", TimeSpan.FromMilliseconds(200));

        await Task.Delay(500);

        (await _lease.TryAcquireAsync(key, "instance-b", Ttl)).Should().BeTrue();
    }

    [Fact]
    public async Task TwentyRacingInstances_ProduceExactlyOneWinner()
    {
        // Khẳng định trung tâm của issue. Với khuôn GET-rồi-SET cũ, nhiều bên cùng đọc thấy trống
        // và cùng thắng; ở đây Redis phải chọn đúng một.
        var key = NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => _lease.TryAcquireAsync(key, $"instance-{i}", Ttl)));

        results.Count(won => won).Should().Be(1);
    }

    [Fact]
    public async Task RepeatedRaces_NeverGrantTwoWinners()
    {
        // Một lượt đua có thể may mắn. Lặp nhiều vòng để bắt được trường hợp hiếm.
        for (var round = 0; round < 25; round++)
        {
            var key = NewKey();

            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(i => _lease.TryAcquireAsync(key, $"r{round}-i{i}", Ttl)));

            results.Count(won => won).Should().Be(1, $"vòng {round}");
        }
    }

    [Theory]
    [InlineData("", "owner")]
    [InlineData("key", "")]
    [InlineData("key", "   ")]
    public async Task EmptyKeyOrOwner_IsRejected(string key, string owner)
    {
        // Chủ sở hữu rỗng làm mọi instance trông giống nhau: ai cũng gia hạn và nhả được quyền của
        // người khác, tức là mất hẳn tác dụng của việc đối chiếu.
        var act = async () => await _lease.TryAcquireAsync(key, owner, Ttl);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task NonPositiveTtl_IsRejected()
    {
        // TTL 0 nghĩa là khoá hết hạn ngay lập tức — quyền độc quyền trở thành trang trí.
        var act = async () => await _lease.TryAcquireAsync(NewKey(), "owner", TimeSpan.Zero);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
