using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using SharedContracts.Events.Chats;
using SharedInfrastructure.Idempotency;
using SharedKernels.Interfaces;
using StackExchange.Redis;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;

namespace TicketService.UnitTests.Consumers;

public class ChatLanguageDetectConsumerTests
{
    private readonly Mock<ILanguageDetectionService> _langDetector = new();
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IInboxStore> _inbox = new();
    private readonly Mock<IConnectionMultiplexer> _redis = new();
    private readonly Mock<IDatabase> _redisDb = new();

    public ChatLanguageDetectConsumerTests()
    {
        _redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_redisDb.Object);
    }

    private ChatLanguageDetectConsumer CreateConsumer()
        => new(_langDetector.Object, _uow.Object, _inbox.Object, _redis.Object,
            NullLogger<ChatLanguageDetectConsumer>.Instance);

    private static ConsumeContext<ChatCreatedEvent> BuildContext(Guid chatId, string body, Guid? messageId = null)
    {
        var @event = new ChatCreatedEvent(chatId, Guid.NewGuid(), Guid.NewGuid(), 4,
            "Customer", body, false, [], Guid.NewGuid(), null);

        var ctx = new Mock<ConsumeContext<ChatCreatedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(@event);
        ctx.SetupGet(c => c.MessageId).Returns(messageId ?? Guid.NewGuid());
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx.Object;
    }

    private static TicketChat BuildChat(Guid id, string? originalLanguage = null) => new()
    {
        Id = id,
        Body = "Hello",
        OriginalLanguage = originalLanguage,
        Ticket = new Ticket { Id = Guid.NewGuid(), Code = "T-001", Title = "Test", Description = "Test" },
    };

    private Mock<IGenericRepository<TicketChat>> SetupChats(params TicketChat[] chats)
    {
        var repo = new Mock<IGenericRepository<TicketChat>>();
        repo.Setup(r => r.GetAllAsync()).Returns(chats.BuildMock());
        _uow.SetupGet(u => u.TicketChats).Returns(repo.Object);
        return repo;
    }

    #region Idempotency

    [Fact]
    public async Task Consume_DuplicateMessage_Skips()
    {
        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await CreateConsumer().Consume(BuildContext(Guid.NewGuid(), "Hello world test message"));

        _langDetector.Verify(d => d.Detect(It.IsAny<string>()), Times.Never);
    }

    #endregion

    #region Language detection — skip cases

    [Fact]
    public async Task Consume_LinguaReturnsUnd_SkipsDbQuery()
    {
        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("und");

        await CreateConsumer().Consume(BuildContext(Guid.NewGuid(), "hi"));

        _uow.VerifyGet(u => u.TicketChats, Times.Never);
    }

    [Fact]
    public async Task Consume_ChatNotFound_SkipsUpdate_NoException()
    {
        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("en");
        SetupChats(); // empty

        await CreateConsumer().Consume(BuildContext(Guid.NewGuid(), "Hello world test sentence"));

        _uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    [Fact]
    public async Task Consume_OriginalLanguageAlreadySet_SkipsUpdate()
    {
        var chatId = Guid.NewGuid();
        var chat = BuildChat(chatId, originalLanguage: "en");

        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("en");
        SetupChats(chat);

        await CreateConsumer().Consume(BuildContext(chatId, "Hello world test sentence"));

        _uow.Verify(u => u.BeginTransactionAsync(), Times.Never);
    }

    #endregion

    #region Happy path

    [Fact]
    public async Task Consume_DetectedEnglish_UpdatesOriginalLanguage()
    {
        var chatId = Guid.NewGuid();
        var chat = BuildChat(chatId);

        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("en");
        var repo = SetupChats(chat);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        await CreateConsumer().Consume(BuildContext(chatId, "Hello world test message"));

        chat.OriginalLanguage.Should().Be("en");
        repo.Verify(r => r.UpdateAsync(chat), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Consume_DetectedVietnamese_UpdatesOriginalLanguage()
    {
        var chatId = Guid.NewGuid();
        var chat = BuildChat(chatId);

        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("vi");
        var repo = SetupChats(chat);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);

        await CreateConsumer().Consume(BuildContext(chatId, "Pin đang ở trạng thái suy giảm"));

        chat.OriginalLanguage.Should().Be("vi");
        repo.Verify(r => r.UpdateAsync(chat), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    #endregion

    #region Error recovery

    [Fact]
    public async Task Consume_CommitThrows_DeletesInboxKey_Rethrows()
    {
        var messageId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var chat = BuildChat(chatId);
        var expectedKey = $"inbox:{nameof(ChatLanguageDetectConsumer)}:{messageId:N}";

        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("en");
        SetupChats(chat);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).ThrowsAsync(new Exception("DB unavailable"));
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        _redisDb.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var act = async () => await CreateConsumer().Consume(BuildContext(chatId, "Hello world test", messageId));

        await act.Should().ThrowAsync<Exception>().WithMessage("DB unavailable");

        _uow.Verify(u => u.RollbackTransactionAsync(), Times.Once);
        _redisDb.Verify(d => d.KeyDeleteAsync(expectedKey, It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Consume_CommitThrows_RedisDeleteFails_StillRethrowsOriginalException()
    {
        var chatId = Guid.NewGuid();
        var chat = BuildChat(chatId);

        _inbox.Setup(i => i.TryMarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _langDetector.Setup(d => d.Detect(It.IsAny<string>())).Returns("en");
        SetupChats(chat);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).ThrowsAsync(new Exception("DB unavailable"));
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);
        _redisDb.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisException("Redis down"));

        var act = async () => await CreateConsumer().Consume(BuildContext(chatId, "Hello world test", Guid.NewGuid()));

        await act.Should().ThrowAsync<Exception>().WithMessage("DB unavailable");
    }

    #endregion
}
