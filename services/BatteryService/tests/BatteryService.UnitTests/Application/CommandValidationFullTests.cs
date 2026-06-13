using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Command.BatteryAsset;
using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.CQRS.Command.SensorReading;
using BatteryService.Application.CQRS.Command.Site;
using BatteryService.Application.CQRS.Command.ThresholdConfig;
using BatteryService.Domain.Enums;
using SharedContracts.Interfaces;

namespace BatteryService.UnitTests.Application;

public class CommandValidationFullTests
{
    // ===== BatteryType =====
    [Fact]
    public async Task CreateBatteryType_AllErrors_Collected()
    {
        var cmd = new CreateBatteryTypeCommand
        {
            Name = new string('x', 101),
            Manufacturer = new string('m', 101),
            NominalCapacityAh = 0,
            NominalVoltage = 0,
            MaxCycleCount = 0,
            Description = new string('d', 501)
        };
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().HaveCountGreaterThan(3);
    }

    [Fact]
    public async Task CreateBatteryType_EmptyName_Error()
    {
        var r = await new CreateBatteryTypeCommand { Name = "", NominalCapacityAh = 1, NominalVoltage = 1, MaxCycleCount = 1 }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Theory]
    [InlineData(typeof(DeleteBatteryTypeCommand))]
    [InlineData(typeof(RestoreBatteryTypeCommand))]
    [InlineData(typeof(DeleteBatteryAssetCommand))]
    [InlineData(typeof(RestoreBatteryAssetCommand))]
    [InlineData(typeof(AcknowledgeAlertCommand))]
    [InlineData(typeof(ResolveAlertCommand))]
    public async Task IdOnlyCommand_EmptyId_Fails(Type cmdType)
    {
        var cmd = Activator.CreateInstance(cmdType)!;
        var validateMethod = cmdType.GetMethod("ValidateAsync");
        var task = (Task)validateMethod!.Invoke(cmd, null)!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        result.GetType().GetProperty("IsSuccess")!.GetValue(result).Should().Be(false);
    }

