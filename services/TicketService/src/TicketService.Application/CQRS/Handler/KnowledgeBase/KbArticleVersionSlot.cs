using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

/// <summary>
/// Ghi bản ghi version của bài viết KB vào đúng ô <c>(article_id, major_version, minor_version)</c>.
/// </summary>
/// <remarks>
/// Vì sao cần: <c>article.Version</c> là major ĐÃ được duyệt/xuất bản (0 = chưa có bản nào duyệt), còn
/// bản đang soạn luôn nằm ở major <c>article.Version + 1</c>. Create/Duplicate/ChatConvert đều tạo sẵn
/// row <c>(1, 0)</c> Pending trong khi để <c>article.Version = 0</c>, nên ô <c>(Version + 1, 0)</c> ĐÃ
/// bị chiếm ngay từ lúc bài viết ra đời. Handler nào cứ thế <c>AddAsync</c> vào ô đó sẽ dính
/// <c>23505 duplicate key … IX_kb_article_versions_article_id_major_version_minor_version</c> và trả 500.
///
/// Lưu ý: unique index KHÔNG lọc <c>is_deleted</c> (xem <c>KbArticleVersionConfiguration</c>), nên row đã
/// soft-delete vẫn giữ chỗ. Tra cứu ở đây cố tình KHÔNG kèm điều kiện <c>!IsDeleted</c> — lọc vào là lại
/// INSERT trúng ô đã có và nổ đúng lỗi cũ.
/// </remarks>
internal static class KbArticleVersionSlot
{
    /// <summary>
    /// Chèn <paramref name="desired"/> nếu ô còn trống; nếu đã có row ở đúng ô đó thì ghi đè nội dung
    /// lên row cũ (và hồi sinh nếu nó đang soft-delete) thay vì tạo row mới.
    /// </summary>
    public static async Task UpsertAsync(
        ITicketUnitOfWork uow,
        KbArticleVersion desired,
        CancellationToken ct)
    {
        var existing = await uow.KbArticleVersions.GetAllAsync()
            .FirstOrDefaultAsync(v => v.ArticleId == desired.ArticleId
                && v.MajorVersion == desired.MajorVersion
                && v.MinorVersion == desired.MinorVersion, ct);

        if (existing is null)
        {
            await uow.KbArticleVersions.AddAsync(desired);
            return;
        }

        existing.Status = desired.Status;
        existing.Title = desired.Title;
        existing.Content = desired.Content;
        existing.Tags = desired.Tags;
        existing.ChangeDescription = desired.ChangeDescription;
        existing.ChangedBy = desired.ChangedBy;
        existing.ManagerRejectReason = null;
        existing.IsDeleted = false;
        existing.DeletedAt = null;

        uow.KbArticleVersions.UpdateAsync(existing);
    }

    /// <summary>
    /// Đánh Rejected các bản còn Pending cùng major nhưng khác minor. Bản ghi trực tiếp (Admin/Manager/
    /// chủ bài viết cập nhật, hoặc rollback) thắng các bản Staff đang chờ duyệt — giống cách
    /// <c>ApproveReviewCommandHandler</c> xử lý. Bỏ bước này thì bản pending cũ vẫn treo, duyệt sau sẽ
    /// ghi đè lại nội dung vừa cập nhật.
    /// </summary>
    public static async Task RejectOtherPendingAsync(
        ITicketUnitOfWork uow,
        Guid articleId,
        int majorVersion,
        int keepMinorVersion,
        string reason,
        CancellationToken ct)
    {
        var stale = await uow.KbArticleVersions.GetAllAsync()
            .Where(v => !v.IsDeleted
                && v.ArticleId == articleId
                && v.MajorVersion == majorVersion
                && v.MinorVersion != keepMinorVersion
                && v.Status == KbVersionStatusEnum.Pending)
            .ToListAsync(ct);

        foreach (var version in stale)
        {
            version.Status = KbVersionStatusEnum.Rejected;
            version.ManagerRejectReason = reason;
            uow.KbArticleVersions.UpdateAsync(version);
        }
    }
}
