using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using SharedContracts.Events.KnowledgeBase;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.KnowledgeBase;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class ApproveReviewCommandHandler : IRequestHandler<ApproveReviewCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public ApproveReviewCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(ApproveReviewCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId && !a.IsDeleted, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
            return Fail(400, "Templates do not have an approval flow. Use the publish endpoint to publish templates.");

        if (article.Status != KbArticleStatusEnum.PendingReview)
            return Fail(409, "Article is not in Pending Review status.");

        // Find the latest pending version to approve
        var nextMajor = article.Version + 1;
        var pendingVersion = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => !v.IsDeleted && v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending)
            .OrderByDescending(v => v.MinorVersion)
            .FirstOrDefaultAsync(ct);

        await _uow.BeginTransactionAsync();
        try
        {
            if (pendingVersion != null)
            {
                // Copy contents to main article
                article.Title = pendingVersion.Title;
                article.Content = pendingVersion.Content;
                article.Tags = pendingVersion.Tags.ToList();
                article.Version = nextMajor; // Update to new major version

                // Mark this version as approved
                pendingVersion.Status = KbVersionStatusEnum.Approved;
                _uow.KbArticleVersions.UpdateAsync(pendingVersion);

                // Optional: Mark other pending versions for this major version as rejected or obsolete
                var otherPendingVersions = await _uow.KbArticleVersions.GetAllAsync()
                    .Where(v => !v.IsDeleted && v.ArticleId == article.Id && v.MajorVersion == nextMajor && v.Status == KbVersionStatusEnum.Pending && v.Id != pendingVersion.Id)
                    .ToListAsync(ct);

                foreach (var v in otherPendingVersions)
                {
                    v.Status = KbVersionStatusEnum.Rejected;
                    v.ManagerRejectReason = "Another version has been approved.";
                    _uow.KbArticleVersions.UpdateAsync(v);
                }
            }

            // Giữ nguyên trạng thái ổn định trước đó thay vì luôn rơi về Draft: duyệt một bài
            // đang Published mà trả về Draft thì bài biến mất khỏi danh sách Published cho tới
            // khi có người publish lại thủ công — approve hoá ra lại "ẩn" bài. Cùng quy tắc với
            // RejectReviewCommandHandler (Version > 0 ⇒ bài đã từng publish).
            // Đọc người đề xuất TRƯỚC khi xoá PendingReviewBy ngay dưới — đọc sau thì luôn null.
            var submittedBy = article.PendingReviewBy;

            article.Status = article.Version > 0 ? KbArticleStatusEnum.Published : KbArticleStatusEnum.Draft;
            article.ReviewRequired = false;
            article.PendingReviewBy = null;
            article.ManagerRejectReason = null;

            _uow.KnowledgeBaseArticles.UpdateAsync(article);

            // Báo cho người đề xuất là bản sửa đã được duyệt. Title lấy SAU khi đã copy nội dung
            // từ pendingVersion ở trên — thông báo phải nói đúng tiêu đề vừa được duyệt, không
            // phải tiêu đề cũ. Bỏ qua khi người duyệt chính là người gửi (Manager tự duyệt bài
            // mình đề xuất thì không cần tự báo cho mình).
            //
            // WriteAsync ghi vào outbox TRONG transaction hiện tại: nếu commit lỗi thì event cũng
            // mất theo, không có chuyện báo "đã duyệt" cho một thay đổi chưa thực sự lưu.
            if (submittedBy.HasValue && submittedBy.Value != Guid.Empty && submittedBy.Value != command.CurrentUserId)
            {
                await _outboxWriter.WriteAsync(new KbArticleReviewDecidedEvent(
                    article.Id,
                    article.Title,
                    submittedBy.Value,
                    command.CurrentUserId,
                    command.CurrentUserName,
                    Approved: true,
                    RejectReason: null), ct);
            }

            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Change request has been approved and the content updated successfully.",
            Data = new KbArticleActionDTO
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
