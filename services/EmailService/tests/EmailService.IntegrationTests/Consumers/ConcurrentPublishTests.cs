using EmailService.IntegrationTests.Fixtures;
using MassTransit.Testing;
using SharedContracts.Events;

namespace EmailService.IntegrationTests.Consumers;

/// <summary>
/// Test khi nhiều event được publish song song: tất cả phải được consume + 1 mailjet call/event,
/// không bị mất hay duplicate. Stress nhẹ in-memory bus + DI scoping.
/// </summary>
[Collection("EmailServiceIntegration")]
public class ConcurrentPublishTests : IAsyncLifetime
{
    private readonly EmailServiceFactory _factory;
    private ITestHarness _harness = null!;

    public ConcurrentPublishTests(EmailServiceFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _ = _factory.CreateClient();
        _harness = await _factory.GetHarnessAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentPublish_20Events_AllConsumed_ExactlyOneMailjetCallPerEvent()
    {
        const int n = 20;
        var events = Enumerable.Range(0, n)
            .Select(i => new SendOtpRegisterEvent(
                $"concurrent-{Guid.NewGuid():N}@x.com",
                $"CC{i}-{Random.Shared.Next(100000, 999999)}"))
            .ToList();

        // Publish song song qua Task.WhenAll.
        await Task.WhenAll(events.Select(e => _harness.Bus.Publish(e)));

        // Đợi từng OTP unique → consumer đã chạy hết.
        foreach (var e in events)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);

        // Mỗi email tương ứng đúng 1 mailjet call (không miss, không duplicate).
        foreach (var e in events)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1, $"chính xác 1 mail cho {e.ToEmail}");
    }

    [Fact]
    public async Task ConcurrentMixedEventTypes_AllRoutedAndProcessed()
    {
        const int eachType = 5;
        var registers = Enumerable.Range(0, eachType).Select(i =>
            new SendOtpRegisterEvent($"mixed-r-{Guid.NewGuid():N}@x.com", $"MR{i}-{Random.Shared.Next(100000, 999999)}")).ToList();
        var resets = Enumerable.Range(0, eachType).Select(i =>
            new SendPasswordResetOtpEvent($"mixed-p-{Guid.NewGuid():N}@x.com", $"MP{i}-{Random.Shared.Next(100000, 999999)}")).ToList();
        var phones = Enumerable.Range(0, eachType).Select(i =>
            new SendPhoneOtpEvent($"+8412{Random.Shared.Next(1000000, 9999999)}-{i}", $"MH{i}")).ToList();

        var publishTasks = new List<Task>();
        publishTasks.AddRange(registers.Select(e => _harness.Bus.Publish(e)));
        publishTasks.AddRange(resets.Select(e => _harness.Bus.Publish(e)));
        publishTasks.AddRange(phones.Select(e => _harness.Bus.Publish(e)));
        await Task.WhenAll(publishTasks);

        // Đợi mọi register + reset render xong (phone consumer là stub không render).
        foreach (var e in registers)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);
        foreach (var e in resets)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);

        // Mỗi register + reset có 1 mailjet call.
        foreach (var e in registers)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1);
        foreach (var e in resets)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1);
    }
}
