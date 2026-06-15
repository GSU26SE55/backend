using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Site;

public class CreateSiteCommand : IRequest<CommonResponse<SiteDto>>, IValidatable<CommonResponse<SiteDto>>
{
    /// <summary>Tên hiển thị.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>ID Customer (Guid).</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Địa chỉ vật lý.</summary>
    public string? Address { get; set; }

    /// <summary>Vĩ độ (-90..90).</summary>
    public decimal? Latitude { get; set; }

    /// <summary>Kinh độ (-180..180).</summary>
    public decimal? Longitude { get; set; }

    /// <summary>Ngày lắp đặt.</summary>
    public DateTime InstallDate { get; set; }

    /// <summary>Filter theo status enum.</summary>
    public SiteStatusEnum Status { get; set; } = SiteStatusEnum.Active;

    /// <summary>Tên người liên hệ.</summary>
    public string? ContactPersonName { get; set; }

    /// <summary>Số điện thoại người liên hệ.</summary>
    public string? ContactPersonPhone { get; set; }

    public Task<CommonResponse<SiteDto>> ValidateAsync()
    {
        var response = new CommonResponse<SiteDto>();
        ValidateShared(response);

        if (CustomerId == Guid.Empty)
            AddError(response, nameof(CustomerId), "Id khách hàng là bắt buộc.");

        return Task.FromResult(response);
    }

    protected void ValidateShared(CommonResponse<SiteDto> response)
    {
        if (string.IsNullOrWhiteSpace(Name))
            AddError(response, nameof(Name), "Tên site là bắt buộc.");
        else if (Name.Trim().Length > 200)
            AddError(response, nameof(Name), "Tên site tối đa 200 ký tự.");

        if (Address?.Length > 500)
            AddError(response, nameof(Address), "Địa chỉ tối đa 500 ký tự.");

        if (Latitude is < -90 or > 90)
            AddError(response, nameof(Latitude), "Vĩ độ phải nằm trong khoảng -90 đến 90.");

        if (Longitude is < -180 or > 180)
            AddError(response, nameof(Longitude), "Kinh độ phải nằm trong khoảng -180 đến 180.");

        if (InstallDate == default)
            AddError(response, nameof(InstallDate), "Ngày lắp đặt là bắt buộc.");
        else if (ToUtc(InstallDate) > DateTime.UtcNow)
            AddError(response, nameof(InstallDate), "Ngày lắp đặt không được nằm trong tương lai.");

        if (ContactPersonName?.Length > 150)
            AddError(response, nameof(ContactPersonName), "Tên người liên hệ tối đa 150 ký tự.");

        if (ContactPersonPhone?.Length > 30)
            AddError(response, nameof(ContactPersonPhone), "Số điện thoại người liên hệ tối đa 30 ký tự.");
    }

    protected static void AddError(CommonResponse<SiteDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu site không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
