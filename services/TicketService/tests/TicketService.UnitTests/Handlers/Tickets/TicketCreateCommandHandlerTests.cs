using FluentAssertions;
using Moq;
using SharedContracts.Events;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketCreateCommandHandlerTests
{
    private readonly Mock<ITicketCodeGenerator> _codeGen = new();
    private readonly Mock<IActivityLogger> _logger = new();
    private readonly Mock<IMessageProducerService> _producer = new();

    [Fact]
    public async Task Handle_ValidRequest_CreatesTicket()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var command = new TicketCreateCommand
        {
            Title = "Battery Overheat",
            Description = "Battery is too hot",
            Category = TicketCategoryEnum.Repair,
            CustomerId = customerId
        };

        var customers = new List<CustomerAccount>
        {
            new CustomerAccount { AccountId = customerId, Status = AccountStatusEnum.Active }
        };

        _codeGen.Setup(x => x.GenerateAsync()).ReturnsAsync("TKT-2605-0001");
        var (uow, tickets, _, _, _, _, _) = MockTicketUnitOfWork.Build(customerSeed: customers);

        var handler = new TicketCreateCommandHandler(uow.Object, _codeGen.Object, _logger.Object, _producer.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.Code.Should().Be("TKT-2605-0001");
        result.Data.Status.Should().Be(TicketStatusEnum.Open);
        result.Data.Id.Should().NotBeNullOrEmpty();

        tickets.Verify(x => x.AddAsync(It.IsAny<TicketService.Domain.Entities.Ticket>()), Times.Once);
        _producer.Verify(x => x.PublishAsync(It.IsAny<TicketCreatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _logger.Verify(x => x.LogAsync(It.IsAny<Guid>(), customerId, ActorRoleEnum.Customer, "Customer", ActivityActionEnum.Created, null, null, null), Times.Once);
    }
}
