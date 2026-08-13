using FluentAssertions;
using SharedContracts.Events;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Implements.Services;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Services;

public class IntegrationEventOutboxWriterTests
{
    [Fact]
    public async Task WriteAsync_UsesIntegrationEventIdAsOutboxPrimaryKey()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        OutboxMessage? captured = null;
        Mock.Get(uow.Object.OutboxMessages)
            .Setup(x => x.AddAsync(It.IsAny<OutboxMessage>()))
            .Callback<OutboxMessage>(message => captured = message)
            .Returns(Task.CompletedTask);
        var sut = new IntegrationEventOutboxWriter(uow.Object);
        var evt = new TicketWorkStartedEvent(
            Guid.NewGuid(), "TKT-1176", Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 1, "Immediate");

        await sut.WriteAsync(evt, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Id.Should().Be(evt.Id);
        captured.AggregateId.Should().Be(evt.Id);
    }
}
