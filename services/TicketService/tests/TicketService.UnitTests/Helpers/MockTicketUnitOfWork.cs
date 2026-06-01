using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;

namespace TicketService.UnitTests.Helpers;

public static class MockTicketUnitOfWork
{
    public static (Mock<ITicketUnitOfWork> uow,
                   Mock<IGenericRepository<Ticket>> tickets,
                   Mock<IGenericRepository<TicketActivity>> activities,
                   Mock<IGenericRepository<CustomerAccount>> customers,
                   Mock<IGenericRepository<StaffAccount>> staff)
        Build(
            IEnumerable<Ticket>? ticketSeed = null,
            IEnumerable<TicketActivity>? activitySeed = null,
            IEnumerable<CustomerAccount>? customerSeed = null,
            IEnumerable<StaffAccount>? staffSeed = null,
            IEnumerable<OutboxMessage>? outboxSeed = null)
    {
        var tickets = new Mock<IGenericRepository<Ticket>>();
        tickets.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(ticketSeed ?? Array.Empty<Ticket>()));
        tickets.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => ticketSeed?.FirstOrDefault(x => x.Id == (Guid)id));

        var activities = new Mock<IGenericRepository<TicketActivity>>();
        activities.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<TicketActivity>(activitySeed ?? Array.Empty<TicketActivity>()));

        var customers = new Mock<IGenericRepository<CustomerAccount>>();
        customers.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<CustomerAccount>(customerSeed ?? Array.Empty<CustomerAccount>()));

        var staff = new Mock<IGenericRepository<StaffAccount>>();
        staff.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<StaffAccount>(staffSeed ?? Array.Empty<StaffAccount>()));

        var outbox = new Mock<IGenericRepository<OutboxMessage>>();
        outbox.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<OutboxMessage>(outboxSeed ?? Array.Empty<OutboxMessage>()));

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(tickets.Object);
        uow.SetupGet(u => u.TicketActivities).Returns(activities.Object);
        uow.SetupGet(u => u.CustomerAccounts).Returns(customers.Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(staff.Object);
        uow.SetupGet(u => u.OutboxMessages).Returns(outbox.Object);

        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, tickets, activities, customers, staff);
    }
}
