using System.Linq;
using System.Linq.Expressions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Utils;

public static class MockTicketUnitOfWork
{
    /// <summary>
    /// Lấy mock repo <see cref="TicketAiSuggestion"/> từ một UoW đã dựng.
    /// </summary>
    /// <remarks>
    /// Không thêm vào tuple trả về vì <c>Build</c>/<c>BuildExtended</c> đang có 88/186 call
    /// site destructure theo vị trí. Cũng KHÔNG dùng static field: xUnit chạy các test class
    /// song song nên field dùng chung bị lớp khác ghi đè giữa chừng — đúng lỗi đã gặp.
    /// Lấy ngược từ chính instance UoW là an toàn với mọi kiểu chạy song song.
    /// </remarks>
    public static Mock<IGenericRepository<TicketAiSuggestion>> AiSuggestionsOf(
        Mock<ITicketUnitOfWork> uow) => Mock.Get(uow.Object.TicketAiSuggestions);

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
            IEnumerable<SlaPauseEvent>? slaPauseEventSeed = null,
            IEnumerable<TicketAssignment>? assignmentSeed = null)
    {
        var result = BuildExtended(
            ticketSeed, activitySeed, customerSeed, staffSeed, outboxSeed, slaTimerSeed, slaPauseEventSeed,
            assignmentSeed: assignmentSeed);

        return (result.uow, result.tickets, result.activities, result.customers, result.staff, result.slaTimers, result.slaPauseEvents);
    }

    public static (Mock<ITicketUnitOfWork> uow,
                   Mock<IGenericRepository<Ticket>> tickets,
                   Mock<IGenericRepository<TicketActivity>> activities,
                   Mock<IGenericRepository<CustomerAccount>> customers,
                   Mock<IGenericRepository<StaffAccount>> staff,
                   Mock<IGenericRepository<SlaTimer>> slaTimers,
                   Mock<IGenericRepository<SlaPauseEvent>> slaPauseEvents,
                   Mock<IGenericRepository<TicketChat>> chats,
                   Mock<IGenericRepository<TicketAttachment>> attachments,
                   Mock<IGenericRepository<MaintenanceLog>> logs,
                   Mock<IGenericRepository<KnowledgeBaseArticle>> kbArticles,
                   Mock<IGenericRepository<KbArticleVersion>> kbVersions,
                   Mock<IGenericRepository<TicketKbReference>> kbReferences,
                   Mock<IGenericRepository<TicketParticipant>> participants)
        BuildExtended(
            IEnumerable<Ticket>? ticketSeed = null,
            IEnumerable<TicketActivity>? activitySeed = null,
            IEnumerable<CustomerAccount>? customerSeed = null,
            IEnumerable<StaffAccount>? staffSeed = null,
            IEnumerable<OutboxMessage>? outboxSeed = null,
            IEnumerable<SlaTimer>? slaTimerSeed = null,
            IEnumerable<SlaPauseEvent>? slaPauseEventSeed = null,
            IEnumerable<TicketChat>? chatSeed = null,
            IEnumerable<TicketAttachment>? attachmentSeed = null,
            IEnumerable<MaintenanceLog>? logSeed = null,
            IEnumerable<KnowledgeBaseArticle>? kbSeed = null,
            IEnumerable<KbArticleVersion>? kbVersionSeed = null,
            IEnumerable<TicketKbReference>? kbRefSeed = null,
            IEnumerable<TicketParticipant>? participantSeed = null,
            IEnumerable<TicketAssignment>? assignmentSeed = null,
            IEnumerable<TicketAiSuggestion>? aiSuggestionSeed = null)
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

        var chatsMock = (chatSeed ?? Array.Empty<TicketChat>()).BuildMock();
        var chats = new Mock<IGenericRepository<TicketChat>>();
        chats.Setup(r => r.GetAllAsync()).Returns(chatsMock);

        var attachmentsMock = (attachmentSeed ?? Array.Empty<TicketAttachment>()).BuildMock();
        var attachments = new Mock<IGenericRepository<TicketAttachment>>();
        attachments.Setup(r => r.GetAllAsync()).Returns(attachmentsMock);

        var logsMock = (logSeed ?? Array.Empty<MaintenanceLog>()).BuildMock();
        var logs = new Mock<IGenericRepository<MaintenanceLog>>();
        logs.Setup(r => r.GetAllAsync()).Returns(logsMock);

        var kbMock = (kbSeed ?? Array.Empty<KnowledgeBaseArticle>()).BuildMock();
        var kb = new Mock<IGenericRepository<KnowledgeBaseArticle>>();
        kb.Setup(r => r.GetAllAsync()).Returns(kbMock);
        kb.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => kbSeed?.FirstOrDefault(x => x.Id == (Guid)id));

        var kbVersionMock = (kbVersionSeed ?? Array.Empty<KbArticleVersion>()).BuildMock();
        var kbVersion = new Mock<IGenericRepository<KbArticleVersion>>();
        kbVersion.Setup(r => r.GetAllAsync()).Returns(kbVersionMock);

        var kbRefMock = (kbRefSeed ?? Array.Empty<TicketKbReference>()).BuildMock();
        var kbRefs = new Mock<IGenericRepository<TicketKbReference>>();
        var aiSuggestions = new Mock<IGenericRepository<TicketAiSuggestion>>();
        // BuildMock() để query async (FirstOrDefaultAsync) chạy được — thiếu nó thì EF ném
        // "provider doesn't implement IAsyncQueryProvider" ngay lần đọc đầu.
        aiSuggestions.Setup(r => r.GetAllAsync())
            .Returns((aiSuggestionSeed ?? Array.Empty<TicketAiSuggestion>()).BuildMock());
        kbRefs.Setup(r => r.GetAllAsync()).Returns(kbRefMock);
        kbRefs.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<TicketKbReference, bool>>>()))
              .ReturnsAsync((Expression<Func<TicketKbReference, bool>> p) => (kbRefSeed ?? Array.Empty<TicketKbReference>()).AsQueryable().Any(p));

        var outboxMock = (outboxSeed ?? Array.Empty<OutboxMessage>()).BuildMock();
        var outbox = new Mock<IGenericRepository<OutboxMessage>>();
        outbox.Setup(r => r.GetAllAsync()).Returns(outboxMock);

        var participantsMock = (participantSeed ?? Array.Empty<TicketParticipant>()).BuildMock();
        var participants = new Mock<IGenericRepository<TicketParticipant>>();
        participants.Setup(r => r.GetAllAsync()).Returns(participantsMock);
        participants.Setup(r => r.FindAsync(It.IsAny<Expression<Func<TicketParticipant, bool>>>())).Returns((Expression<Func<TicketParticipant, bool>> p) => participantsMock.Where(p));
        participants.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<TicketParticipant, bool>>>()))
              .ReturnsAsync((Expression<Func<TicketParticipant, bool>> p) => (participantSeed ?? Array.Empty<TicketParticipant>()).AsQueryable().Any(p));

        var assignmentsMock = (assignmentSeed ?? Array.Empty<TicketAssignment>()).BuildMock();
        var ticketAssignments = new Mock<IGenericRepository<TicketAssignment>>();
        ticketAssignments.Setup(r => r.GetAllAsync()).Returns(assignmentsMock);
        ticketAssignments.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<TicketAssignment, bool>>>()))
            .ReturnsAsync((Expression<Func<TicketAssignment, bool>> p) => (assignmentSeed ?? Array.Empty<TicketAssignment>()).AsQueryable().Any(p));

        var uow = new Mock<ITicketUnitOfWork>();
        uow.SetupGet(u => u.Tickets).Returns(tickets.Object);
        uow.SetupGet(u => u.TicketActivities).Returns(activities.Object);
        uow.SetupGet(u => u.CustomerAccounts).Returns(customers.Object);
        uow.SetupGet(u => u.StaffAccounts).Returns(staff.Object);
        uow.SetupGet(u => u.SlaTimers).Returns(slaTimers.Object);
        uow.SetupGet(u => u.SlaPauseEvents).Returns(slaPauseEvents.Object);
        uow.SetupGet(u => u.TicketChats).Returns(chats.Object);
        uow.SetupGet(u => u.TicketAttachments).Returns(attachments.Object);

        var ticketBatteryAssets = new Mock<IGenericRepository<TicketBatteryAsset>>();
        ticketBatteryAssets.Setup(r => r.AddAsync(It.IsAny<TicketBatteryAsset>())).Returns(Task.CompletedTask);
        uow.SetupGet(u => u.TicketBatteryAssets).Returns(ticketBatteryAssets.Object);
        uow.SetupGet(u => u.MaintenanceLogs).Returns(logs.Object);
        uow.SetupGet(u => u.KnowledgeBaseArticles).Returns(kb.Object);
        uow.SetupGet(u => u.KbArticleVersions).Returns(kbVersion.Object);
        uow.SetupGet(u => u.TicketKbReferences).Returns(kbRefs.Object);
        uow.SetupGet(u => u.TicketAiSuggestions).Returns(aiSuggestions.Object);
        uow.SetupGet(u => u.OutboxMessages).Returns(outbox.Object);
        uow.SetupGet(u => u.TicketParticipants).Returns(participants.Object);
        uow.SetupGet(u => u.TicketAssignments).Returns(ticketAssignments.Object);

        // Blog repos — default empty, test files override via SetupBlog*
        var blogPosts = new Mock<IGenericRepository<BlogPost>>();
        blogPosts.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogPost>().BuildMock());
        blogPosts.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<BlogPost, bool>>>())).ReturnsAsync(false);
        uow.SetupGet(u => u.BlogPosts).Returns(blogPosts.Object);

        var blogVersions = new Mock<IGenericRepository<BlogPostVersion>>();
        blogVersions.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogPostVersion>().BuildMock());
        uow.SetupGet(u => u.BlogPostVersions).Returns(blogVersions.Object);

        var blogTemplates = new Mock<IGenericRepository<BlogTemplate>>();
        blogTemplates.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogTemplate>().BuildMock());
        blogTemplates.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<BlogTemplate, bool>>>())).ReturnsAsync(false);
        uow.SetupGet(u => u.BlogTemplates).Returns(blogTemplates.Object);

        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task> operation, CancellationToken ct) => operation(ct));

        // Default rỗng cho 3 repo Chat Wave 4 (#536/#539/#541) — test nào cần seed data thì gọi
        // lại uow.SetupMentions/SetupReactions/SetupReads (MockChatExtraRepos.cs) sau BuildExtended,
        // Moq override setup cũ. Tránh NullReferenceException ở handler nào gọi GetAllAsync() trên
        // các repo này mà test chưa setup riêng (ví dụ TicketChatsQueryHandler populate Mentions/Reactions).
        uow.SetupMentions();
        uow.SetupReactions();
        uow.SetupReads();

        return (uow, tickets, activities, customers, staff, slaTimers, slaPauseEvents, chats, attachments, logs, kb, kbVersion, kbRefs, participants);
    }
}
