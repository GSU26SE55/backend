using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.CQRS.Handler.ChatAi;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Chats;

public class ChatTranslateCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IChatTextAiClient> _aiClient = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IChatAuthorizationService> _chatAuth = new();
    private readonly Mock<IPiiDetector> _piiDetector = new();
    private readonly Mock<ITechnicalTermMasker> _termMasker = new();

    private ChatTranslateCommandHandler CreateHandler()
    {
        // Default: auth allows access for all tickets/users
        _chatAuth
            .Setup(a => a.CanAccessTicketAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Default: Redis cache miss for both translation and lock keys
        _cache
            .Setup(c => c.GetAsync<string>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Cache write operations are no-ops
        _cache
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cache
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Default: PII detector is a pass-through (no PII in test bodies)
        _piiDetector
            .Setup(p => p.MaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => (text, string.Empty));
        _piiDetector
            .Setup(p => p.UnmaskAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, string _, CancellationToken _) => text);

        // Default: term masker is a pass-through (no terms in default test bodies)
        _termMasker
            .Setup(m => m.Mask(It.IsAny<string>()))
            .Returns((string text) => (text, (IReadOnlyDictionary<string, string>)new Dictionary<string, string>()));
        _termMasker
            .Setup(m => m.Unmask(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()))
            .Returns((string text, IReadOnlyDictionary<string, string> _) => text);

        return new(_uow.Object, _aiClient.Object, _cache.Object, _chatAuth.Object,
            _piiDetector.Object, _termMasker.Object, NullLogger<ChatTranslateCommandHandler>.Instance);
    }

    private static Ticket BuildTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Battery fault",
        Description = "Description",
        Status = TicketStatusEnum.Open,
    };

    private static TicketChat BuildChat(Guid ticketId, string body = "Xin chào") => new()
    {
        Id = Guid.NewGuid(),
        TicketId = ticketId,
        Ticket = BuildTicket(ticketId),
        Body = body,
        AuthorRole = ActorRoleEnum.Customer,
        AuthorDisplayName = "Customer",
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
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync("Xin chào", "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

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

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

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

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
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

    #region Redis Cache-Aside

    [Fact]
    public async Task Handle_RedisCacheHit_ReturnsFromCache_NoAiCall()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var handler = CreateHandler();

        // Override default cache-miss with a hit for translation keys (must be after CreateHandler)
        _cache
            .Setup(c => c.GetAsync<string>(It.Is<string>(k => k.StartsWith("chat-translation:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Hello (cached)");

        var result = await handler.Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello (cached)");
        result.Data.FromCache.Should().BeTrue();
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region OriginalLanguage — source == target skip / knownSource

    [Fact]
    public async Task Handle_OriginalLanguageMatchesTarget_ReturnsOriginalBody_NoAiCall()
    {
        var ticketId = Guid.NewGuid();
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = BuildTicket(ticketId),
            Body = "Hello there",
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Customer",
            OriginalLanguage = "en",  // consumer already set this
        };
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello there");
        result.Data.Provider.Should().Be("None");
        result.Data.FromCache.Should().BeFalse();
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_OriginalLanguageSet_UsedAsKnownSourceForAi()
    {
        // OriginalLanguage already set by consumer → passed as knownSource to AI
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");  // OriginalLanguage = "vi"
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();
        _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", "vi", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OriginalLanguageNull_PassesNullKnownSourceToAi()
    {
        // Consumer hasn't run yet (race) → OriginalLanguage null → AI detects itself
        var ticketId = Guid.NewGuid();
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = BuildTicket(ticketId),
            Body = "Xin chào",
            AuthorRole = ActorRoleEnum.Customer,
            AuthorDisplayName = "Customer",
            OriginalLanguage = null,
        };
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();
        _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Security

    [Fact]
    public async Task Handle_AccessDenied_Returns403()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId);
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        var handler = CreateHandler();

        // Override default "allow all" with deny (must be after CreateHandler)
        _chatAuth
            .Setup(a => a.CanAccessTicketAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await handler.Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Access denied.");
    }

    #endregion

    #region Mutex Lock — soft lock contention

    [Fact]
    public async Task Handle_LockContention_DbStillEmpty_Returns202()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId);
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations(); // DB empty

        var handler = CreateHandler();

        // Lock is held, translation key is a miss
        _cache
            .Setup(c => c.GetAsync<string>(It.Is<string>(k => k.StartsWith("translation-lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1");

        var result = await handler.Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(202);
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_LockContention_DbRetrySucceeds_ReturnsTranslation()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        // First call: DB empty (step 5). After lock detected: DB has translation (retry at step 7).
        var translationFound = false;
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

        var translationsRepo = new Mock<IGenericRepository<TicketChatTranslation>>();
        translationsRepo
            .Setup(r => r.GetAllAsync())
            .Returns(() => translationFound
                ? new List<TicketChatTranslation> { existingTranslation }.BuildMock()
                : new List<TicketChatTranslation>().BuildMock());
        _uow.SetupGet(u => u.TicketChatTranslations).Returns(translationsRepo.Object);

        var handler = CreateHandler();

        // Lock key hit — triggers the retry DB check
        _cache
            .Setup(c => c.GetAsync<string>(It.Is<string>(k => k.StartsWith("translation-lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1")
            .Callback(() => translationFound = true); // simulate race resolved when lock check runs

        var result = await handler.Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello");
        _aiClient.Verify(c => c.TranslateWithDetectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region DbUpdateException race condition

    [Fact]
    public async Task Handle_DbUpdateException_ReloadsFromDb_ReturnsTranslation()
    {
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        // Empty initially; populated after DbUpdateException (simulating race resolved in DB)
        var raceTranslation = new TicketChatTranslation
        {
            Id = Guid.NewGuid(),
            ChatId = chat.Id,
            TargetLanguage = "en",
            TranslatedBody = "Hello (race winner)",
            Provider = TranslationProviderEnum.DeepSeekAi,
            TranslatedAt = DateTime.UtcNow,
            Chat = chat,
        };

        var callCount = 0;
        var translationsRepo = new Mock<IGenericRepository<TicketChatTranslation>>();
        translationsRepo
            .Setup(r => r.GetAllAsync())
            .Returns(() =>
            {
                callCount++;
                // First 2 calls: empty (step 5 + step 7 retry if lock). Third call (post-DbUpdateException reload): has translation.
                return callCount >= 2
                    ? new List<TicketChatTranslation> { raceTranslation }.BuildMock()
                    : new List<TicketChatTranslation>().BuildMock();
            });
        _uow.SetupGet(u => u.TicketChatTranslations).Returns(translationsRepo.Object);

        _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync())
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("Unique constraint", new Exception()));

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TranslatedBody.Should().Be("Hello (race winner)");
    }

    #endregion

    #region Technical Term Masking

    [Fact]
    public async Task Handle_AiTranslate_CallsTermMaskerMaskOnPiiMaskedBodyAndUnmaskOnAiResult()
    {
        // Verify mask() is called with the PII-masked chat body,
        // and unmask() is called after AI translates. Exact roundtrip tested in TechnicalTermMaskerTests.
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();
        _uow.SetupChatTranslationUsers();
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        _aiClient
            .Setup(c => c.TranslateWithDetectAsync(It.IsAny<string>(), "en", "vi", It.IsAny<CancellationToken>()))
            .ReturnsAsync(("Hello", "vi"));

        await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "en"
        }, CancellationToken.None);

        // Mask called once with the (PII-masked) body
        _termMasker.Verify(m => m.Mask("Xin chào"), Times.Once);
        // Unmask called once after AI returns
        _termMasker.Verify(m => m.Unmask(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OriginalLanguageMatchesTarget_TermMaskerNotCalled()
    {
        // When source == target, AI is skipped — term masker must NOT be called
        var ticketId = Guid.NewGuid();
        var chat = BuildChat(ticketId, "Xin chào");  // OriginalLanguage = "vi"
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);
        _uow.SetupChatTranslations();

        await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            TargetLanguage = "vi"  // same as OriginalLanguage
        }, CancellationToken.None);

        _termMasker.Verify(m => m.Mask(It.IsAny<string>()), Times.Never);
        _termMasker.Verify(m => m.Unmask(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>>()), Times.Never);
    }

    #endregion

    #region Security

    [Fact]
    public async Task Handle_InternalChat_CustomerCannotTranslate_Returns403()
    {
        // TC04: Customer dịch chat IsInternal = true → 403
        var ticketId = Guid.NewGuid();
        var chat = new TicketChat
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Ticket = BuildTicket(ticketId),
            Body = "Internal note",
            AuthorRole = ActorRoleEnum.Staff,
            AuthorDisplayName = "Staff member",
            OriginalLanguage = "en",
            IsInternal = true,
        };
        SetupTickets(BuildTicket(ticketId));
        SetupChats(chat);

        // General access allowed, but internal view denied
        _chatAuth
            .Setup(a => a.CanViewInternalChatsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await CreateHandler().Handle(new ChatTranslateCommand
        {
            TicketId = ticketId,
            ChatId = chat.Id,
            CurrentUserId = Guid.NewGuid(),
            CurrentUserRoles = ["Customer"],
            TargetLanguage = "vi"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("You do not have permission to translate internal messages.");
    }

    #endregion
}
