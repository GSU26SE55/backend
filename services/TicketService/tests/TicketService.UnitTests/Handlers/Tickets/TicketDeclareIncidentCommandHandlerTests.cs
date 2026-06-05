using SharedContracts.Interfaces;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.TicketDeclareIncident;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketDeclareIncidentCommandHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IGenericRepository<Ticket>> _ticketRepositoryMock;
    private readonly Mock<IGenericRepository<TicketActivity>> _ticketActivityRepositoryMock;
    private readonly Mock<IMessageProducerService> _producerMock;
    private readonly TicketDeclareIncidentCommandHandler _handler;

    public TicketDeclareIncidentCommandHandlerTests()
    {
        _unitOfWorkMock = new Mock<ITicketUnitOfWork>();
        _ticketRepositoryMock = new Mock<IGenericRepository<Ticket>>();
        _ticketActivityRepositoryMock = new Mock<IGenericRepository<TicketActivity>>();
        _producerMock = new Mock<IMessageProducerService>();

        _unitOfWorkMock.Setup(u => u.Tickets).Returns(_ticketRepositoryMock.Object);
        _unitOfWorkMock.Setup(u => u.TicketActivities).Returns(_ticketActivityRepositoryMock.Object);

        _handler = new TicketDeclareIncidentCommandHandler(_unitOfWorkMock.Object, _producerMock.Object);
    }

    [Fact]
    public async Task Handle_GivenValidCommand_ShouldDeclareIncidentAndAddActivity()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            IsIncident = false,
            Code = "T-1",
            Status = TicketStatusEnum.Open,
            Title = "Test Ticket",
            Description = "Test Description"
        };
        var command = new TicketDeclareIncidentCommand(ticketId, userId);

        _ticketRepositoryMock.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync(ticket);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        ticket.IsIncident.Should().BeTrue();
        _ticketActivityRepositoryMock.Verify(r => r.AddAsync(It.Is<TicketActivity>(a => a.TicketId == ticketId && a.Action == ActivityActionEnum.IncidentDeclared)), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitTransactionAsync(), Times.Once);
    }

    [Fact]
    public async Task Handle_GivenInvalidTicketId_ShouldReturnNotFoundResponse()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new TicketDeclareIncidentCommand(ticketId, userId);

        _ticketRepositoryMock.Setup(r => r.GetByIdAsync(ticketId)).ReturnsAsync((Ticket?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Ticket not found");
    }
}
