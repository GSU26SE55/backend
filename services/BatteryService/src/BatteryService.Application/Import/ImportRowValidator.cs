using System.Globalization;
using System.Text.RegularExpressions;
using BatteryService.Domain.Enums;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.Import;

/// <summary>Hiện thực <see cref="IImportRowValidator"/>.</summary>
public partial class ImportRowValidator : IImportRowValidator
{
    // Giới hạn độ dài lấy đúng theo cột trong cơ sở dữ liệu. Kiểm ở đây để dòng sai bị đánh dấu
    // kèm lý do đọc được, thay vì để cơ sở dữ liệu ném lỗi cắt chuỗi giữa lúc ghi hàng loạt.
    private const int ExternalRefMaxLength = 128;
    private const int EmailMaxLength = 256;
    private const int FullNameMaxLength = 150;
    private const int PhoneMaxLength = 20;
    private const int SiteNameMaxLength = 200;
    private const int AddressMaxLength = 500;
    private const int ContactNameMaxLength = 150;
    private const int ContactPhoneMaxLength = 30;
    private const int SerialMinLength = 5;
    private const int SerialMaxLength = 64;
    private const int BatteryTypeNameMaxLength = 100;
    private const int ManufacturerMaxLength = 100;
    private const int LocationMaxLength = 255;
    private const int NotesMaxLength = 1000;

