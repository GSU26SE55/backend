using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.ChatTemplates;
using TicketService.Application.CQRS.Query.ChatTemplate;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;
using Xunit;

namespace TicketService.UnitTests.Handlers.ChatTemplates;

public class ChatTemplateGetByIdQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<ChatTemplate>> _templatesRepo;

    public ChatTemplateGetByIdQueryHandlerTests()
    {
        _templatesRepo = _uow.SetupChatTemplates();
    }

    private ChatTemplateGetByIdQueryHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_TemplateExistsAndIsVisible_Returns200()
    {
        var templateId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "Welcome",
            Content = "Hello",
            Category = ChatTemplateCategoryEnum.Greeting,
            Scope = ChatTemplateScopeEnum.Global,
            CreatedByUserId = Guid.NewGuid(),
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);

        var query = new ChatTemplateGetByIdQuery
        {
            TemplateId = templateId,
            ActorUserId = actorUserId,
            ActorRoles = new[] { "Staff" }
        };

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(templateId.ToString());
    }

    [Fact]
    public async Task Handle_TemplateDoesNotExist_Returns404()
    {
        var templateId = Guid.NewGuid();
        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync((ChatTemplate?)null);

        var query = new ChatTemplateGetByIdQuery
        {
            TemplateId = templateId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = new[] { "Staff" }
        };

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Không tìm thấy template.");
    }

    [Fact]
    public async Task Handle_PersonalTemplateOwnedByOtherAndNotAdmin_Returns403()
    {
        var templateId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid(); // different user
        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "My Personal",
            Content = "Private",
            Category = ChatTemplateCategoryEnum.Greeting,
            Scope = ChatTemplateScopeEnum.Personal,
            CreatedByUserId = ownerId,
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);

        var query = new ChatTemplateGetByIdQuery
        {
            TemplateId = templateId,
            ActorUserId = actorUserId,
            ActorRoles = new[] { "Staff" }
        };

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Không có quyền truy cập tài nguyên này.");
    }

    [Fact]
    public async Task Handle_PersonalTemplateOwnedByOtherAndIsAdmin_Returns200()
    {
        var templateId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var template = new ChatTemplate
        {
            Id = templateId,
            Name = "My Personal",
            Content = "Private",
            Category = ChatTemplateCategoryEnum.Greeting,
            Scope = ChatTemplateScopeEnum.Personal,
            CreatedByUserId = ownerId,
            IsActive = true
        };

        _templatesRepo.Setup(r => r.GetByIdAsync(templateId)).ReturnsAsync(template);

        var query = new ChatTemplateGetByIdQuery
        {
            TemplateId = templateId,
            ActorUserId = actorUserId,
            ActorRoles = new[] { "Admin" } // Admin bypass
        };

        var result = await CreateHandler().Handle(query, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(templateId.ToString());
    }
}
