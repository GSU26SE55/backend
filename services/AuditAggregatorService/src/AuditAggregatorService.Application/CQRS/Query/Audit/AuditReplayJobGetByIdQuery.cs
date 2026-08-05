using MediatR;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Application.CQRS.Query.Audit;

/// <summary>
/// GH-728 — tra tiến độ một job replay.
/// Message 202 của <c>POST /replay</c> trỏ người dùng tới đây, nên endpoint này là phần
/// bắt buộc để lời hứa "xử lý bất đồng bộ" kiểm chứng được.
/// </summary>
public class AuditReplayJobGetByIdQuery : IRequest<CommonResponse<AuditReplayJobDto>>
{
    public Guid JobId { get; set; }
}

/// <summary>GH-728 — hình dạng trả về cho màn hình vận hành.</summary>
public class AuditReplayJobDto
{
    public string Id { get; set; } = string.Empty;

    /// <summary><c>null</c> = replay tất cả service.</summary>
    public string? Service { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>Requested / InProgress / Completed / CompletedWithErrors.</summary>
    public string Status { get; set; } = string.Empty;

    public int ExpectedResponders { get; set; }
    public int RespondedCount { get; set; }

    /// <summary>Tổng số bản ghi audit đã được phát lại.</summary>
    public int RepublishedCount { get; set; }

    /// <summary>
    /// True nếu có service chạm trần an toàn hoặc gặp payload hỏng ⇒ dữ liệu CHƯA đầy đủ,
    /// dù trạng thái đã là kết thúc.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>Các service đã phản hồi — để biết đang chờ ai khi job treo.</summary>
    public List<string> RespondedServices { get; set; } = new();

    /// <summary>Các service CHƯA phản hồi (chỉ có nghĩa khi replay tất cả).</summary>
    public List<string> PendingServices { get; set; } = new();

    public string? Error { get; set; }
    public string? RequestedByAccountId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
