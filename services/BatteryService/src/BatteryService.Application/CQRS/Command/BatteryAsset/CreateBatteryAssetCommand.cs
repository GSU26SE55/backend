using System.Text.RegularExpressions;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.BatteryAsset;

public class CreateBatteryAssetCommand : IRequest<CommonResponse<BatteryAssetDto>>, IValidatable<CommonResponse<BatteryAssetDto>>
{
    public string SerialNumber { get; set; } = string.Empty;

    public Guid BatteryTypeId { get; set; }

    public Guid? SiteId { get; set; }

    public Guid? BatteryGroupId { get; set; }

    public Guid CustomerId { get; set; }

    public DateTime InstallDate { get; set; }

    public DateTime? WarrantyEndDate { get; set; }

    public string? Location { get; set; }

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }

    public string? Notes { get; set; }

    public Task<CommonResponse<BatteryAssetDto>> ValidateAsync()
    {
        var response = new CommonResponse<BatteryAssetDto>();
        ValidateShared(response);

        if (CustomerId == Guid.Empty)
            AddError(response, nameof(CustomerId), "Id khách hàng là bắt buộc.");

        return Task.FromResult(response);
    }

    protected void ValidateShared(CommonResponse<BatteryAssetDto> response)
    {
        var serial = SerialNumber.Trim();
        if (string.IsNullOrWhiteSpace(serial))
            AddError(response, nameof(SerialNumber), "Serial pin là bắt buộc.");
        else if (serial.Length is < 5 or > 64)
            AddError(response, nameof(SerialNumber), "Serial pin phải dài 5-64 ký tự.");
        else if (!Regex.IsMatch(serial, "^[A-Z0-9-]+$", RegexOptions.CultureInvariant))
            AddError(response, nameof(SerialNumber), "Serial pin chỉ được chứa chữ in hoa, số và dấu gạch ngang.");

        if (BatteryTypeId == Guid.Empty)
            AddError(response, nameof(BatteryTypeId), "Id loại pin là bắt buộc.");

        if (SiteId == Guid.Empty)
            AddError(response, nameof(SiteId), "Id site không hợp lệ.");

        if (BatteryGroupId == Guid.Empty)
            AddError(response, nameof(BatteryGroupId), "Id nhóm pin không hợp lệ.");

        if (InstallDate == default)
        {
            AddError(response, nameof(InstallDate), "Ngày lắp đặt là bắt buộc.");
        }
        else
        {
            var installDateUtc = InstallDate.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(InstallDate, DateTimeKind.Utc)
                : InstallDate.ToUniversalTime();

            if (installDateUtc > DateTime.UtcNow)
                AddError(response, nameof(InstallDate), "Ngày lắp đặt không được nằm trong tương lai.");
        }

        if (WarrantyEndDate.HasValue && WarrantyEndDate.Value <= InstallDate)
            AddError(response, nameof(WarrantyEndDate), "Ngày hết bảo hành phải sau ngày lắp đặt.");

        if (Location?.Length > 255)
            AddError(response, nameof(Location), "Vị trí tối đa 255 ký tự.");

        if (Latitude is < -90 or > 90)
            AddError(response, nameof(Latitude), "Vĩ độ phải nằm trong khoảng -90 đến 90.");

        if (Longitude is < -180 or > 180)
            AddError(response, nameof(Longitude), "Kinh độ phải nằm trong khoảng -180 đến 180.");

        if (Notes?.Length > 1000)
            AddError(response, nameof(Notes), "Ghi chú tối đa 1000 ký tự.");
    }

    protected static void AddError(CommonResponse<BatteryAssetDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu tài sản pin không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
