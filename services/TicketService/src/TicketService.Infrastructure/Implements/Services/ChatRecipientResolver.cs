using Microsoft.EntityFrameworkCore;
using TicketService.Application.Common.Utils;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Gom danh sách người nhận thông báo cho một chat mới.
///
/// <para><b>Nguyên tắc duy nhất:</b> chỉ báo cho người ĐỌC ĐƯỢC tin đó. Quyền đọc ghi chú nội bộ
/// không được định nghĩa lại ở đây mà hỏi <see cref="TicketQueryHelper"/> — đúng cái hàm mà toàn bộ
/// query đọc chat, hub realtime và <c>ChatAuthorizationService</c> đang dùng. Nhờ vậy "được báo" và
/// "đọc được" không bao giờ lệch nhau: không bỏ sót người có quyền, và không hé nội dung nội bộ cho
/// người không có quyền.</para>
///
/// <para><b>Vì sao phải tính ở TicketService:</b> NotificationService không có bảng assignment lẫn
/// participant nên không thể tự suy ra. Publisher tính sẵn rồi gắn vào <c>ChatCreatedEvent</c>.</para>
/// </summary>
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

        // Người đã từng nhắn trên ticket cũng phải được báo. Admin/Manager nhảy vào trả lời không
        // hề được thêm vào assignment lẫn participant, nên nếu chỉ dựa vào hai nguồn trên thì họ
        // nhắn xong là mất hút, không bao giờ biết có người trả lời lại.
        var priorAuthors = await _unitOfWork.TicketChats.GetAllAsync()
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId && !c.IsDeleted)
            .Select(c => new { c.AuthorUserId, c.AuthorRole })
            .Distinct()
            .ToListAsync(cancellationToken);

        // Gom mọi ứng viên kèm bằng chứng về vai trò và về ô "được xem nội bộ".
        var candidates = new Dictionary<Guid, Candidate>();

        // Chủ ticket. Vai trò Customer, nên chỉ đọc được ghi chú nội bộ khi có dòng participant
        // cấp cờ CanViewInternal tường minh (#522) — gộp ở vòng lặp participant bên dưới.
        Add(candidates, customerId, ActorRoleEnum.Customer);

        // Khách của ticket nguồn đã bị gộp vào ticket này (merged source tickets).
        // Họ cần nhận thông báo tin nhắn public trên ticket master để không bị mất thông tin.
        // GetAllAsync() trả về IQueryable — KHÔNG await (be-rules §3); phải lọc !IsDeleted thủ công.
        var mergedSourceCustomerIds = await _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Where(t => !t.IsDeleted
                        && t.MergedIntoTicketId == ticketId)
            .Select(t => t.CustomerId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var mergedCustomerId in mergedSourceCustomerIds)
            Add(candidates, mergedCustomerId, ActorRoleEnum.Customer);

        // Người được phân công luôn là nhân sự vận hành.
        foreach (var staffId in assignedStaffIds)
            Add(candidates, staffId, ActorRoleEnum.Staff);

        foreach (var p in participants)
            Add(candidates, p.UserId, p.UserRole, p.CanViewInternal);

        foreach (var a in priorAuthors)
            Add(candidates, a.AuthorUserId, a.AuthorRole);

        var recipients = candidates.Values
            .Where(c => !isInternal || CanReadInternal(c))
            .Select(c => c.UserId)
            .ToHashSet();

        // Tác giả không tự nhận thông báo về tin của chính mình. Guid.Empty là dấu vết của actor
        // hệ thống hoặc dữ liệu cũ, không phải một người nhận được.
        recipients.Remove(authorUserId);
        recipients.Remove(Guid.Empty);

        return recipients.ToList();
    }

    /// <summary>
    /// Đúng luật đang chạy ở tầng đọc: Admin/Manager/Staff xem được nội bộ nhờ vai trò; ngoài ra
    /// một participant bất kỳ (kể cả Customer) xem được nếu đã được cấp cờ <c>CanViewInternal</c>.
    /// </summary>
    private static bool CanReadInternal(Candidate candidate) =>
        TicketQueryHelper.CanViewInternalChats(candidate.RoleNames, candidate.CanViewInternal);

    /// <summary>
    /// Ghi nhận một ứng viên. Cùng một người có thể xuất hiện ở nhiều nguồn (vừa được phân công,
    /// vừa là participant, vừa từng nhắn) nên vai trò được GỘP lại thay vì ghi đè: bỏ sót một vai
    /// trò nghĩa là bỏ sót quyền đọc mà người đó thực sự có. Cờ <c>CanViewInternal</c> cũng gộp
    /// theo hướng "đã cấp ở bất kỳ dòng nào thì tính là có cấp".
    /// </summary>
    private static void Add(
        Dictionary<Guid, Candidate> candidates,
        Guid userId,
        ActorRoleEnum role,
        bool canViewInternal = false)
    {
        if (userId == Guid.Empty)
            return;

        if (!candidates.TryGetValue(userId, out var candidate))
        {
            candidate = new Candidate(userId);
            candidates[userId] = candidate;
        }

        candidate.RoleNames.Add(role.ToString());
        candidate.CanViewInternal |= canViewInternal;
    }

    private class Candidate
    {
        public Candidate(Guid userId) => UserId = userId;

        public Guid UserId { get; }

        /// <summary>
        /// Vai trò để dạng chuỗi vì <see cref="TicketQueryHelper"/> so khớp theo chuỗi (nó nhận role
        /// lấy từ JWT). Dùng đúng kiểu đó để không phải dịch qua lại rồi lệch nhau lúc nào không hay.
        /// </summary>
        public HashSet<string> RoleNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CanViewInternal { get; set; }
    }
}
