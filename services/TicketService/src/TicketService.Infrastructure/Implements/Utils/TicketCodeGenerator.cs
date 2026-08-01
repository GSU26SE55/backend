using Microsoft.EntityFrameworkCore;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Utils;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Utils;

/// <summary>
/// Sinh mã ticket dạng <c>TKT-YYMM-NNNN</c>.
///
/// FIX race-condition: trước đây là read-then-write thuần (SELECT max → +1 → INSERT).
/// Khi 2 message xử lý song song (consumer + saga, hoặc 2 event cùng lúc) cả hai đọc
/// cùng số cuối → sinh trùng mã → INSERT thứ hai vỡ unique constraint IX_tickets_code
/// (Postgres 23505) → message rơi vào error queue, ticket KHÔNG được tạo.
///
/// Nay bọc trong PostgreSQL advisory lock (transaction-scoped): tại 1 thời điểm chỉ 1
/// transaction đọc-và-cấp số cho cùng prefix tháng, transaction khác chờ rồi đọc lại giá
/// trị mới. Lock tự nhả khi transaction kết thúc — không cần migration / đổi schema.
/// </summary>
public class TicketCodeGenerator : ITicketCodeGenerator
{
    // Khóa cố định cho namespace "ticket code" — tránh đụng advisory lock của tính năng khác.
    private const int AdvisoryLockNamespace = 0x544B_5400; // "TKT"

    private readonly ITicketUnitOfWork _uow;
    private readonly TicketDbContext _db;

    public TicketCodeGenerator(ITicketUnitOfWork uow, TicketDbContext db)
    {
        _uow = uow;
        _db = db;
    }

    public async Task<string> GenerateAsync()
    {
        var now = DateTime.UtcNow;
        var prefix = $"TKT-{now:yyMM}-";

        // Key thứ 2 = yyMM dạng số (vd 2607) — deterministic giữa các process.
        // KHÔNG dùng string.GetHashCode(): .NET randomize hash per-process nên 2 instance
        // sẽ khóa 2 giá trị khác nhau → lock vô tác dụng.
        var monthKey = now.Year % 100 * 100 + now.Month;

        // pg_advisory_xact_lock nhả tự động khi transaction kết thúc → serialize việc cấp số.
        //
        // Guard theo provider: `pg_advisory_xact_lock` là hàm riêng của PostgreSQL, và
        // `ExecuteSqlInterpolatedAsync` là API relational-only. Không có guard này thì class hard-require
        // Postgres ⇒ provider khác (InMemory trong unit test) ném InvalidOperationException ngay ở dòng
        // đầu, khiến phần sinh mã bên dưới không cách nào test được.
        //
        // ⚠️ Runtime thật LUÔN là Npgsql (DI: ManageDependencyInjection.cs → options.UseNpgsql;
        // design-time: TicketDbContextFactory cũng UseNpgsql), nên guard KHÔNG làm mất bảo vệ
        // race-condition trong production. Nếu sau này đổi provider, chống trùng mã phải được cài lại
        // bằng cơ chế khác — nếu không sẽ quay lại đúng lỗi 23505 mô tả ở phần tóm tắt trên.
        if (_db.Database.IsNpgsql())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdvisoryLockNamespace}, {monthKey})");
        }

        var lastTicket = await _uow.Tickets.GetAllAsync()
            .Where(t => t.Code.StartsWith(prefix))
            .OrderByDescending(t => t.Code)
            .FirstOrDefaultAsync();

        int nextNumber = 1;
        if (lastTicket != null && !string.IsNullOrEmpty(lastTicket.Code))
        {
            // Extract the NNNN part from TKT-YYMM-NNNN
            var parts = lastTicket.Code.Split('-');
            if (parts.Length == 3 && int.TryParse(parts[2], out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D4}";
    }
}
