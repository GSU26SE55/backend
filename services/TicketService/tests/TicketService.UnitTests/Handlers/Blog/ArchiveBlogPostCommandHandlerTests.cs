using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class ArchiveBlogPostCommandHandlerTests
{
    private static ArchiveBlogPostCommandHandler Build(IEnumerable<BlogPost> posts)
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var mock = new Mock<IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);
        return new ArchiveBlogPostCommandHandler(uow.Object);
    }

    [Fact]
    public async Task Handle_PostNotFound_Returns404()
    {
        var handler = Build(Array.Empty<BlogPost>());
        var result = await handler.Handle(new ArchiveBlogPostCommand { BlogPostId = Guid.NewGuid() }, default);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(BlogPostStatusEnum.Generating)]
    [InlineData(BlogPostStatusEnum.GenerationFailed)]
    public async Task Handle_NotReadyStatus_Returns409(BlogPostStatusEnum status)
    {
        var id = Guid.NewGuid();
        var handler = Build(new[] { new BlogPost { Id = id, Status = status } });
        var result = await handler.Handle(new ArchiveBlogPostCommand { BlogPostId = id }, default);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_AlreadyArchived_Returns409()
    {
        var id = Guid.NewGuid();
        var handler = Build(new[] { new BlogPost { Id = id, Status = BlogPostStatusEnum.Archived } });
        var result = await handler.Handle(new ArchiveBlogPostCommand { BlogPostId = id }, default);
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Theory]
    [InlineData(BlogPostStatusEnum.Draft)]
    [InlineData(BlogPostStatusEnum.Published)]
    public async Task Handle_ValidPost_ArchivesSuccessfully(BlogPostStatusEnum status)
    {
        var id = Guid.NewGuid();
        var post = new BlogPost { Id = id, Title = "T", Status = status };
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var mock = new Mock<IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);
        var handler = new ArchiveBlogPostCommandHandler(uow.Object);

        var result = await handler.Handle(new ArchiveBlogPostCommand { BlogPostId = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Status.Should().Be(BlogPostStatusEnum.Archived);
        mock.Verify(r => r.UpdateAsync(It.Is<BlogPost>(p => p.Status == BlogPostStatusEnum.Archived)), Times.Once);
    }
}
