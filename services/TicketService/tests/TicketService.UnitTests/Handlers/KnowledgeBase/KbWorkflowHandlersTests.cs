using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Handler.KnowledgeBase;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.KnowledgeBase;

public class KbWorkflowHandlersTests
{
    [Fact]
    public async Task Handle_PublishCommand_UpdatesStatusToPublished()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.Draft
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;

        var handler = new PublishKbArticleCommandHandler(uow.Object);
        var command = new PublishKbArticleCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be((int)KbArticleStatusEnum.Published);
        kbArticles.Verify(x => x.UpdateAsync(It.Is<KnowledgeBaseArticle>(a => a.Status == KbArticleStatusEnum.Published)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ApproveReview_UpdatesStatusToPublished()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new KnowledgeBaseArticle
        {
            Id = articleId,
            Status = KbArticleStatusEnum.PendingReview,
            ReviewRequired = true
        };

        var resultExtended = MockTicketUnitOfWork.BuildExtended(kbSeed: new[] { article });
        var uow = resultExtended.uow;

        var handler = new ApproveReviewCommandHandler(uow.Object);
        var command = new ApproveReviewCommand { ArticleId = articleId };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be((int)KbArticleStatusEnum.Published);
        article.ReviewRequired.Should().BeFalse();
    }
}
