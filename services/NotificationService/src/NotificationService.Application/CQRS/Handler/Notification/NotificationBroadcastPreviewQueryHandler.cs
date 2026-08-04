using MediatR;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;

namespace NotificationService.Application.CQRS.Handler.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-07 — xem trước, KHÔNG ghi gì vào DB.
///
/// <para>Dùng lại <c>BroadcastPlanner</c> của lần gửi thật. Đây là điểm mấu chốt: nếu xem trước tự
/// tính theo cách riêng thì sớm muộn hai con số sẽ lệch nhau, mà lệch ở đây nghĩa là admin thấy
/// "12 người" rồi bấm gửi và chỉ 9 người nhận — không có gì báo lỗi.</para>
/// </summary>
public class NotificationBroadcastPreviewQueryHandler
    : IRequestHandler<NotificationBroadcastPreviewQuery, NotificationBroadcastPreviewResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;
    private readonly IRecipientResolver _recipientResolver;

    public NotificationBroadcastPreviewQueryHandler(
        INotificationUnitOfWork unitOfWork, IRecipientResolver recipientResolver)
    {
        _unitOfWork = unitOfWork;
        _recipientResolver = recipientResolver;
    }

    public async Task<NotificationBroadcastPreviewResponse> Handle(
        NotificationBroadcastPreviewQuery request, CancellationToken cancellationToken)
    {
        var plan = await BroadcastPlanner.BuildAsync(
            _unitOfWork, _recipientResolver, request.GroupIds, request.UserIds, cancellationToken);

        var channelCount = request.Channels.Distinct().Count();

        return new NotificationBroadcastPreviewResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new NotificationBroadcastPreviewDto
            {
                RecipientCount = plan.Recipients.Count,
                NotificationCount = plan.Recipients.Count * channelCount,
                RawCount = plan.RawCount,
                SkippedUsers = plan.SkippedUsers,
                MissingGroups = plan.MissingGroups,
            },
        };
    }
}
