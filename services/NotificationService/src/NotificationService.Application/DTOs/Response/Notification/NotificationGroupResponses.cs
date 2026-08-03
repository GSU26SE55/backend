using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Notification;

/// <summary>Một trang nhóm người nhận.</summary>
public class NotificationGroupListResponse : CommonResponse<PaginationResponse<NotificationGroupDto>> { }

/// <summary>Chi tiết một nhóm.</summary>
public class NotificationGroupResponse : CommonResponse<NotificationGroupDto> { }

/// <summary>
/// Kết quả một hành động thay đổi trạng thái nhóm (tạo / sửa / xoá).
/// <c>Data</c> là Id nhóm vừa tác động, để client chọn đúng dòng sau khi làm mới danh sách.
/// </summary>
public class NotificationGroupActionResponse : CommonResponse<Guid> { }

/// <summary>Một trang thành viên của nhóm.</summary>
public class NotificationGroupMemberListResponse : CommonResponse<PaginationResponse<NotificationGroupMemberDto>> { }

/// <summary>
/// Kết quả thêm nhiều thành viên cùng lúc. Tách rõ ba con số vì thêm hàng loạt gần như luôn có
/// phần tử bị bỏ qua, mà im lặng bỏ qua thì admin tưởng đã thêm đủ.
/// </summary>
public class NotificationGroupAddMembersDto
{
    /// <summary>Số người thực sự được thêm mới.</summary>
    public int Added { get; set; }

    /// <summary>Số người đã có sẵn trong nhóm ⇒ bỏ qua, KHÔNG coi là lỗi.</summary>
    public int AlreadyMembers { get; set; }

    /// <summary>
    /// Số id không tìm thấy trong read-model tài khoản ⇒ bỏ qua. Thường là id gõ sai, hoặc tài
    /// khoản vừa tạo mà snapshot đồng bộ chưa tới (chạy
    /// <c>POST /api/admin/accounts/resync</c> bên AuthService rồi thử lại).
    /// </summary>
    public int UnknownAccounts { get; set; }

    /// <summary>Tổng số người nhận của nhóm sau thao tác (đã lọc người không hoạt động).</summary>
    public int MemberCount { get; set; }
}

/// <summary>Kết quả thêm nhiều thành viên.</summary>
public class NotificationGroupAddMembersResponse : CommonResponse<NotificationGroupAddMembersDto> { }
