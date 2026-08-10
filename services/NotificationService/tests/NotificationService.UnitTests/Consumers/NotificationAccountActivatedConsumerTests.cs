using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class AccountActivatedConsumerTests
{
    [Fact]
    public async Task AccountActivated_Writes_InApp_To_AccountId()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<NotificationAccountActivatedConsumer>();
        var accountId = Guid.NewGuid();
        var evt = new AccountActivatedEvent(
            AccountId: accountId,
            Email: "user@example.com",
            FullName: "Nguyễn Văn A",
            PhoneNumber: null,
            Role: "Customer",
            CreationSource: "SelfRegister");

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<AccountActivatedEvent>()).Should().BeTrue();

        written.Should().HaveCount(1);
        var n = written[0];
        n.Type.Should().Be(NotificationTypeEnum.AccountActivated);
        n.Channel.Should().Be(NotificationChannelEnum.InApp);
        n.UserId.Should().Be(accountId);
        n.EntityType.Should().Be("Account");
        n.EntityId.Should().Be(accountId);
        n.Body.Should().Contain("Nguyễn Văn A");

        await harness.Stop();
    }
}
