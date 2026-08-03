using MediatR;
using NotificationService.Application.CQRS.Query.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

public class NotificationGroupGetListQueryHandler
    : IRequestHandler<NotificationGroupGetListQuery, NotificationGroupListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationGroupGetListQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationGroupListResponse> Handle(
        NotificationGroupGetListQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.NotificationGroups.GetAllAsync(false).Where(g => !g.IsDeleted);

        if (request.Kind.HasValue)
            query = query.Where(g => g.Kind == request.Kind.Value);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // So trên normalized_name (đã là CHỮ HOA) để tìm không phân biệt hoa-thường mà không
            // cần gọi lower() trên cột — cột này còn mang unique index, giữ nguyên dạng thì index
            // vẫn dùng được cho phần so khớp đầu chuỗi.
            var needle = request.Search.Trim().ToUpperInvariant();
            query = query.Where(g => g.NormalizedName.Contains(needle));
        }

        var page = await query
            // Nhóm hệ thống lên trước, rồi tới nhóm tự tạo theo tên.
            .OrderByDescending(g => g.IsSystem)
            .ThenBy(g => g.Name)
            // Chốt chặn cuối: tên đã unique trong số nhóm chưa xoá, nhưng thứ tự KHÔNG toàn phần thì
            // Postgres được phép trả khác nhau giữa các lần chạy — một dòng có thể lọt qua 2 trang
            // hoặc biến mất hẳn.
            .ThenBy(g => g.Id)
            .Select(g => new NotificationGroupDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                Kind = g.Kind,
                RoleFilter = g.RoleFilter,
                IsSystem = g.IsSystem,
                CreatedAt = g.CreatedAt,
                UpdatedAt = g.UpdatedAt,
                // MemberCount điền ở bước sau — đếm gộp cho cả trang, xem bên dưới.
            })
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);

        var keys = page.Items
            .Select(g => new NotificationGroupMembership.GroupKey(g.Id, g.Kind, g.RoleFilter))
            .ToList();

        var counts = await NotificationGroupMembership.CountRecipientsAsync(
            _unitOfWork, keys, cancellationToken);

        foreach (var item in page.Items)
            item.MemberCount = counts.TryGetValue(item.Id, out var count) ? count : 0;

        return new NotificationGroupListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page,
        };
    }
}