    /// <summary>Các dạng ngày chấp nhận được, theo thứ tự thử.</summary>
    /// <remarks>
    /// Chỉ nhận dạng có tháng đứng giữa hoặc dạng ISO. Cố tình KHÔNG nhận <c>MM/dd/yyyy</c> lẫn
    /// <c>dd/MM/yyyy</c> cùng lúc: "03/04/2021" khi đó có hai nghĩa và không có cách nào biết đúng,
    /// mà đoán sai thì ngày lắp lệch cả tháng mà không ai phát hiện.
    /// </remarks>
    private static readonly string[] AcceptedDateFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "dd-MM-yyyy",
        "dd/MM/yyyy",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssZ"
    ];

    private readonly ImportOptions _options;

    public ImportRowValidator(IOptions<ImportOptions> options)
    {
        _options = options.Value;
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.IgnoreCase)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[A-Z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SerialPattern();

    public ImportValidation<ValidatedCustomer> ValidateCustomer(ImportCustomerRow row)
    {
        var errors = new List<Errors>();

        var externalCode = RequireReference(row.ExternalCustomerCode, nameof(row.ExternalCustomerCode), errors);
        var fullName = RequireText(row.FullName, nameof(row.FullName), FullNameMaxLength, errors);
        var email = (row.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (email.Length == 0)
            Add(errors, nameof(row.Email), "Email is required.");
        else if (email.Length > EmailMaxLength)
            Add(errors, nameof(row.Email), $"Email must not exceed {EmailMaxLength} characters.");
        else if (!EmailPattern().IsMatch(email))
            Add(errors, nameof(row.Email), "Email format is invalid.");

        var phone = OptionalText(row.Phone);
        if (phone is not null && phone.Length > PhoneMaxLength)
            Add(errors, nameof(row.Phone), $"Phone number must not exceed {PhoneMaxLength} characters.");

        return errors.Count > 0
            ? ImportValidation<ValidatedCustomer>.Invalid(errors)
            : ImportValidation<ValidatedCustomer>.Ok(new ValidatedCustomer(
                externalCode, row.ExternalCustomerCode.Trim(), fullName, email, phone));
    }

    public ImportValidation<ValidatedSite> ValidateSite(ImportSiteRow row)
    {
        var errors = new List<Errors>();

        var externalCode = RequireReference(row.ExternalSiteCode, nameof(row.ExternalSiteCode), errors);
        var customerCode = RequireReference(row.ExternalCustomerCode, nameof(row.ExternalCustomerCode), errors);
        var name = RequireText(row.SiteName, nameof(row.SiteName), SiteNameMaxLength, errors);
        var address = OptionalTextWithLimit(row.Address, nameof(row.Address), AddressMaxLength, errors);
        var contactName = OptionalTextWithLimit(row.ContactPersonName, nameof(row.ContactPersonName), ContactNameMaxLength, errors);
        var contactPhone = OptionalTextWithLimit(row.ContactPersonPhone, nameof(row.ContactPersonPhone), ContactPhoneMaxLength, errors);

        var latitude = ParseCoordinate(row.Latitude, nameof(row.Latitude), -90m, 90m, errors);
        var longitude = ParseCoordinate(row.Longitude, nameof(row.Longitude), -180m, 180m, errors);

        // Ngày lắp của site không bắt buộc trong file: nhiều đơn vị lắp đặt chỉ ghi ngày cho từng
        // quả pin. Thiếu thì lấy ngày hôm nay, vì cột trong cơ sở dữ liệu không cho phép rỗng.
        var installDate = ParseDate(row.InstallDate, nameof(row.InstallDate), required: false, errors)
                          ?? DateTime.UtcNow.Date;
        ValidateInstallDateRange(installDate, nameof(row.InstallDate), errors);

        return errors.Count > 0
            ? ImportValidation<ValidatedSite>.Invalid(errors)
            : ImportValidation<ValidatedSite>.Ok(new ValidatedSite(
                externalCode, row.ExternalSiteCode.Trim(), customerCode, name, address,
                latitude, longitude, installDate, contactName, contactPhone));
    }

    public ImportValidation<ValidatedAsset> ValidateAsset(ImportAssetRow row)
    {
        var errors = new List<Errors>();

        var externalCode = RequireReference(row.ExternalAssetCode, nameof(row.ExternalAssetCode), errors);
        var siteCode = RequireReference(row.ExternalSiteCode, nameof(row.ExternalSiteCode), errors);

        var serial = ImportSerialNormalizer.Normalize(row.SerialNumber);
        if (serial.Length == 0)
        {
            Add(errors, nameof(row.SerialNumber), "Battery serial number is required.");
        }
        else if (serial.Length < SerialMinLength || serial.Length > SerialMaxLength)
        {
            Add(errors, nameof(row.SerialNumber),
                $"Battery serial number must be {SerialMinLength}-{SerialMaxLength} characters after normalization (normalized value: \"{serial}\").");
        }
        else if (!SerialPattern().IsMatch(serial))
        {
            // Về lý thuyết không tới được nhánh này vì bộ chuẩn hoá đã loại hết ký tự lạ. Giữ lại
            // như một chốt chặn: nếu ai đó đổi bộ chuẩn hoá, lỗi lộ ra ở đây thay vì lộ ra lúc ghi.
            Add(errors, nameof(row.SerialNumber), "Battery serial number contains unsupported characters after normalization.");
        }

        var typeName = RequireText(row.BatteryTypeName, nameof(row.BatteryTypeName), BatteryTypeNameMaxLength, errors);
        var manufacturer = OptionalTextWithLimit(row.Manufacturer, nameof(row.Manufacturer), ManufacturerMaxLength, errors);
        var location = OptionalTextWithLimit(row.Location, nameof(row.Location), LocationMaxLength, errors);
        var notes = OptionalTextWithLimit(row.Notes, nameof(row.Notes), NotesMaxLength, errors);

        var capacity = ParsePositiveDecimal(row.NominalCapacityAh, nameof(row.NominalCapacityAh), errors);
        var voltage = ParsePositiveDecimal(row.NominalVoltage, nameof(row.NominalVoltage), errors);
        var chemistry = ParseChemistry(row.Chemistry, nameof(row.Chemistry), errors);

        var installDate = ParseDate(row.InstallDate, nameof(row.InstallDate), required: false, errors)
                          ?? DateTime.UtcNow.Date;
        ValidateInstallDateRange(installDate, nameof(row.InstallDate), errors);

        var warrantyEnd = ParseDate(row.WarrantyEndDate, nameof(row.WarrantyEndDate), required: false, errors);
        if (warrantyEnd.HasValue && warrantyEnd.Value <= installDate)
            Add(errors, nameof(row.WarrantyEndDate), "Warranty end date must be after the install date.");

        return errors.Count > 0
            ? ImportValidation<ValidatedAsset>.Invalid(errors)
            : ImportValidation<ValidatedAsset>.Ok(new ValidatedAsset(
                externalCode, row.ExternalAssetCode.Trim(), siteCode, serial, row.SerialNumber.Trim(),
                typeName, manufacturer, capacity, voltage, chemistry,
                installDate, warrantyEnd, location, notes));
    }


    private void ValidateInstallDateRange(DateTime installDate, string field, List<Errors> errors)
    {
        if (installDate.Date > DateTime.UtcNow.Date)
        {
            Add(errors, field, "Install date cannot be in the future.");
            return;
        }

        var oldest = DateTime.UtcNow.Date.AddYears(-_options.MaxInstallDateAgeYears);
        if (installDate.Date < oldest)
        {
            Add(errors, field,
                $"Install date is too far in the past (maximum {_options.MaxInstallDateAgeYears} years).");
        }
    }

    private static string RequireReference(string? raw, string field, List<Errors> errors)
    {
        var normalized = ImportSerialNormalizer.NormalizeReference(raw);
        if (normalized.Length == 0)
            Add(errors, field, "This reference code is required.");
        else if (normalized.Length > ExternalRefMaxLength)
            Add(errors, field, $"Reference code must not exceed {ExternalRefMaxLength} characters.");

        return normalized;
    }

    private static string RequireText(string? raw, string field, int maxLength, List<Errors> errors)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0)
            Add(errors, field, "This value is required.");
        else if (value.Length > maxLength)
            Add(errors, field, $"Value must not exceed {maxLength} characters.");

        return value;
    }

    private static string? OptionalText(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? OptionalTextWithLimit(string? raw, string field, int maxLength, List<Errors> errors)
    {
        var value = OptionalText(raw);
        if (value is not null && value.Length > maxLength)
            Add(errors, field, $"Value must not exceed {maxLength} characters.");

        return value;
    }

    private static decimal? ParseCoordinate(string? raw, string field, decimal min, decimal max, List<Errors> errors)
    {
        var value = OptionalText(raw);
        if (value is null)
            return null;

        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            Add(errors, field, "Value must be a decimal number using a dot as the decimal separator.");
            return null;
        }

        if (parsed < min || parsed > max)
        {
            Add(errors, field, $"Value must be between {min} and {max}.");
            return null;
        }

        return parsed;
    }

    private static decimal? ParsePositiveDecimal(string? raw, string field, List<Errors> errors)
    {
        var value = OptionalText(raw);
        if (value is null)
            return null;

        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            Add(errors, field, "Value must be a decimal number using a dot as the decimal separator.");
            return null;
        }

        if (parsed <= 0)
        {
            Add(errors, field, "Value must be greater than zero.");
            return null;
        }

        return parsed;
    }

    private static DateTime? ParseDate(string? raw, string field, bool required, List<Errors> errors)
    {
        var value = OptionalText(raw);
        if (value is null)
        {
            if (required)
                Add(errors, field, "A date is required.");
            return null;
        }

        if (!DateTime.TryParseExact(value, AcceptedDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            Add(errors, field, $"Date must use one of these formats: {string.Join(", ", AcceptedDateFormats)}.");
            return null;
        }

        return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
    }

    private static BatteryChemistryEnum? ParseChemistry(string? raw, string field, List<Errors> errors)
    {
        var value = OptionalText(raw);
        if (value is null)
            return null;

        if (Enum.TryParse<BatteryChemistryEnum>(value, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        Add(errors, field,
            $"Unknown chemistry \"{value}\". Accepted values: {string.Join(", ", Enum.GetNames<BatteryChemistryEnum>())}.");
        return null;
    }

    private static void Add(List<Errors> errors, string field, string detail) =>
        errors.Add(new Errors { Field = field, Detail = detail });
}
