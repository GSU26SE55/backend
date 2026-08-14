using TicketService.Domain.Enums;

namespace TicketService.Application.DTOs.Response.Tickets;

public class TicketAssignmentDTO
{
    public string StaffId { get; set; } = string.Empty;
    public AssignmentRoleEnum Role { get; set; }

    /// <summary>
    /// Tên nhân viên, lấy từ bảng StaffAccount đã sync sang TicketService.
    /// Null khi chưa sync kịp — FE fallback hiển thị StaffId.
    ///
    /// Trước đây DTO chỉ có StaffId nên FE buộc phải gọi thêm /api/staff để tra
    /// tên; endpoint đó chỉ cho Admin/Manager nên Staff không hiển thị được ai
    /// đang phụ trách. Trả tên sẵn ở đây để mọi role dùng chung 1 nguồn.
    /// </summary>
    public string? StaffName { get; set; }
}
