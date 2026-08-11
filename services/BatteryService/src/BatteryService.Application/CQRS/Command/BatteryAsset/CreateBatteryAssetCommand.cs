using System.Text.RegularExpressions;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class CreateBatteryAssetCommand : IRequest<CommonResponse<BatteryAssetDto>>, IValidatable<CommonResponse<BatteryAssetDto>>
{
    /// <summary>Serial number của asset (unique).</summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>ID BatteryType (Guid).</summary>
    public Guid BatteryTypeId { get; set; }

    /// <summary>ID Site (Guid).</summary>
    public Guid? SiteId { get; set; }

    /// <summary>ID Customer (Guid).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Ngày lắp đặt.</summary>
    public DateTime InstallDate { get; set; }

    /// <summary>Ngày hết bảo hành.</summary>
    public DateTime? WarrantyEndDate { get; set; }

    /// <summary>Vị trí lắp đặt (vd "Block A - Rack 01").</summary>
    public string? Location { get; set; }

    /// <summary>Vĩ độ (-90..90).</summary>
    public decimal? Latitude { get; set; }

    /// <summary>Kinh độ (-180..180).</summary>
    public decimal? Longitude { get; set; }

    /// <summary>Ghi chú tự do.</summary>
    public string? Notes { get; set; }

    public Task<CommonResponse<BatteryAssetDto>> ValidateAsync()
    {
        var response = new CommonResponse<BatteryAssetDto>();
        ValidateShared(response);

        if (CustomerId == Guid.Empty)
            AddError(response, nameof(CustomerId), "Customer Id is required.");

        return Task.FromResult(response);
    }

    protected void ValidateShared(CommonResponse<BatteryAssetDto> response)
    {
        var serial = SerialNumber.Trim();
        if (string.IsNullOrWhiteSpace(serial))
            AddError(response, nameof(SerialNumber), "Battery serial number is required.");
        else if (serial.Length is < 5 or > 64)
            AddError(response, nameof(SerialNumber), "Battery serial number must be 5-64 characters long.");
        else if (!Regex.IsMatch(serial, "^[A-Z0-9-]+$", RegexOptions.CultureInvariant))
            AddError(response, nameof(SerialNumber), "Battery serial number may only contain uppercase letters, digits, and hyphens.");

        if (BatteryTypeId == Guid.Empty)
            AddError(response, nameof(BatteryTypeId), "Battery type Id is required.");

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "Invalid site Id.");

        if (InstallDate == default)
        {
            AddError(response, nameof(InstallDate), "Install date is required.");
        }
        else
        {
            var installDateUtc = InstallDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(InstallDate, DateTimeKind.Utc)
                : InstallDate.ToUniversalTime();

            if (installDateUtc > DateTime.UtcNow)
                AddError(response, nameof(InstallDate), "Install date cannot be in the future.");
            else if (installDateUtc < DateTime.UtcNow.AddYears(-5))
                AddError(response, nameof(InstallDate), "Invalid install date (too far in the past, maximum 5 years).");
        }

        if (WarrantyEndDate.HasValue && WarrantyEndDate.Value <= InstallDate)
            AddCrossFieldError(response, nameof(WarrantyEndDate), "Warranty end date must be after the install date.");

        if (Location?.Length > 255)
            AddError(response, nameof(Location), "Location must not exceed 255 characters.");

        if (Latitude is < -90 or > 90)
            AddError(response, nameof(Latitude), "Latitude must be between -90 and 90.");

        if (Longitude is < -180 or > 180)
            AddError(response, nameof(Longitude), "Longitude must be between -180 and 180.");

        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Notes must not exceed 1000 characters.");
    }

    protected static void AddError(CommonResponse<BatteryAssetDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid battery asset data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    protected static void AddCrossFieldError(CommonResponse<BatteryAssetDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        // Cross-field business rule violation → 422.
        // Do not overwrite 400 (field-level format errors take precedence).
        if (response.StatusCode != 400)
            response.StatusCode = 422;
        response.Message = "Invalid battery asset data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
