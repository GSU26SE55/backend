using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.CQRS.Handler.KnowledgeBase;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.KnowledgeBase;

public class CreateKbArticleCommandHandlerTests
{
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
            Symptoms = "Symptoms here",
            DiagnosisSteps = "Steps here",
            SolutionSteps = "Solution here",
            Tags = new List<string> { "tag1" }
        };

        _codeGen.Setup(x => x.GenerateNextCodeAsync(It.IsAny<CancellationToken>())).ReturnsAsync("KB-2026-0001");
        var resultExtended = MockTicketUnitOfWork.BuildExtended();
        var uow = resultExtended.uow;
        var kbArticles = resultExtended.kbArticles;
        var kbVersions = resultExtended.kbVersions;

        var handler = new CreateKbArticleCommandHandler(uow.Object, _codeGen.Object);

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
