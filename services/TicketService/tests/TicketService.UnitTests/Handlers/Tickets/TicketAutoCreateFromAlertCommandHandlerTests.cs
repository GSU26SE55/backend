using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketAutoCreateFromAlertCommandHandlerTests
{
    private readonly Mock<ITicketCodeGenerator> _codeGen = new();
    private readonly Mock<IPriorityCalculator> _priorityCalc = new();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IMessageProducerService> _producer = new();

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

        var handler = new TicketAutoCreateFromAlertCommandHandler(uow.Object, _codeGen.Object, _priorityCalc.Object, _logger.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

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

        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
