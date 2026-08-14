using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.TicketActivityTimeline;

public class TicketActivityTimelineQueryHandler : IRequestHandler<TicketActivityTimelineQuery, CommonResponse<List<TicketActivityDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;

    /// <summary>
    /// Action chỉ hiện với người được xem nội bộ (Staff/Manager/Admin hoặc participant có
    /// CanViewInternal). Hai nhóm:
    /// <list type="bullet">
    /// <item>Chat* — mọi handler chat đều ghi trích đoạn body vào OldValue/NewValue/Reason, mà
    /// TicketActivity không lưu IsInternal nên không tách được dòng nội bộ khỏi dòng công khai.</item>
    /// <item>Participant* — khớp với ParticipantHistoryQueryHandler, nơi Customer đã bị chặn 403.</item>
    /// </list>
    /// Các mốc vòng đời (StatusChanged, Resolved, Rated, Sla*, Escalated…) KHÔNG nằm ở đây —
    /// Customer vẫn phải thấy tiến độ ticket của mình.
    /// </summary>
    private static readonly ActivityActionEnum[] InternalOnlyActions =
    [
        ActivityActionEnum.Chatted,
        ActivityActionEnum.ChatEdited,
        ActivityActionEnum.ChatDeleted,
        ActivityActionEnum.ChatRestored,
        ActivityActionEnum.ChatReplied,
        ActivityActionEnum.ChatPinned,
        ActivityActionEnum.ChatUnpinned,
        ActivityActionEnum.ChatFlagged,
        ActivityActionEnum.ParticipantAdded,
        ActivityActionEnum.ParticipantRemoved,
        ActivityActionEnum.ParticipantRoleChanged
    ];

    public TicketActivityTimelineQueryHandler(ITicketUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<CommonResponse<List<TicketActivityDTO>>> Handle(TicketActivityTimelineQuery request, CancellationToken cancellationToken)
    {
        var ticket = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.CustomerId, PrimaryHandlerStaffId = t.Assignments.Where(a => !a.IsDeleted && a.Role == AssignmentRoleEnum.PrimaryHandler).Select(a => (Guid?)a.StaffId).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);

        if (ticket is null)
            return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = false, StatusCode = 404, Message = "Not found" };

        var activeParticipants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == request.TicketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.CanViewInternal })
            .ToListAsync(cancellationToken);

        if (!TicketQueryHelper.CanAccessTicket(ticket.CustomerId, ticket.PrimaryHandlerStaffId, request.ActorUserId, request.ActorRoles, activeParticipants.Select(p => p.UserId).ToList()))
            return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = false, StatusCode = 403, Message = "Forbidden" };

        // Ai không được xem chat nội bộ thì cũng không được thấy dấu vết của nó trên timeline.
        // TicketActivity không có cột IsInternal nên không phân biệt được activity chat nào là
        // nội bộ — mà mọi action nhóm Chat* đều nhét trích đoạn body vào Old/NewValue/Reason
        // (ChatAdd ghi "[Nội bộ]" + 50 ký tự đầu, ChatEdit/Pin/Delete ghi ChatTextHelper.Truncate).
        // Vì vậy loại cả nhóm thay vì lọc từng dòng: thà mất mốc "đã nhắn tin" — Customer đã thấy
        // ở tab chat — còn hơn rò nội dung. Nhóm Participant* cũng ẩn cho khớp
        // ParticipantHistoryQueryHandler (vốn đã chặn Customer bằng đúng helper này).
        var participantCanViewInternal = activeParticipants.Any(p => p.UserId == request.ActorUserId && p.CanViewInternal);
        var canViewInternal = TicketQueryHelper.CanViewInternalChats(request.ActorRoles, participantCanViewInternal);

        var activities = await _unitOfWork.TicketActivities.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.TicketId == request.TicketId)
            .Where(a => canViewInternal || !InternalOnlyActions.Contains(a.Action))
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new TicketActivityDTO
            {
                Id = a.Id.ToString(),
                TicketId = a.TicketId.ToString(),
                ActorUserId = a.ActorUserId.HasValue ? a.ActorUserId.Value.ToString() : null,
                ActorRole = a.ActorRole,
                ActorDisplayName = a.ActorDisplayName,
                Action = a.Action,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                Reason = a.Reason,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new CommonResponse<List<TicketActivityDTO>> { IsSuccess = true, StatusCode = 200, Data = activities };
    }
}
