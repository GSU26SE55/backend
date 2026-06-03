using FluentAssertions;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Helpers;

namespace TicketService.UnitTests.Helpers;

public class PriorityCalculatorTests
{
    private readonly PriorityCalculator _calculator = new();

    [Theory]
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.High, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.Medium, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.Low, TicketPriorityEnum.P2High)]
    [InlineData(ImpactScopeEnum.Site, UrgencyLevelEnum.High, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.Site, UrgencyLevelEnum.Medium, TicketPriorityEnum.P2High)]
    [InlineData(ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.High, TicketPriorityEnum.P2High)]
    [InlineData(ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Low, TicketPriorityEnum.P3Normal)]
    public void Calculate_VariousInputs_ReturnsCorrectPriority(ImpactScopeEnum impact, UrgencyLevelEnum urgency, TicketPriorityEnum expected)
    {
        var result = _calculator.Calculate(impact, urgency);
        result.Should().Be(expected);
    }
}
