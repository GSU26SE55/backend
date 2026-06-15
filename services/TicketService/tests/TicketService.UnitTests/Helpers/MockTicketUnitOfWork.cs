using System.Linq;
using System.Linq.Expressions;
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
        var result = BuildExtended(
            ticketSeed, activitySeed, customerSeed, staffSeed, outboxSeed, slaTimerSeed, slaPauseEventSeed);

        return (result.uow, result.tickets, result.activities, result.customers, result.staff, result.slaTimers, result.slaPauseEvents);
    }

    public static (Mock<ITicketUnitOfWork> uow,
                   Mock<IGenericRepository<Ticket>> tickets,
                   Mock<IGenericRepository<TicketActivity>> activities,
                   Mock<IGenericRepository<CustomerAccount>> customers,
                   Mock<IGenericRepository<StaffAccount>> staff,
                   Mock<IGenericRepository<SlaTimer>> slaTimers,
                   Mock<IGenericRepository<SlaPauseEvent>> slaPauseEvents,
                   Mock<IGenericRepository<TicketComment>> comments,
                   Mock<IGenericRepository<TicketAttachment>> attachments,
                   Mock<IGenericRepository<MaintenanceLog>> logs)
        BuildExtended(
            IEnumerable<Ticket>? ticketSeed = null,
            IEnumerable<TicketActivity>? activitySeed = null,
            IEnumerable<CustomerAccount>? customerSeed = null,
            IEnumerable<StaffAccount>? staffSeed = null,
            IEnumerable<OutboxMessage>? outboxSeed = null,
            IEnumerable<SlaTimer>? slaTimerSeed = null,
            IEnumerable<SlaPauseEvent>? slaPauseEventSeed = null,
            IEnumerable<TicketComment>? commentSeed = null,
            IEnumerable<TicketAttachment>? attachmentSeed = null,
            IEnumerable<MaintenanceLog>? logSeed = null)
    {
        var ticketsMock = (ticketSeed ?? Array.Empty<Ticket>()).BuildMock();
        var tickets = new Mock<IGenericRepository<Ticket>>();
        tickets.Setup(r => r.GetAllAsync()).Returns(ticketsMock);
        tickets.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => ticketSeed?.FirstOrDefault(x => x.Id == (Guid)id));
        tickets.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Ticket, bool>>>())).Returns((Expression<Func<Ticket, bool>> p) => ticketsMock.Where(p));

        var activitiesMock = (activitySeed ?? Array.Empty<TicketActivity>()).BuildMock();
        var activities = new Mock<IGenericRepository<TicketActivity>>();
        activities.Setup(r => r.GetAllAsync()).Returns(activitiesMock);

        var customersMock = (customerSeed ?? Array.Empty<CustomerAccount>()).BuildMock();
        var customers = new Mock<IGenericRepository<CustomerAccount>>();
        customers.Setup(r => r.GetAllAsync()).Returns(customersMock);
        customers.Setup(r => r.FindAsync(It.IsAny<Expression<Func<CustomerAccount, bool>>>())).Returns((Expression<Func<CustomerAccount, bool>> p) => customersMock.Where(p));

        var staffMock = (staffSeed ?? Array.Empty<StaffAccount>()).BuildMock();
        var staff = new Mock<IGenericRepository<StaffAccount>>();
        staff.Setup(r => r.GetAllAsync()).Returns(staffMock);
        staff.Setup(r => r.FindAsync(It.IsAny<Expression<Func<StaffAccount, bool>>>())).Returns((Expression<Func<StaffAccount, bool>> p) => staffMock.Where(p));

        var slaTimersMock = (slaTimerSeed ?? Array.Empty<SlaTimer>()).BuildMock();
        var slaTimers = new Mock<IGenericRepository<SlaTimer>>();
        slaTimers.Setup(r => r.GetAllAsync()).Returns(slaTimersMock);

        var slaPauseEventsMock = (slaPauseEventSeed ?? Array.Empty<SlaPauseEvent>()).BuildMock();
        var slaPauseEvents = new Mock<IGenericRepository<SlaPauseEvent>>();
        slaPauseEvents.Setup(r => r.GetAllAsync()).Returns(slaPauseEventsMock);

        var commentsMock = (commentSeed ?? Array.Empty<TicketComment>()).BuildMock();
        var comments = new Mock<IGenericRepository<TicketComment>>();
        comments.Setup(r => r.GetAllAsync()).Returns(commentsMock);

        var attachmentsMock = (attachmentSeed ?? Array.Empty<TicketAttachment>()).BuildMock();
        var attachments = new Mock<IGenericRepository<TicketAttachment>>();
        attachments.Setup(r => r.GetAllAsync()).Returns(attachmentsMock);

        var logsMock = (logSeed ?? Array.Empty<MaintenanceLog>()).BuildMock();
        var logs = new Mock<IGenericRepository<MaintenanceLog>>();
        logs.Setup(r => r.GetAllAsync()).Returns(logsMock);

        var outboxMock = (outboxSeed ?? Array.Empty<OutboxMessage>()).BuildMock();
        var outbox = new Mock<IGenericRepository<OutboxMessage>>();
        outbox.Setup(r => r.GetAllAsync()).Returns(outboxMock);

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(tickets.Object);
        uow.SetupGet(u => u.TicketActivities).Returns(activities.Object);
        uow.SetupGet(u => u.CustomerAccounts).Returns(customers.Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(staff.Object);
        uow.SetupGet(u => u.SlaTimers).Returns(slaTimers.Object);
        uow.SetupGet(u => u.SlaPauseEvents).Returns(slaPauseEvents.Object);
        uow.SetupGet(u => u.TicketComments).Returns(comments.Object);
        uow.SetupGet(u => u.TicketAttachments).Returns(attachments.Object);
        uow.SetupGet(u => u.MaintenanceLogs).Returns(logs.Object);
        uow.SetupGet(u => u.OutboxMessages).Returns(outbox.Object);

        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        return (uow, tickets, activities, customers, staff, slaTimers, slaPauseEvents, comments, attachments, logs);
    }
}
