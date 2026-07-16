using BatteryService.Application.CQRS.Command.AnomalyClassification;
using BatteryService.Application.DTOs;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using ClassificationEntity = BatteryService.Domain.Entities.AnomalyClassification;

namespace BatteryService.Application.CQRS.Handler.AnomalyClassification;

/// <summary>Sprint Bonus NS-26 (#666, F2) — ghi Staff feedback vào AnomalyClassification.</summary>
public class SubmitAnomalyClassificationFeedbackCommandHandler
    : IRequestHandler<SubmitAnomalyClassificationFeedbackCommand, CommonResponse<AnomalyClassificationDto>>
{
    private readonly IBatteryUnitOfWork _uow;

    public SubmitAnomalyClassificationFeedbackCommandHandler(IBatteryUnitOfWork uow)
    {
        _uow = uow;
    }

    public async Task<CommonResponse<AnomalyClassificationDto>> Handle(
        SubmitAnomalyClassificationFeedbackCommand request, CancellationToken cancellationToken)
    {
        var entity = await _uow.AnomalyClassifications.GetAllAsync()
            .FirstOrDefaultAsync(c => c.Id == request.Id && !c.IsDeleted, cancellationToken);

        if (entity is null)
            return new CommonResponse<AnomalyClassificationDto> { IsSuccess = false, StatusCode = 404, Message = "Không tìm thấy classification." };

        entity.StaffFeedback = request.Feedback;
        entity.StaffFeedbackByUserId = request.StaffFeedbackByUserId;
        entity.StaffFeedbackAt = DateTime.UtcNow;
        _uow.AnomalyClassifications.UpdateAsync(entity);
        await _uow.SaveChangesAsync(cancellationToken);

        return new CommonResponse<AnomalyClassificationDto>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Đã ghi nhận feedback.",
            Data = Map(entity)
        };
    }

    internal static AnomalyClassificationDto Map(ClassificationEntity c) => new()
    {
        Id = c.Id.ToString(),
        AlertId = c.AlertId?.ToString(),
        BatteryAssetId = c.BatteryAssetId.ToString(),
        Classification = c.Classification,
        AnomalyScore = c.AnomalyScore,
        Confidence = c.Confidence,
        ModelVersion = c.ModelVersion,
        ClassifiedAt = c.ClassifiedAt,
        LatencyMs = c.LatencyMs,
        StaffFeedback = c.StaffFeedback,
        StaffFeedbackByUserId = c.StaffFeedbackByUserId?.ToString(),
        StaffFeedbackAt = c.StaffFeedbackAt
    };
}
