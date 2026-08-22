using FluentAssertions;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Handler.KnowledgeBase;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.KnowledgeBase;

public class CreateKbArticleCommandHandlerTests
{

    /// <summary>
    /// Outbox writer giả cho các test không quan tâm tới integration event. Handler KB ghi event
    /// "chờ duyệt"/"đã duyệt" vào outbox, nhưng những test dưới đây kiểm tra chuyển trạng thái
    /// bài viết — mock rỗng để chúng không phải khai báo thứ chúng không assert.
    /// </summary>
    private static IIntegrationEventOutboxWriter NoOpOutbox()
        => new Mock<IIntegrationEventOutboxWriter>().Object;
    private readonly Mock<IKbCodeGenerator> _codeGen = new();

    [Fact]
    public async Task Handle_ValidRequest_CreatesPendingReviewArticle()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new CreateKbArticleCommand
        {
            CurrentUserId = userId,
            CurrentUserRole = "Staff",
            Category = TicketCategoryEnum.Charging,
            Title = "Charging Issue",
            Content = "Symptoms here. Steps here. Solution here.",
            Tags = new List<string> { "tag1" }
        };

        _codeGen.Setup(x => x.GenerateNextCodeAsync(It.IsAny<CancellationToken>())).ReturnsAsync("KB-2026-0001");
        var resultExtended = MockTicketUnitOfWork.BuildExtended();
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;
        var kbVersions = resultExtended.kbVersions;

        var handler = new CreateKbArticleCommandHandler(uow.Object, _codeGen.Object, NoOpOutbox());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Code.Should().Be("KB-2026-0001");
        result.Data.Status.Should().Be(KbArticleStatusEnum.PendingReview);

        kbArticles.Verify(x => x.AddAsync(It.IsAny<KnowledgeBaseArticle>()), Times.Once);
        kbVersions.Verify(x => x.AddAsync(It.IsAny<KbArticleVersion>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
