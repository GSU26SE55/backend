using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.CascadeRisk;

/// <summary>Sprint 7 B4 (§31.7) — Admin set electrical topology cho asset (ảnh hưởng cascade risk).</summary>
public class SetBatteryAssetTopologyCommand
    : IRequest<CommonResponse<CascadeRiskDto>>, IValidatable<CommonResponse<CascadeRiskDto>>
{
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    public ElectricalTopologyEnum ElectricalTopology { get; set; }

    public Task<CommonResponse<CascadeRiskDto>> ValidateAsync()
    {
        var response = new CommonResponse<CascadeRiskDto>();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id tài sản pin là bắt buộc.");

        if (!Enum.IsDefined(typeof(ElectricalTopologyEnum), ElectricalTopology))
            AddError(response, nameof(ElectricalTopology),
                "ElectricalTopology không hợp lệ (1=Independent, 2=SeriesString, 3=ParallelBank, 4=SeriesParallel).");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<CascadeRiskDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu cập nhật electrical topology không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
