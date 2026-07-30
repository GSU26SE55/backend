using MassTransit.Testing;
using NotificationService.Application.Consumers;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using SharedContracts.Events;

namespace NotificationService.UnitTests.Consumers;

public class TicketAssignedConsumerTests
{
    /// <summary>
    /// Sprint 6.2 NOTI-05 (#676) — event có CustomerId nên consumer ghi cho CẢ Staff và Customer,
    /// mỗi người 3 kênh (InApp + Push + Email) theo spec §3.4.
    /// </summary>
    [Fact]
    public async Task TicketAssigned_Writes_To_Staff_And_Customer()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketAssignedConsumer>();
        var staffId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = new TicketAssignedEvent(Guid.NewGuid(), "TKT-002", staffId, "P1", customerId);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketAssignedEvent>()).Should().BeTrue();

        written.Should().HaveCount(6);
        written.Should().AllSatisfy(n =>
        {
            n.Type.Should().Be(NotificationTypeEnum.TicketAssigned);
            n.EntityId.Should().Be(evt.TicketId);
        });

        var staffRecords = written.Where(n => n.UserId == staffId).ToList();
        staffRecords.Should().HaveCount(3);
        staffRecords.Select(n => n.Channel).Should().BeEquivalentTo(new[]
        {
            NotificationChannelEnum.InApp, NotificationChannelEnum.Push, NotificationChannelEnum.Email
        });
        staffRecords.Should().AllSatisfy(n => n.Body.Should().Contain("P1"));

        var customerRecords = written.Where(n => n.UserId == customerId).ToList();
        customerRecords.Should().HaveCount(3);
        customerRecords.Should().AllSatisfy(n => n.Title.Should().Contain("TKT-002"));

        await harness.Stop();
    }

    /// <summary>CustomerId rỗng (dữ liệu cũ trên bus) → chỉ ghi cho Staff, không nổ exception.</summary>
    [Fact]
    public async Task TicketAssigned_EmptyCustomerId_WritesStaffOnly()
    {
        var (harness, written, _) = await ConsumerTestHarness.StartAsync<TicketAssignedConsumer>();
        var staffId = Guid.NewGuid();
        var evt = new TicketAssignedEvent(Guid.NewGuid(), "TKT-003", staffId, "P2", Guid.Empty);

        await harness.Bus.Publish(evt);
        (await harness.Consumed.Any<TicketAssignedEvent>()).Should().BeTrue();

        written.Should().HaveCount(3);
        written.Should().AllSatisfy(n => n.UserId.Should().Be(staffId));

        await harness.Stop();
    }
}
