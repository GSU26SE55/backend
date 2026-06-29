using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;

namespace TicketService.UnitTests.Application.Services;

public class SlaCalculatorTests
{
    private readonly SlaCalculator _sut = new();

    [Theory]
    [InlineData(TicketPriorityEnum.P1Critical, 4)]
    [InlineData(TicketPriorityEnum.P2High, 24)]
    [InlineData(TicketPriorityEnum.P3Normal, 72)]
    public void CalculateSlaDueDate_ShouldReturnCorrectDueDate_ForGivenPriority(TicketPriorityEnum priority, int expectedHours)
    {
        // Arrange
        var createdAt = new DateTime(2023, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var ticket = new Ticket { CreatedAt = createdAt, Priority = priority, Code = "T1", Title = "Test", Description = "Test", Category = TicketCategoryEnum.Other, Status = TicketStatusEnum.New, Origin = TicketOriginEnum.ManualByCustomer };
        var expectedDueDate = createdAt.AddHours(expectedHours);

        // Act
        var dueDate = _sut.CalculateSlaDueDate(ticket);

        // Assert
        dueDate.Should().Be(expectedDueDate);
    }

    [Theory]
    [InlineData((TicketPriorityEnum)0)]
    [InlineData((TicketPriorityEnum)99)]
    [InlineData((TicketPriorityEnum)(-1))]
    public void CalculateSlaDueDate_ShouldThrowArgumentOutOfRangeException_WhenPriorityIsInvalid(TicketPriorityEnum invalidPriority)
    {
        // Arrange
        var ticket = new Ticket
        {
            CreatedAt = DateTime.UtcNow,
            Priority = invalidPriority,
            Code = "T1",
            Title = "Test",
            Description = "Test",
            Category = TicketCategoryEnum.Other,
            Status = TicketStatusEnum.New,
            Origin = TicketOriginEnum.ManualByCustomer
        };

        // Act
        Action act = () => _sut.CalculateSlaDueDate(ticket);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage($"*Giá trị Priority {invalidPriority} không được hỗ trợ*");
    }

    [Fact]
    public void CalculateSlaDueDate_ShouldThrowArgumentNullException_WhenPriorityIsNull()
    {
        // Arrange
        var ticket = new Ticket { CreatedAt = DateTime.UtcNow, Priority = null, Code = "T1", Title = "Test", Description = "Test", Category = TicketCategoryEnum.Other, Status = TicketStatusEnum.New, Origin = TicketOriginEnum.ManualByCustomer };

        // Act
        Action act = () => _sut.CalculateSlaDueDate(ticket);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
