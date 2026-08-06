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
    private readonly IAiClassificationFeedbackClient _aiFeedback;

    public SubmitAnomalyClassificationFeedbackCommandHandler(
        IBatteryUnitOfWork uow,
        IAiClassificationFeedbackClient aiFeedback)
    {
        _uow = uow;
        _aiFeedback = aiFeedback;
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

        // F4 — gửi ngược về AI SAU khi đã lưu. Thứ tự này là chủ ý: phản hồi của Staff phải
        // được giữ lại kể cả khi AI đang sập. Trả về false chỉ nghĩa là vòng học chậm lại,
        // KHÔNG được biến thao tác đã thành công của người dùng thành lỗi.
        await _aiFeedback.SubmitAsync(
            entity.BatteryAssetId,
            entity.Classification,
            request.Feedback,
            entity.ModelVersion,
            entity.ClassifiedAt,
            cancellationToken);

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
