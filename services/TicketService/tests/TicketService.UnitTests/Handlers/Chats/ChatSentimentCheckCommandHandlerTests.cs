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

public class ChatSentimentCheckCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IChatTextAiClient> _aiClient = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly Mock<ITicketChatRealtimeNotifier> _notifier = new();
    private readonly IOptions<ChatOptions> _opts = Options.Create(new ChatOptions());

    private ChatSentimentCheckCommandHandler CreateHandler()
    {
        _piiDetector
            .Setup(p => p.MaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => (text, string.Empty));
        return new(_uow.Object, _aiClient.Object, _piiDetector.Object, _notifier.Object, _opts);
    }

    private static Ticket BuildTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Pin bị lỗi",
        Description = "Mô tả lỗi",
        Status = TicketStatusEnum.Open,
    };

    private static TicketChat BuildCustomerChat(Guid ticketId, string body = "Tôi không hài lòng") => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = BuildTicket(ticketId),
        Body = body,
        AuthorRole = ActorRoleEnum.Customer,
        IsInternal = false,
        CreatedAt = DateTime.UtcNow,
    };

    private void SetupTickets(params Ticket[] tickets)
    {
        var ticketsMock = tickets.BuildMock();
        var repo = new Mock<IGenericRepository<Ticket>>();
        repo.Setup(r => r.GetAllAsync()).Returns(ticketsMock);
        _uow.SetupGet(u => u.Tickets).Returns(repo.Object);
    }

    private void SetupChats(params TicketChat[] chats)
    {
        var chatsMock = chats.BuildMock();
        var repo = new Mock<IGenericRepository<TicketChat>>();
        repo.Setup(r => r.GetAllAsync()).Returns(chatsMock);
        _uow.SetupGet(u => u.TicketChats).Returns(repo.Object);
    }

    #region Ticket Not Found

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsFailure()
    {
        SetupTickets();
        SetupChats();

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Ticket not found", result.Message);
        Assert.Equal(200, result.StatusCode);
        _aiClient.Verify(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region No Customer Chats

    [Fact]
    public async Task Handle_NoCustomerChats_ReturnsFailure()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        // Only staff chats — no customer chats
        SetupChats(new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = BuildTicket(ticketId),
            Body = "Note nội bộ",
            AuthorRole = ActorRoleEnum.Staff,
            IsInternal = true,
        });

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("No customer chats to analyze", result.Message);
        Assert.Equal(200, result.StatusCode);
        _aiClient.Verify(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Happy Path — Labels

    [Theory]
    [InlineData(0.5, "Positive")]
    [InlineData(0.3, "Positive")]
    [InlineData(0.0, "Neutral")]
    [InlineData(-0.29, "Neutral")]
    [InlineData(-0.3, "Neutral")]
    [InlineData(-0.5, "Negative")]
    [InlineData(-0.69, "Negative")]
    [InlineData(-0.71, "Critical")]
    [InlineData(-1.0, "Critical")]
    public async Task Handle_ValidScore_ReturnsCorrectLabel(double score, string expectedLabel)
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(score);

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(score, result.Data!.Score);
        Assert.Equal(expectedLabel, result.Data.Label);
    }

    #endregion

    #region Sentiment Alert

    [Fact]
    public async Task Handle_ScoreBelowThreshold_SendsSignalRAlertAndSetsIsAlertSent()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId, "Tôi rất tức giận!"));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(-0.9); // below default threshold -0.7
        _notifier.Setup(n => n.NotifySentimentAlertAsync(ticketId, It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data!.IsAlertSent);
        _notifier.Verify(n => n.NotifySentimentAlertAsync(ticketId, -0.9, "Critical", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ScoreAboveThreshold_NoAlertSent()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId, "Cảm ơn bạn."));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(-0.5); // above default threshold -0.7

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data!.IsAlertSent);
        _notifier.Verify(n => n.NotifySentimentAlertAsync(It.IsAny<Guid>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NotifierThrows_StillReturnsSuccessWithIsAlertSentFalse()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId, "Tôi rất tức giận!"));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(-0.9);
        _notifier.Setup(n => n.NotifySentimentAlertAsync(It.IsAny<Guid>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new Exception("SignalR down"));

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);   // không crash dù SignalR fail
        Assert.False(result.Data!.IsAlertSent);
    }

    #endregion

    #region AI Client Error

    [Fact]
    public async Task Handle_AiClientThrows_ReturnsAiUnavailable()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new HttpRequestException("Gemini down"));

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AI service unavailable", result.Message);
        Assert.Equal(200, result.StatusCode);
        _notifier.Verify(n => n.NotifySentimentAlertAsync(It.IsAny<Guid>(), It.IsAny<double>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Score Clamping

    [Fact]
    public async Task Handle_ScoreOutOfRange_ClampedToMinusOne()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats(BuildCustomerChat(ticketId));
        _aiClient.Setup(c => c.AnalyzeSentimentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(-5.0); // out of range

        var result = await CreateHandler().Handle(new ChatSentimentCheckCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-1.0, result.Data!.Score);
    }

    #endregion
}
