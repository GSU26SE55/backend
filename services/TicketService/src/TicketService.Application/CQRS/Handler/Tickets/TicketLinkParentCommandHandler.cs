using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketEntity = TicketService.Domain.Entities.Ticket;

namespace TicketService.Application.CQRS.Handler.Tickets;

/// <summary>
/// Gắn/gỡ ticket con vào ticket cha cùng nguyên nhân gốc.
///
/// Đối lập với <see cref="TicketMergeCommandHandler"/>: merge KẾT LUẬN hai ticket là trùng lặp
/// và đóng cái nguồn; link chỉ nói chúng cùng nguyên nhân. Vì vậy handler này KHÔNG đụng tới
/// Status, SlaTimer, CloseReason hay attachment — chỉ ghi một khoá ngoại và một dòng timeline.
/// </summary>
public class TicketLinkParentCommandHandler
    : IRequestHandler<TicketLinkParentCommand, TicketActionResponse>
{
    private readonly ITicketUnitOfWork _uow;

    public TicketLinkParentCommandHandler(ITicketUnitOfWork uow) => _uow = uow;

    public async Task<TicketActionResponse> Handle(
        TicketLinkParentCommand request, CancellationToken ct)
    {
        var child = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == request.Id && !t.IsDeleted, ct);
        if (child is null)
            return Fail(404, "Ticket not found.");

        if (child.MergedIntoTicketId.HasValue)
            return Fail(409, "A merged ticket cannot be linked.");

        // ── Gỡ liên kết ──
        if (!request.ParentTicketId.HasValue)
        {
            if (child.ParentTicketId is null)
                return Fail(409, "This ticket is not linked to a parent.");

            var previousParentId = child.ParentTicketId.Value;
            child.ParentTicketId = null;
            _uow.Tickets.UpdateAsync(child);
            await LogAsync(request, child, $"Unlinked from parent ticket {previousParentId}.", ct);
            return Ok(child);
        }

        var parentId = request.ParentTicketId.Value;

        if (parentId == child.Id)
            return Fail(400, "A ticket cannot be its own parent.");

        var parent = await _uow.Tickets.GetAllAsync()
            .FirstOrDefaultAsync(t => t.Id == parentId && !t.IsDeleted, ct);
        if (parent is null)
            return Fail(404, "Parent ticket not found.");

        if (parent.MergedIntoTicketId.HasValue)
            return Fail(409, "A merged ticket cannot be used as a parent.");

        if (parent.CustomerId != child.CustomerId)
            return Fail(409, "Both tickets must belong to the same customer.");

        // Chỉ một cấp. Cho phép cha-của-cha thì panel "ticket liên quan" phải duyệt cây, và
        // A→B→A tạo vòng lặp vô hạn. Quan hệ ở đây là "sự cố + các triệu chứng của nó", vốn
        // phẳng — không cần cây.
        if (parent.ParentTicketId.HasValue)
            return Fail(409, "The parent ticket is already linked to another ticket. Links are one level deep.");

        var hasChildren = await _uow.Tickets.GetAllAsync()
            .AnyAsync(t => t.ParentTicketId == child.Id && !t.IsDeleted, ct);
        if (hasChildren)
            return Fail(409, "This ticket already has linked tickets and cannot become a child.");

        child.ParentTicketId = parentId;
        _uow.Tickets.UpdateAsync(child);
        await LogAsync(request, child, $"Linked to parent ticket {parent.Code} (same root cause).", ct);
        return Ok(child);
    }

    private async Task LogAsync(
        TicketLinkParentCommand request, TicketEntity child, string reason, CancellationToken ct)
    {
        await _uow.TicketActivities.AddAsync(new TicketActivity
        {
            Id = Guid.NewGuid(),
            TicketId = child.Id,
            ActorUserId = request.ActorId,
            ActorRole = ActorRoleEnum.Manager,
            ActorDisplayName = request.ActorName ?? string.Empty,
            Action = ActivityActionEnum.StatusChanged,
            Ticket = child,
            // Status KHÔNG đổi — ghi lại giá trị hiện tại để timeline không hiện một chuyển
            // trạng thái chưa từng xảy ra.
            NewValue = child.Status.ToString(),
            Reason = reason
        });
        await _uow.SaveChangesAsync(ct);
    }

    private static TicketActionResponse Ok(TicketEntity child) => new()
    {
        IsSuccess = true,
        StatusCode = 200,
        Message = "Ticket link updated.",
        Data = new TicketActionDTO
        {
            Id = child.Id.ToString(),
            TicketId = child.Id.ToString(),
            Code = child.Code,
            Status = child.Status
        }
    };

    private static TicketActionResponse Fail(int statusCode, string message) => new()
    {
        IsSuccess = false,
        StatusCode = statusCode,
        Message = message
    };
}
