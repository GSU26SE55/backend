using BatteryService.Application.CQRS.Command.CascadeRisk;
using BatteryService.Domain.Enums;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint 7 B4 (§31.7) — field-level validation của SetBatteryAssetTopologyCommand.
/// Lỗi field → ghi vào ListErrors (Field + Detail); input hợp lệ → IsSuccess + ListErrors rỗng.
/// </summary>
public class SetBatteryAssetTopologyCommandTests
{
    [Fact]
    public async Task Valid_Input_NoErrors()
    {
        var cmd = new SetBatteryAssetTopologyCommand
        {
            Id = Guid.NewGuid(),
            ElectricalTopology = ElectricalTopologyEnum.SeriesString
        };

        var res = await cmd.ValidateAsync();

        res.IsSuccess.Should().BeTrue();
        res.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_Enum_Adds_FieldError()
    {
        var cmd = new SetBatteryAssetTopologyCommand
        {
            Id = Guid.NewGuid(),
            ElectricalTopology = (ElectricalTopologyEnum)99   // ngoài 1..4
        };

        var res = await cmd.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(400);
        res.ListErrors.Should().ContainSingle(e => e.Field == nameof(SetBatteryAssetTopologyCommand.ElectricalTopology));
        res.ListErrors.Single().Detail.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Empty_Id_Adds_FieldError()
    {
        var cmd = new SetBatteryAssetTopologyCommand
        {
            Id = Guid.Empty,
            ElectricalTopology = ElectricalTopologyEnum.Independent
        };

        var res = await cmd.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.ListErrors.Should().Contain(e => e.Field == nameof(SetBatteryAssetTopologyCommand.Id));
    }
}
