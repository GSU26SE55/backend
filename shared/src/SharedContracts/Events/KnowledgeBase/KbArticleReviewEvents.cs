using SharedContracts.Events.Root;

namespace SharedContracts.Events.KnowledgeBase;

/// <summary>
/// Publish khi một bài KB chuyển sang <c>PendingReview</c> — Staff sửa nội dung bài đã có, hoặc
/// tạo bài mới cần duyệt. Subscriber: NotificationService <c>KbArticleReviewRequestedConsumer</c>
/// → báo cho Manager/Admin là có bài đang chờ duyệt.
///
/// Trước đây luồng duyệt KB hoàn toàn im lặng: người duyệt không nhận được gì, chỉ có badge
/// "chờ duyệt" ở sidebar (cache 60s, không poll) làm manh mối.
/// </summary>
/// <param name="ArticleId">Bài KB — FE deep-link tới <c>/{role}/kb/{id}</c>.</param>
/// <param name="ArticleTitle">Tiêu đề tại thời điểm gửi duyệt, để hiện trong nội dung thông báo.</param>
/// <param name="RequestedByUserId">Người đề xuất thay đổi (article.PendingReviewBy).</param>
/// <param name="RequestedByName">Tên người đề xuất — thông báo nói "ai" chứ không in ra Guid.</param>
/// <param name="ChangeDescription">Mô tả thay đổi người sửa nhập, có thể rỗng.</param>
/// <param name="IsNewArticle">true = bài tạo mới chờ duyệt; false = bản sửa của bài đã tồn tại.</param>
public record KbArticleReviewRequestedEvent(
    Guid ArticleId,
    string ArticleTitle,
    Guid RequestedByUserId,
    string? RequestedByName,
    string? ChangeDescription,
    bool IsNewArticle
) : IntegrationEvent;

/// <summary>
/// Publish khi Manager/Admin duyệt hoặc từ chối một bản sửa KB. Subscriber: NotificationService
/// <c>KbArticleReviewDecidedConsumer</c> → báo cho NGƯỜI ĐỀ XUẤT, không phải người duyệt.
///
/// Gộp approve/reject vào một event vì hai nhánh chỉ khác nhau ở <paramref name="Approved"/> và
/// lý do từ chối; tách đôi sẽ nhân đôi consumer mà không thêm thông tin nào.
/// </summary>
/// <param name="ArticleId">Bài KB liên quan.</param>
/// <param name="ArticleTitle">Tiêu đề bài, để hiện trong nội dung thông báo.</param>
/// <param name="SubmittedByUserId">Người đề xuất — NGƯỜI NHẬN thông báo này.</param>
/// <param name="DecidedByUserId">Người bấm duyệt/từ chối.</param>
/// <param name="DecidedByName">Tên người duyệt, để thông báo nói rõ ai đã quyết định.</param>
/// <param name="Approved">true = duyệt, false = từ chối.</param>
/// <param name="RejectReason">Lý do từ chối. Chỉ có ý nghĩa khi <paramref name="Approved"/> = false.</param>
public record KbArticleReviewDecidedEvent(
    Guid ArticleId,
    string ArticleTitle,
    Guid SubmittedByUserId,
    Guid DecidedByUserId,
    string? DecidedByName,
    bool Approved,
    string? RejectReason
) : IntegrationEvent;
