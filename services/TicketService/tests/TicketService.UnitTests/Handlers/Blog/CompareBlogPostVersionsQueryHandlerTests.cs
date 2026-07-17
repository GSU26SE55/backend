using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Domain.Entities;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class CompareBlogPostVersionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_InvalidVersionNumbers_ReturnsBadRequest()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var handler = new CompareBlogPostVersionsQueryHandler(ext.uow.Object);

        var result = await handler.Handle(new CompareBlogPostVersionsQuery
        {
            BlogPostId = Guid.NewGuid(),
            OldVersionNumber = 1,
            NewVersionNumber = 1
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_OldVersionNotFound_ReturnsNotFound()
    {
        var postId = Guid.NewGuid();
        var v2 = new BlogPostVersion { Id = Guid.NewGuid(), BlogPostId = postId, VersionNumber = 2, Title = "T", Summary = "S", ContentHtml = "<p>v2</p>" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPostVersion>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { v2 }.BuildMock());
        uow.SetupGet(u => u.BlogPostVersions).Returns(mock.Object);

        var handler = new CompareBlogPostVersionsQueryHandler(uow.Object);
        var result = await handler.Handle(new CompareBlogPostVersionsQuery
        {
            BlogPostId = postId,
            OldVersionNumber = 1,
            NewVersionNumber = 2
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Contain("version 1");
    }

    [Fact]
    public async Task Handle_BothVersionsFound_ReturnsDiff()
    {
        var postId = Guid.NewGuid();
        var v1 = new BlogPostVersion { Id = Guid.NewGuid(), BlogPostId = postId, VersionNumber = 1, Title = "T", Summary = "S", ContentHtml = "<p>v1</p>" };
        var v2 = new BlogPostVersion { Id = Guid.NewGuid(), BlogPostId = postId, VersionNumber = 2, Title = "T", Summary = "S", ContentHtml = "<p>v2</p>" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPostVersion>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { v1, v2 }.BuildMock());
        uow.SetupGet(u => u.BlogPostVersions).Returns(mock.Object);

        var handler = new CompareBlogPostVersionsQueryHandler(uow.Object);
        var result = await handler.Handle(new CompareBlogPostVersionsQuery
        {
            BlogPostId = postId,
            OldVersionNumber = 1,
            NewVersionNumber = 2
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.OldContentHtml.Should().Be("<p>v1</p>");
        result.Data.NewContentHtml.Should().Be("<p>v2</p>");
        result.Data.OldVersionNumber.Should().Be(1);
        result.Data.NewVersionNumber.Should().Be(2);
    }
}
