using NotificationService.Domain.Enums;
using SharedKernels.Domain;

namespace NotificationService.Domain.Entities;

/// <summary>
/// Sprint 6.4 NOTI4-06 — bảng nối quan hệ <b>nhiều-nhiều</b> giữa lần gửi và nhóm:
/// một lần gửi nhắm nhiều nhóm, một nhóm nhận nhiều lần gửi.
///
/// <para>Cho phép trộn cả nhóm lẫn cá nhân trong cùng một lần gửi, nên "gửi cho nhóm Quản lý
/// <b>và</b> thêm anh A" là <b>một</b> lần gửi chứ không phải hai — người vừa ở nhóm vừa được thêm
/// đích danh cũng chỉ nhận một lần.</para>
/// </summary>
public class NotificationBatchTarget : AuditableEntity
{
    /// <summary>Khoá ngoại thật (<c>ON DELETE CASCADE</c>) — cùng database, cùng transaction.</summary>
    public Guid BatchId { get; set; }

    public NotificationBatch? Batch { get; set; }

    public NotificationBatchTargetKindEnum TargetKind { get; set; }

    /// <summary>
    /// Bắt buộc khi <see cref="TargetKind"/> = <c>Group</c> (CHECK constraint ở DB).
    ///
    /// <para>Có khoá ngoại sang <c>notification_groups</c> nhưng <b>KHÔNG</b> cascade: xoá nhóm thì
    /// lịch sử gửi phải còn nguyên — "đã từng gửi cho nhóm này" là sự thật lịch sử, xoá nhóm không
    /// làm nó chưa từng xảy ra. Dùng <c>ON DELETE RESTRICT</c>; nhóm chỉ xoá mềm nên không bao giờ
    /// chạm tới ràng buộc này trong thực tế.</para>
    /// </summary>
    public Guid? GroupId { get; set; }

    public NotificationGroup? Group { get; set; }

    /// <summary>
    /// Bắt buộc khi <see cref="TargetKind"/> = <c>User</c>. KHÔNG đặt khoá ngoại — trỏ sang
    /// read-model đồng bộ qua bus, xem chú thích ở <see cref="NotificationGroupMember.UserId"/>.
    /// </summary>
    public Guid? UserId { get; set; }
}
