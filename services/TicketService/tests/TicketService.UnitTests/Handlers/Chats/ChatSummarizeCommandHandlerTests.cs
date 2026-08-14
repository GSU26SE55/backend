using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.CQRS.Handler.ChatAi;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatSummarizeCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IChatTextAiClient> _aiClient = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly IOptions<ChatOptions> _opts = Options.Create(new ChatOptions());

    private ChatSummarizeCommandHandler CreateHandler()
    {
        _piiDetector
            .Setup(p => p.MaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => (text, string.Empty));
        return new(_uow.Object, _aiClient.Object, _piiDetector.Object, _opts, NullLogger<ChatSummarizeCommandHandler>.Instance);
    }

    private static Ticket BuildTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Battery fault",
        Description = "Fault description",
        Status = TicketStatusEnum.Open,
    };

    private static TicketChat BuildChat(Guid ticketId, string body, ActorRoleEnum role = ActorRoleEnum.Customer) => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = BuildTicket(ticketId),
        Body = body,
        AuthorRole = role,
        AuthorDisplayName = role == ActorRoleEnum.Customer ? "Customer" : "Technician",
        IsInternal = false,
        CreatedAt = DateTime.UtcNow,
    };

    private void SetupTickets(params Ticket[] tickets)
    {
        var mock = tickets.BuildMock();
        var repo = new Mock<IGenericRepository<Ticket>>();
        repo.Setup(r => r.GetAllAsync()).Returns(mock);
        _uow.SetupGet(u => u.Tickets).Returns(repo.Object);
    }

    private void SetupChats(params TicketChat[] chats)
    {
        var mock = chats.BuildMock();
        var repo = new Mock<IGenericRepository<TicketChat>>();
        repo.Setup(r => r.GetAllAsync()).Returns(mock);
        _uow.SetupGet(u => u.TicketChats).Returns(repo.Object);
    }

    #region Ticket Not Found

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsFailure()
    {
        SetupTickets();
        SetupChats();

        var result = await CreateHandler().Handle(new ChatSummarizeCommand
        {
            TicketId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Ticket not found", result.Message);
        Assert.Equal(200, result.StatusCode);
        _aiClient.Verify(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region No Chats

    [Fact]
    public async Task Handle_NoChats_ReturnsFailure()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(); // empty

        var result = await CreateHandler().Handle(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("No chats to summarize", result.Message);
        Assert.Equal(200, result.StatusCode);
        _aiClient.Verify(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Happy Path

    [Fact]
    public async Task Handle_ValidTicketWithChats_ReturnsSummary()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(
            BuildChat(ticketId, "My battery will not charge"),
            BuildChat(ticketId, "We will check it", ActorRoleEnum.Staff)
        );
        const string expectedSummary = "- Customer reports the battery is not charging\n- Technician is checking it";
        _aiClient.Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(expectedSummary);

        var result = await CreateHandler().Handle(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(expectedSummary, result.Data!.Summary);
        _aiClient.Verify(c => c.SummarizeAsync(
            It.IsAny<string>(),
            5, // default SummarizeLinesCount from ChatOptions
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PassesLinesCountFromConfig()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildChat(ticketId, "Test message"));

        var opts = Options.Create(new ChatOptions
        {
            Ai = new ChatOptions.AiSection { SummarizeLinesCount = 3 }
        });
        var handler = new ChatSummarizeCommandHandler(_uow.Object, _aiClient.Object, _piiDetector.Object, opts, NullLogger<ChatSummarizeCommandHandler>.Instance);

        _aiClient.Setup(c => c.SummarizeAsync(It.IsAny<string>(), 3, It.IsAny<CancellationToken>()))
                 .ReturnsAsync("- point 1\n- point 2\n- point 3");

        var result = await handler.Handle(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _aiClient.Verify(c => c.SummarizeAsync(It.IsAny<string>(), 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region AI Client Error

    [Fact]
    public async Task Handle_AiClientThrows_ReturnsAiUnavailable()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildChat(ticketId, "Test message"));
        _aiClient.Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new HttpRequestException("Gemini down"));

        var result = await CreateHandler().Handle(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AI service unavailable", result.Message);
        Assert.Equal(200, result.StatusCode);
    }

    #endregion

    #region Context Building

    [Fact]
    public async Task Handle_ContextIncludesAllRoles()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(
            BuildChat(ticketId, "Hello", ActorRoleEnum.Customer),
            BuildChat(ticketId, "Hi there", ActorRoleEnum.Staff),
            BuildChat(ticketId, "Note internal", ActorRoleEnum.Manager)
        );

        string? capturedContext = null;
        _aiClient.Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .Callback((string ctx, int _, CancellationToken _) => capturedContext = ctx)
                 .ReturnsAsync("- summary");

        await CreateHandler().Handle(new ChatSummarizeCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.NotNull(capturedContext);
        Assert.Contains("Customer", capturedContext);
        Assert.Contains("Staff", capturedContext);
        Assert.Contains("Manager", capturedContext);
    }

    #endregion
}
