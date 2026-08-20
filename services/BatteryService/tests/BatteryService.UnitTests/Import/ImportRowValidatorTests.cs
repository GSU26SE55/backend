using BatteryService.Application.Import;
using BatteryService.Domain.Enums;
using Microsoft.Extensions.Options;

namespace BatteryService.UnitTests.Import;

/// <summary>I2 — bộ kiểm định riêng của đường nhập dữ liệu.</summary>
public class ImportRowValidatorTests
{
    private static ImportRowValidator Build(int maxInstallAgeYears = 15) =>
        new(Options.Create(new ImportOptions { MaxInstallDateAgeYears = maxInstallAgeYears }));

    [Fact]
    public void ValidateCustomer_CollectsEveryErrorOnTheRow_NotJustTheFirst()
    {
        // Người dùng sửa file một lần rồi nạp lại, nên một dòng phải trả về đủ lỗi của nó.
        var result = Build().ValidateCustomer(new ImportCustomerRow
        {
            ExternalCustomerCode = "",
            FullName = "",
            Email = "not-an-email"
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.Field)
            .Should().Contain(new[] { "ExternalCustomerCode", "FullName", "Email" });
    }

    [Fact]
    public void ValidateCustomer_LowercasesAndTrimsEmail()
    {
        var result = Build().ValidateCustomer(new ImportCustomerRow
        {
            ExternalCustomerCode = "KH-001",
            FullName = "  Cong ty A  ",
            Email = "  A@Example.COM  "
        });

        result.IsValid.Should().BeTrue();
        result.Value!.Email.Should().Be("a@example.com");
        result.Value.FullName.Should().Be("Cong ty A");
    }

    [Fact]
    public void ValidateAsset_InstallDateSixYearsAgo_IsAccepted()
    {
        // Đây là điểm khác biệt then chốt so với đường tạo thủ công, vốn chặn ở 5 năm. Dữ liệu bàn
        // giao từ một đơn vị lắp đặt lâu năm gần như chắc chắn có pin cũ hơn thế.
        var sixYearsAgo = DateTime.UtcNow.Date.AddYears(-6).ToString("yyyy-MM-dd");

        var result = Build().ValidateAsset(NewAssetRow(installDate: sixYearsAgo));

        result.IsValid.Should().BeTrue();
        result.Value!.InstallDate.Date.Should().Be(DateTime.UtcNow.Date.AddYears(-6));
    }

    [Fact]
    public void ValidateAsset_InstallDateBeyondConfiguredLimit_IsRejected()
    {
        var tooOld = DateTime.UtcNow.Date.AddYears(-20).ToString("yyyy-MM-dd");

        var result = Build(maxInstallAgeYears: 15).ValidateAsset(NewAssetRow(installDate: tooOld));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "InstallDate");
    }

    [Fact]
    public void ValidateAsset_FutureInstallDate_IsRejected()
    {
        var future = DateTime.UtcNow.Date.AddDays(1).ToString("yyyy-MM-dd");

        var result = Build().ValidateAsset(NewAssetRow(installDate: future));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Detail.Contains("future"));
    }

    [Fact]
    public void ValidateAsset_MessySerial_IsNormalizedAndKeepsTheOriginal()
    {
        var row = NewAssetRow();
        row.SerialNumber = "pyl/us3000c 88a21";

        var result = Build().ValidateAsset(row);

        result.IsValid.Should().BeTrue();
        result.Value!.SerialNumber.Should().Be("PYL-US3000C-88A21");
        result.Value.SerialNumberRaw.Should().Be("pyl/us3000c 88a21");
    }

    [Fact]
    public void ValidateAsset_SerialTooShortAfterNormalization_ReportsTheNormalizedValue()
    {
        var row = NewAssetRow();
        row.SerialNumber = "a b";

        var result = Build().ValidateAsset(row);

        result.IsValid.Should().BeFalse();
        // Thông báo phải cho thấy giá trị sau chuẩn hoá, nếu không người dùng nhìn file gốc sẽ
        // không hiểu vì sao một chuỗi trông đủ dài lại bị coi là quá ngắn.
        result.Errors.Should().Contain(error => error.Detail.Contains("A-B"));
    }

    [Fact]
    public void ValidateAsset_WarrantyBeforeInstall_IsRejected()
    {
        var row = NewAssetRow(installDate: "2021-03-15");
        row.WarrantyEndDate = "2020-01-01";

        var result = Build().ValidateAsset(row);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "WarrantyEndDate");
    }

    [Fact]
    public void ValidateAsset_UnknownChemistry_ListsTheAcceptedValues()
    {
        var row = NewAssetRow();
        row.Chemistry = "Unobtanium";

        var result = Build().ValidateAsset(row);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error =>
            error.Field == "Chemistry" && error.Detail.Contains(nameof(BatteryChemistryEnum.LiFePO4)));
    }

    [Fact]
    public void ValidateAsset_CapacityWithCommaDecimalSeparator_IsRejectedClearly()
    {
        // "74,5" là cách viết quen thuộc ở Việt Nam nhưng đọc theo chuẩn bất biến sẽ ra 745 —
        // sai gấp mười lần mà không có dấu hiệu nào. Từ chối rõ ràng an toàn hơn nhiều.
        var row = NewAssetRow();
        row.NominalCapacityAh = "74,5";

        var result = Build().ValidateAsset(row);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Field == "NominalCapacityAh");
    }

    [Fact]
    public void ValidateSite_CoordinatesOutOfRange_AreRejected()
    {
        var result = Build().ValidateSite(new ImportSiteRow
        {
            ExternalSiteCode = "ST-001",
            ExternalCustomerCode = "KH-001",
            SiteName = "Nha may Long An",
            Latitude = "120",
            Longitude = "-200"
        });

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.Field).Should().Contain(new[] { "Latitude", "Longitude" });
    }

    [Fact]
    public void ValidateSite_MissingInstallDate_FallsBackToToday()
    {
        // Cột này không bắt buộc trong file vì nhiều đơn vị chỉ ghi ngày cho từng quả pin, nhưng
        // cột trong cơ sở dữ liệu không cho phép rỗng.
        var result = Build().ValidateSite(new ImportSiteRow
        {
            ExternalSiteCode = "ST-001",
            ExternalCustomerCode = "KH-001",
            SiteName = "Nha may Long An"
        });

        result.IsValid.Should().BeTrue();
        result.Value!.InstallDate.Date.Should().Be(DateTime.UtcNow.Date);
    }


    private static ImportAssetRow NewAssetRow(string? installDate = null) => new()
    {
        ExternalAssetCode = "AS-0001",
        ExternalSiteCode = "ST-001",
        SerialNumber = "PYL-US3000C-88A21",
        BatteryTypeName = "US3000C",
        Manufacturer = "Pylontech",
        NominalCapacityAh = "74",
        NominalVoltage = "48",
        Chemistry = "LiFePO4",
        InstallDate = installDate ?? DateTime.UtcNow.Date.AddYears(-2).ToString("yyyy-MM-dd")
    };
}
