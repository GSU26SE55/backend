using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class PublishBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_GeneratingStatus_ReturnsConflict()
    {
        var postId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Status = BlogPostStatusEnum.Generating };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);

        var handler = new PublishBlogPostCommandHandler(uow.Object);
        var result = await handler.Handle(new PublishBlogPostCommand { BlogPostId = postId }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_DraftStatus_SetsPublished()
    {
        var postId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Title = "My Post", Status = BlogPostStatusEnum.Draft, CurrentVersion = 1 };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);

        var handler = new PublishBlogPostCommandHandler(uow.Object);
        var result = await handler.Handle(new PublishBlogPostCommand { BlogPostId = postId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Status.Should().Be(BlogPostStatusEnum.Published);
        mock.Verify(r => r.UpdateAsync(It.Is<BlogPost>(p => p.Status == BlogPostStatusEnum.Published)), Times.Once);
    }
}
