using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Query.Suggestions;
using TicketService.Application.DTOs.Response.Suggestions;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.Suggestions;

/// <summary>
/// Kỹ thuật viên được phân công: xếp hạng bài viết KB để tham khảo khi sửa chữa.
///
/// Chỉ đọc — bấm áp dụng mới tạo <c>TicketKbReference</c> qua lệnh riêng.
/// </summary>
public class TicketKbSuggestionsQueryHandler
    : IRequestHandler<TicketKbSuggestionsQuery, CommonResponse<KbSuggestionListDto>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IAiKbSuggestClient _ai;
    private readonly ITicketCurrentUserService _currentUser;

    public TicketKbSuggestionsQueryHandler(
        ITicketUnitOfWork uow, IAiKbSuggestClient ai, ITicketCurrentUserService currentUser)
    {
        _uow = uow;
        _ai = ai;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<KbSuggestionListDto>> Handle(
        TicketKbSuggestionsQuery request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets.GetAllAsync()
            .Where(t => t.Id == request.TicketId && !t.IsDeleted)
            .Select(t => new { t.Id, t.Title, t.Description, t.Category })
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return Fail(404, "Ticket not found.");

        // Phân quyền: Admin/Manager xem mọi ticket; Staff phải được phân công vào ticket này.
        // Nới hơn AddTicketKbReferenceCommandHandler (chỉ PrimaryHandler): Supporter cũng
        // đang sửa chữa và đã có CanViewInternal. XEM thì nới, GẮN tài liệu vẫn giữ nguyên
        // chỉ PrimaryHandler.
        var role = _currentUser.Role;
        if (role != "Admin" && role != "Manager")
        {
            if (role != "Staff")
                return Fail(403, "You do not have permission to view document suggestions for this Ticket.");

            if (!Guid.TryParse(_currentUser.UserId, out var currentUserId))
                return Fail(403, "Unable to determine the current user.");

            var isAssigned = await _uow.TicketAssignments.GetAllAsync()
                .AnyAsync(a => a.TicketId == ticket.Id
                    && a.StaffId == currentUserId
                    && !a.IsDeleted
                    && a.Role != AssignmentRoleEnum.PreviousPrimaryHandler, ct);

            if (!isAssigned)
                return Fail(403, "Only staff assigned to handle this Ticket can view document suggestions.");
        }

        // Chỉ bài đã xuất bản — Draft/PendingReview chưa qua duyệt, không đưa cho kỹ thuật viên.
        // KHÔNG lọc theo Category: đó là việc của AI (chỉ cộng điểm), vì ticket Performance
        // vẫn có thể cần bài an toàn nhiệt.
        // KHÔNG select Content (jsonb, nặng) — chấm điểm chỉ dùng title/tags.
        var articles = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .Where(a => !a.IsDeleted && a.Status == KbArticleStatusEnum.Published)
            .Select(a => new
            {
                a.Id,
                a.Code,
                a.Title,
                a.Tags,
                a.Category,
                a.HelpfulCount
            })
            .ToListAsync(ct);

        if (articles.Count == 0)
        {
            return Ok(new KbSuggestionListDto
            {
                Note = "No articles have been published in the system yet."
            });
        }

        // Dữ liệu AI đã sinh cho chính ticket này (Phase 0). Ticket do Customer tạo không có
        // → danh sách rỗng, AI chỉ dựa vào mô tả.
        var ai = await _uow.TicketAiSuggestions.GetAllAsync()
            .Where(s => s.TicketId == ticket.Id && !s.IsDeleted)
            .Select(s => new { s.ActionSteps, s.SopReferences, s.KbDocRefs })
            .FirstOrDefaultAsync(ct);

        var candidates = articles
            .Select(a => new AiKbCandidate(
                KbId: a.Id.ToString(),
                Code: a.Code,
                Title: a.Title,
                Tags: a.Tags ?? new List<string>(),
                Category: (int)a.Category,
                HelpfulCount: a.HelpfulCount))
            .ToList();

        var description = $"{ticket.Title} {ticket.Description}".Trim();
        var result = await _ai.SuggestKbAsync(
            category: (int)ticket.Category,
            description: description,
            candidates: candidates,
            topN: Math.Clamp(request.TopN, 1, 10),
            aiActionSteps: ai?.ActionSteps ?? new List<string>(),
            aiSopReferences: ai?.SopReferences ?? new List<string>(),
            aiKbDocRefs: ai?.KbDocRefs ?? new List<string>(),
            ct: ct);

        if (result is null)
        {
            return Ok(new KbSuggestionListDto
            {
                AiAvailable = false,
                Note = "Unable to get suggestions from AI right now. You can still look up documents manually."
            });
        }

        var items = result.Suggestions
            .Select(s => new KbSuggestionDto
            {
                KbArticleId = s.KbId,
                Code = s.Code,
                Title = s.Title,
                Score = s.Score,
                Reason = s.Reason
            })
            .ToList();

        return Ok(new KbSuggestionListDto { Items = items, Note = result.Note });
    }

    private static CommonResponse<KbSuggestionListDto> Ok(KbSuggestionListDto data) =>
        new() { IsSuccess = true, StatusCode = 200, Data = data };

    private static CommonResponse<KbSuggestionListDto> Fail(int statusCode, string message) =>
        new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