    [Fact]
    public async Task DeleteBatteryType_ValidId_Succeeds()
    {
        var r = await new DeleteBatteryTypeCommand { Id = Guid.NewGuid() }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    // ===== BatteryAsset =====
    [Fact]
    public async Task CreateBatteryAsset_AllValidations()
    {
        var cmd = new CreateBatteryAssetCommand
        {
            SerialNumber = "",
            BatteryTypeId = Guid.Empty,
            CustomerId = Guid.Empty,
            InstallDate = default,
            Location = new string('l', 256),
            Latitude = 100,
            Longitude = 200,
            Notes = new string('n', 1001)
        };
        var r = await cmd.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public async Task CreateBatteryAsset_ShortSerial_Error()
    {
        var r = await new CreateBatteryAssetCommand { SerialNumber = "ABC", BatteryTypeId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow.AddDays(-1) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "SerialNumber");
    }

    [Fact]
    public async Task CreateBatteryAsset_TooOldInstall_Error()
    {
        var r = await new CreateBatteryAssetCommand { SerialNumber = "ABC-9", BatteryTypeId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow.AddYears(-10) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "InstallDate");
    }

    [Fact]
    public async Task CreateBatteryAsset_WarrantyBeforeInstall_Error()
    {
        var install = DateTime.UtcNow.AddDays(-1);
        var r = await new CreateBatteryAssetCommand { SerialNumber = "ABC-9", BatteryTypeId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), InstallDate = install, WarrantyEndDate = install.AddDays(-2) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "WarrantyEndDate");
    }

    [Fact]
    public async Task UpdateBatteryAsset_EmptyId_Error()
    {
        IValidatable<SharedContracts.Common.Responses.CommonResponse<BatteryService.Application.DTOs.BatteryAssetDto>> cmd =
            new UpdateBatteryAssetCommand { SerialNumber = "ABC-1", BatteryTypeId = Guid.NewGuid(), CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow.AddDays(-1) };
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Fact]
    public async Task TransferAsset_EmptyIds_Error()
    {
        var r = await new TransferBatteryAssetOwnerCommand().ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Id");
        r.ListErrors.Should().Contain(e => e.Field == "NewCustomerId");
    }

    [Fact]
    public async Task TransferAsset_LongReason_Error()
    {
        var r = await new TransferBatteryAssetOwnerCommand { Id = Guid.NewGuid(), NewCustomerId = Guid.NewGuid(), Reason = new string('r', 501) }.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Reason");
    }

    [Fact]
    public async Task TransferAsset_Valid_Succeeds()
    {
        var r = await new TransferBatteryAssetOwnerCommand { Id = Guid.NewGuid(), NewCustomerId = Guid.NewGuid() }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    // ===== Site =====
    [Fact]
    public async Task CreateSite_AllInvalid()
    {
        var r = await new CreateSiteCommand
        {
            Name = new string('x', 201),
            Address = new string('a', 501),
            CustomerId = Guid.Empty,
            Latitude = 100,
            Longitude = 200,
            InstallDate = DateTime.UtcNow.AddDays(1),
            ContactPersonName = new string('n', 151),
            ContactPersonPhone = new string('p', 31)
        }.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().HaveCountGreaterThan(6);
    }

    [Fact]
    public async Task UpdateSite_EmptyId_Error()
    {
        IValidatable<SharedContracts.Common.Responses.CommonResponse<BatteryService.Application.DTOs.SiteDto>> cmd =
            new UpdateSiteCommand { Name = "N", CustomerId = Guid.NewGuid(), InstallDate = DateTime.UtcNow.AddDays(-1) };
        var r = await cmd.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    // ===== ThresholdConfig =====
    [Fact]
    public async Task UpsertThresholdConfig_AllInvalid()
    {
        var r = await new UpsertThresholdConfigCommand
        {
            BatteryTypeId = Guid.Empty,
            VoltageMin = 0,
            VoltageMax = -1,
            TemperatureMin = 50,
            TemperatureMax = 10,
            SocWarningThreshold = -1,
            SocCriticalThreshold = 200,
            CurrentMaxCharge = 0,
            CurrentMaxDischarge = 0
        }.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public async Task UpsertThresholdConfig_Valid_Succeeds()
    {
        var r = await new UpsertThresholdConfigCommand
        {
            BatteryTypeId = Guid.NewGuid(),
            VoltageMin = 10,
            VoltageMax = 14,
            TemperatureMin = -10,
            TemperatureMax = 60,
            SocWarningThreshold = 30,
            SocCriticalThreshold = 10,
            CurrentMaxCharge = 5,
            CurrentMaxDischarge = 5
        }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    // ===== SensorReading =====
    [Fact]
    public async Task BatchIngest_Empty_Error()
    {
        var r = await new BatchIngestSensorReadingsCommand().ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Items");
    }

    [Fact]
    public async Task BatchIngest_PerItemErrors()
    {
        var r = await new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                new() { BatteryAssetId = Guid.Empty, Time = default, Voltage = -1, Temperature = -200, SocPercent = 200, CycleCount = -1, SourceDeviceId = new string('s', 65) },
                new() { BatteryAssetId = Guid.NewGuid(), Time = DateTime.UtcNow.AddYears(1), Voltage = 10, Temperature = 30, SocPercent = 50 }
            }
        }.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().HaveCountGreaterThan(5);
    }

    [Fact]
    public async Task BatchIngest_Valid_Succeeds()
    {
        var r = await new BatchIngestSensorReadingsCommand
        {
            Items = new List<SensorReadingItem>
            {
                new() { BatteryAssetId = Guid.NewGuid(), Time = DateTime.UtcNow, Voltage = 12, Current = 1, Temperature = 25, SocPercent = 50 }
            }
        }.ValidateAsync();
        r.IsSuccess.Should().BeTrue();
    }

    // Ensure enum usage compiles
    [Fact]
    public void EnumsBaseline_Default()
    {
        BatteryStatusEnum.Active.Should().Be(BatteryStatusEnum.Active);
    }
}
