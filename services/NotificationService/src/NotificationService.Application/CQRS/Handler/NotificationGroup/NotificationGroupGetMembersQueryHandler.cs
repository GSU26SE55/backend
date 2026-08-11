using MediatR;
using Microsoft.EntityFrameworkCore;
using NotificationService.Application.CQRS.Query.NotificationGroup;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Interfaces.Repositories;
using NotificationService.Application.Services;
using NotificationService.Domain.Enums;
using SharedInfrastructure.Extensions;

namespace NotificationService.Application.CQRS.Handler.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-03 — liệt kê thành viên.
///
/// <para>Hai đường hoàn toàn khác nhau tuỳ loại nhóm: nhóm <c>Static</c> đọc bảng thành viên rồi
/// JOIN sang read-model để lấy tên/email; nhóm <c>Role</c> KHÔNG có dòng thành viên nào nên đọc
/// thẳng read-model theo role.</para>
///
/// <para>Mặc định trả cả người <b>không</b> hoạt động (kèm cờ <c>IsActive = false</c>) để admin
/// thấy mà dọn — nếu ẩn đi thì nhóm hiển thị 3 người trong khi bảng có 5 dòng, không ai hiểu vì sao.</para>
/// </summary>
public class NotificationGroupGetMembersQueryHandler
    : IRequestHandler<NotificationGroupGetMembersQuery, NotificationGroupMemberListResponse>
{
    private readonly INotificationUnitOfWork _unitOfWork;

    public NotificationGroupGetMembersQueryHandler(INotificationUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<NotificationGroupMemberListResponse> Handle(
        NotificationGroupGetMembersQuery request, CancellationToken cancellationToken)
    {
        var group = await _unitOfWork.NotificationGroups.GetAllAsync(false)
            .FirstOrDefaultAsync(g => g.Id == request.GroupId && !g.IsDeleted, cancellationToken);

        if (group is null)
        {
            return new NotificationGroupMemberListResponse
            {
                IsSuccess = false,
                StatusCode = 404,
                Message = "Group not found.",
            };
        }

        var needle = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim().ToLower();
        var accounts = _unitOfWork.Accounts.GetAllAsync(false).Where(a => !a.IsDeleted);

        IQueryable<NotificationGroupMemberDto> projected;

        if (group.Kind == NotificationGroupKindEnum.Role)
        {
            var role = (group.RoleFilter ?? string.Empty).ToLower();
            var query = accounts.Where(a => a.Role.ToLower() == role);

            if (request.ActiveOnly == true)
                query = query.Where(a => a.IsActive);
            if (needle is not null)
                query = query.Where(a => a.FullName.ToLower().Contains(needle) || a.Email.Contains(needle));

            projected = query
                .OrderBy(a => a.FullName).ThenBy(a => a.Id)
                .Select(a => new NotificationGroupMemberDto
                {
                    UserId = a.Id,
                    Email = a.Email,
                    FullName = a.FullName,
                    Role = a.Role,
                    IsActive = a.IsActive,
                    // Nhóm Role không có dòng thành viên thật ⇒ không có mốc "được thêm lúc nào".
                    AddedAt = null,
                });
        }
        else
        {
            var members = _unitOfWork.NotificationGroupMembers.GetAllAsync(false)
                .Where(m => !m.IsDeleted && m.GroupId == group.Id);

            var joined = members.Join(
                accounts,
                m => m.UserId,
                a => a.Id,
                (m, a) => new { Member = m, Account = a });

            if (request.ActiveOnly == true)
                joined = joined.Where(x => x.Account.IsActive);
            if (needle is not null)
            {
                joined = joined.Where(x =>
                    x.Account.FullName.ToLower().Contains(needle) || x.Account.Email.Contains(needle));
            }

            projected = joined
                // Người không hoạt động dồn xuống cuối để admin thấy ngay ai đang dùng được.
                .OrderByDescending(x => x.Account.IsActive)
                .ThenBy(x => x.Account.FullName)
                .ThenBy(x => x.Member.Id)
                .Select(x => new NotificationGroupMemberDto
                {
                    UserId = x.Account.Id,
                    Email = x.Account.Email,
                    FullName = x.Account.FullName,
                    Role = x.Account.Role,
                    IsActive = x.Account.IsActive,
                    AddedAt = x.Member.CreatedAt,
                });
        }

        var page = await projected.ToPagedEntityListAsync(
            request.PageNumber, request.PageSize, cancellationToken);

        return new NotificationGroupMemberListResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page,
        };
    }
}
