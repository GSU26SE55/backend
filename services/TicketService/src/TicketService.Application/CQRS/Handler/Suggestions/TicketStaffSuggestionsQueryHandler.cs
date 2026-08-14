using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Query.Suggestions;
using TicketService.Application.DTOs.Response.Suggestions;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Suggestions;

/// <summary>
/// Manager triage: xếp hạng nhân viên phù hợp xử lý ticket.
///
/// BE truy vấn ứng viên + đếm tải rồi gửi sang AI chấm điểm (ai-module không đọc được
/// DB này). Chỉ đọc — không phân công. Manager vẫn có thể chọn người ngoài danh sách.
/// </summary>
public class TicketStaffSuggestionsQueryHandler
    : IRequestHandler<TicketStaffSuggestionsQuery, CommonResponse<StaffSuggestionListDto>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IAiStaffSuggestClient _ai;

    /// <summary>
    /// Trạng thái tính là "đang chiếm chỗ" của nhân viên.
    /// </summary>
    /// <remarks>
    /// PHẢI khớp <c>ux_tickets_active_auto_per_asset_category</c> (status NOT IN 8,10,11,12) —
    /// cùng danh sách <c>CreateTicketFromAlertConsumer</c> dùng. Lệch là đếm sai tải, dẫn tới
    /// gợi ý người đã đầy việc.
    /// </remarks>
    private static readonly TicketStatusEnum[] ActiveStatuses =
    {
        TicketStatusEnum.Open,
        TicketStatusEnum.Pending,
        TicketStatusEnum.InProgress,
        TicketStatusEnum.Request,
        TicketStatusEnum.ReAssign
    };

    public TicketStaffSuggestionsQueryHandler(ITicketUnitOfWork uow, IAiStaffSuggestClient ai)
    {
        _uow = uow;
        _ai = ai;
    }

    public async Task<CommonResponse<StaffSuggestionListDto>> Handle(
        TicketStaffSuggestionsQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Title, t.Description, t.Category, t.Priority })
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        // Chỉ lấy nhân viên kỹ thuật: bảng staff_accounts chứa CẢ Manager/Admin
        // (AccountSyncConsumer tạo cho cả ba vai trò) nên bắt buộc lọc theo Role,
        // nếu không Manager sẽ tự thấy mình trong danh sách đề xuất.
        var staff = await _uow.StaffAccounts.GetAllAsync()
            .Where(s => !s.IsDeleted
                && s.Status == AccountStatusEnum.Active
                && s.IsAvailable
                && s.Role == "Staff")
            .Select(s => new
            {
                s.AccountId,
                s.FullName,
                s.SkillTier,
                s.SkillCodes,
                s.MaxConcurrentTickets
            })
            .ToListAsync(ct);

        if (staff.Count == 0)
        {
            return Ok(new StaffSuggestionListDto
            {
                Note = "No staff members are currently available to take on a ticket."
            });
        }

        var staffIds = staff.Select(s => s.AccountId).ToList();

        // Đếm tải GOM MỘT truy vấn — đếm từng người là N+1 trên danh sách vài chục nhân viên.
        // Loại PreviousPrimaryHandler: đó là người ĐÃ bị chuyển giao, không còn xử lý ticket;
        // đếm cả họ sẽ khiến nhân viên bị coi là đầy tải oan.
        var workload = await _uow.TicketAssignments.GetAllAsync()
            .Where(a => !a.IsDeleted
                && staffIds.Contains(a.StaffId)
                && a.Role != AssignmentRoleEnum.PreviousPrimaryHandler
                && a.Ticket != null
                && !a.Ticket.IsDeleted
                && ActiveStatuses.Contains(a.Ticket.Status))
            .GroupBy(a => a.StaffId)
            .Select(g => new { StaffId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StaffId, x => x.Count, ct);

        var candidates = staff
            .Select(s => new AiStaffCandidate(
                StaffId: s.AccountId.ToString(),
                FullName: s.FullName,
                SkillTier: (int)s.SkillTier,
                SkillCodes: s.SkillCodes ?? new List<string>(),
                ActiveTickets: workload.TryGetValue(s.AccountId, out var n) ? n : 0,
                MaxConcurrent: s.MaxConcurrentTickets))
            .ToList();

        var description = $"{ticket.Title} {ticket.Description}".Trim();
        var result = await _ai.SuggestStaffAsync(
            category: (int)ticket.Category,
            // Ticket chưa triage chưa có Priority — gửi 0, AI sẽ bỏ qua bước lọc tier.
            priority: ticket.Priority.HasValue ? (int)ticket.Priority.Value : 0,
            description: description,
            candidates: candidates,
            topN: Math.Clamp(request.TopN, 1, 10),
            ct: ct);

        if (result is null)
        {
            // Phân biệt rõ với "không có ai phù hợp" — đây là lỗi kỹ thuật.
            return Ok(new StaffSuggestionListDto
            {
                AiAvailable = false,
                Note = "Unable to get suggestions from AI right now. You can still assign manually."
            });
        }

        var byId = candidates.ToDictionary(c => c.StaffId);
        var items = result.Suggestions
            .Select(s =>
            {
                byId.TryGetValue(s.StaffId, out var c);
                return new StaffSuggestionDto
                {
                    StaffId = s.StaffId,
                    FullName = string.IsNullOrWhiteSpace(s.FullName) ? c?.FullName ?? "" : s.FullName,
                    SkillTier = c?.SkillTier ?? 0,
                    SkillCodes = c?.SkillCodes.ToList() ?? new List<string>(),
                    ActiveTickets = c?.ActiveTickets ?? 0,
                    MaxConcurrentTickets = c?.MaxConcurrent ?? 0,
                    Score = s.Score,
                    Reason = s.Reason
                };
            })
            .ToList();

        return Ok(new StaffSuggestionListDto { Items = items, Note = result.Note });
    }

    private static CommonResponse<StaffSuggestionListDto> Ok(StaffSuggestionListDto data) =>
        new() { IsSuccess = true, StatusCode = 200, Data = data };

    private static CommonResponse<StaffSuggestionListDto> Fail(int statusCode, string message) =>
        new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
