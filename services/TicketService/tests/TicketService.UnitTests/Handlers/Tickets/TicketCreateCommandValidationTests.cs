using FluentAssertions;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Tickets;

public class TicketCreateCommandValidationTests
{
    [Fact]
    public async Task ValidateAsync_TwoBatteryIds_ReturnsValidationError()
    {
        var command = ValidCommand();
        command.BatteryAssetIds = [Guid.NewGuid(), Guid.NewGuid()];

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(error => error.Field == "BatteryAssetIds"
            && error.Detail.Contains("Exactly one battery"));
    }

    [Fact]
    public async Task ValidateAsync_OneBatteryId_IsValid()
    {
        var result = await ValidCommand().ValidateAsync();

        result.IsSuccess.Should().BeTrue();
        result.ListErrors.Should().BeEmpty();
    }

    private static TicketCreateCommand ValidCommand() => new()
    {
        Title = "Battery maintenance",
        Description = "Battery requires maintenance.",
        Category = TicketCategoryEnum.Repair,
        CustomerId = Guid.NewGuid(),
        BatteryAssetIds = [Guid.NewGuid()],
        IncidentDetectedAt = DateTime.UtcNow.AddMinutes(-1)
    };
}
