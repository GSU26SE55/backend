using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class ManagerQueueCountQueryHandlerTests
{
    [Fact]
    public async Task Handle_CountsOnlyOpenNonDeletedAndNonMergedTickets()
    {
        var queueTicket = MakeTicket(TicketStatusEnum.Open);
        var deletedTicket = MakeTicket(TicketStatusEnum.Open);
        deletedTicket.IsDeleted = true;
        var mergedTicket = MakeTicket(TicketStatusEnum.Open);
        mergedTicket.MergedIntoTicketId = Guid.NewGuid();

        var repository = new Mock<IGenericRepository<Ticket>>();
        repository.Setup(r => r.GetAllAsync()).Returns(new TestAsyncEnumerable<Ticket>([
            queueTicket,
            deletedTicket,
            mergedTicket,
            MakeTicket(TicketStatusEnum.Assigned)
        ]));

        var unitOfWork = new Mock<ITicketUnitOfWork>();
        unitOfWork.Setup(u => u.Tickets).Returns(repository.Object);
        var handler = new ManagerQueueCountQueryHandler(unitOfWork.Object);

        var result = await handler.Handle(new ManagerQueueCountQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().Be(1);
    }

    private static Ticket MakeTicket(TicketStatusEnum status) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Title = "Test",
        Description = "Test",
        Category = TicketCategoryEnum.Other,
        Status = status,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow
    };
}
