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

public class ChatSuggestCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly Mock<IChatAiSuggestionClient> _aiClient = new();
    private readonly IOptions<ChatOptions> _opts = Options.Create(new ChatOptions());
    private readonly Mock<IGenericRepository<ChatAiSuggestion>> _aiSuggestions = new();

    public ChatSuggestCommandHandlerTests()
    {
        _uow.SetupGet(u => u.ChatAiSuggestions).Returns(_aiSuggestions.Object);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        _piiDetector
            .Setup(p => p.MaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(("masked context", "pii:mask:test"));
    }

    private ChatSuggestCommandHandler CreateHandler() =>
        new(_uow.Object, _piiDetector.Object, _aiClient.Object, _opts);

    private static Ticket BuildTicket(Guid id, List<TicketChat>? chats = null) => new()
    {
        Id = id,
        Code = "TK-001",
        Title = "Pin bị lỗi",
        Description = "Mô tả lỗi pin",
        Category = TicketCategoryEnum.Charging,
        Status = TicketStatusEnum.Open,
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Origin = TicketOriginEnum.CreatedByStaff,
        Chats = chats ?? new List<TicketChat>(),
    };

    #region Happy Path

    [Fact]
    public async Task Handle_ValidTicket_ReturnsSuggestions()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        _uow.SetupGet(u => u.Tickets).Returns(
            new Mock<IGenericRepository<Ticket>>()
                .Also(m => m.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.BuildMock()))
                .Object);

        var expectedSuggestions = new List<string> { "Gợi ý 1", "Gợi ý 2", "Gợi ý 3" };
        _aiClient
            .Setup(c => c.SuggestAsync(It.IsAny<string>(), It.IsAny<ChatAiIntentEnum>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedSuggestions);

        var command = new ChatSuggestCommand
        {
            TicketId = ticketId,
            CurrentUserId = Guid.NewGuid(),
            Intent = ChatAiIntentEnum.TechnicalAnswer,
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Suggestions.Count);
        Assert.Equal("Gợi ý 1", result.Data.Suggestions[0]);
        _aiSuggestions.Verify(r => r.AddAsync(It.IsAny<ChatAiSuggestion>()), Times.Once);
    }

    #endregion

    #region Ticket Not Found

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsFailure()
    {
        // Arrange
        _uow.SetupGet(u => u.Tickets).Returns(
            new Mock<IGenericRepository<Ticket>>()
                .Also(m => m.Setup(r => r.GetAllAsync()).Returns(new List<Ticket>().BuildMock()))
                .Object);

        var command = new ChatSuggestCommand
        {
            TicketId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            Intent = ChatAiIntentEnum.TechnicalAnswer,
        };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("Ticket not found", result.Message);
        _aiClient.Verify(c => c.SuggestAsync(It.IsAny<string>(), It.IsAny<ChatAiIntentEnum>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region AI Client Throws

    [Fact]
    public async Task Handle_AiClientThrows_ReturnsAiUnavailable()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        _uow.SetupGet(u => u.Tickets).Returns(
            new Mock<IGenericRepository<Ticket>>()
                .Also(m => m.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.BuildMock()))
                .Object);

        _aiClient
            .Setup(c => c.SuggestAsync(It.IsAny<string>(), It.IsAny<ChatAiIntentEnum>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Gemini down"));

        var command = new ChatSuggestCommand { TicketId = ticketId, CurrentUserId = Guid.NewGuid() };

        // Act
        var result = await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("AI service unavailable", result.Message);
        _aiSuggestions.Verify(r => r.AddAsync(It.IsAny<ChatAiSuggestion>()), Times.Never);
    }

    #endregion

    #region PII Mask Called Before AI

    [Fact]
    public async Task Handle_PiiMaskCalledBeforeAiClient()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = BuildTicket(ticketId);

        _uow.SetupGet(u => u.Tickets).Returns(
            new Mock<IGenericRepository<Ticket>>()
                .Also(m => m.Setup(r => r.GetAllAsync()).Returns(new List<Ticket> { ticket }.BuildMock()))
                .Object);

        var callOrder = new List<string>();

        _piiDetector
            .Setup(p => p.MaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, CancellationToken _) => callOrder.Add("mask"))
            .ReturnsAsync(("masked", "key"));

        _aiClient
            .Setup(c => c.SuggestAsync(It.IsAny<string>(), It.IsAny<ChatAiIntentEnum>(),
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback((string _, ChatAiIntentEnum _, string? _, int _, CancellationToken _) => callOrder.Add("ai"))
            .ReturnsAsync(new List<string> { "s1", "s2", "s3" });

        var command = new ChatSuggestCommand { TicketId = ticketId, CurrentUserId = Guid.NewGuid() };

        // Act
        await CreateHandler().Handle(command, CancellationToken.None);

        // Assert
        Assert.Equal(new[] { "mask", "ai" }, callOrder);
    }

    #endregion
}

// Extension helper để setup Mock inline
file static class MockExtensions
{
    public static Mock<T> Also<T>(this Mock<T> mock, Action<Mock<T>> setup) where T : class
    {
        setup(mock);
        return mock;
    }
}
