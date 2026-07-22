using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class DeleteBlogPostCommandHandlerTests
{
    private static (DeleteBlogPostCommandHandler handler, Mock<IGenericRepository<BlogPost>> mock, Mock<ITicketUnitOfWork> uow)
        Build(IEnumerable<BlogPost> posts)
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var mock = new Mock<IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);
        return (new DeleteBlogPostCommandHandler(uow.Object), mock, uow);
    }

    [Fact]
    public async Task Handle_PostNotFound_Returns404()
    {
        var (handler, _, _) = Build(Array.Empty<BlogPost>());
        var result = await handler.Handle(new DeleteBlogPostCommand { BlogPostId = Guid.NewGuid() }, default);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_GeneratingStatus_Returns409()
    {
        var id = Guid.NewGuid();
        var (handler, _, _) = Build(new[] { new BlogPost { Id = id, Status = BlogPostStatusEnum.Generating } });
        var result = await handler.Handle(new DeleteBlogPostCommand { BlogPostId = id }, default);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Theory]
    [InlineData(BlogPostStatusEnum.Draft)]
    [InlineData(BlogPostStatusEnum.Published)]
    [InlineData(BlogPostStatusEnum.Archived)]
    [InlineData(BlogPostStatusEnum.GenerationFailed)]
    public async Task Handle_DeletableStatus_SoftDeletesSuccessfully(BlogPostStatusEnum status)
    {
        var id = Guid.NewGuid();
        var post = new BlogPost { Id = id, Title = "T", Status = status };
        var (handler, mock, _) = Build(new[] { post });

        var result = await handler.Handle(new DeleteBlogPostCommand { BlogPostId = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        mock.Verify(r => r.DeleteAsync(It.Is<BlogPost>(p => p.Id == id)), Times.Once);
    }
}
