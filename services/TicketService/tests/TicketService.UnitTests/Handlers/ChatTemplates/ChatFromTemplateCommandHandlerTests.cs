using Microsoft.Extensions.Logging;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events.Chats;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.CQRS.Handler.ChatTemplates;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.ChatTemplates;

public class ChatFromTemplateCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<ITemplateRenderer> _renderer = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _notifier = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();
    private readonly Mock<IChatCacheService> _chatCache = new();
    private readonly Mock<ILogger<ChatFromTemplateCommandHandler>> _logger = new();

    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketChat>> _chatsRepo = new();
    private readonly Mock<IGenericRepository<TicketParticipant>> _participantsRepo = new();
    private readonly Mock<IGenericRepository<ChatTemplate>> _templatesRepo;

    public ChatFromTemplateCommandHandlerTests()
    {
        _uow.SetupGet(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.SetupGet(u => u.TicketChats).Returns(_chatsRepo.Object);
        _uow.SetupGet(u => u.TicketParticipants).Returns(_participantsRepo.Object);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _templatesRepo = _uow.SetupChatTemplates();

        _participantsRepo.Setup(r => r.GetAllAsync())
            .Returns(() => new TestAsyncEnumerable<TicketParticipant>(new List<TicketParticipant>()));
    }

    private ChatFromTemplateCommandHandler CreateHandler() =>
        new(_uow.Object, _renderer.Object, _notifier.Object, _outboxWriter.Object, _chatCache.Object, _logger.Object);

    private static Ticket MakeTicket(Guid id, Guid customerId, Guid? assignedStaffId = null) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test",
        Description = "desc",
        CustomerId = customerId,
        AssignedStaffId = assignedStaffId,
        Status = TicketStatusEnum.InProgress
    };

    private void SetupTickets(List<Ticket> tickets) =>
        _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_ValidTemplateAndTicket_CreatesChat()
    {
        var ticketId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, customerId, assignedStaffId: actorId);

        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "Welcome",
            Content = "Hello {{name}}!",
            IsInternalDefault = false,
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        SetupTickets([ticket]);
        _renderer.Setup(r => r.Render("Hello {{name}}!", It.IsAny<object>())).Returns("Hello Staff!");

        var command = new ChatFromTemplateCommand
        {
            TicketId = ticketId,
            TemplateId = templateId,
            ActorUserId = actorId,
            ActorRole = ActorRoleEnum.Staff,
            ActorDisplayName = "Staff User",
            ActorRoles = ["Staff"],
            Variables = new Dictionary<string, string> { ["name"] = "Staff" }
        };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        _chatsRepo.Verify(r => r.AddAsync(It.Is<TicketChat>(c =>
            c.TicketId == ticketId &&
            c.Body == "Hello Staff!" &&
            c.AuthorUserId == actorId &&
            c.IsInternal == false)), Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateNotFound_Returns404()
    {
        var templateId = Guid.NewGuid();
        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync((ChatTemplate?)null);

        var command = new ChatFromTemplateCommand
        {
            TicketId = Guid.NewGuid(),
            TemplateId = templateId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var templateId = Guid.NewGuid();
        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "T",
            Content = "C",
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);
        _ticketsRepo.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((Ticket?)null);
        SetupTickets([]);

        var command = new ChatFromTemplateCommand
        {
            TicketId = Guid.NewGuid(),
            TemplateId = templateId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Staff"]
        };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ActorNoAccess_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var ticket = MakeTicket(ticketId, ownerId);
        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "T",
            Content = "C",
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);
        _ticketsRepo.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);
        SetupTickets([ticket]);

        var command = new ChatFromTemplateCommand
        {
            TicketId = ticketId,
            TemplateId = templateId,
            ActorUserId = strangerId,
            ActorRoles = ["Staff"]
        };

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }
}
