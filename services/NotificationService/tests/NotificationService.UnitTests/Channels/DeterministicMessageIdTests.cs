using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using NotificationService.Infrastructure.Channels;
using SharedContracts.Events;
using SharedContracts.Events.Root;

namespace NotificationService.UnitTests.Channels;

/// <summary>
/// GH-792 — message gửi cho EmailService/SmsService phải mang ID suy ra từ <c>NotificationId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Dispatcher gọi provider TRƯỚC khi ghi <c>Sent</c>. Tiến trình chết đúng khoảng giữa hai việc đó
/// thì bản ghi vẫn nằm trong hàng đợi và sẽ được gửi lại — đúng đắn nếu lần trước chưa tới nơi, tai
/// hại nếu lần trước đã tới. Phía nhận phân biệt được hai trường hợp đó bằng <c>ProcessOnceAsync</c>,
/// khoá theo <see cref="IntegrationEvent.Id"/>.
/// </para>
/// <para>
/// ID ngẫu nhiên mỗi lần publish làm hỏng đúng cơ chế này: lần gửi lại trông như một việc hoàn toàn
/// mới, và người dùng nhận email/SMS lần thứ hai.
/// </para>
/// </remarks>
public class DeterministicMessageIdTests
{
    private static readonly Guid NotificationId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");

    private static SendRequest Request() => new()
    {
        NotificationId = NotificationId,
        UserId = Guid.NewGuid(),
        Type = NotificationTypeEnum.TicketCreated,
        Title = "Title",
        Body = "Content",
        Email = "user@x.com",
        PhoneNumber = "0901234567",
    };

    private static IConfiguration EmptyConfig()
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    // ── Email ────────────────────────────────────────────────────────────────

    private static (Mock<IPublishEndpoint> Publisher, EmailBusChannel Channel, List<SendNotificationEmailEvent> Sent) BuildEmail()
    {
        var sent = new List<SendNotificationEmailEvent>();
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
           .Callback<SendNotificationEmailEvent, CancellationToken>((e, _) => sent.Add(e))
           .Returns(Task.CompletedTask);

        var channel = new EmailBusChannel(
            pub.Object,
            NullLogger<EmailBusChannel>.Instance,
            new UnsubscribeTokenService(EmptyConfig()));

        return (pub, channel, sent);
    }

    [Fact]
    public async Task EmailMessage_CarriesIdDerivedFromNotificationId()
    {
        var (_, channel, sent) = BuildEmail();

        await channel.SendAsync(Request());

        sent.Should().ContainSingle();
        sent[0].Id.Should().Be(DeterministicEventId.From(NotificationId, "email"));
    }

    [Fact]
    public async Task EmailMessage_KeepsTheSameId_WhenTheSameNotificationIsSentAgain()
    {
        // Đây chính là kịch bản gửi lại sau sự cố. Hai ID khác nhau = EmailService coi là hai việc.
        var (_, channel, sent) = BuildEmail();

        await channel.SendAsync(Request());
        await channel.SendAsync(Request());

        sent.Should().HaveCount(2);
        sent[1].Id.Should().Be(sent[0].Id, "gửi lại cùng một notification phải mang đúng ID cũ");
    }

    [Fact]
    public async Task EmailMessage_OfADifferentNotification_GetsADifferentId()
    {
        // Chiều âm: nếu mọi message dùng chung một ID thì phía nhận sẽ nuốt hết các thông báo sau.
        var (_, channel, sent) = BuildEmail();

        await channel.SendAsync(Request());
        var other = Request();
        other.NotificationId = Guid.NewGuid();
        await channel.SendAsync(other);

        sent[1].Id.Should().NotBe(sent[0].Id);
    }

    // ── SMS ──────────────────────────────────────────────────────────────────

    private static (SmsBusChannel Channel, List<SendSmsCommand> Sent) BuildSms()
    {
        var sent = new List<SendSmsCommand>();
        var pub = new Mock<IPublishEndpoint>();
        pub.Setup(p => p.Publish(It.IsAny<SendSmsCommand>(), It.IsAny<CancellationToken>()))
           .Callback<SendSmsCommand, CancellationToken>((e, _) => sent.Add(e))
           .Returns(Task.CompletedTask);

        return (new SmsBusChannel(pub.Object, NullLogger<SmsBusChannel>.Instance), sent);
    }

    [Fact]
    public async Task SmsMessage_CarriesIdDerivedFromNotificationId()
    {
        var (channel, sent) = BuildSms();

        await channel.SendAsync(Request());

        sent.Should().ContainSingle();
        sent[0].Id.Should().Be(DeterministicEventId.From(NotificationId, "sms"));
    }

    [Fact]
    public async Task SmsMessage_KeepsTheSameId_WhenTheSameNotificationIsSentAgain()
    {
        var (channel, sent) = BuildSms();

        await channel.SendAsync(Request());
        await channel.SendAsync(Request());

        sent[1].Id.Should().Be(sent[0].Id);
    }

    [Fact]
    public async Task EmailAndSms_OfTheSameNotification_DoNotCollide()
    {
        // Cùng NotificationId nhưng hai kênh khác nhau: trùng ID thì kênh thứ hai bị coi là bản sao
        // và bị bỏ — người dùng mất hẳn một kênh mà không có lỗi nào nổi lên.
        var (_, emailChannel, emailSent) = BuildEmail();
        var (smsChannel, smsSent) = BuildSms();

        await emailChannel.SendAsync(Request());
        await smsChannel.SendAsync(Request());

        smsSent[0].Id.Should().NotBe(emailSent[0].Id);
    }
}
