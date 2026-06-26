using FluentAssertions;
using Moq;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.StateMachine;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketSlaPauseResumeTests
{
    private readonly Mock<ITicketStateMachine> _stateMachine = MockTicketStateMachine.Create();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<ISlaService> _slaService = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task Hold_ValidRequest_CallsSlaServicePause()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Status = TicketStatusEnum.InProgress, Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketHoldCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            Reason = PauseReasonEnum.WaitingParts,
            Note = "Waiting for spare parts"
        };

        var handler = new TicketHoldCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _slaService.Verify(x => x.PauseSlaAsync(ticketId, PauseReasonEnum.WaitingParts, "Waiting for spare parts", staffId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resume_ValidRequest_CallsSlaServiceResume()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket { Id = ticketId, Code = "T-001", Status = TicketStatusEnum.WaitingParts, Title = "T", Description = "D" };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });

        var command = new TicketResumeCommand
        {
            TicketId = ticketId,
            StaffId = staffId
        };

        var handler = new TicketResumeCommandHandler(uow.Object, _stateMachine.Object, _logger.Object, _slaService.Object, _producer.Object, Moq.Mock.Of<MediatR.IPublisher>());

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _slaService.Verify(x => x.ResumeSlaAsync(ticketId, staffId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
