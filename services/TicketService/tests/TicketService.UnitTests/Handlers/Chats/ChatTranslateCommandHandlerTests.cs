using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.CQRS.Handler.ChatAi;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatTranslateCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IChatTextAiClient> _aiClient = new();

    private ChatTranslateCommandHandler CreateHandler() =>
        new(_uow.Object, _aiClient.Object, NullLogger<ChatTranslateCommandHandler>.Instance);

    private static Ticket BuildTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Pin bị lỗi",
        Description = "Mô tả",
        Status = TicketStatusEnum.Open,
    };

    private static TicketChat BuildChat(Guid ticketId, string body = "Xin chào") => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = BuildTicket(ticketId),
        Body = body,
        AuthorRole = ActorRoleEnum.Customer,
        AuthorDisplayName = "Khách hàng",
        OriginalLanguage = "vi",
    };

    private void SetupTickets(params Ticket[] tickets)
    {
        var repo = new Mock<IGenericRepository<Ticket>>();
        repo.Setup(r => r.GetAllAsync()).Returns(tickets.BuildMock());
        _uow.SetupGet(u => u.Tickets).Returns(repo.Object);
    }

    private void SetupChats(params TicketChat[] chats)
    {
        var repo = new Mock<IGenericRepository<TicketChat>>();
        repo.Setup(r => r.GetAllAsync()).Returns(chats.BuildMock());
        _uow.SetupGet(u => u.TicketChats).Returns(repo.Object);
    }

    #region Validation

    [Fact]
    public async Task Handle_EmptyTargetLanguage_ReturnsFailure()
    {
        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = string.Empty
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Target language is required.");
        _aiClient.Verify(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TargetLanguageTooLong_ReturnsFailure()
    {
        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "english"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Target language code must not exceed 5 characters.");
        _aiClient.Verify(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Not Found

    [Fact]
    public async Task Handle_TicketNotFound_ReturnsFailure()
    {
        SetupTickets();
        SetupChats();

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Ticket not found.");
    }

    [Fact]
    public async Task Handle_ChatNotFound_ReturnsFailure()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(BuildTicket(ticketId));
        SetupChats();
        _uow.SetupChatTranslations();

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Chat not found.");
    }

    #endregion

    #region DB Hit — reuse existing translation

    [Fact]
    public async Task Handle_DbHit_UserNotLinked_AddsUserLink_NoAiCall()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chat = BuildChat(ticketId);
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var existingTranslation = new TicketChatTranslation
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            TargetLanguage = "en",
            TranslatedBody = "Hello",
            Provider = TranslationProviderEnum.DeepSeekAi,
            TranslatedAt = DateTime.UtcNow,
            Chat = chat,
        };
        _uow.SetupChatTranslations(new List<TicketChatTranslation> { existingTranslation });

        var userLinkRepo = _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = userId,
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello");
        _aiClient.Verify(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        userLinkRepo.Verify(r => r.AddAsync(It.Is<TicketChatTranslationUser>(u =>
            u.TranslationId == existingTranslation.Id && u.UserId == userId)), Times.Once);
    }

    [Fact]
    public async Task Handle_DbHit_UserAlreadyLinked_NoNewLink_NoAiCall()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chat = BuildChat(ticketId);
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var translationId = Guid.NewGuid();
        var existingTranslation = new TicketChatTranslation
        {
            Id = translationId,
            ChatId = chat.Id,
            TargetLanguage = "en",
            TranslatedBody = "Hello",
            Provider = TranslationProviderEnum.DeepSeekAi,
            TranslatedAt = DateTime.UtcNow,
            Chat = chat,
        };
        _uow.SetupChatTranslations(new List<TicketChatTranslation> { existingTranslation });

        var existingLink = new TicketChatTranslationUser
        {
            Id = Guid.NewGuid(),
            TranslationId = translationId,
            UserId = userId,
        };
        var userLinkRepo = _uow.SetupChatTranslationUsers(new List<TicketChatTranslationUser> { existingLink });

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = userId,
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello");
        _aiClient.Verify(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        userLinkRepo.Verify(r => r.AddAsync(It.IsAny<TicketChatTranslationUser>()), Times.Never);
    }

    #endregion

    #region Happy Path — AI Translate

    [Fact]
    public async Task Handle_AiTranslate_PersistsTranslationAndUserLink()
    {
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var translations = new List<TicketChatTranslation>();
        _uow.SetupChatTranslations(translations);

        var userLinkRepo = _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        _aiClient.Setup(c => c.TranslateAsync("Xin chào", "en", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Hello");

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = userId,
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello");
        result.Data.TargetLanguage.Should().Be("en");
        result.Data.FromCache.Should().BeFalse();

        translations.Should().ContainSingle(t =>
            t.TranslatedBody == "Hello" &&
            t.TargetLanguage == "en");

        userLinkRepo.Verify(r => r.AddAsync(It.Is<TicketChatTranslationUser>(u =>
            u.UserId == userId)), Times.Once);
    }

    [Fact]
    public async Task Handle_TargetLanguageUpperCase_NormalizesToLowercase()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var translations = new List<TicketChatTranslation>();
        _uow.SetupChatTranslations(translations);
        _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        _aiClient.Setup(c => c.TranslateAsync(It.IsAny<string>(), "en", It.IsAny<CancellationToken>()))
                 .ReturnsAsync("Hello");

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "EN"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        translations.Should().ContainSingle(t => t.TargetLanguage == "en");
    }

    #endregion

    #region AI Error

    [Fact]
    public async Task Handle_AiRateLimited_Returns429()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId);
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();
        _uow.SetupChatTranslationUsers();

        _aiClient.Setup(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("RATE_LIMITED"));

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task Handle_AiClientThrows_ReturnsTranslationUnavailable()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();
        _uow.SetupChatTranslationUsers();

        _aiClient.Setup(c => c.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new HttpRequestException("DeepSeek down"));

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("Translation service unavailable.");
    }

    #endregion
}
