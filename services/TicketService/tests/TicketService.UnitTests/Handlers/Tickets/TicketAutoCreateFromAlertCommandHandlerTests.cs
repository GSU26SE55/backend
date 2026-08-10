using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using SharedContracts.Saga.AlertTicket;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketAutoCreateFromAlertCommandHandlerTests
{
    private readonly Mock<ITicketCodeGenerator> _codeGen = new();
    private readonly Mock<IPriorityCalculator> _priorityCalc = new();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IIntegrationEventOutboxWriter> _outboxWriter = new();

    [Theory]
    [InlineData("EnvironmentalIncident", ImpactScopeEnum.Site, UrgencyLevelEnum.High, TicketPriorityEnum.P1Critical)]
    [InlineData("Overheat", ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.High, TicketPriorityEnum.P2High)]
    [InlineData("SohDegradation", ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Low, TicketPriorityEnum.P3Normal)]
    public async Task Handle_AnomalyAlert_SetsCorrectB3AndPriority(string category, ImpactScopeEnum expectedImpact, UrgencyLevelEnum expectedUrgency, TicketPriorityEnum expectedPriority)
    {
        // Arrange
        var command = new TicketAutoCreateFromAlertCommand
        {
            AnomalyCategory = category,
            OriginAlertId = Guid.NewGuid(),
            BatteryAssetId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = "Alert",
            Description = "Desc"
        };

        _codeGen.Setup(x => x.GenerateAsync()).ReturnsAsync("TKT-AUTO-001");
        _priorityCalc.Setup(x => x.Calculate(expectedImpact, expectedUrgency)).Returns(expectedPriority);

        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build();

        var handler = new TicketAutoCreateFromAlertCommandHandler(uow.Object, _codeGen.Object, _priorityCalc.Object, _logger.Object, _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data!.Code.Should().Be("TKT-AUTO-001");
        result.Data.Status.Should().Be(TicketStatusEnum.Open);
        result.Data.Id.Should().NotBeNullOrEmpty();

        tickets.Verify(x => x.AddAsync(It.Is<TicketService.Domain.Entities.Ticket>(t =>
            t.ImpactScope == expectedImpact &&
            t.UrgencyLevel == expectedUrgency &&
            t.Priority == expectedPriority)), Times.Once);

        _outboxWriter.Verify(x => x.WriteAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ===== BE-AI structured — ghi ticket_ai_suggestions =====

    private TicketAutoCreateFromAlertCommandHandler BuildHandler(Mock<ITicketUnitOfWork> uow)
    {
        _codeGen.Setup(x => x.GenerateAsync()).ReturnsAsync("TKT-AUTO-001");
        _priorityCalc
            .Setup(x => x.Calculate(It.IsAny<ImpactScopeEnum>(), It.IsAny<UrgencyLevelEnum>()))
            .Returns(TicketPriorityEnum.P3Normal);

        return new TicketAutoCreateFromAlertCommandHandler(
            uow.Object, _codeGen.Object, _priorityCalc.Object, _logger.Object,
            _outboxWriter.Object, Moq.Mock.Of<MediatR.IPublisher>());
    }

    private static TicketAutoCreateFromAlertCommand BaseCommand() => new()
    {
        AnomalyCategory = "SohDegradation",
        OriginAlertId = Guid.NewGuid(),
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Title = "Alert",
        Description = "Desc"
    };

    [Fact]
    public async Task Handle_WithAiSuggestion_PersistsStructuredData()
    {
        var command = BaseCommand();
        command.AiPrescriptionText = "Thay module BMS";
        command.AiSuggestion = new AiSuggestionPayload(
            ActionSteps: new[] { "Ngắt tải", "Đo điện áp cell" },
            PpeRequired: new[] { "Găng cách điện" },
            SopReferences: new[] { "SOP-BMS-01" },
            KbDocRefs: new[] { "maintenance/bms_warning_codes.md" },
            HumanVerificationRequired: true,
            Enriched: true,
            LlmProvider: "deepseek",
            PrescriptionId: "presc-abc-123");

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var aiRepo = MockTicketUnitOfWork.AiSuggestionsOf(uow);

        var result = await BuildHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aiRepo.Verify(x => x.AddAsync(It.Is<TicketAiSuggestion>(s =>
            s.Prescription == "Thay module BMS" &&
            s.ActionSteps.Count == 2 &&
            s.SopReferences.Contains("SOP-BMS-01") &&
            s.KbDocRefs.Contains("maintenance/bms_warning_codes.md") &&
            s.HumanVerificationRequired &&
            s.Enriched &&
            s.LlmProvider == "deepseek" &&
            s.PrescriptionId == "presc-abc-123")), Times.Once);
    }

    [Fact]
    public async Task Handle_WithoutAiSuggestion_DoesNotPersistAnything()
    {
        // Ticket từ threshold engine — không gọi AI, không được tạo bản ghi trắng.
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var aiRepo = MockTicketUnitOfWork.AiSuggestionsOf(uow);

        var result = await BuildHandler(uow).Handle(BaseCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aiRepo.Verify(x => x.AddAsync(It.IsAny<TicketAiSuggestion>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithEmptyAiPayload_DoesNotPersistAnything()
    {
        // Payload dựng từ command của saga cũ (mọi field null) — IsEmpty phải chặn lại.
        var command = BaseCommand();
        command.AiSuggestion = new AiSuggestionPayload();

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var aiRepo = MockTicketUnitOfWork.AiSuggestionsOf(uow);

        var result = await BuildHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        aiRepo.Verify(x => x.AddAsync(It.IsAny<TicketAiSuggestion>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenAiSuggestionWriteThrows_StillCreatesTicket()
    {
        // Best-effort: hỏng chỗ ghi gợi ý KHÔNG được làm chết việc tạo ticket.
        var command = BaseCommand();
        command.AiSuggestion = new AiSuggestionPayload(ActionSteps: new[] { "Ngắt tải" });

        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        MockTicketUnitOfWork.AiSuggestionsOf(uow)
            .Setup(x => x.AddAsync(It.IsAny<TicketAiSuggestion>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await BuildHandler(uow).Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tickets.Verify(x => x.AddAsync(It.IsAny<TicketService.Domain.Entities.Ticket>()), Times.Once);
        _outboxWriter.Verify(
            x => x.WriteAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
