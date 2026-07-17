using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events.Blog;
using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Consumers;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Consumers;

public class BlogGenerationConsumerTests
{
    private readonly Mock<IBlogGeneratorService> _generatorMock = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxMock = new();

    private static Mock<ConsumeContext<BlogGenerationRequestedEvent>> MakeContext(BlogGenerationRequestedEvent evt)
    {
        var ctx = new Mock<ConsumeContext<BlogGenerationRequestedEvent>>();
        ctx.SetupGet(c => c.Message).Returns(evt);
        ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_BlogPostNotFound_SkipsWithoutError()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var consumer = new BlogGenerationConsumer(uow.Object, _generatorMock.Object, _outboxMock.Object,
            NullLogger<BlogGenerationConsumer>.Instance);

        var evt = new BlogGenerationRequestedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await consumer.Consume(MakeContext(evt).Object);

        _generatorMock.Verify(g => g.GenerateFromKbArticleAsync(It.IsAny<KnowledgeBaseArticle>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_BlogPostNotGenerating_SkipsWithoutError()
    {
        var postId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Status = BlogPostStatusEnum.Draft, Title = "T" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var blogPostsMock = new Mock<IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        var consumer = new BlogGenerationConsumer(uow.Object, _generatorMock.Object, _outboxMock.Object,
            NullLogger<BlogGenerationConsumer>.Instance);

        var evt = new BlogGenerationRequestedEvent(postId, Guid.NewGuid(), Guid.NewGuid());
        await consumer.Consume(MakeContext(evt).Object);

        _generatorMock.Verify(g => g.GenerateFromKbArticleAsync(It.IsAny<KnowledgeBaseArticle>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_KbArticleNotFound_MarksGenerationFailed()
    {
        var postId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Status = BlogPostStatusEnum.Generating, Title = "T" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var blogPostsMock = new Mock<IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);
        // KnowledgeBaseArticles stays empty (default from BuildExtended)

        _outboxMock.Setup(o => o.WriteAsync(It.IsAny<BlogGenerationStatusChangedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var consumer = new BlogGenerationConsumer(uow.Object, _generatorMock.Object, _outboxMock.Object,
            NullLogger<BlogGenerationConsumer>.Instance);

        var evt = new BlogGenerationRequestedEvent(postId, kbId, userId);
        await consumer.Consume(MakeContext(evt).Object);

        blogPostsMock.Verify(r => r.UpdateAsync(It.Is<BlogPost>(p => p.Status == BlogPostStatusEnum.GenerationFailed)), Times.Once);
        _outboxMock.Verify(o => o.WriteAsync(
            It.Is<BlogGenerationStatusChangedEvent>(e => e.Status == "GenerationFailed" && e.BlogPostId == postId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_DeepSeekSuccess_UpdatesPostToDraftAndPublishesEvent()
    {
        var postId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var post = new BlogPost { Id = postId, Status = BlogPostStatusEnum.Generating, Title = "AI Blog" };
        var article = new KnowledgeBaseArticle
        {
            Id = kbId,
            Title = "KB Article",
            Status = KbArticleStatusEnum.Published,
            Symptoms = "S",
            DiagnosisSteps = "D",
            SolutionSteps = "Sol",
            Tags = new List<string>()
        };

        var ext = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = ext.uow;

        var blogPostsMock = new Mock<IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        var blogVersionsMock = new Mock<IGenericRepository<BlogPostVersion>>();
        uow.SetupGet(u => u.BlogPostVersions).Returns(blogVersionsMock.Object);

        _generatorMock.Setup(g => g.GenerateFromKbArticleAsync(It.IsAny<KnowledgeBaseArticle>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("<h1>Generated Blog</h1><p>Content here.</p>");
        _outboxMock.Setup(o => o.WriteAsync(It.IsAny<BlogGenerationStatusChangedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var consumer = new BlogGenerationConsumer(uow.Object, _generatorMock.Object, _outboxMock.Object,
            NullLogger<BlogGenerationConsumer>.Instance);

        var evt = new BlogGenerationRequestedEvent(postId, kbId, userId);
        await consumer.Consume(MakeContext(evt).Object);

        blogVersionsMock.Verify(r => r.AddAsync(It.Is<BlogPostVersion>(v =>
            v.BlogPostId == postId && v.VersionNumber == 1 && v.ChangedByUserId == userId
        )), Times.Once);

        blogPostsMock.Verify(r => r.UpdateAsync(It.Is<BlogPost>(p =>
            p.Status == BlogPostStatusEnum.Draft && p.CurrentVersion == 1
        )), Times.Once);

        _outboxMock.Verify(o => o.WriteAsync(
            It.Is<BlogGenerationStatusChangedEvent>(e => e.BlogPostId == postId && e.Status == "Draft" && e.RequestedByUserId == userId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_DeepSeekThrows_MarksFailedAndRethrows()
    {
        var postId = Guid.NewGuid();
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var post = new BlogPost { Id = postId, Status = BlogPostStatusEnum.Generating, Title = "AI Blog" };
        var article = new KnowledgeBaseArticle
        {
            Id = kbId,
            Title = "KB Article",
            Status = KbArticleStatusEnum.Published,
            Symptoms = "S",
            DiagnosisSteps = "D",
            SolutionSteps = "Sol",
            Tags = new List<string>()
        };

        var ext = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = ext.uow;

        var blogPostsMock = new Mock<IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        _generatorMock.Setup(g => g.GenerateFromKbArticleAsync(It.IsAny<KnowledgeBaseArticle>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("DeepSeek timeout"));
        _outboxMock.Setup(o => o.WriteAsync(It.IsAny<BlogGenerationStatusChangedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var consumer = new BlogGenerationConsumer(uow.Object, _generatorMock.Object, _outboxMock.Object,
            NullLogger<BlogGenerationConsumer>.Instance);

        var evt = new BlogGenerationRequestedEvent(postId, kbId, userId);
        await Assert.ThrowsAsync<HttpRequestException>(() => consumer.Consume(MakeContext(evt).Object));

        blogPostsMock.Verify(r => r.UpdateAsync(It.Is<BlogPost>(p => p.Status == BlogPostStatusEnum.GenerationFailed)), Times.Once);
        _outboxMock.Verify(o => o.WriteAsync(
            It.Is<BlogGenerationStatusChangedEvent>(e => e.Status == "GenerationFailed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
