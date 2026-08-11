using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.Site;

public class UpdateSiteCommand : CreateSiteCommand, IRequest<CommonResponse<SiteDto>>, IValidatable<CommonResponse<SiteDto>>
{
    /// <summary>Định danh resource.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    Task<CommonResponse<SiteDto>> IValidatable<CommonResponse<SiteDto>>.ValidateAsync()
    {
        var response = new CommonResponse<SiteDto>();
        ValidateShared(response);

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Site Id is required.");

        return Task.FromResult(response);
    }
}
