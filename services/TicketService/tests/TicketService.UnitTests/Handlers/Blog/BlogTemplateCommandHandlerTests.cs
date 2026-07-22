using System.Text.Json;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Blog;
using TicketService.Application.CQRS.Handler.Blog;
using TicketService.Domain.Entities;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Blog;

public class BlogTemplateCommandHandlerTests
{
    // ── CreateBlogTemplateCommandHandler ──────────────────────────────

    [Fact]
    public async Task Create_ValidCommand_ReturnsCreatedTemplate()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var userId = Guid.NewGuid();
        var handler = new CreateBlogTemplateCommandHandler(uow.Object);

        var cmd = new CreateBlogTemplateCommand
        {
            Name = "Standard Template",
            Description = "Desc",
            ContentHtml = "<h1>{{Title}}</h1>",
            CurrentUserId = userId,
        };

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Name.Should().Be("Standard Template");
        result.Data.CreatedByUserId.Should().Be(userId.ToString());
    }

    // ── UpdateBlogTemplateCommandHandler ──────────────────────────────

    [Fact]
    public async Task Update_TemplateNotFound_Returns404()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var handler = new UpdateBlogTemplateCommandHandler(uow.Object);

        var result = await handler.Handle(new UpdateBlogTemplateCommand { TemplateId = Guid.NewGuid(), Name = "X", ContentHtml = "Y" }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Update_ExistingTemplate_UpdatesSuccessfully()
    {
        var id = Guid.NewGuid();
        var template = new BlogTemplate { Id = id, Name = "Old", Description = "OldDesc", ContentHtml = JsonDocument.Parse("\"<p>old</p>\""), IsActive = true };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var mock = new Mock<IGenericRepository<BlogTemplate>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { template }.BuildMock());
        uow.SetupGet(u => u.BlogTemplates).Returns(mock.Object);

        var handler = new UpdateBlogTemplateCommandHandler(uow.Object);
        var cmd = new UpdateBlogTemplateCommand
        {
            TemplateId = id,
            Name = "New Name",
            Description = "New Desc",
            ContentHtml = "<p>new</p>",
            IsActive = false,
        };

        var result = await handler.Handle(cmd, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Name.Should().Be("New Name");
        result.Data.IsActive.Should().BeFalse();
        mock.Verify(r => r.UpdateAsync(It.Is<BlogTemplate>(t => t.Name == "New Name" && !t.IsActive)), Times.Once);
    }

    // ── DeleteBlogTemplateCommandHandler ──────────────────────────────

    [Fact]
    public async Task Delete_TemplateNotFound_Returns404()
    {
        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var handler = new DeleteBlogTemplateCommandHandler(uow.Object);

        var result = await handler.Handle(new DeleteBlogTemplateCommand { TemplateId = Guid.NewGuid() }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Delete_ExistingTemplate_SoftDeletesSuccessfully()
    {
        var id = Guid.NewGuid();
        var template = new BlogTemplate { Id = id, Name = "T", ContentHtml = JsonDocument.Parse("\"<p/>\"") };

        var ext = MockTicketUnitOfWork.BuildExtended();
        var uow = ext.uow;
        var mock = new Mock<IGenericRepository<BlogTemplate>>();
        mock.Setup(r => r.GetAllAsync()).Returns(new[] { template }.BuildMock());
        uow.SetupGet(u => u.BlogTemplates).Returns(mock.Object);

        var handler = new DeleteBlogTemplateCommandHandler(uow.Object);
        var result = await handler.Handle(new DeleteBlogTemplateCommand { TemplateId = id }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        mock.Verify(r => r.DeleteAsync(It.Is<BlogTemplate>(t => t.Id == id)), Times.Once);
    }
}
