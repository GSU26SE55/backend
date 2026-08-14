using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Chats;

public class TicketChatsQueryHandler : IRequestHandler<TicketChatsQuery, CommonResponse<PaginationResponse<TicketChatDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly IChatCacheService _chatCache;

    public TicketChatsQueryHandler(ITicketUnitOfWork unitOfWork, IChatCacheService chatCache)
    {
        _unitOfWork = unitOfWork;
        _chatCache = chatCache;
    }

    public async Task<CommonResponse<PaginationResponse<TicketChatDTO>>> Handle(TicketChatsQuery request, CancellationToken cancellationToken)
    {
        // 1. Kiểm tra ticket có tồn tại không và check quyền truy cập ticket
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return new CommonResponse<PaginationResponse<TicketChatDTO>> { IsSuccess = false, StatusCode = 404, Message = "Ticket not found" };

        var activeParticipants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new CommonResponse<PaginationResponse<TicketChatDTO>> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        // 2. Xác định xem actor có quyền xem chat nội bộ không
        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternalChats = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        // Skip cache khi user có hidden chats (cache là shared, không per-user).
        var hasHiddenChats = await _unitOfWork.TicketChatHides.AnyAsync(
            h => h.UserId == request.ActorUserId && !h.IsDeleted);

        // Cache hit — chỉ khi page 1, pageSize default (10), không có filter nào.
        // canViewInternalChats phải vào key để tránh Customer thấy internal chats từ cache của Staff.
        var isDefaultQuery = request.PageNumber == 1
            && request.PageSize == 10
            && string.IsNullOrWhiteSpace(request.Search)
            && request.AuthorUserId == null
            && request.AuthorRole == null
            && request.IsInternal == null
            && request.IsPinned == null
            && request.HasAttachments == null
            && request.MentionedMe == null
            && request.DateFrom == null
            && request.DateTo == null
            && !hasHiddenChats;

        if (isDefaultQuery)
        {
            var cached = await _chatCache.GetPageAsync(request.TicketId, 1, request.PageSize, canViewInternalChats, cancellationToken);
            if (cached != null)
            {
                // Cache dùng chung nên KHÔNG chứa IsRead — tính lại cho actor hiện tại.
                await FillIsReadAsync(cached.Items, request.ActorUserId, cancellationToken);

                return new CommonResponse<PaginationResponse<TicketChatDTO>>
                {
                    IsSuccess = true,
                    StatusCode = 200,
                    Data = new PaginationResponse<TicketChatDTO>
                    {
                        Items = cached.Items,
                        TotalItems = cached.TotalItems,
                        PageNumber = 1,
                        PageSize = request.PageSize
                    }
                };
            }
        }

        // 3. Query chats — include soft-deleted (returned with placeholder body)
        var query = _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == request.TicketId);

        // 4. Lọc chat nội bộ nếu là Customer
        if (!canViewInternalChats)
            query = query.Where(c => !c.IsInternal);

        // 5. Ẩn chat mà user đã chọn hide (anti-join subquery → NOT EXISTS)
        if (hasHiddenChats)
        {
            var hiddenChatIdsQuery = _unitOfWork.TicketChatHides.GetAllAsync()
                .Where(h => h.UserId == request.ActorUserId && !h.IsDeleted)
                .Select(h => h.ChatId);
            query = query.Where(c => !hiddenChatIdsQuery.Contains(c.Id));
        }

        // #549 — Extended filters (deleted messages are excluded from filtered views; they appear only in unfiltered timeline)
        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => !c.IsDeleted && c.Body.Contains(request.Search));

        if (request.AuthorUserId.HasValue)
            query = query.Where(c => c.AuthorUserId == request.AuthorUserId.Value);

        if (request.AuthorRole.HasValue)
            query = query.Where(c => c.AuthorRole == request.AuthorRole.Value);

        if (request.IsInternal.HasValue)
            query = query.Where(c => c.IsInternal == request.IsInternal.Value);

        if (request.IsPinned.HasValue)
            query = query.Where(c => c.IsPinned == request.IsPinned.Value);

        if (request.HasAttachments.HasValue)
        {
            query = request.HasAttachments.Value
                ? query.Where(c => !c.IsDeleted && c.AttachmentFileIds != null && c.AttachmentFileIds.Count > 0)
                : query.Where(c => !c.IsDeleted && (c.AttachmentFileIds == null || c.AttachmentFileIds.Count == 0));
        }

        if (request.MentionedMe == true)
        {
            var mentionedChatIds = _unitOfWork.TicketChatMentions.GetAllAsync()
                .AsNoTracking()
                .Where(m => m.MentionedUserId == request.ActorUserId && !m.IsDeleted)
                .Select(m => m.ChatId);
            query = query.Where(c => !c.IsDeleted && mentionedChatIds.Contains(c.Id));
        }

        if (request.DateFrom.HasValue)
            query = query.Where(c => c.CreatedAt >= request.DateFrom.Value);

        if (request.DateTo.HasValue)
            query = query.Where(c => c.CreatedAt <= request.DateTo.Value);

        // Phân trang trên entity: sau đó còn nạp dữ liệu con (mention/reaction) theo lô rồi mới dựng DTO.
        var page = await query
            .OrderByDescending(c => c.IsPinned)
            .ThenByDescending(c => c.CreatedAt)
            .ThenBy(c => c.Id) // tie-breaker cố định — pagination ổn định
            .ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);
        var rawChats = page.Items;

        var nonDeletedIds = rawChats.Where(c => !c.IsDeleted).Select(c => c.Id).ToList();
        var mentionsByChat = await ChatChildDataLoader.LoadMentionsAsync(_unitOfWork, nonDeletedIds, cancellationToken);
        var reactionsByChat = await ChatChildDataLoader.LoadReactionsAsync(_unitOfWork, nonDeletedIds, cancellationToken);
        var translationsByChat = await ChatChildDataLoader.LoadTranslationsForUserAsync(_unitOfWork, nonDeletedIds, request.ActorUserId, cancellationToken);

        var items = rawChats.Select(c => new TicketChatDTO
        {
            Id = c.Id.ToString(),
            TicketId = c.TicketId.ToString(),
            AuthorUserId = c.AuthorUserId.ToString(),
            AuthorRole = c.AuthorRole,
            AuthorDisplayName = c.AuthorDisplayName,
            IsInternal = c.IsInternal,
            CreatedAt = c.CreatedAt,
            ParentChatId = c.ParentChatId?.ToString(),
            ThreadRootId = c.ThreadRootId?.ToString(),
            ReplyCount = c.ReplyCount,
            IsPinned = c.IsPinned,
            PinnedAt = c.PinnedAt,
            PinnedByUserId = c.PinnedByUserId?.ToString(),
            IsDeleted = c.IsDeleted,
            Body = c.IsDeleted ? "This message has been deleted." : c.Body,
            BodyHtml = c.IsDeleted ? null : c.BodyHtml,
            BodyFormat = c.IsDeleted ? default : c.BodyFormat,
            AttachmentFileIds = c.IsDeleted ? [] : c.AttachmentFileIds.Select(id => id.ToString()).ToList(),
            EditedAt = c.IsDeleted ? null : c.EditedAt,
            EditCount = c.IsDeleted ? 0 : c.EditCount,
            LastEditedByUserId = c.IsDeleted ? null : c.LastEditedByUserId?.ToString(),
            Mentions = c.IsDeleted ? [] : (mentionsByChat.TryGetValue(c.Id, out var m) ? m : []),
            Reactions = c.IsDeleted ? new() : (reactionsByChat.TryGetValue(c.Id, out var r) ? r : new TicketChatReactionsAggregateDTO()),
            ActiveTranslation = c.IsDeleted ? null : (translationsByChat.TryGetValue(c.Id, out var tr) ? tr : null),
        }).ToList();

        if (isDefaultQuery)
            await _chatCache.SetPageAsync(request.TicketId, 1, request.PageSize, canViewInternalChats, items, page.TotalItems, cancellationToken);

        // IsRead điền SAU khi ghi cache: cache là shared giữa các user (key chỉ gồm
        // ticket + page + canViewInternal), nhét read-state per-user vào đó sẽ khiến
        // user sau đọc nhầm trạng thái của user trước.
        await FillIsReadAsync(items, request.ActorUserId, cancellationToken);

        return new CommonResponse<PaginationResponse<TicketChatDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.WithItems(items)
        };
    }

    /// <summary>
    /// Set <see cref="TicketChatDTO.IsRead"/> cho actor hiện tại. Tin của chính actor luôn
    /// là "đã đọc" — BE không ghi read-receipt cho tác giả, thiếu bước này thì mọi tin mình
    /// vừa gửi đều hiện dưới mốc "chưa đọc".
    /// </summary>
    private async Task FillIsReadAsync(
        List<TicketChatDTO> items, Guid? actorUserId, CancellationToken ct)
    {
        if (items.Count == 0 || !actorUserId.HasValue)
            return;

        var chatIds = items
            .Select(i => Guid.TryParse(i.Id, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToList();

        var readIds = await ChatChildDataLoader.LoadReadChatIdsForUserAsync(
            _unitOfWork, chatIds, actorUserId.Value, ct);

        var actorId = actorUserId.Value.ToString();
        foreach (var item in items)
        {
            item.IsRead = item.AuthorUserId == actorId
                || (Guid.TryParse(item.Id, out var id) && readIds.Contains(id));
        }
    }
}
