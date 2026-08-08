using FluentAssertions;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;

namespace TicketService.UnitTests.Utils;

public class PriorityCalculatorTests
{
    private readonly PriorityCalculator _calculator = new();

    // Phủ đủ 9 ô của bảng trong User Guide §3.9 "Cách hệ thống tính mức ưu tiên":
    //
    //   Phạm vi \ Độ khẩn   Low   Medium   High
    //   MultiSite            P1     P1      P1
    //   Site                 P3     P2      P1
    //   SingleAsset          P3     P3      P2
    [Theory]
    // MultiSite luôn P1 bất kể độ khẩn — nhiều trạm cùng lỗi thì nguyên nhân ở hệ thống.
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.High, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.Medium, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.MultiSite, UrgencyLevelEnum.Low, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.Site, UrgencyLevelEnum.High, TicketPriorityEnum.P1Critical)]
    [InlineData(ImpactScopeEnum.Site, UrgencyLevelEnum.Medium, TicketPriorityEnum.P2High)]
    [InlineData(ImpactScopeEnum.Site, UrgencyLevelEnum.Low, TicketPriorityEnum.P3Normal)]
    [InlineData(ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.High, TicketPriorityEnum.P2High)]
    [InlineData(ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Medium, TicketPriorityEnum.P3Normal)]
    [InlineData(ImpactScopeEnum.SingleAsset, UrgencyLevelEnum.Low, TicketPriorityEnum.P3Normal)]
    public void Calculate_VariousInputs_ReturnsCorrectPriority(ImpactScopeEnum impact, UrgencyLevelEnum urgency, TicketPriorityEnum expected)
    {
        var result = _calculator.Calculate(impact, urgency);
        result.Should().Be(expected);
    }
}
