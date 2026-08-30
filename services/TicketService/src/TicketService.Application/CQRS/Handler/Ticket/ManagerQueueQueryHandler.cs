using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedInfrastructure.Extensions;
using TicketService.Application.Common.Utils;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Ticket;

public class ManagerQueueQueryHandler : IRequestHandler<ManagerQueueQuery, CommonResponse<PaginationResponse<TicketDTO>>>
{
    private readonly ITicketUnitOfWork _unitOfWork;
    private readonly ITicketCurrentUserService _currentUserService;
    private readonly ISlaCalculator _slaCalculator;

    public ManagerQueueQueryHandler(
        ITicketUnitOfWork unitOfWork,
        ITicketCurrentUserService currentUserService,
        ISlaCalculator slaCalculator)
    {
        _unitOfWork = unitOfWork;
        _currentUserService = currentUserService;
        _slaCalculator = slaCalculator;
    }

    public async Task<CommonResponse<PaginationResponse<TicketDTO>>> Handle(ManagerQueueQuery request, CancellationToken cancellationToken)
    {
        var query = _unitOfWork.Tickets.GetAllAsync()
            .AsNoTracking()
            .Include(t => t.SlaTimer)
            .Include(t => t.BatteryAssets)
            // Thiếu Include này thì t.Assignments luôn rỗng → mọi card danh sách
            // đều hiện "Chưa phân công" dù ticket đã gán Staff (detail thì đúng
            // vì TicketGetByIdQueryHandler có Include). Lọc !IsDeleted như bên detail.
            .Include(t => t.Assignments.Where(a => !a.IsDeleted))
            // Hàng chờ = mọi ticket CHƯA có người quyết mức ưu tiên:
            //  - New: chờ Manager triage (luồng Customer tạo).
            //  - Open + Origin=AutoFromAlert: ticket do alert/AI tự tạo — đã được gán sẵn
            //    Impact/Urgency/Priority trong TicketAutoCreateFromAlertCommandHandler nên
            //    KHÔNG đi qua TicketTriageCommandHandler. Trước đây chúng rơi khỏi mọi hàng
            //    chờ: Manager không có chỗ nào rà lại mức AI đoán trước khi gán Staff.
            //    Đưa vào đây để Manager xác nhận/sửa priority (nút Đổi mức ưu tiên) trước
            //    khi phân công. Ticket Open do người triage thủ công KHÔNG lọt vào —
            //    mức ưu tiên của chúng đã có người chịu trách nhiệm.
            .Where(t => !t.IsDeleted && t.MergedIntoTicketId == null &&
                        t.Status == TicketStatusEnum.Open);

        if (request.Priority.HasValue)
            query = query.Where(t => t.Priority == request.Priority.Value);

        if (request.Category.HasValue)
            query = query.Where(t => t.Category == request.Category.Value);

        // Ticket New chưa có Priority (null). Postgres xếp NULL CUỐI khi ORDER BY ASC, nên
        // nếu sort thẳng theo Priority thì ticket chưa triage bị đẩy xuống sau ticket auto —
        // ngược với ý nghĩa hàng chờ. Ưu tiên nhóm New lên trước, rồi mới P1→P2→P3.
        // Rank dùng chung với TicketGetListQueryHandler để hai nơi không lệch thứ tự.
        query = query.OrderBy(TicketQueryHelper.PriorityRank)
            .ThenBy(t => t.CreatedAt)
            .ThenBy(t => t.Id); // tie-breaker cố định — pagination ổn định

        // Phân trang trên entity: sau đó còn phải truy vấn phụ (chat chưa đọc) rồi mới dựng DTO,
        // nên không chiếu sang DTO trong SQL được.
        var page = await query.ToPagedEntityListAsync(request.PageNumber, request.PageSize, cancellationToken);
        var rawItems = page.Items;

        var ticketIds = rawItems.Select(t => t.Id).ToList();
        HashSet<Guid> unreadTicketIds;
        if (ticketIds.Count == 0 || !Guid.TryParse(_currentUserService.UserId, out var actorId))
        {
            unreadTicketIds = new HashSet<Guid>();
        }
        else
        {
            var actorRoles = new[] { _currentUserService.Role ?? "Manager" };
            bool canViewInternal = TicketQueryHelper.CanViewInternalChats(actorRoles);
            var readChatIds = _unitOfWork.TicketChatReads.GetAllAsync().AsNoTracking()
                .Where(r => r.UserId == actorId && !r.IsDeleted).Select(r => r.ChatId);
            var chatsBase = _unitOfWork.TicketChats.GetAllAsync().AsNoTracking()
                .Where(c => ticketIds.Contains(c.TicketId) && !c.IsDeleted && c.AuthorUserId != actorId);
            if (!canViewInternal)
                chatsBase = chatsBase.Where(c => !c.IsInternal);
            var unreadList = await chatsBase
                .Where(c => !readChatIds.Contains(c.Id))
                .Select(c => c.TicketId).Distinct()
                .ToListAsync(cancellationToken);
            unreadTicketIds = unreadList.ToHashSet();
        }

        // Tên staff cho phần "phụ trách": gom StaffId của cả trang rồi tra 1 lần
        // (không phải mỗi ticket 1 query). Nhờ vậy MỌI role đọc được tên mà không
        // cần gọi /api/staff — endpoint đó chỉ mở cho Admin/Manager.
        var staffIds = page.Items
            .SelectMany(t => t.Assignments.Where(a => !a.IsDeleted).Select(a => a.StaffId))
            .Distinct()
            .ToList();
        var staffNames = staffIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _unitOfWork.StaffAccounts.GetAllAsync().AsNoTracking()
                .Where(s => staffIds.Contains(s.AccountId) && !s.IsDeleted)
                .ToDictionaryAsync(s => s.AccountId, s => s.FullName, cancellationToken);

        return new CommonResponse<PaginationResponse<TicketDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = page.Map(t => TicketQueryHelper.MapToTicketDTO(
                t, _slaCalculator, DateTime.UtcNow, unreadTicketIds.Contains(t.Id), staffNames))
        };
    }
}
