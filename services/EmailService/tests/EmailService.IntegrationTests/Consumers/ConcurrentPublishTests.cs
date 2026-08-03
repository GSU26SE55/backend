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
        // Sprint 6.2 NOTI-15 (#686) — SendPhoneOtpEvent đã bỏ khỏi EmailService (stub consumer bị xoá).
        // Thay bằng email đổi địa chỉ để vẫn kiểm tra routing nhiều loại event cùng lúc.
        var emailChanges = Enumerable.Range(0, eachType).Select(i =>
            new SendEmailChangeOtpEvent($"mixed-c-{Guid.NewGuid():N}@x.com", $"MC{i}-{Random.Shared.Next(100000, 999999)}")).ToList();

        var publishTasks = new List<Task>();
        publishTasks.AddRange(registers.Select(e => _harness.Bus.Publish(e)));
        publishTasks.AddRange(resets.Select(e => _harness.Bus.Publish(e)));
        publishTasks.AddRange(emailChanges.Select(e => _harness.Bus.Publish(e)));
        await Task.WhenAll(publishTasks);

        foreach (var e in emailChanges)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);

        // Đợi mọi register + reset render xong.
        foreach (var e in registers)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);
        foreach (var e in resets)
            await _factory.WaitForRenderCallAsync(e.Otp, timeoutMs: 10000);

        // Render xảy ra TRƯỚC khi gửi Mailjet — đợi tới khi HTTP call thực sự hoàn tất rồi mới đếm.
        foreach (var e in registers)
            await _factory.WaitForMailjetCallAsync(e.ToEmail, timeoutMs: 10000);
        foreach (var e in resets)
            await _factory.WaitForMailjetCallAsync(e.ToEmail, timeoutMs: 10000);

        // Mỗi register + reset có 1 mailjet call.
        foreach (var e in registers)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1);
        foreach (var e in resets)
            _factory.CountMailjetCallsContaining(e.ToEmail).Should().Be(1);
    }
}
