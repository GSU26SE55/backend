using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace AuditAggregatorService.Application.Consumers;

/// <summary>
/// GH-775 — phân biệt "hai consumer cùng chèn một event" với lỗi DB THẬT.
/// </summary>
/// <remarks>
/// <para>
/// Bản cũ bắt trọn <see cref="DbUpdateException"/> và coi mọi thứ là đua khoá trùng. Nhưng cấu hình
/// bảng còn có ràng buộc độ dài và cột jsonb, nên một <c>ServiceName</c> quá dài (SQLSTATE 22001)
/// hay payload không phải JSON hợp lệ (22P02) cũng rơi vào đúng nhánh đó: message được ACK, bản ghi
/// kiểm toán biến mất, không retry, không DLQ, và log chỉ ghi "trùng lặp". Với một hệ thống kiểm
/// toán thì đây là kiểu mất mát tệ nhất — mất trong im lặng và tự nhận là bình thường.
/// </para>
/// <para>
/// Cố ý KHÔNG dùng <c>Npgsql.PostgresException</c>: tầng Application không tham chiếu driver, và
/// không cần. <see cref="DbException.SqlState"/> là API chuẩn của .NET (từ .NET 5) nên đọc được mã
/// lỗi mà vẫn giữ tầng này sạch — đồng thời test dựng được ngoại lệ giả không cần Postgres thật.
/// </para>
/// </remarks>
public static class DuplicateAuditDetection
{
    /// <summary>SQLSTATE 23505 — unique_violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>
    /// Unique index trên <c>(event_id, occurred_at)</c>. Xem <c>AuditAggregateConfiguration</c>.
    /// </summary>
    public const string EventUniqueIndexName = "ux_agg_event_occurred";

    /// <summary>
    /// True chỉ khi đây đúng là lần chèn trùng event đã có — trường hợp DUY NHẤT được nuốt.
    /// </summary>
    /// <remarks>
    /// Khi đọc được tên ràng buộc thì đòi khớp <see cref="EventUniqueIndexName"/> (hoặc khoá chính):
    /// một vi phạm unique ở ràng buộc KHÁC không phải chuyện trùng event, và nuốt nó là che mất lỗi
    /// dữ liệu. Khi driver không cho biết tên (bản in-memory dùng trong test, hoặc driver khác) thì
    /// chỉ dựa vào SQLSTATE — vẫn chặt hơn hẳn bản cũ vốn không kiểm gì.
    /// </remarks>
    public static bool IsDuplicateEventInsert(DbUpdateException exception)
    {
        var dbException = FindDbException(exception);
        if (dbException?.SqlState != UniqueViolation)
            return false;

        var constraint = ReadConstraintName(dbException);
        if (string.IsNullOrEmpty(constraint))
            return true;

        return constraint.Contains(EventUniqueIndexName, StringComparison.OrdinalIgnoreCase)
               || constraint.Contains("pk_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lần theo chuỗi inner exception tới <see cref="DbException"/> đầu tiên.</summary>
    private static DbException? FindDbException(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is DbException db)
                return db;
        }
        return null;
    }

    /// <summary>
    /// Đọc tên ràng buộc theo cách không phụ thuộc driver. Npgsql đưa các trường lỗi của Postgres
    /// vào <see cref="Exception.Data"/>, nên không cần tham chiếu kiểu cụ thể.
    /// </summary>
    private static string? ReadConstraintName(DbException exception)
    {
        foreach (var key in new[] { "ConstraintName", "constraint_name" })
        {
            if (exception.Data.Contains(key) && exception.Data[key] is string value && value.Length > 0)
                return value;
        }
        return null;
    }
}
