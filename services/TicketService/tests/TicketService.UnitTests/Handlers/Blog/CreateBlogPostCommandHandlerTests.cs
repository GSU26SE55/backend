using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class CreateBlogPostCommandHandlerTests
{
    [Fact]
    public async Task Handle_DuplicateSlug_ReturnsConflict()
    {
        var existingPost = new BlogPost { Id = Guid.NewGuid(), Slug = "my-slug" };
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;

        var blogPostsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(new[] { existingPost }.BuildMock());
        blogPostsMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>()))
            .ReturnsAsync(true);
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        var handler = new CreateBlogPostCommandHandler(uow.Object);
        var cmd = new CreateBlogPostCommand
        {
            Title = "Title",
            Slug = "my-slug",
            Summary = "Sum",
            ContentHtml = "<p>Content</p>",
            CurrentUserId = Guid.NewGuid()
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesDraftWithVersionSnapshot()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var userId = Guid.NewGuid();

        var blogPostsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPost>>();
        blogPostsMock.Setup(r => r.GetAllAsync()).Returns(Array.Empty<BlogPost>().BuildMock());
        blogPostsMock.Setup(r => r.AnyAsync(It.IsAny<System.Linq.Expressions.Expression<Func<BlogPost, bool>>>()))
            .ReturnsAsync(false);
        uow.SetupGet(u => u.BlogPosts).Returns(blogPostsMock.Object);

        var blogVersionsMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogPostVersion>>();
        uow.SetupGet(u => u.BlogPostVersions).Returns(blogVersionsMock.Object);

        var handler = new CreateBlogPostCommandHandler(uow.Object);
        var cmd = new CreateBlogPostCommand
        {
            Title = "My Blog",
            Slug = "my-blog",
            Summary = "Summary here",
            ContentHtml = "<p>Hello World</p>",
            CurrentUserId = userId
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Status.Should().Be(BlogPostStatusEnum.Draft);
        result.Data.CurrentVersion.Should().Be(1);

        blogPostsMock.Verify(r => r.AddAsync(It.Is<BlogPost>(p =>
            p.Title == "My Blog" &&
            p.Status == BlogPostStatusEnum.Draft &&
            p.Origin == BlogPostOriginEnum.Manual &&
            p.CurrentVersion == 1 &&
            p.AuthorUserId == userId
        )), Times.Once);

        blogVersionsMock.Verify(r => r.AddAsync(It.Is<BlogPostVersion>(v =>
            v.VersionNumber == 1 &&
            v.Title == "My Blog" &&
            v.ChangedByUserId == userId
        )), Times.Once);
    }
}
