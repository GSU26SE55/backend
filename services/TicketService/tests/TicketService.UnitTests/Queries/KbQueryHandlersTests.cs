using System.Text.Json;
using TicketService.Application.CQRS.Handler.KnowledgeBase;
using TicketService.Application.CQRS.Query.KnowledgeBase;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class KbQueryHandlersTests
{
    private readonly Mock<ITicketCurrentUserService> _currentUserService = new();

    [Fact]
    public async Task GetById_ValidPublished_ReturnsArticle()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _currentUserService.SetupGet(c => c.UserId).Returns(userId);
        _currentUserService.SetupGet(c => c.Role).Returns("Customer");

        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "Trouble",
            Status = KbArticleStatusEnum.Published,
            IsInternalOnly = false,
            IsDeleted = false
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new GetKbArticleByIdQueryHandler(uow.Object, _currentUserService.Object);
        var query = new GetKbArticleByIdQuery { ArticleId = articleId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Title.Should().Be("Trouble");
    }

    [Fact]
    public async Task GetById_InternalOnlyForCustomer_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _currentUserService.SetupGet(c => c.UserId).Returns(userId);
        _currentUserService.SetupGet(c => c.Role).Returns("Customer");

        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "Secret",
            Status = KbArticleStatusEnum.Published,
            IsInternalOnly = true,
            IsDeleted = false
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new GetKbArticleByIdQueryHandler(uow.Object, _currentUserService.Object);
        var query = new GetKbArticleByIdQuery { ArticleId = articleId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetList_Customer_ReturnsOnlyPublishedPublic()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString();
        _currentUserService.SetupGet(c => c.UserId).Returns(userId);
        _currentUserService.SetupGet(c => c.Role).Returns("Customer");

        var article1 = new KnowledgeBaseArticle { Id = Guid.NewGuid(), Title = "A", Status = KbArticleStatusEnum.Published, IsInternalOnly = false };
        var article2 = new KnowledgeBaseArticle { Id = Guid.NewGuid(), Title = "B", Status = KbArticleStatusEnum.Draft, IsInternalOnly = false };
        var article3 = new KnowledgeBaseArticle { Id = Guid.NewGuid(), Title = "C", Status = KbArticleStatusEnum.Published, IsInternalOnly = true };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article1, article2, article3 });
        var uow = resultExtended.uow;

        var handler = new GetKbArticleListQueryHandler(uow.Object, _currentUserService.Object);
        var query = new GetKbArticleListQuery { PageNumber = 1, PageSize = 10 };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items.First().Title.Should().Be("A");
    }

    [Fact]
    public async Task Suggest_ValidTicket_ReturnsMatchingArticles()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-01",
            Title = "Overheat issue",
            Description = "Overheat",
            Category = TicketCategoryEnum.Overheat
        };

        var article1 = new KnowledgeBaseArticle
        {
            Id = Guid.NewGuid(),
            Title = "Overheat Fix",
            Category = TicketCategoryEnum.Overheat,
            Status = KbArticleStatusEnum.Published,
            IsInternalOnly = false,
            HelpfulCount = 5,
            ViewCount = 10
        };

        var article2 = new KnowledgeBaseArticle
        {
            Id = Guid.NewGuid(),
            Title = "Charging Fix",
            Category = TicketCategoryEnum.Charging,
            Status = KbArticleStatusEnum.Published,
            IsInternalOnly = false,
            HelpfulCount = 10,
            ViewCount = 20
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article1, article2 }
        );
        var uow = resultExtended.uow;

        var handler = new SuggestKbArticlesQueryHandler(uow.Object);
        var query = new SuggestKbArticlesQuery { TicketId = ticketId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data!.First().Title.Should().Be("Overheat Fix");
    }

    [Fact]
    public async Task Handle_CompareVersions_ValidVersions_ReturnsDiff()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var fromVersionId = Guid.NewGuid();
        var toVersionId = Guid.NewGuid();

        var fromVersion = new KbArticleVersion
        {
            Id = fromVersionId,
            ArticleId = articleId,
            MajorVersion = 1,
            MinorVersion = 0,
            Title = "Title A",
            Content = JsonDocument.Parse("\"Symptoms A\""),
            Tags = new List<string> { "Tag A" }
        };

        var toVersion = new KbArticleVersion
        {
            Id = toVersionId,
            ArticleId = articleId,
            MajorVersion = 2,
            MinorVersion = 0,
            Title = "Title B",
            Content = JsonDocument.Parse("\"Symptoms B\""),
            Tags = new List<string> { "Tag B" }
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(
            kbVersionSeed: new[] { fromVersion, toVersion }
        );
        var uow = resultExtended.uow;

        var handler = new CompareKbArticleVersionsQueryHandler(uow.Object);
        var query = new CompareKbArticleVersionsQuery
        {
            ArticleId = articleId,
            FromVersionId = fromVersionId,
            ToVersionId = toVersionId
        };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.TitleDiff.OldValue.Should().Be("Title A");
        result.Data.TitleDiff.NewValue.Should().Be("Title B");
        result.Data.TitleDiff.IsChanged.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_CompareVersions_FromVersionNotFound_Returns404()
    {
        // Arrange
        var resultExtended = MockTicketUnitOfWork.BuildExtended();
        var uow = resultExtended.uow;

        var handler = new CompareKbArticleVersionsQueryHandler(uow.Object);
        var query = new CompareKbArticleVersionsQuery
        {
            ArticleId = Guid.NewGuid(),
            FromVersionId = Guid.NewGuid(),
            ToVersionId = Guid.NewGuid()
        };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_CopyTemplate_ValidTemplate_ReturnsTemplateDto()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "Template Title",
            Content = JsonDocument.Parse("\"Template Symptoms\""),
            Tags = new List<string> { "general" },
            IsTemplate = true,
            Status = KbArticleStatusEnum.Published,
            IsDeleted = false
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new CopyKbArticleTemplateQueryHandler(uow.Object);
        var query = new CopyKbArticleTemplateQuery { ArticleId = articleId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Content.Should().Be("Template Symptoms");
    }

    [Fact]
    public async Task Handle_CopyTemplate_NotFound_Returns404()
    {
        // Arrange
        var resultExtended = MockTicketUnitOfWork.BuildExtended();
        var uow = resultExtended.uow;

        var handler = new CopyKbArticleTemplateQueryHandler(uow.Object);
        var query = new CopyKbArticleTemplateQuery { ArticleId = Guid.NewGuid() };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_CopyTemplate_NotTemplateTag_Returns400()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Title = "Just an article",
            Tags = new List<string> { "general" },
            IsDeleted = false
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new CopyKbArticleTemplateQueryHandler(uow.Object);
        var query = new CopyKbArticleTemplateQuery { ArticleId = articleId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_GetVersionById_Valid_ReturnsVersionDto()
    {
        // Arrange
        var versionId = Guid.NewGuid();
        var version = new KbArticleVersion
        {
            Id = versionId,
            Title = "Version Title",
            MajorVersion = 1,
            MinorVersion = 0
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbVersionSeed: new[] { version });
        var uow = resultExtended.uow;

        var handler = new GetKbArticleVersionByIdQueryHandler(uow.Object);
        var query = new GetKbArticleVersionByIdQuery { VersionId = versionId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Title.Should().Be("Version Title");
    }

    [Fact]
    public async Task Handle_GetVersions_ReturnsListOrderedByVersion()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var version1 = new KbArticleVersion { Id = Guid.NewGuid(), ArticleId = articleId, MajorVersion = 1, MinorVersion = 0, Title = "V1" };
        var version2 = new KbArticleVersion { Id = Guid.NewGuid(), ArticleId = articleId, MajorVersion = 2, MinorVersion = 0, Title = "V2" };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbVersionSeed: new[] { version1, version2 });
        var uow = resultExtended.uow;

        var handler = new GetKbArticleVersionsQueryHandler(uow.Object);
        var query = new GetKbArticleVersionsQuery { ArticleId = articleId };

        // Act
        var result = await handler.Handle(query, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.First().Title.Should().Be("V2"); // Descending order
    }
}
