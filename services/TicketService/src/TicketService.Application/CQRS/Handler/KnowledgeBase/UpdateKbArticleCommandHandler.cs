using System.Text.Json;
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

public class UpdateKbArticleCommandHandler : IRequestHandler<UpdateKbArticleCommand, CommonResponse<KbArticleDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IIntegrationEventOutboxWriter _outboxWriter;

    public UpdateKbArticleCommandHandler(ITicketUnitOfWork uow, IIntegrationEventOutboxWriter outboxWriter)
    {
        _uow = uow;
        _outboxWriter = outboxWriter;
    }

    public async Task<CommonResponse<KbArticleDTO>> Handle(UpdateKbArticleCommand command, CancellationToken ct)
    {
        var article = await _uow.KnowledgeBaseArticles.GetAllAsync()
            .FirstOrDefaultAsync(a => a.Id == command.ArticleId, ct);

        if (article == null)
            return Fail(404, "Article not found.");

        if (article.IsTemplate)
        {
            if (!command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                return Fail(403, "Only Admin can update templates.");

            return await HandleTemplateUpdate(article, command, ct);
        }

        // Controller đã chặn ở [Authorize(Roles = "Staff,Manager,Admin")], nên tới được đây
        // nghĩa là user thuộc 1 trong 3 role đó và đều được phép đề xuất sửa. Khối kiểm tra
        // 403 cũ ở đây là dead code: điều kiện của nó luôn false với cả 3 role.
        var isManagerOrAdmin = command.CurrentUserRole.Equals("Manager", StringComparison.OrdinalIgnoreCase) ||
                               command.CurrentUserRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);

        // Chỉ Manager/Admin được ghi thẳng. Chủ sở hữu cũng phải qua phê duyệt: mọi thay đổi
        // nội dung KB đều cần một người có quyền duyệt xác nhận, kể cả khi người sửa chính là
        // người viết ra bài — nếu không, tác giả có thể tự đẩy nội dung sai lên bài đã Published
        // mà không ai rà lại.
        if (isManagerOrAdmin)
            return await HandleDirectUpdate(article, command, ct);

        // Determine next version numbers
        var nextMajor = article.Version + 1;
        var lastMinor = await _uow.KbArticleVersions.GetAllAsync()
            .Where(v => v.ArticleId == article.Id && v.MajorVersion == nextMajor)
            .OrderByDescending(v => v.MinorVersion)
            .Select(v => v.MinorVersion)
            .FirstOrDefaultAsync(ct);

        var nextMinor = lastMinor + 1;

        // Create new version as "Draft/Pending"
        var newVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = nextMajor,
            MinorVersion = nextMinor,
            Status = KbVersionStatusEnum.Pending,
            Title = command.Title,
            Content = J(command.Content),
            Tags = command.Tags ?? new List<string>(),
            ChangeDescription = command.ChangeDescription ?? "Staff updated content",
            ChangedBy = command.CurrentUserId
        };
        await _uow.KbArticleVersions.AddAsync(newVersion);

        article.ReviewRequired = true;
        article.Status = KbArticleStatusEnum.PendingReview;
        article.PendingReviewBy = command.CurrentUserId;

        _uow.KnowledgeBaseArticles.UpdateAsync(article);

        // Báo cho Manager/Admin là có bài đang chờ duyệt. Đây là điểm mấu chốt của luồng: trước
        // đây bài chuyển sang PendingReview hoàn toàn im lặng, người duyệt không hề biết có việc.
        //
        // Ghi outbox TRƯỚC SaveChangesAsync để event nằm cùng transaction với thay đổi trạng
        // thái — không bao giờ báo "có bài chờ duyệt" cho một thay đổi chưa lưu được.
        // Dùng command.Title (nội dung vừa gửi) chứ không phải article.Title: ở nhánh này bài gốc
        // chưa đổi tiêu đề, tiêu đề mới còn nằm trong bản version chờ duyệt.
        await _outboxWriter.WriteAsync(new KbArticleReviewRequestedEvent(
            article.Id,
            command.Title,
            command.CurrentUserId,
            command.CurrentUserName,
            command.ChangeDescription,
            IsNewArticle: false), ct);

        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Change draft has been saved and is pending approval.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    /// <summary>
    /// Cập nhật trực tiếp — CHỈ Manager/Admin: ghi thẳng nội dung mới vào article, đồng thời lưu
    /// 1 bản ghi version đã Approved để giữ lịch sử thay đổi. Chủ sở hữu KHÔNG đi đường này;
    /// bài của họ vẫn phải qua approve-review như mọi người khác.
    /// </summary>
    private async Task<CommonResponse<KbArticleDTO>> HandleDirectUpdate(
        KnowledgeBaseArticle article, UpdateKbArticleCommand command, CancellationToken ct)
    {
        var nextMajor = article.Version + 1;

        await _uow.BeginTransactionAsync();
        try
        {
            var newVersion = new KbArticleVersion
            {
                Id = Guid.NewGuid(),
                ArticleId = article.Id,
                MajorVersion = nextMajor,
                MinorVersion = 0,
                Status = KbVersionStatusEnum.Approved,
                ChangeDescription = command.ChangeDescription ?? "Direct update",
                ChangedBy = command.CurrentUserId
            };
            ApplyContentToVersion(newVersion, command);

            // Ô (nextMajor, 0) thường đã bị chiếm sẵn bởi bản Pending tạo lúc khởi tạo bài viết —
            // xem KbArticleVersionSlot. AddAsync thẳng vào đây là 23505 → 500.
            await KbArticleVersionSlot.UpsertAsync(_uow, newVersion, ct);
            await KbArticleVersionSlot.RejectOtherPendingAsync(
                _uow, article.Id, nextMajor, 0, "A direct update has replaced it.", ct);

            ApplyContentToArticle(article, command);
            article.Category = command.Category;
            article.Version = nextMajor;
            // Trả Status về trạng thái ổn định. Trước đây chỉ clear ReviewRequired/PendingReviewBy
            // mà bỏ quên Status, nên một bài đang PendingReview bị ghi thẳng sẽ kẹt lại ở
            // PendingReview với ReviewRequired=false — trạng thái tự mâu thuẫn, và badge
            // "chờ duyệt" của FE đếm cả bài không còn gì để duyệt.
            //
            // KHÔNG đụng tới bài đã Archived: archive là quyết định "ngừng dùng bài này", và
            // sửa nội dung không phải là cách để rút lại quyết định đó. Nếu set Published ở đây
            // thì một lần chỉnh chính tả cũng đủ đưa hướng dẫn đã khai tử trở lại luồng gợi ý
            // cho kỹ thuật viên. Muốn dùng lại thì bấm publish (un-archive) một cách có chủ đích.
            if (article.Status != KbArticleStatusEnum.Archived)
                article.Status = KbArticleStatusEnum.Published;
            article.ReviewRequired = false;
            article.PendingReviewBy = null;
            article.ManagerRejectReason = null;

            _uow.KnowledgeBaseArticles.UpdateAsync(article);
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Article updated successfully.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private async Task<CommonResponse<KbArticleDTO>> HandleTemplateUpdate(
        KnowledgeBaseArticle article, UpdateKbArticleCommand command, CancellationToken ct)
    {
        await _uow.BeginTransactionAsync();
        try
        {
            ApplyContentToArticle(article, command);

            if (article.Status == KbArticleStatusEnum.Draft)
            {
                var pending = new KbArticleVersion
                {
                    Id = Guid.NewGuid(),
                    ArticleId = article.Id,
                    MajorVersion = article.Version + 1,
                    MinorVersion = 0,
                    Status = KbVersionStatusEnum.Pending,
                    ChangeDescription = command.ChangeDescription ?? "Admin updated template",
                    ChangedBy = command.CurrentUserId
                };
                ApplyContentToVersion(pending, command);

                await KbArticleVersionSlot.UpsertAsync(_uow, pending, ct);
            }
            else if (article.Status == KbArticleStatusEnum.Published)
            {
                // Bump major version, create Approved version record immediately
                var nextMajor = article.Version + 1;
                article.Version = nextMajor;

                var newVersion = new KbArticleVersion
                {
                    Id = Guid.NewGuid(),
                    ArticleId = article.Id,
                    MajorVersion = nextMajor,
                    MinorVersion = 0,
                    Status = KbVersionStatusEnum.Approved,
                    ChangeDescription = command.ChangeDescription ?? "Admin updated template",
                    ChangedBy = command.CurrentUserId
                };
                ApplyContentToVersion(newVersion, command);

                await KbArticleVersionSlot.UpsertAsync(_uow, newVersion, ct);
            }

            _uow.KnowledgeBaseArticles.UpdateAsync(article);
            await _uow.CommitTransactionAsync();
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }

        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = true,
            StatusCode = 200,
            Message = "Template updated successfully.",
            Data = KnowledgeBaseMapper.ToDto(article)
        };
    }

    private static void ApplyContentToArticle(KnowledgeBaseArticle article, UpdateKbArticleCommand command)
    {
        article.Title = command.Title;
        article.Content = J(command.Content);
        article.Tags = command.Tags ?? new List<string>();
    }

    private static void ApplyContentToVersion(KbArticleVersion version, UpdateKbArticleCommand command)
    {
        version.Title = command.Title;
        version.Content = J(command.Content);
        version.Tags = command.Tags ?? new List<string>();
    }

    private static JsonDocument J(string? v) => KnowledgeBaseMapper.ToJsonDoc(v);

    private static CommonResponse<KbArticleDTO> Fail(int statusCode, string message)
    {
        return new CommonResponse<KbArticleDTO>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            Message = message
        };
    }
}
