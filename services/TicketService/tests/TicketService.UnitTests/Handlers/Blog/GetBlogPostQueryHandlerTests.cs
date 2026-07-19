using System.Text.Json;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class GetBlogPostQueryHandlerTests
{
    // ──────────────────────────── GetBlogPostByIdQueryHandler ────────────────────────────

    [Fact]
    public async Task GetById_PostNotFound_ReturnsNotFound()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var handler = new GetBlogPostByIdQueryHandler(ext.uow.Object);

        var result = await handler.Handle(new GetBlogPostByIdQuery { BlogPostId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_PostFound_ReturnsCorrectData()
    {
        var postId = Guid.NewGuid();
        var authorId = Guid.NewGuid();
        var post = new BlogPost
        {
            Id = postId,
            Title = "Hello Blog",
            Slug = "hello-blog",
            Summary = "summary",
            ContentHtml = JsonDocument.Parse("\"<p>content</p>\""),
            Status = BlogPostStatusEnum.Published,
            Origin = BlogPostOriginEnum.Manual,
            AuthorUserId = authorId,
            CurrentVersion = 1,
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostByIdQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostByIdQuery { BlogPostId = postId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(postId.ToString());
        result.Data.Title.Should().Be("Hello Blog");
        result.Data.Slug.Should().Be("hello-blog");
        result.Data.Status.Should().Be(BlogPostStatusEnum.Published);
        result.Data.AuthorUserId.Should().Be(authorId.ToString());
    }

    [Fact]
    public async Task GetById_DeletedPost_ReturnsNotFound()
    {
        var postId = Guid.NewGuid();
        var post = new BlogPost { Id = postId, IsDeleted = true, Title = "deleted" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(new[] { post }.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostByIdQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostByIdQuery { BlogPostId = postId }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ──────────────────────────── GetBlogPostListQueryHandler ────────────────────────────

    [Fact]
    public async Task GetList_NoStatusFilter_ReturnsOnlyPublished()
    {
        var posts = new[]
        {
            new BlogPost { Id = Guid.NewGuid(), Title = "Draft", Status = BlogPostStatusEnum.Draft, Slug = "d", AuthorUserId = Guid.NewGuid() },
            new BlogPost { Id = Guid.NewGuid(), Title = "Published", Status = BlogPostStatusEnum.Published, Slug = "p", AuthorUserId = Guid.NewGuid() },
            new BlogPost { Id = Guid.NewGuid(), Title = "Archived", Status = BlogPostStatusEnum.Archived, Slug = "a", AuthorUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostListQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Title.Should().Be("Published");
        result.Data.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task GetList_WithStatusFilter_ReturnsFiltered()
    {
        var posts = new[]
        {
            new BlogPost { Id = Guid.NewGuid(), Title = "Draft1", Status = BlogPostStatusEnum.Draft, Slug = "d1", AuthorUserId = Guid.NewGuid() },
            new BlogPost { Id = Guid.NewGuid(), Title = "Draft2", Status = BlogPostStatusEnum.Draft, Slug = "d2", AuthorUserId = Guid.NewGuid() },
            new BlogPost { Id = Guid.NewGuid(), Title = "Published", Status = BlogPostStatusEnum.Published, Slug = "p", AuthorUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostListQuery { Status = BlogPostStatusEnum.Draft }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.TotalItems.Should().Be(2);
        result.Data.Items.Should().AllSatisfy(x => x.Status.Should().Be(BlogPostStatusEnum.Draft));
    }

    [Fact]
    public async Task GetList_WithOriginFilter_ReturnsFiltered()
    {
        var posts = new[]
        {
            new BlogPost { Id = Guid.NewGuid(), Title = "Manual", Status = BlogPostStatusEnum.Published, Origin = BlogPostOriginEnum.Manual, Slug = "m", AuthorUserId = Guid.NewGuid() },
            new BlogPost { Id = Guid.NewGuid(), Title = "AI", Status = BlogPostStatusEnum.Published, Origin = BlogPostOriginEnum.AiGeneratedFromKb, Slug = "ai", AuthorUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostListQuery
        {
            Status = BlogPostStatusEnum.Published,
            Origin = BlogPostOriginEnum.AiGeneratedFromKb
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Title.Should().Be("AI");
    }

    [Fact]
    public async Task GetList_PageLessThanOne_NormalizesToPageOne()
    {
        var posts = new[]
        {
            new BlogPost { Id = Guid.NewGuid(), Title = "P", Status = BlogPostStatusEnum.Published, Slug = "p", AuthorUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.GetAllAsync()).Returns(posts.BuildMock());
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostListQuery { Page = 0, PageSize = 0 }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.PageNumber.Should().Be(1);
        result.Data.PageSize.Should().Be(20);
    }

    // ──────────────────────────── GetBlogPostVersionsQueryHandler ────────────────────────────

    [Fact]
    public async Task GetVersions_PostNotFound_ReturnsNotFound()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>())).ReturnsAsync(false);
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var handler = new GetBlogPostVersionsQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostVersionsQuery { BlogPostId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetVersions_PostFound_ReturnsVersionsDescending()
    {
        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var v1 = new BlogPostVersion { Id = Guid.NewGuid(), BlogPostId = postId, VersionNumber = 1, Title = "T", ContentHtml = JsonDocument.Parse("\"<p>v1</p>\""), ChangedByUserId = userId };
        var v2 = new BlogPostVersion { Id = Guid.NewGuid(), BlogPostId = postId, VersionNumber = 2, Title = "T", ContentHtml = JsonDocument.Parse("\"<p>v2</p>\""), ChangedByUserId = userId };

        var ext = MockTicketUnitOfWork.BuildExtended();

        var blogPostMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>())).ReturnsAsync(true);
        ext.uow.SetupGet(u => u.BlogPosts).Returns(blogPostMock.Object);

        var versionMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPostVersion>>();
        versionMock.Setup(r => r.GetAllAsync()).Returns(new[] { v1, v2 }.BuildMock());
        ext.uow.SetupGet(u => u.BlogPostVersions).Returns(versionMock.Object);

        var handler = new GetBlogPostVersionsQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogPostVersionsQuery { BlogPostId = postId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data![0].VersionNumber.Should().Be(2);
        result.Data[1].VersionNumber.Should().Be(1);
    }
}
