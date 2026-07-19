using System.Text.Json;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Application.CQRS.Query.Blog;
using TicketService.Domain.Entities;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class GetBlogTemplateQueryHandlerTests
{
    // ──────────────────────────── GetBlogTemplateByIdQueryHandler ────────────────────────────

    [Fact]
    public async Task GetById_TemplateNotFound_ReturnsNotFound()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var handler = new GetBlogTemplateByIdQueryHandler(ext.uow.Object);

        var result = await handler.Handle(new GetBlogTemplateByIdQuery { TemplateId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task GetById_TemplateFound_ReturnsCorrectData()
    {
        var templateId = Guid.NewGuid();
        var creatorId = Guid.NewGuid();
        var template = new BlogTemplate
        {
            Id = templateId,
            Name = "Standard Template",
            Description = "desc",
            ContentHtml = JsonDocument.Parse("\"<div>template</div>\""),
            IsActive = true,
            CreatedByUserId = creatorId,
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var templateMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogTemplate>>();
        templateMock.Setup(r => r.GetAllAsync()).Returns(new[] { template }.BuildMock());
        ext.uow.SetupGet(u => u.BlogTemplates).Returns(templateMock.Object);

        var handler = new GetBlogTemplateByIdQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogTemplateByIdQuery { TemplateId = templateId }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(templateId.ToString());
        result.Data.Name.Should().Be("Standard Template");
        result.Data.IsActive.Should().BeTrue();
        result.Data.CreatedByUserId.Should().Be(creatorId.ToString());
    }

    [Fact]
    public async Task GetById_DeletedTemplate_ReturnsNotFound()
    {
        var templateId = Guid.NewGuid();
        var template = new BlogTemplate { Id = templateId, IsDeleted = true, Name = "deleted" };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var templateMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogTemplate>>();
        templateMock.Setup(r => r.GetAllAsync()).Returns(new[] { template }.BuildMock());
        ext.uow.SetupGet(u => u.BlogTemplates).Returns(templateMock.Object);

        var handler = new GetBlogTemplateByIdQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogTemplateByIdQuery { TemplateId = templateId }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ──────────────────────────── GetBlogTemplateListQueryHandler ────────────────────────────

    [Fact]
    public async Task GetList_NoFilter_ReturnsAll()
    {
        var templates = new[]
        {
            new BlogTemplate { Id = Guid.NewGuid(), Name = "T1", IsActive = true, CreatedByUserId = Guid.NewGuid() },
            new BlogTemplate { Id = Guid.NewGuid(), Name = "T2", IsActive = false, CreatedByUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var templateMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogTemplate>>();
        templateMock.Setup(r => r.GetAllAsync()).Returns(templates.BuildMock());
        ext.uow.SetupGet(u => u.BlogTemplates).Returns(templateMock.Object);

        var handler = new GetBlogTemplateListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogTemplateListQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetList_IsActiveTrue_ReturnsOnlyActive()
    {
        var templates = new[]
        {
            new BlogTemplate { Id = Guid.NewGuid(), Name = "Active", IsActive = true, CreatedByUserId = Guid.NewGuid() },
            new BlogTemplate { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false, CreatedByUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var templateMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogTemplate>>();
        templateMock.Setup(r => r.GetAllAsync()).Returns(templates.BuildMock());
        ext.uow.SetupGet(u => u.BlogTemplates).Returns(templateMock.Object);

        var handler = new GetBlogTemplateListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogTemplateListQuery { IsActive = true }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Name.Should().Be("Active");
    }

    [Fact]
    public async Task GetList_IsActiveFalse_ReturnsOnlyInactive()
    {
        var templates = new[]
        {
            new BlogTemplate { Id = Guid.NewGuid(), Name = "Active", IsActive = true, CreatedByUserId = Guid.NewGuid() },
            new BlogTemplate { Id = Guid.NewGuid(), Name = "Inactive", IsActive = false, CreatedByUserId = Guid.NewGuid() },
        };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var templateMock = new Mock<SharedKernels.Interfaces.IGenericRepository<BlogTemplate>>();
        templateMock.Setup(r => r.GetAllAsync()).Returns(templates.BuildMock());
        ext.uow.SetupGet(u => u.BlogTemplates).Returns(templateMock.Object);

        var handler = new GetBlogTemplateListQueryHandler(ext.uow.Object);
        var result = await handler.Handle(new GetBlogTemplateListQuery { IsActive = false }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(1);
        result.Data![0].Name.Should().Be("Inactive");
    }
}
