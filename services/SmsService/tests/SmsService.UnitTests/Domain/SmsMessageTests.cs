using SmsService.Domain.Entities;
using SmsService.Domain.Enums;

namespace SmsService.UnitTests.Domain;

public class SmsMessageTests
{
    private static SmsMessage NewPending() => new()
    {
        Id = Guid.NewGuid(),
        PhoneNumber = "+84901234567",
        Message = "test",
        Status = SmsStatus.Pending,
        SourceService = "auth",
        CorrelationId = Guid.NewGuid(),
        MaxRetryCount = 3,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public void Claim_TransitionsPendingToSending_AndSetsDevice()
    {
        var sms = NewPending();
        var now = DateTime.UtcNow;

        sms.Claim("device-A", Guid.Parse("11111111-1111-1111-1111-111111111111"), now);

        sms.Status.Should().Be(SmsStatus.Sending);
        sms.GatewayDeviceCode.Should().Be("device-A");
        sms.GatewayDeviceId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        sms.PickedAt.Should().Be(now);
        sms.UpdatedAt.Should().Be(now);
    }

    [Fact]
    public void MarkSent_TransitionsSendingToSent_ClearsError()
    {
        var sms = NewPending();
        sms.ErrorMessage = "previous error";
        sms.Claim("device-A", Guid.NewGuid(), DateTime.UtcNow);
        var now = DateTime.UtcNow;

        sms.MarkSent(now);

        sms.Status.Should().Be(SmsStatus.Sent);
        sms.SentAt.Should().Be(now);
        sms.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MarkRetry_BumpsRetryCount_AndResetsToPending()
    {
        var sms = NewPending();
        sms.Claim("device-A", Guid.NewGuid(), DateTime.UtcNow);
        var now = DateTime.UtcNow;

        sms.MarkRetry("network error", now);

        sms.RetryCount.Should().Be(1);
        sms.Status.Should().Be(SmsStatus.Pending);
        sms.ErrorMessage.Should().Be("network error");
        sms.GatewayDeviceCode.Should().BeNull();
        sms.GatewayDeviceId.Should().BeNull();
        sms.PickedAt.Should().BeNull();
    }

    [Fact]
    public void MarkFailedFinal_BumpsRetryCount_AndTransitionsToFailed()
    {
        var sms = NewPending();
        sms.RetryCount = 2;
        sms.MaxRetryCount = 3;
        sms.Claim("device-A", Guid.NewGuid(), DateTime.UtcNow);
        var now = DateTime.UtcNow;

        sms.MarkFailedFinal("permanent", now);

        // Semantic: cuối luồng RetryCount == MaxRetryCount
        sms.RetryCount.Should().Be(3);
        sms.Status.Should().Be(SmsStatus.Failed);
        sms.FailedAt.Should().Be(now);
        sms.ErrorMessage.Should().Be("permanent");
    }

    [Fact]
    public void ReapStaleClaim_DoesNotBumpRetryCount()
    {
        var sms = NewPending();
        sms.Claim("device-A", Guid.NewGuid(), DateTime.UtcNow.AddMinutes(-10));
        sms.RetryCount = 0;
        var now = DateTime.UtcNow;

        sms.ReapStaleClaim(now);

        sms.Status.Should().Be(SmsStatus.Pending);
        sms.RetryCount.Should().Be(0); // KHÔNG bump — không phải attempt thất bại từ device
        sms.GatewayDeviceCode.Should().BeNull();
        sms.PickedAt.Should().BeNull();
    }

    [Fact]
    public void Redact_RemovesMessage_AndSetsRedactedAt()
    {
        var sms = NewPending();
        sms.MarkSent(DateTime.UtcNow);
        var now = DateTime.UtcNow.AddHours(24);

        sms.Redact(now);

        sms.Message.Should().BeNull();
        sms.RedactedAt.Should().Be(now);
    }

    [Fact]
    public void Cancel_TransitionsToCancelled()
    {
        var sms = NewPending();
        var now = DateTime.UtcNow;

        sms.Cancel(now);

        sms.Status.Should().Be(SmsStatus.Cancelled);
        sms.UpdatedAt.Should().Be(now);
    }
}
