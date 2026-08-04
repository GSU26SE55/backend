using System.Text.Json;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class UpdateBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_PostNotFound_ReturnsNotFound()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        uow.SetupGet(u => u.BlogPosts).Returns(EmptyBlogPosts().Object);

        var handler = new UpdateBlogPostCommandHandler(uow.Object);
        var cmd = new UpdateBlogPostCommand { BlogPostId = Guid.NewGuid(), Title = "T", Slug = "s", Summary = "S", ContentHtml = "<p>C</p>", CurrentVersion = 1, CurrentUserId = Guid.NewGuid() };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ConcurrencyMismatch_ReturnsConflict()
    {
        var postId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Title = "Old", Slug = "old", Summary = "s", ContentHtml = JsonDocument.Parse("\"<p>old</p>\""), Status = BlogPostStatusEnum.Draft, CurrentVersion = 2 };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        SetupBlogPostsRepo(uow, new[] { post });

        var handler = new UpdateBlogPostCommandHandler(uow.Object);
        var cmd = new UpdateBlogPostCommand
        {
            BlogPostId = postId,
            Title = "New",
            Slug = "new",
            Summary = "S",
            ContentHtml = "<p>C</p>",
            CurrentVersion = 1, // wrong version
            CurrentUserId = Guid.NewGuid()
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("cập nhật bởi người khác");
    }

    [Fact]
    public async Task Handle_ValidConcurrencyVersion_CreatesNewVersionSnapshot()
    {
        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, Title = "Old", Slug = "old", Summary = "s", ContentHtml = JsonDocument.Parse("\"<p>old</p>\""), Status = BlogPostStatusEnum.Draft, CurrentVersion = 1 };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        SetupBlogPostsRepo(uow, new[] { post });

        var blogVersionsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPostVersion>>();
        uow.SetupGet(u => u.BlogPostVersions).Returns(blogVersionsMock.Object);

        var handler = new UpdateBlogPostCommandHandler(uow.Object);
        var cmd = new UpdateBlogPostCommand
        {
            BlogPostId = postId,
            Title = "New Title",
            Slug = "new-slug",
            Summary = "New Sum",
            ContentHtml = "<p>New</p>",
            CurrentVersion = 1,
            ChangeNote = "Updated content",
            CurrentUserId = userId
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.CurrentVersion.Should().Be(2);

        blogVersionsMock.Verify(r => r.AddAsync(It.Is<BlogPostVersion>(v =>
            v.BlogPostId == postId &&
            v.VersionNumber == 2 &&
            v.ChangedByUserId == userId
        )), Times.Once);
    }

    private static Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>> EmptyBlogPosts()
    {
        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogPost>().BuildMock());
        return mock;
    }

    private static void SetupBlogPostsRepo(Moq.Mock<TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork> uow, BlogPost[] posts)
    {
        var mock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        mock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        mock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>()))
            .ReturnsAsync(false);
        uow.SetupGet(u => u.BlogPosts).Returns(mock.Object);
    }
}
