using MediatR;
using NotificationService.Application.CQRS.Query.Notification;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace NotificationService.Application.CQRS.Handler.Notification;

public class NotificationBatchGetListQueryHandler
    : IRequestHandler<NotificationBatchGetListQuery, NotificationBatchListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationBatchGetListQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationBatchListResponse> Handle(
        NotificationBatchGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.NotificationBatches.GetAllAsync(false).Where(b => !b.IsDeleted);

        if (request.Source.HasValue)
            query = query.Where(b => b.Source == request.Source.Value);
        if (request.Type.HasValue)
            query = query.Where(b => b.Type == request.Type.Value);

        var page = await query
            .OrderByDescending(b => b.CreatedAt)
            // Chốt chặn cuối: nhiều lần gửi có thể trùng mốc thời gian tới từng mili-giây (một sự
            // kiện sinh ra vài batch). Thứ tự KHÔNG toàn phần thì Postgres được phép trả khác nhau
            // giữa các lần chạy — một dòng có thể lọt qua 2 trang hoặc biến mất hẳn.
            .ThenBy(b => b.Id)
            // Chiếu ChannelValues (cột thật) chứ KHÔNG chiếu Channels: Channels là NotMapped nên EF
            // không dịch được sang SQL, đưa vào Select là lỗi lúc chạy chứ không phải lúc biên dịch.
            .Select(b => new
            {
                b.Id,
                b.Type,
                b.Title,
                b.Body,
                b.ChannelValues,
                b.Source,
                b.Status,
                b.RecipientCount,
                b.NotificationCount,
                b.CreatedBy,
                b.CreatedAt,
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        return new NotificationBatchListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(b => new NotificationBatchDto
            {
                Id = b.Id,
                Type = b.Type,
                Title = b.Title,
                Body = b.Body,
                Channels = b.ChannelValues.Select(v => (NotificationChannelEnum)v).ToList(),
                Source = b.Source,
                Status = b.Status,
                RecipientCount = b.RecipientCount,
                NotificationCount = b.NotificationCount,
                CreatedBy = b.CreatedBy,
                CreatedAt = b.CreatedAt,
            }),
        };
    }
}
