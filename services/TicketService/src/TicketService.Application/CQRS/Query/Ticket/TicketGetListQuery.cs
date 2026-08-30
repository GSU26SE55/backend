using MediatR;
using SharedContracts.Common.Requests;
using SharedContracts.Common.Responses;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Query.Ticket;

public class TicketGetListQuery : PaginationRequest, IRequest<CommonResponse<PaginationResponse<TicketDTO>>>
{
    /// <summary>
    /// Từ khóa tìm kiếm.
    /// </summary>
    public string? Keyword { get; set; }
    public TicketStatusEnum? Status { get; set; }
    public TicketPriorityEnum? Priority { get; set; }
    /// <summary>
    /// Danh mục phân loại.
    /// </summary>
    public TicketCategoryEnum? Category { get; set; }
    public Guid? BatteryAssetId { get; set; }

    /// <summary>
    /// Bỏ bộ lọc ẩn ticket Open mặc định của Manager, trả về MỌI trạng thái trong một lần gọi.
    /// </summary>
    /// <remarks>
    /// Dành cho màn so sánh trước khi gộp ticket (Manager chọn ticket đích): ở đó Manager cần
    /// nhìn thấy cả ticket Open — ticket do AI gợi ý là trùng lặp thường vẫn đang chờ triage.
    /// Trước khi có cờ này, FE phải gọi endpoint hai lần (một lần mặc định, một lần Status=Open)
    /// rồi tự nối kết quả, khiến phân trang và sắp xếp sai vì mỗi lần lấy riêng một trang 100 bản ghi.
    ///
    /// <para>KHÔNG phải là cờ nâng quyền: Open vốn đã đọc được qua <c>Status=Open</c>, cờ này chỉ
    /// gộp hai lượt gọi đó làm một. Bỏ qua khi <see cref="Status"/> được set (lọc tường minh
    /// luôn thắng) và không có tác dụng với Admin vì Admin không bị lọc sẵn.</para>
    /// </remarks>
    public bool IncludeOpen { get; set; }

    /// <summary>
    /// Lọc theo tình trạng SLA: Paused | Warning | Breached.
    /// Độc lập với <see cref="Status"/> — cả ba đều là ticket đang xử lý, chỉ khác đồng hồ SLA.
    /// </summary>
    public SlaFilterEnum? Sla { get; set; }

    /// <summary>
    /// Lọc theo nguồn tạo ticket. Không map 1-1 với <see cref="TicketOriginEnum"/>:
    /// Environmental / PeriodicMaintenance / CascadeRisk đều là Origin = System, tách nhau
    /// bằng field chuyên biệt. Xem <see cref="TicketSourceFilterEnum"/>.
    /// </summary>
    public TicketSourceFilterEnum? Source { get; set; }

    /// <summary>
    /// Đảo chiều theo CreatedAt (legacy — giữ tương thích ngược).
    /// Nếu <see cref="SortDir"/> được set thì SortDir thắng.
    /// </summary>
    public bool IsDescending { get; set; } = true;

    /// <summary>
    /// Cột sort. Whitelist: code | title | category | status | priority | createdAt.
    /// Giá trị ngoài whitelist → createdAt (mặc định).
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>Hướng sort: asc | desc. Nếu set sẽ ghi đè <see cref="IsDescending"/>.</summary>
    public string? SortDir { get; set; }
}
