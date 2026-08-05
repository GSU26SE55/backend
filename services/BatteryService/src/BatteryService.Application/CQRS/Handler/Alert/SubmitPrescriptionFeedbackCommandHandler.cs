using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.Helpers;
using BatteryService.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;

namespace BatteryService.Application.CQRS.Handler.Alert;

/// <summary>
/// GH-778 — chuyển phản hồi của kỹ thuật viên về AI, khép vòng học của prescription.
/// </summary>
public class SubmitPrescriptionFeedbackCommandHandler
    : IRequestHandler<SubmitPrescriptionFeedbackCommand, CommonResponse<string>>
{
    private readonly IBatteryUnitOfWork _unitOfWork;
    private readonly IAiPrescriptionFeedbackClient _feedbackClient;
    private readonly IBatteryCurrentUserService _currentUser;

    public SubmitPrescriptionFeedbackCommandHandler(
        IBatteryUnitOfWork unitOfWork,
        IAiPrescriptionFeedbackClient feedbackClient,
        IBatteryCurrentUserService currentUser)
    {
        _unitOfWork = unitOfWork;
        _feedbackClient = feedbackClient;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<string>> Handle(
        SubmitPrescriptionFeedbackCommand request, CancellationToken cancellationToken)
    {
        var alert = await _unitOfWork.Alerts
            .GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == request.AlertId && !a.IsDeleted, cancellationToken);

        if (alert is null)
            return Fail(404, "Không tìm thấy alert.");

        // Giới hạn theo tenant: alert của khách khác trả 404 chứ không 403 — 403 xác nhận alert đó
        // có thật, biến endpoint thành công cụ dò. Khớp quy ước GH-722/GH-774.
        var scope = BatteryTenantScopeHelper.Resolve(_currentUser.UserId, _currentUser.Roles);
        if (!scope.IsUnrestricted)
        {
            // Alert gắn tenant qua CẢ asset LẪN site — alert cấp site không có asset. Kiểm đúng
            // đường nào có; không xác định được đường nào thì TỪ CHỐI (fail closed).
            var owns = alert.BatteryAssetId.HasValue
                ? await BatteryTenantAccessGuard.CanAccessAssetAsync(
                    _unitOfWork, alert.BatteryAssetId.Value, scope, cancellationToken)
                : alert.SiteId.HasValue
                    && await BatteryTenantAccessGuard.CanAccessSiteAsync(
                        _unitOfWork, alert.SiteId.Value, scope, cancellationToken);
            if (!owns)
                return Fail(404, "Không tìm thấy alert.");
        }

        if (string.IsNullOrWhiteSpace(alert.AiPrescriptionId))
        {
            // Alert chưa từng được prescribe (hoặc prescribe không bật / AI không trả id). Đây là
            // XUNG ĐỘT trạng thái chứ không phải "không tìm thấy": alert có thật, chỉ là không có
            // gì để phản hồi. Trả 404 ở đây sẽ khiến người dùng đi tìm một alert vốn đang hiện ra
            // trước mắt họ.
            return Fail(409, "Alert này chưa có prescription của AI để phản hồi.");
        }

        var outcome = await _feedbackClient.SubmitFeedbackAsync(
            alert.AiPrescriptionId,
            request.Status.Trim().ToLowerInvariant(),
            request.EditedSteps?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
            request.Note,
            cancellationToken);

        return outcome switch
        {
            AiFeedbackOutcome.Recorded => new CommonResponse<string>
            {
                IsSuccess = true,
                StatusCode = 200,
                Message = "Đã ghi nhận phản hồi.",
                Data = alert.AiPrescriptionId
            },

            // AI không còn giữ id này. Thử lại cũng vô ích nên KHÔNG trả 5xx — client sẽ retry vô nghĩa.
            AiFeedbackOutcome.NotFound =>
                Fail(410, "Prescription đã hết hạn ở phía AI — không ghi nhận được phản hồi nữa."),

            // AI sập: đây là lỗi TẠM THỜI, phải nói rõ để client biết thử lại sau.
            _ => Fail(503, "Không kết nối được AI để ghi nhận phản hồi. Thử lại sau.")
        };
    }

    private static CommonResponse<string> Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
