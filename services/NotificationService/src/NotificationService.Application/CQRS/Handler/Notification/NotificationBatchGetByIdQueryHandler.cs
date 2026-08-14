using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;

namespace NotificationService.Application.CQRS.Handler.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-09 — chi tiết một lần gửi kèm thống kê giao nhận.
///
/// <para>Đây chính là câu hỏi trước sprint này <b>không trả lời được</b>: "thông báo X đã tới ai,
/// bao nhiêu người đã đọc". Trước đây phải gom mò theo <c>(type, entity_id, giây)</c> — cách gom
/// đó sai, vì cùng một <c>entity_id</c> có tới 50 dòng trong một giây.</para>
/// </summary>
public class NotificationBatchGetByIdQueryHandler
    : IRequestHandler<NotificationBatchGetByIdQuery, NotificationBatchDetailResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationBatchGetByIdQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationBatchDetailResponse> Handle(
        NotificationBatchGetByIdQuery request, CancellationToken cancellationToken)
    {
        var batch = await _unitOfWork.NotificationBatches.GetAllAsync(false)
            .FirstOrDefaultAsync(b => b.Id == request.Id && !b.IsDeleted, cancellationToken);

        if (batch is null)
        {
            return new NotificationBatchDetailResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Batch not found.",
            };
        }

        // LEFT JOIN sang nhóm và CỐ Ý KHÔNG lọc `!IsDeleted`: nhóm đã xoá vẫn phải hiện trong lịch
        // sử, và hiện kèm ĐÚNG TÊN nó mang lúc được gửi. Lọc bỏ dòng đã xoá thì tên thành rỗng và
        // người xem chỉ còn thấy "một nhóm nào đó" — mất luôn thông tin đáng giá nhất. "đã từng gửi
        // cho nhóm này" là sự thật lịch sử, xoá nhóm không làm nó chưa từng xảy ra.
        var targets = await _unitOfWork.NotificationBatchTargets.GetAllAsync(false)
            .Where(t => t.BatchId == batch.Id && !t.IsDeleted)
            .GroupJoin(
                _unitOfWork.NotificationGroups.GetAllAsync(false),
                t => t.GroupId,
                g => (Guid?)g.Id,
                (t, groups) => new { Target = t, Groups = groups })
            .SelectMany(
                x => x.Groups.DefaultIfEmpty(),
                (x, g) => new NotificationBatchTargetDto
                {
                    TargetKind = x.Target.TargetKind,
                    GroupId = x.Target.GroupId,
                    GroupName = g != null ? g.Name : null,
                    UserId = x.Target.UserId,
                })
            .ToListAsync(cancellationToken);

        var rows = _unitOfWork.Notifications.GetAllAsync(false)
            .Where(n => n.BatchId == batch.Id && !n.IsDeleted);

        // Gộp mọi con số vào MỘT truy vấn: 6 lần Count riêng lẻ là 6 lượt quét cùng một tập dòng.
        var stats = await rows
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalRows = g.Count(),
                DistinctRecipients = g.Select(n => n.UserId).Distinct().Count(),
                SentCount = g.Count(n => n.Status == NotificationStatusEnum.Sent),
                ReadCount = g.Count(n => n.ReadAt != null),
                FailedCount = g.Count(n => n.Status == NotificationStatusEnum.Failed),
                // GH-792 — Processing đếm CHUNG với Pending. Trạng thái đó nghĩa là "đã chiếm để
                // gửi, chưa biết kết quả", tức vẫn thuộc phần chưa xong. Bỏ sót nó thì
                // Pending + Sent + Failed < Total và màn hình chi tiết batch của Admin hiện ra
                // cảnh các bản ghi biến mất khỏi mọi ô đếm giữa chừng rồi lại xuất hiện.
                PendingCount = g.Count(n => n.Status == NotificationStatusEnum.Pending
                                            || n.Status == NotificationStatusEnum.Processing),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new NotificationBatchDetailResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new NotificationBatchDetailDto
            {
                Id = batch.Id,
                Type = batch.Type,
                Title = batch.Title,
                Body = batch.Body,
                Channels = batch.Channels.ToList(),
                Source = batch.Source,
                Status = batch.Status,
                RecipientCount = batch.RecipientCount,
                NotificationCount = batch.NotificationCount,
                CreatedBy = batch.CreatedBy,
                CreatedAt = batch.CreatedAt,
                Targets = targets,
                // stats null = lần gửi chưa sinh dòng nào (mới ở trạng thái Pending).
                TotalRows = stats?.TotalRows ?? 0,
                DistinctRecipients = stats?.DistinctRecipients ?? 0,
                SentCount = stats?.SentCount ?? 0,
                ReadCount = stats?.ReadCount ?? 0,
                FailedCount = stats?.FailedCount ?? 0,
                PendingCount = stats?.PendingCount ?? 0,
            },
        };
    }
}
