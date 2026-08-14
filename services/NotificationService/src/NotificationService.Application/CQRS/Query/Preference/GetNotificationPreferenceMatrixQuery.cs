using MediatR;
using NotificationService.Application.DTOs.Response.Preference;

namespace NotificationService.Application.CQRS.Query.Preference;

/// <summary>Sprint 6.3 NOTI3-04 (#704) — đọc ma trận nhóm × kênh của user hiện tại.</summary>
public class GetNotificationPreferenceMatrixQuery : IRequest<NotificationPreferenceMatrixResponse>
{
    public Guid UserId { get; set; }
}
