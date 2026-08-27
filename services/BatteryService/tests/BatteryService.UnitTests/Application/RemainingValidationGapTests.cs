using BatteryService.Application.CQRS.Command.BatteryType;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Command.IotDevice;
using BatteryService.Application.CQRS.Query.IotDevice;
using BatteryService.Application.CQRS.Query.SensorReading;
using BatteryService.Domain.Enums;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Đóng nốt các lớp <c>IValidatable</c> của BatteryService chưa được kiểm ở tầng <c>ValidateAsync</c>.
/// </summary>
public class UpdateBatteryTypeCommandValidationTests
{
    private static UpdateBatteryTypeCommand Valid() => new()
    {
        Id = Guid.NewGuid(),
        Name = "LFP 48V 100Ah",
        Manufacturer = "CATL",
        NominalCapacityAh = 100m,
        NominalVoltage = 48m,
        Chemistry = BatteryChemistryEnum.LiFePO4,
        MaxCycleCount = 3000,
        Description = "Standard rack module"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingName_Fails(string name)
    {
        var c = Valid();
        c.Name = name;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task NameTooLong_Fails()
    {
        var c = Valid();
        c.Name = new string('n', 101);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Name");
    }

    [Fact]
    public async Task ManufacturerTooLong_Fails()
    {
        var c = Valid();
        c.Manufacturer = new string('m', 101);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Manufacturer");
    }

    /// <summary>Dung lượng và điện áp phải dương — 0 hay âm là dữ liệu vô nghĩa.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositiveCapacity_Fails(int capacity)
    {
        var c = Valid();
        c.NominalCapacityAh = capacity;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NominalCapacityAh");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-48)]
    public async Task NonPositiveVoltage_Fails(int voltage)
    {
        var c = Valid();
        c.NominalVoltage = voltage;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "NominalVoltage");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task NonPositiveMaxCycleCount_Fails(int cycles)
    {
        var c = Valid();
        c.MaxCycleCount = cycles;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "MaxCycleCount");
    }

    [Fact]
    public async Task DescriptionTooLong_Fails()
    {
        var c = Valid();
        c.Description = new string('d', 501);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Description");
    }

    [Fact]
    public async Task EmptyId_Fails()
    {
        var c = Valid();
        c.Id = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Id");
    }

    /// <summary>Các trường tuỳ chọn bỏ trống thì không bị kiểm độ dài.</summary>
    [Fact]
    public async Task OptionalFieldsNull_Passes()
    {
        var c = Valid();
        c.Manufacturer = null;
        c.Description = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    /// <summary>Mọi luật cùng chạy — sai hết thì phải thấy đủ lỗi từng trường, không dừng ở lỗi đầu.</summary>
    [Fact]
    public async Task AllFieldsInvalid_ReportsEveryField()
    {
        var r = await new UpdateBatteryTypeCommand
        {
            Id = Guid.Empty,
            Name = "",
            Manufacturer = new string('m', 101),
            NominalCapacityAh = 0m,
            NominalVoltage = 0m,
            MaxCycleCount = 0,
            Description = new string('d', 501)
        }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "Id");
        r.ListErrors.Should().Contain(e => e.Field == "Name");
        r.ListErrors.Should().Contain(e => e.Field == "Manufacturer");
        r.ListErrors.Should().Contain(e => e.Field == "NominalCapacityAh");
        r.ListErrors.Should().Contain(e => e.Field == "NominalVoltage");
        r.ListErrors.Should().Contain(e => e.Field == "MaxCycleCount");
        r.ListErrors.Should().Contain(e => e.Field == "Description");
    }
}

public class CreateIotDeviceCalibrationCommandValidationTests
{
    private static CreateIotDeviceCalibrationCommand Valid() => new()
    {
        IotDeviceId = Guid.NewGuid(),
        Channel = "voltage",
        BatteryAssetId = Guid.NewGuid(),
        Scale = 1.02m,
        Offset = -0.05m,
        Unit = "V",
        CalibratedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        ExpiresAt = new DateTime(2027, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        Notes = "Bench calibration"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingChannel_Fails(string channel)
    {
        var c = Valid();
        c.Channel = channel;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Channel");
    }

    [Fact]
    public async Task ChannelTooLong_Fails()
    {
        var c = Valid();
        c.Channel = new string('c', 33);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Channel");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task MissingUnit_Fails(string unit)
    {
        var c = Valid();
        c.Unit = unit;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Unit");
    }

    [Fact]
    public async Task UnitTooLong_Fails()
    {
        var c = Valid();
        c.Unit = new string('u', 17);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Unit");
    }

    /// <summary>
    /// Scale = 0 làm mọi số đọc thành 0 sau hiệu chuẩn, nên bị chặn; giá trị âm vẫn hợp lệ
    /// (dùng để đảo chiều cảm biến lắp ngược).
    /// </summary>
    [Fact]
    public async Task ZeroScale_Fails()
    {
        var c = Valid();
        c.Scale = 0m;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Scale");
    }

    [Fact]
    public async Task NegativeScale_Passes()
    {
        var c = Valid();
        c.Scale = -1m;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Scale");
    }

    [Fact]
    public async Task MissingCalibratedAt_Fails()
    {
        var c = Valid();
        c.CalibratedAt = default;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "CalibratedAt");
    }

    /// <summary>Hạn hiệu chuẩn phải sau ngày hiệu chuẩn; bằng nhau cũng không hợp lệ.</summary>
    [Fact]
    public async Task ExpiresAtBeforeCalibratedAt_Fails()
    {
        var c = Valid();
        c.ExpiresAt = c.CalibratedAt.AddDays(-1);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ExpiresAt");
    }

    [Fact]
    public async Task ExpiresAtEqualsCalibratedAt_Fails()
    {
        var c = Valid();
        c.ExpiresAt = c.CalibratedAt;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ExpiresAt");
    }

    /// <summary>Không đặt hạn nghĩa là hiệu chuẩn không hết hạn — hợp lệ.</summary>
    [Fact]
    public async Task NullExpiresAt_Passes()
    {
        var c = Valid();
        c.ExpiresAt = null;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NotesTooLong_Fails()
    {
        var c = Valid();
        c.Notes = new string('n', 501);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Notes");
    }
}

public class GetSensorReadingAggregateQueryValidationTests
{
    private static GetSensorReadingAggregateQuery Valid() => new()
    {
        BatteryAssetId = Guid.NewGuid(),
        From = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        To = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc),
        Interval = "1h"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyBatteryAssetId_Fails()
    {
        var q = Valid();
        q.BatteryAssetId = Guid.Empty;

        var r = await q.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "BatteryAssetId");
    }

    [Theory]
    [InlineData("1m")]
    [InlineData("5m")]
    [InlineData("15m")]
    [InlineData("1h")]
    [InlineData("1d")]
    [InlineData("1H")]   // so sánh không phân biệt hoa thường
    public async Task AllowedInterval_Passes(string interval)
    {
        var q = Valid();
        q.Interval = interval;

        (await q.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Interval");
    }

    [Theory]
    [InlineData("2h")]
    [InlineData("30s")]
    [InlineData("")]
    [InlineData("hour")]
    public async Task DisallowedInterval_Fails(string interval)
    {
        var q = Valid();
        q.Interval = interval;

        (await q.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Interval");
    }

    /// <summary>Khoảng thời gian đảo ngược là lỗi cross-field nên trả 422, không phải 400.</summary>
    [Fact]
    public async Task FromAfterTo_Returns422()
    {
        var q = Valid();
        q.From = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);
        q.To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var r = await q.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(422);
        r.ListErrors.Should().Contain(e => e.Field == "To");
    }

    /// <summary>
    /// Lỗi từng trường (400) được ưu tiên hơn lỗi cross-field (422) khi cả hai cùng xảy ra.
    /// </summary>
    [Fact]
    public async Task FieldErrorTakesPrecedenceOverCrossField()
    {
        var q = Valid();
        q.Interval = "2h";
        q.From = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);
        q.To = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var r = await q.ValidateAsync();

        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Interval");
        r.ListErrors.Should().Contain(e => e.Field == "To");
    }

    /// <summary>From bằng To là một điểm thời gian — hợp lệ.</summary>
    [Fact]
    public async Task FromEqualsTo_Passes()
    {
        var q = Valid();
        q.To = q.From;

        (await q.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    /// <summary>Không truyền khoảng thời gian thì không kiểm cross-field.</summary>
    [Fact]
    public async Task NoTimeRange_Passes()
    {
        var q = Valid();
        q.From = null;
        q.To = null;

        (await q.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}

/// <summary>
/// Luật validate của lệnh nạp dữ liệu đối tác (thêm ở Sprint hiện tại). Luật "phải có ít nhất một
/// file" đã được kiểm ở bộ test handler; phần còn thiếu là độ dài tên file và các tổ hợp file.
/// </summary>
public class CreateImportBatchCommandValidationTests
{
    private static byte[] Csv() => "id,name\n1,Acme"u8.ToArray();

    [Fact]
    public async Task CustomersOnly_Passes()
    {
        var r = await new CreateImportBatchCommand { CustomersCsv = Csv() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Nạp riêng lẻ từng loại đều hợp lệ, miễn có ít nhất một file có nội dung.</summary>
    [Fact]
    public async Task SitesOnly_Passes()
    {
        var r = await new CreateImportBatchCommand { SitesCsv = Csv() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AssetsOnly_Passes()
    {
        var r = await new CreateImportBatchCommand { AssetsCsv = Csv() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>File gửi lên nhưng rỗng byte không được tính là "có file".</summary>
    [Fact]
    public async Task AllFilesEmpty_Fails()
    {
        var r = await new CreateImportBatchCommand
        {
            CustomersCsv = [],
            SitesCsv = [],
            AssetsCsv = []
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "files");
    }

    [Fact]
    public async Task FileNameTooLong_Fails()
    {
        var r = await new CreateImportBatchCommand
        {
            CustomersCsv = Csv(),
            FileName = new string('f', 256)
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "FileName" && e.Detail.Contains("255"));
    }

    [Fact]
    public async Task FileNameExactly255_Passes()
    {
        var r = await new CreateImportBatchCommand
        {
            CustomersCsv = Csv(),
            FileName = new string('f', 255)
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Không truyền tên file vẫn hợp lệ — tên chỉ để hiển thị.</summary>
    [Fact]
    public async Task NullFileName_Passes()
    {
        var r = await new CreateImportBatchCommand { CustomersCsv = Csv(), FileName = null }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Không có file VÀ tên quá dài phải sinh đủ hai lỗi.</summary>
    [Fact]
    public async Task NoFilesAndLongName_ReportsBothErrors()
    {
        var r = await new CreateImportBatchCommand { FileName = new string('f', 300) }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "files");
        r.ListErrors.Should().Contain(e => e.Field == "FileName");
    }
}

/// <summary>
/// Luật validate của query lịch sử heartbeat (IOT3-58). Phân trang theo con trỏ nên
/// <c>Limit</c> là chốt chặn duy nhất ngăn một request kéo về toàn bộ bảng heartbeat.
/// </summary>
public class GetIotDeviceHeartbeatsQueryValidationTests
{
    private static GetIotDeviceHeartbeatsQuery Valid() => new()
    {
        DeviceId = Guid.NewGuid(),
        From = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        To = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc),
        Limit = 100
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyDeviceId_Fails()
    {
        var q = Valid();
        q.DeviceId = Guid.Empty;

        var r = await q.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "DeviceId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(GetIotDeviceHeartbeatsQuery.MaxLimit + 1)]
    public async Task LimitOutOfRange_Fails(int limit)
    {
        var q = Valid();
        q.Limit = limit;

        (await q.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Limit");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(GetIotDeviceHeartbeatsQuery.MaxLimit)]
    public async Task LimitAtBoundary_Passes(int limit)
    {
        var q = Valid();
        q.Limit = limit;

        (await q.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "Limit");
    }

    /// <summary>Khoảng thời gian đảo ngược là lỗi liên trường nên trả 422, không phải 400.</summary>
    [Fact]
    public async Task FromAfterTo_Returns422()
    {
        var q = Valid();
        q.From = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        q.To = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var r = await q.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(422);
        r.ListErrors.Should().Contain(e => e.Field == "To");
    }

    /// <summary>Lỗi từng trường (400) được ưu tiên hơn lỗi liên trường (422).</summary>
    [Fact]
    public async Task FieldErrorTakesPrecedenceOverCrossField()
    {
        var q = Valid();
        q.Limit = 0;
        q.From = new DateTime(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);
        q.To = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        var r = await q.ValidateAsync();

        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Limit");
        r.ListErrors.Should().Contain(e => e.Field == "To");
    }

    /// <summary>Không truyền khoảng thời gian thì không kiểm liên trường.</summary>
    [Fact]
    public async Task NoTimeRange_Passes()
    {
        var q = Valid();
        q.From = null;
        q.To = null;

        (await q.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}
