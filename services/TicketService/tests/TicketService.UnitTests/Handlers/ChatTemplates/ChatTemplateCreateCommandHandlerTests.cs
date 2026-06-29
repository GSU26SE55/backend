using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.ChatTemplates;
using TicketService.Application.CQRS.Handler.ChatTemplates;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.ChatTemplates;

public class ChatTemplateCreateCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<ChatTemplate>> _templatesRepo;

    public ChatTemplateCreateCommandHandlerTests()
    {
        _templatesRepo = _uow.SetupChatTemplates();
    }

    private ChatTemplateCreateCommandHandler CreateHandler() => new(_uow.Object);

    [Fact]
    public async Task Handle_ValidCommand_CreatesTemplate()
    {
        var command = new ChatTemplateCreateCommand
        {
            ActorUserId = Guid.NewGuid(),
            Name = "Greeting",
            Content = "Hello {{name}}, welcome!",
            Category = ChatTemplateCategoryEnum.Greeting,
            Scope = ChatTemplateScopeEnum.Personal
        };

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data.Should().NotBeNull();
        result.Data!.Name.Should().Be("Greeting");
        result.Data.Content.Should().Be("Hello {{name}}, welcome!");
        result.Data.CreatedByUserId.Should().Be(command.ActorUserId.ToString());

        _templatesRepo.Verify(r => r.AddAsync(It.Is<ChatTemplate>(t =>
            t.Name == "Greeting" &&
            t.Content == "Hello {{name}}, welcome!" &&
            t.CreatedByUserId == command.ActorUserId &&
            t.IsActive == true &&
            t.UsageCount == 0)), Times.Once);
    }

    [Fact]
    public async Task Handle_NameIsTrimmed_WhenCreating()
    {
        var command = new ChatTemplateCreateCommand
        {
            ActorUserId = Guid.NewGuid(),
            Name = "  Padded Name  ",
            Content = "Some content",
            Scope = ChatTemplateScopeEnum.Personal
        };

        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _templatesRepo.Verify(r => r.AddAsync(It.Is<ChatTemplate>(t => t.Name == "Padded Name")), Times.Once);
    }

}
