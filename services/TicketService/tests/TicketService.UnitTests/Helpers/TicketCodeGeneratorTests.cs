using FluentAssertions;
using Moq;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Infrastructure.Implements.Helpers;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Helpers;

public class TicketCodeGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsCorrectFormat()
    {
        // Arrange
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var generator = new TicketCodeGenerator(uow.Object);

        // Act
        var code = await generator.GenerateAsync();

        // Assert
        code.Should().StartWith("TKT-");
        code.Should().HaveLength(13); // TKT-YYMM-XXXX = 3+1+4+1+4 = 13
    }

    [Fact]
    public async Task GenerateAsync_IncrementsSequence()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var prefix = $"TKT-{now:yyMM}-";
        var existingTicket = new Ticket
        {
            Id = Guid.NewGuid(),
            Code = $"{prefix}0005",
            Title = "T",
            Description = "D"
        };

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { existingTicket });
        var generator = new TicketCodeGenerator(uow.Object);

        // Act
        var code = await generator.GenerateAsync();

        // Assert
        code.Should().Be($"{prefix}0006");
    }
}
