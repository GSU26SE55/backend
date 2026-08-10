using SharedInfrastructure.Leasing;

namespace NotificationService.UnitTests.Helpers;

/// <summary>
/// GH-793 — bản <see cref="IDistributedLease"/> trong bộ nhớ cho test.
/// </summary>
/// <remarks>
/// Giữ đúng ngữ nghĩa quan trọng nhất của bản thật: <b>đối chiếu chủ sở hữu</b>. Một bản giả luôn
/// trả <c>true</c> sẽ khiến mọi test về quyền chạy độc quyền xanh mà chẳng chứng minh điều gì.
/// Thời hạn được bỏ qua có chủ ý — test không nên phụ thuộc đồng hồ thật.
/// </remarks>
public sealed class InMemoryLease : IDistributedLease
{
    private readonly Dictionary<string, string> _owners = [];

    /// <summary>Số lần gia hạn đã gọi — để test kiểm việc gia hạn giữa lượt chạy dài.</summary>
    public int RenewCalls { get; private set; }

    /// <summary>Đặt true để mô phỏng đã mất quyền vào tay instance khác.</summary>
    public bool RenewFails { get; set; }

    public Task<bool> TryAcquireAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        if (!_owners.TryGetValue(key, out var current))
        {
            _owners[key] = owner;
            return Task.FromResult(true);
        }

        return Task.FromResult(current == owner);
    }

    public Task<bool> TryRenewAsync(string key, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        RenewCalls++;
        if (RenewFails)
            return Task.FromResult(false);

        return Task.FromResult(_owners.TryGetValue(key, out var current) && current == owner);
    }

    public Task ReleaseAsync(string key, string owner, CancellationToken ct = default)
    {
        if (_owners.TryGetValue(key, out var current) && current == owner)
            _owners.Remove(key);

        return Task.CompletedTask;
    }
}
