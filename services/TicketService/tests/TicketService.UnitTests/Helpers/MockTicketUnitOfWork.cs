using System.Linq.Expressions;
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
                   Mock<IGenericRepository<StaffAccount>> staff,
                   Mock<IGenericRepository<SlaTimer>> slaTimers,
                   Mock<IGenericRepository<SlaPauseEvent>> slaPauseEvents)
        Build(
            IEnumerable<Ticket>? ticketSeed = null,
            IEnumerable<TicketActivity>? activitySeed = null,
            IEnumerable<CustomerAccount>? customerSeed = null,
            IEnumerable<StaffAccount>? staffSeed = null,
            IEnumerable<OutboxMessage>? outboxSeed = null,
            IEnumerable<SlaTimer>? slaTimerSeed = null,
            IEnumerable<SlaPauseEvent>? slaPauseEventSeed = null)
    {
        var ticketsMock = (ticketSeed ?? Array.Empty<Ticket>()).AsQueryable().BuildMock();
        var tickets = new Mock<IGenericRepository<Ticket>>();
        tickets.Setup(r => r.GetAllAsync()).Returns(ticketsMock);
        tickets.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => ticketSeed?.FirstOrDefault(x => x.Id == (Guid)id));
        tickets.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Ticket, bool>>>())).Returns((Expression<Func<Ticket, bool>> p) => ticketsMock.Where(p));

        var activitiesMock = (activitySeed ?? Array.Empty<TicketActivity>()).AsQueryable().BuildMock();
        var activities = new Mock<IGenericRepository<TicketActivity>>();
        activities.Setup(r => r.GetAllAsync()).Returns(activitiesMock);

        var customersMock = (customerSeed ?? Array.Empty<CustomerAccount>()).AsQueryable().BuildMock();
        var customers = new Mock<IGenericRepository<CustomerAccount>>();
        customers.Setup(r => r.GetAllAsync()).Returns(customersMock);
        customers.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CustomerAccount, bool>>>())).Returns((Expression<Func<CustomerAccount, bool>> p) => customersMock.Where(p));

        var staffMock = (staffSeed ?? Array.Empty<StaffAccount>()).AsQueryable().BuildMock();
        var staff = new Mock<IGenericRepository<StaffAccount>>();
        staff.Setup(r => r.GetAllAsync()).Returns(staffMock);
        staff.Setup(r => r.FindAsync(It.IsAny<Expression<Func<StaffAccount, bool>>>())).Returns((Expression<Func<StaffAccount, bool>> p) => staffMock.Where(p));

        var slaTimersMock = (slaTimerSeed ?? Array.Empty<SlaTimer>()).AsQueryable().BuildMock();
        var slaTimers = new Mock<IGenericRepository<SlaTimer>>();
        slaTimers.Setup(r => r.GetAllAsync()).Returns(slaTimersMock);

        var slaPauseEventsMock = (slaPauseEventSeed ?? Array.Empty<SlaPauseEvent>()).AsQueryable().BuildMock();
        var slaPauseEvents = new Mock<IGenericRepository<SlaPauseEvent>>();
        slaPauseEvents.Setup(r => r.GetAllAsync()).Returns(slaPauseEventsMock);

        var outboxMock = (outboxSeed ?? Array.Empty<OutboxMessage>()).AsQueryable().BuildMock();
        var outbox = new Mock<IGenericRepository<OutboxMessage>>();
        outbox.Setup(r => r.GetAllAsync()).Returns(outboxMock);

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(tickets.Object);
        uow.SetupGet(u => u.TicketActivities).Returns(activities.Object);
        uow.SetupGet(u => u.CustomerAccounts).Returns(customers.Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(staff.Object);
        uow.SetupGet(u => u.SlaTimers).Returns(slaTimers.Object);
        uow.SetupGet(u => u.SlaPauseEvents).Returns(slaPauseEvents.Object);
        uow.SetupGet(u => u.OutboxMessages).Returns(outbox.Object);

        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, tickets, activities, customers, staff, slaTimers, slaPauseEvents);
    }
}
