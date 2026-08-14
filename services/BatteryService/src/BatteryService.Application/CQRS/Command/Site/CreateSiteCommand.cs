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
            AddError(response, nameof(CustomerId), "Customer Id is required.");

        return Task.FromResult(response);
    }

    protected void ValidateShared(CommonResponse<SiteDto> response)
    {
        if (string.IsNullOrWhiteSpace(Name))
            AddError(response, nameof(Name), "Site name is required.");
        else if (Name.Trim().Length > 200)
            AddError(response, nameof(Name), "Site name must not exceed 200 characters.");

        if (Address?.Length > 500)
            AddError(response, nameof(Address), "Address must not exceed 500 characters.");

        if (Latitude is < -90 or > 90)
            AddError(response, nameof(Latitude), "Latitude must be between -90 and 90.");

        if (Longitude is < -180 or > 180)
            AddError(response, nameof(Longitude), "Longitude must be between -180 and 180.");

        if (InstallDate == default)
            AddError(response, nameof(InstallDate), "Install date is required.");
        else if (ToUtc(InstallDate) > DateTime.UtcNow)
            AddError(response, nameof(InstallDate), "Install date cannot be in the future.");

        if (ContactPersonName?.Length > 150)
            AddError(response, nameof(ContactPersonName), "Contact person name must not exceed 150 characters.");

        if (ContactPersonPhone?.Length > 30)
            AddError(response, nameof(ContactPersonPhone), "Contact person phone must not exceed 30 characters.");
    }

    protected static void AddError(CommonResponse<SiteDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Invalid site data.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }

    private static DateTime ToUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
    }
}
