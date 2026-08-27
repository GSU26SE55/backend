using SharedContracts.Events.Root;

namespace SharedContracts.Events;

/// <summary>
/// 02/08/2026 — Ảnh chụp TRẠNG THÁI HIỆN TẠI của một account, dùng để các service khác đồng bộ
/// read-model của mình. Khác hẳn về ngữ nghĩa với nhóm event vòng đời
/// (<see cref="AccountActivatedEvent"/> / <see cref="AccountProfileUpdatedEvent"/> /
/// <see cref="AccountDeletedEvent"/>): những event kia trả lời "vừa xảy ra chuyện gì",
/// còn event này trả lời "bây giờ account đang thế nào".
///
/// <para><b>Vì sao phải tách event riêng thay vì tái dùng <see cref="AccountActivatedEvent"/>:</b>
/// bên NotificationService có <c>AccountActivatedConsumer</c> ghi welcome notification. Nếu dùng
/// AccountActivatedEvent để đồng bộ lại (backfill / đổi role), mỗi lần đồng bộ sẽ đẻ ra một welcome
/// notification giả cho người dùng đã đăng ký từ lâu. Snapshot không mang ngữ nghĩa "vừa kích hoạt"
/// nên không consumer nghiệp vụ nào bám vào.</para>
///
/// <para><b>Publisher (AuthService):</b>
/// <list type="bullet">
/// <item><c>ChangeAccountRoleCommandHandler</c> — Reason = <c>RoleChanged</c>.</item>
/// <item><c>ChangeAccountStatusCommandHandler</c> — Reason = <c>StatusChanged</c>.</item>
/// <item><c>AccountResyncCommandHandler</c> — Reason = <c>Resync</c> (đối soát thủ công).</item>
/// <item><c>AuthDataSeeder</c> — Reason = <c>Seed</c> (môi trường dựng mới).</item>
/// </list></para>
///
/// <para><b>Subscriber:</b> NotificationService <c>AccountSnapshotSyncConsumer</c> — upsert toàn bộ
/// trường vào <c>AccountReadModel</c>. Consumer PHẢI idempotent vì snapshot có thể được gửi lại
/// nhiều lần cho cùng một account (resync chạy bao nhiêu lần cũng được).</para>
///
/// <para><b>Thứ tự đến:</b> RabbitMQ không bảo đảm thứ tự tuyệt đối giữa các message. Nếu hai
/// snapshot của cùng account về ngược thứ tự thì read-model có thể lùi về trạng thái cũ. Trường
/// <see cref="SnapshotAtUtc"/> tồn tại để consumer bỏ qua snapshot cũ hơn bản đã ghi — xem
/// <c>AccountSnapshotSyncConsumer</c>.</para>
/// </summary>
/// <param name="AccountId">Id account bên AuthService — cũng là khoá chính của read-model.</param>
/// <param name="Email">Email đã chuẩn hoá về chữ thường ở phía consumer.</param>
/// <param name="FullName">Họ tên hiển thị.</param>
/// <param name="PhoneNumber">Số điện thoại, có thể null.</param>
/// <param name="Role">Tên role hiện tại (1 account = 1 role): Admin | Manager | Staff | Customer.</param>
/// <param name="IsActive">
/// Cờ eligibility dành cho read-model thông báo: <c>true</c> với Active hoặc Locked (khóa tạm vẫn
/// phải nhận cảnh báo vận hành), <c>false</c> với các trạng thái còn lại. Consumer cần trạng thái
/// nghiệp vụ chính xác phải dùng <paramref name="AccountStatus"/>, không được suy ra từ cờ này.
/// </param>
/// <param name="IsDeleted">Account đã soft-delete bên AuthService hay chưa.</param>
/// <param name="SnapshotAtUtc">Thời điểm chụp ảnh trạng thái (UTC) — dùng để chống message về trễ.</param>
/// <param name="Reason">Nguồn phát: RoleChanged | StatusChanged | Resync | Seed. Chỉ để log/trace.</param>
/// <param name="AccountStatus">
/// Giá trị số của <c>AuthService.Domain.Enums.AccountStatusEnum</c>. Giữ riêng với
/// <paramref name="IsActive"/> để TicketService phân biệt Pending/Locked/Inactive/Suspended/Banned.
/// </param>
/// <param name="HasStaffProfileSnapshot">
/// <c>true</c> khi event mang toàn bộ projection hồ sơ Staff. Chỉ các lượt reconciliation đầy đủ
/// mới bật cờ này; snapshot role/status realtime không được xóa nhầm dữ liệu staff vì không load
/// hồ sơ.
/// </param>
/// <param name="EmployeeCode">Mã nhân viên authoritative khi có staff profile.</param>
/// <param name="MaxConcurrentTickets">Sức chứa ticket authoritative của nhân viên.</param>
/// <param name="IsAvailable">Trạng thái sẵn sàng nhận việc authoritative.</param>
/// <param name="SkillTier">Giá trị số staff skill tier bên AuthService.</param>
/// <param name="SkillCodes">Tập mã kỹ năng authoritative đã chuẩn hoá.</param>
public record AccountSyncSnapshotEvent(
    Guid AccountId,
    string Email,
    string FullName,
    string? PhoneNumber,
    string Role,
    bool IsActive,
    bool IsDeleted,
    DateTime SnapshotAtUtc,
    string Reason,
    int AccountStatus = 0,
    bool HasStaffProfileSnapshot = false,
    string? EmployeeCode = null,
    int MaxConcurrentTickets = 3,
    bool IsAvailable = true,
    int SkillTier = 0,
    List<string>? SkillCodes = null
) : IntegrationEvent;
