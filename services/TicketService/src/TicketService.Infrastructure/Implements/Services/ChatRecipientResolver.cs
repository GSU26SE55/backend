using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

public class ChatRecipientResolver : IChatRecipientResolver
{
    private readonly ITicketUnitOfWork _unitOfWork;

    public ChatRecipientResolver(ITicketUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<List<Guid>> ResolveAsync(
        Guid ticketId,
        Guid customerId,
        Guid authorUserId,
        bool isInternal,
        CancellationToken cancellationToken = default)
    {
        // Assignment là nguồn sự thật của phía vận hành: primary handler + supporter.
        // PreviousPrimaryHandler cố tình bỏ qua — đã bàn giao thì không cần làm phiền nữa.
        var assignedStaffIds = await _unitOfWork.TicketAssignments.GetAllAsync()
            .AsNoTracking()
            .Where(a => a.TicketId == ticketId
                        && !a.IsDeleted
                        && (a.Role == AssignmentRoleEnum.PrimaryHandler || a.Role == AssignmentRoleEnum.Supporter))
            .Select(a => a.StaffId)
            .ToListAsync(cancellationToken);

        var participants = await _unitOfWork.TicketParticipants.GetAllAsync()
            .AsNoTracking()
            .Where(p => p.TicketId == ticketId && p.RemovedAt == null && !p.IsDeleted)
            .Select(p => new { p.UserId, p.UserRole, p.CanViewInternal })
            .ToListAsync(cancellationToken);

        // Người đã từng nhắn trên ticket cũng phải được báo. Admin/Manager nhảy vào trả lời
        // không hề được thêm vào assignment lẫn participant, nên nếu chỉ dựa vào hai nguồn trên
        // thì họ nhắn xong là mất hút, không bao giờ biết có người trả lời lại.
        var priorAuthors = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId && !c.IsDeleted)
            .Select(c => new { c.AuthorUserId, c.AuthorRole })
            .Distinct()
            .ToListAsync(cancellationToken);

        var recipients = new HashSet<Guid>(assignedStaffIds);

        if (isInternal)
        {
            // Ghi chú nội bộ: Customer KHÔNG bao giờ nhận, kể cả khi là participant hay đã
            // từng nhắn công khai trên ticket. Lọc thêm CanViewInternal cho participant để
            // khớp đúng quyền đọc của TicketChatHub.
            foreach (var p in participants)
            {
                if (p.UserRole != ActorRoleEnum.Customer && p.CanViewInternal)
                    recipients.Add(p.UserId);
            }

            foreach (var a in priorAuthors)
            {
                if (a.AuthorRole != ActorRoleEnum.Customer)
                    recipients.Add(a.AuthorUserId);
            }
        }
        else
        {
            recipients.Add(customerId);

            foreach (var p in participants)
                recipients.Add(p.UserId);

            foreach (var a in priorAuthors)
                recipients.Add(a.AuthorUserId);
        }

        recipients.Remove(authorUserId);
        recipients.Remove(Guid.Empty);

        return recipients.ToList();
    }
}
