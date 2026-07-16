using System.Text.Json.Serialization;
using BatteryService.Application.DTOs;
using BatteryService.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace BatteryService.Application.CQRS.Command.AnomalyClassification;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2 — spec §30.7/§30.12) — Staff gửi feedback cho 1 classification của AI
/// sau khi resolve (Correct / FalsePositive / FalseNegative) → phục vụ đánh giá precision/recall + retrain.
/// </summary>
public class SubmitAnomalyClassificationFeedbackCommand
    : IRequest<CommonResponse<AnomalyClassificationDto>>,
      IValidatable<CommonResponse<AnomalyClassificationDto>>
{
    /// <summary>Id của AnomalyClassification — lấy từ route, không bind từ client.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid Id { get; set; }

    /// <summary>Đánh giá của Staff.</summary>
    public StaffFeedbackEnum Feedback { get; set; }

    /// <summary>User id Staff — set từ token ở controller (không nhận từ body).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid StaffFeedbackByUserId { get; set; }

    public Task<CommonResponse<AnomalyClassificationDto>> ValidateAsync()
    {
        var response = new CommonResponse<AnomalyClassificationDto>();

        if (Id == Guid.Empty)
            AddError(response, nameof(Id), "Id classification là bắt buộc.");

        if (!Enum.IsDefined(typeof(StaffFeedbackEnum), Feedback))
            AddError(response, nameof(Feedback), "Feedback không hợp lệ (Correct/FalsePositive/FalseNegative).");

        return Task.FromResult(response);
    }

    private static void AddError(CommonResponse<AnomalyClassificationDto> response, string field, string detail)
    {
        response.IsSuccess = false;
        response.StatusCode = 400;
        response.Message = "Dữ liệu không hợp lệ.";
        response.ListErrors.Add(new Errors { Field = field, Detail = detail });
    }
}
