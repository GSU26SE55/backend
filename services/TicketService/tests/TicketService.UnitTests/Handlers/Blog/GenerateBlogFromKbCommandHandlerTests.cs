using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Events.Blog;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class GenerateBlogFromKbCommandHandlerTests
{
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxMock = new();

    [Fact]
    public async Task Handle_KbNotFound_ReturnsNotFound()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        // no KB seeded
        var kbMock = Array.Empty<KnowledgeBaseArticle>().BuildMock();
        uow.SetupGet(u => u.KnowledgeBaseArticles).Returns(SetupKbRepo(kbMock).Object);

        var handler = new GenerateBlogFromKbCommandHandler(uow.Object, _outboxMock.Object);
        var command = new GenerateBlogFromKbCommand { KbArticleId = Guid.NewGuid(), CurrentUserId = Guid.NewGuid() };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_KbNotPublished_ReturnsBadRequest()
    {
        var kbId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = kbId, Title = "Test", Status = KbArticleStatusEnum.Draft };

        var ext = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = ext.uow;

        var handler = new GenerateBlogFromKbCommandHandler(uow.Object, _outboxMock.Object);
        var command = new GenerateBlogFromKbCommand { KbArticleId = kbId, CurrentUserId = Guid.NewGuid() };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("Published");
    }

    [Fact]
    public async Task Handle_DuplicateBlogExists_ReturnsConflict()
    {
        var kbId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle { Id = kbId, Title = "Test", Status = KbArticleStatusEnum.Published };
        var existingBlog = new BlogPost { Id = Guid.NewGuid(), SourceKbArticleId = kbId, Status = BlogPostStatusEnum.Draft };

        var ext = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = ext.uow;

        var blogPostsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { existingBlog }.BuildMock());
        blogPostsMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>()))
            .ReturnsAsync(true);
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        var handler = new GenerateBlogFromKbCommandHandler(uow.Object, _outboxMock.Object);
        var command = new GenerateBlogFromKbCommand { KbArticleId = kbId, CurrentUserId = Guid.NewGuid() };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesBlogGeneratingAndPublishesEvent()
    {
        var kbId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = kbId,
            Title = "Test KB",
            Status = KbArticleStatusEnum.Published,
            Symptoms = "S",
            DiagnosisSteps = "D",
            SolutionSteps = "Sol",
            Tags = new List<string>()
        };

        var ext = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = ext.uow;

        var blogPostsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogPost>().BuildMock());
        blogPostsMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>()))
            .ReturnsAsync(false);
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        _outboxMock.Setup(o => o.WriteAsync(It.IsAny<BlogGenerationRequestedEvent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new GenerateBlogFromKbCommandHandler(uow.Object, _outboxMock.Object);
        var command = new GenerateBlogFromKbCommand { KbArticleId = kbId, CurrentUserId = userId };

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
        result.Data!.Status.Should().Be(BlogPostStatusEnum.Generating);

        blogPostsMock.Verify(r => r.AddAsync(It.Is<BlogPost>(p =>
            p.SourceKbArticleId == kbId &&
            p.Status == BlogPostStatusEnum.Generating &&
            p.Origin == BlogPostOriginEnum.AiGeneratedFromKb
        )), Times.Once);

        _outboxMock.Verify(o => o.WriteAsync(It.Is<BlogGenerationRequestedEvent>(e =>
            e.KbArticleId == kbId && e.RequestedByUserId == userId
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Mock<SharedKernels.Interfaces.IGenericRepository<KnowledgeBaseArticle>> SetupKbRepo(
        IQueryable<KnowledgeBaseArticle> data)
    {
        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<KnowledgeBaseArticle>>();
        mock.Setup(r => r.GetAllAsync()).Returns(data);
        return mock;
    }
}
