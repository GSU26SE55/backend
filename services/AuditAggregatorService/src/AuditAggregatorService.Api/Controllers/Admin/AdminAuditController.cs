using System.Text;
using System.Text.Json;
using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SharedContracts.Common.Responses;

namespace AuditAggregatorService.Api.Controllers.Admin;

/// <summary>
/// **Audit Explorer (Admin)** — API tra cứu nhật ký audit hợp nhất (read-store) của TOÀN hệ thống (Sprint audit <c>#AUDIT-17</c>).
/// </summary>
/// <remarks>
/// **Tác dụng:** Cung cấp 1 điểm truy vấn duy nhất cho toàn bộ audit event đã được các microservice (Auth/Battery/Ticket/
/// File/Notification/Sms…) publish về read-store <c>audit_aggregate</c>. Dùng cho điều tra forensic, truy vết bảo mật,
/// dựng dòng thời gian hoạt động của 1 tài khoản, trace chuỗi nhân-quả xuyên service, xuất dữ liệu compliance và GDPR.
///
/// **Actor:** Chỉ **Admin** (role <c>SecurityOfficer</c> đã gộp vào <c>Admin</c> cho scope capstone — quyết định D13/<c>#AUDIT-18</c>).
/// Mọi request thiếu/invalid token → <b>401</b>; sai role → <b>403</b>.
///
/// **Giới hạn:** Rate limit nhóm "audit" 200 req/phút (vượt → <b>429</b>). Đây là read-store sao chép, replay được từ
/// source-of-truth ở từng service nếu hỏng.
///
/// **Kiến trúc:** Controller chỉ điều phối qua <c>IMediator</c> (CQRS) — toàn bộ logic ở các Query/Command Handler.
/// </remarks>
[ApiController]
[Route("api/admin/audit")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("audit")]
public class AdminAuditController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminAuditController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Tìm kiếm (tra cứu) audit event hợp nhất XUYÊN SERVICE với bộ lọc nâng cao + phân trang — màn hình chính của Audit Explorer.
    /// </summary>
    /// <remarks>
    /// Endpoint trung tâm để Admin điều tra/forensic: truy vấn read-store <c>audit_aggregate</c> (gom audit của Auth/Battery/
    /// Ticket/File/Notification/Sms... về 1 chỗ) theo nhiều tiêu chí, trả về theo trang (mới nhất trước).
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c> (role <c>SecurityOfficer</c> đã gộp Admin — D13/#AUDIT-18). Thiếu token → 401; sai role → 403.
    /// - Rate limit nhóm "audit": 200 req/phút (vượt → 429).
    ///
    /// Query parameters (đều optional, để trống = không lọc theo tiêu chí đó):
    /// - <c>service</c>: lọc theo service phát sinh (vd "AuthService").
    /// - <c>action</c>: mã hành động (vd "LoginSucceeded").
    /// - <c>category</c>: nhóm hành động (Authentication/DataModification/...).
    /// - <c>severity</c>: mức độ (Info/Warning/Critical/Security).
    /// - <c>actorId</c>: account thực hiện hành động.
    /// - <c>targetId</c>: đối tượng bị tác động.
    /// - <c>correlationId</c>: id luồng nghiệp vụ (trace xuyên service).
    /// - <c>isSuccess</c>: lọc theo thành công/thất bại.
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC).
    /// - <c>pageNumber</c> (mặc định 1) / <c>pageSize</c> (mặc định 50, trần 100).
    ///
    /// Cách hoạt động:
    /// - Controller gom query string vào <see cref="AuditSearchQuery"/> rồi gửi qua MediatR.
    /// - Handler áp filter trên <c>IQueryable</c> (AsNoTracking) + đếm tổng + lấy trang + map sang DTO.
    /// - Kết quả bọc <c>CommonResponse&lt;PaginationResponse&lt;AuditAggregateDto&gt;&gt;</c>.
    ///
    /// Use case:
    /// - Điều tra sự cố bảo mật, rà soát hoạt động bất thường (vd login fail tăng).
    /// - Tra audit của 1 account/đối tượng cụ thể.
    /// - Audit/compliance định kỳ theo khoảng thời gian.
    /// </remarks>
    /// <param name="query">Bộ lọc + phân trang (service, action, category, severity, actorId, targetId, correlationId, isSuccess, from, to, pageNumber, pageSize≤100).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách audit khớp filter, có phân trang.</returns>
    /// <response code="200">Tra cứu thành công — trả <c>CommonResponse&lt;PaginationResponse&lt;AuditAggregateDto&gt;&gt;</c>.</response>
    /// <response code="400">Filter tập-đóng không hợp lệ (<c>severity</c>/<c>category</c> sai value hoặc sai hoa-thường) — chi tiết field trong <c>listErrors</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="429">Vượt rate limit (200 req/phút).</response>
    [HttpGet("search")]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<AuditAggregateDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 400)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    [ProducesResponseType(typeof(CommonResponse<object>), 429)]
    public async Task<IActionResult> Search([FromQuery] AuditSearchQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy CHI TIẾT đầy đủ 1 audit event theo <c>event_id</c> — màn hình xem chi tiết 1 bản ghi nhật ký.
    /// </summary>
    /// <remarks>
    /// Trả toàn bộ trường của 1 audit event (actor, target, metadata, geo IP, correlation/causation, thời điểm...) khi đã biết
    /// <c>event_id</c> — thường mở từ 1 dòng trong kết quả <c>/search</c> hoặc <c>/timeline</c>.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Path parameter:
    /// - <c>eventId</c>: định danh duy nhất (UUID) của audit event. Lấy từ route, KHÔNG nhận từ body/query.
    ///
    /// Cách hoạt động:
    /// - Controller tạo <see cref="AuditGetByEventIdQuery"/> từ route → MediatR → handler tìm theo <c>event_id</c>.
    /// - Tìm thấy → 200 + DTO; không có → 404 (read-store có thể đã bị retention drop nếu quá cũ).
    ///
    /// Use case:
    /// - Xem sâu 1 sự kiện đáng ngờ sau khi search.
    /// - Lấy <c>correlation_id</c>/<c>causation_id</c> của event để trace tiếp.
    /// </remarks>
    /// <param name="eventId">Định danh duy nhất của audit event (UUID, từ route).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Chi tiết 1 audit event.</returns>
    /// <response code="200">Tìm thấy — trả <c>CommonResponse&lt;AuditAggregateDto&gt;</c>.</response>
    /// <response code="404">Không tồn tại <c>event_id</c> trong read-store (hoặc đã bị retention drop).</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("{eventId:guid}")]
    [ProducesResponseType(typeof(CommonResponse<AuditAggregateDto>), 200)]
    [ProducesResponseType(typeof(CommonResponse<AuditAggregateDto>), 404)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> GetByEventId(Guid eventId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AuditGetByEventIdQuery { EventId = eventId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Truy vết (trace) toàn bộ chuỗi sự kiện XUYÊN SERVICE theo <c>correlation_id</c> — dựng lại 1 luồng nghiệp vụ end-to-end.
    /// </summary>
    /// <remarks>
    /// Gom mọi audit event có cùng <c>correlation_id</c> (1 request/flow trải nhiều service) thành 1 chuỗi theo thứ tự thời gian,
    /// giúp trả lời "chuyện gì đã xảy ra từ đầu tới cuối". Quan hệ nhân-quả thể hiện qua <c>causation_id</c>
    /// (vd: anomaly pin → tự tạo ticket → gửi notification đều mang cùng correlation).
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Path parameter:
    /// - <c>correlationId</c>: id luồng nghiệp vụ cần trace (UUID, từ route).
    ///
    /// Cách hoạt động:
    /// - Controller tạo <see cref="AuditGetByCorrelationQuery"/> → MediatR → handler lọc theo correlation, sắp xếp tăng dần theo <c>occurred_at</c>.
    /// - Trả danh sách (có thể rỗng nếu correlation không tồn tại / đã bị retention drop).
    ///
    /// Use case:
    /// - Điều tra root-cause: 1 lỗi/luồng đã đi qua những service nào, theo trình tự nào.
    /// - Trace anomaly → ticket tự động → notification.
    /// </remarks>
    /// <param name="correlationId">Correlation id của luồng nghiệp vụ cần trace (UUID, từ route).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Chuỗi audit event cùng correlation, sắp theo thời gian.</returns>
    /// <response code="200">Trả <c>CommonResponse&lt;List&lt;AuditAggregateDto&gt;&gt;</c> (ordered theo <c>occurred_at</c>) — có thể rỗng.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("correlation/{correlationId:guid}")]
    [ProducesResponseType(typeof(CommonResponse<List<AuditAggregateDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> GetByCorrelation(Guid correlationId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AuditGetByCorrelationQuery { CorrelationId = correlationId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Dựng DÒNG THỜI GIAN hoạt động của 1 tài khoản trên TOÀN hệ thống (làm chủ thể hoặc bị tác động).
    /// </summary>
    /// <remarks>
    /// Liệt kê mọi audit event mà account là <b>chủ thể (actor)</b> HOẶC <b>đối tượng bị tác động (target)</b> — dựng hồ sơ
    /// hành vi/lịch sử của 1 user xuyên service (vd: đăng nhập, đổi mật khẩu, bị admin khoá, được gán ticket, file bị truy cập...).
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Parameters:
    /// - <c>accountId</c> (path): account cần dựng timeline (UUID).
    /// - <c>limit</c> (query): số bản ghi tối đa, mặc định 100, trần 500 (ngoài khoảng tự về 100).
    ///
    /// Cách hoạt động:
    /// - Controller tạo <see cref="AuditGetAccountTimelineQuery"/> → MediatR → handler lọc <c>actor_account_id = accountId OR target_id = accountId</c>, sắp giảm dần theo <c>occurred_at</c>, lấy tối đa <c>limit</c>.
    ///
    /// Use case:
    /// - Điều tra 1 tài khoản nghi vấn (bị chiếm dụng, hành vi bất thường).
    /// - Hỗ trợ khiếu nại của user ("tài khoản tôi đã làm gì / bị làm gì").
    /// - Audit truy cập dữ liệu của 1 người dùng (GDPR/compliance).
    /// </remarks>
    /// <param name="accountId">Account cần dựng timeline (UUID, từ route).</param>
    /// <param name="limit">Số bản ghi tối đa (mặc định 100, trần 500; ngoài khoảng tự về 100). Client truyền qua query.</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Timeline audit của account, mới nhất trước.</returns>
    /// <response code="200">Trả <c>CommonResponse&lt;List&lt;AuditAggregateDto&gt;&gt;</c> (actor hoặc target = accountId), mới nhất trước.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("account/{accountId:guid}/timeline")]
    [ProducesResponseType(typeof(CommonResponse<List<AuditAggregateDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> GetAccountTimeline(Guid accountId, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new AuditGetAccountTimelineQuery { AccountId = accountId, Limit = limit }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Thống kê (đếm) số lượng audit event gộp theo nhóm (<c>service</c> | <c>action</c> | <c>severity</c>) — số liệu cho dashboard/biểu đồ.
    /// </summary>
    /// <remarks>
    /// Trả số liệu tổng hợp (aggregate count) để vẽ biểu đồ và phát hiện bất thường, thay vì liệt kê từng bản ghi.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters:
    /// - <c>from</c> / <c>to</c>: khoảng thời gian (UTC, optional).
    /// - <c>groupBy</c>: tiêu chí gộp — <c>service</c> | <c>action</c> | <c>severity</c> (mặc định <c>severity</c>).
    ///
    /// Cách hoạt động:
    /// - Controller tạo <see cref="AuditGetStatsQuery"/> → MediatR → handler <c>GROUP BY</c> theo tiêu chí + đếm, sắp giảm dần theo count.
    /// - Trả danh sách cặp <c>{ key, count }</c>.
    ///
    /// Use case:
    /// - Dashboard phân bố severity, top action/service.
    /// - Phát hiện tăng đột biến (vd login fail tăng vọt → nghi brute-force).
    /// - Báo cáo audit định kỳ.
    /// </remarks>
    /// <param name="from">Mốc đầu khoảng thời gian (UTC, optional).</param>
    /// <param name="to">Mốc cuối khoảng thời gian (UTC, optional).</param>
    /// <param name="groupBy">Tiêu chí gộp: <c>service</c> | <c>action</c> | <c>severity</c> (mặc định <c>severity</c>).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Danh sách { key, count } theo nhóm.</returns>
    /// <response code="200">Trả <c>CommonResponse&lt;List&lt;AuditStatsItemDto&gt;&gt;</c> — các cặp {key, count}.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(CommonResponse<List<AuditStatsItemDto>>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> Stats([FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string groupBy = "severity", CancellationToken ct = default)
    {
        var result = await _mediator.Send(new AuditGetStatsQuery { From = from, To = to, GroupBy = groupBy }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xuất (export) audit khớp filter ra FILE CSV/JSON theo kiểu streaming — phục vụ lưu trữ/compliance/phân tích offline.
    /// </summary>
    /// <remarks>
    /// Tải toàn bộ audit khớp bộ lọc ra file để lưu trữ dài hạn, nộp cho regulator/audit, hoặc phân tích bằng công cụ ngoài.
    /// Ghi theo kiểu <b>streaming</b> (không nạp hết vào RAM) nên an toàn với 100k+ bản ghi. Đây là <b>file download</b>
    /// (không bọc CommonResponse).
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Parameters:
    /// - <c>query</c>: bộ lọc giống <c>/search</c> (service/action/severity/actor/target/from/to...), KHÔNG cần phân trang — xuất toàn bộ khớp filter.
    /// - <c>format</c>: định dạng file — <c>csv</c> (mặc định) hoặc <c>json</c>.
    ///
    /// Cách hoạt động:
    /// - Controller gửi <see cref="AuditExportQuery"/> → handler trả <c>IAsyncEnumerable</c> (lazy) → controller stream từng bản ghi xuống Response (CSV header + dòng, hoặc mảng JSON).
    /// - Header <c>Content-Disposition: attachment; filename=audit-export.csv|json</c>.
    ///
    /// Use case:
    /// - Nộp hồ sơ audit/compliance định kỳ.
    /// - Sao lưu nhật ký trước khi retention drop.
    /// - Đưa dữ liệu sang Excel/BI để phân tích.
    /// </remarks>
    /// <param name="query">Bộ lọc giống endpoint search (không phân trang — xuất toàn bộ khớp filter).</param>
    /// <param name="format">Định dạng file: <c>csv</c> (mặc định) hoặc <c>json</c>.</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <response code="200">Trả file đính kèm (<c>text/csv</c> hoặc <c>application/json</c>) qua streaming download.</response>
    /// <response code="400">Filter tập-đóng không hợp lệ (<c>severity</c>/<c>category</c>) — trả <c>CommonResponse</c> JSON, chi tiết field trong <c>listErrors</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet("export")]
    [Produces("text/csv", "application/json")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 400)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task Export([FromQuery] AuditExportQuery query, [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        // E (A+E, #AUDIT-17): validate filter tập-đóng trước khi stream. Export là file download (không qua MediatR result),
        // nên trả 400 + CommonResponse JSON (camelCase, khớp ErrorsListJsonConverter) thẳng vào Response body.
        var validationErrors = AuditSearchValidator.Validate(query);
        if (validationErrors.Count > 0)
        {
            Response.StatusCode = 400;
            Response.ContentType = "application/json";
            var bad = new CommonResponse<object> { IsSuccess = false, StatusCode = 400, ListErrors = validationErrors };
            await using var errWriter = new StreamWriter(Response.Body, Encoding.UTF8);
            await errWriter.WriteAsync(JsonSerializer.Serialize(bad, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            await errWriter.FlushAsync(ct);
            return;
        }

        var stream = await _mediator.Send(query, ct);

        var isJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
        Response.ContentType = isJson ? "application/json" : "text/csv";
        Response.Headers.ContentDisposition = $"attachment; filename=audit-export.{(isJson ? "json" : "csv")}";

        await using var writer = new StreamWriter(Response.Body, Encoding.UTF8);

        if (isJson)
        {
            await writer.WriteAsync("[");
            var first = true;
            await foreach (var dto in stream.WithCancellation(ct))
            {
                if (!first)
                    await writer.WriteAsync(",");
                await writer.WriteAsync(JsonSerializer.Serialize(dto));
                first = false;
            }
            await writer.WriteAsync("]");
        }
        else
        {
            await writer.WriteLineAsync("EventId,ServiceName,ActionCode,Category,Severity,ActorId,ActorDisplay,TargetId,IsSuccess,OccurredAt,GeoCountry");
            await foreach (var d in stream.WithCancellation(ct))
            {
                await writer.WriteLineAsync(string.Join(',',
                    d.EventId, Csv(d.ServiceName), Csv(d.ActionCode), Csv(d.ActionCategory), Csv(d.Severity),
                    d.ActorAccountId, Csv(d.ActorDisplay), d.TargetId, d.IsSuccess,
                    d.OccurredAt.ToString("O"), Csv(d.GeoCountry)));
            }
        }
        await writer.FlushAsync(ct);
    }

    /// <summary>
    /// Yêu cầu REPLAY (tái nạp) audit từ source-of-truth về read-store khi <c>audit_aggregate</c> hỏng/mất dữ liệu — xử lý bất đồng bộ.
    /// </summary>
    /// <remarks>
    /// Công cụ vận hành (admin tool) để khôi phục read-store: vì <c>audit_aggregate</c> chỉ là bản sao (materialized view),
    /// dữ liệu gốc vẫn nằm ở bảng append-only <c>{service}_audit_logs</c> của từng service nên có thể re-publish lại.
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameters (đều optional):
    /// - <c>service</c>: chỉ replay 1 service (null/để trống = tất cả).
    /// - <c>from</c> / <c>to</c>: giới hạn khoảng thời gian replay (UTC).
    ///
    /// Cách hoạt động:
    /// - <b>Bất đồng bộ:</b> nhận yêu cầu → trả ngay <b>202 Accepted</b>, việc re-ingestion chạy nền (ghi meta-audit <c>AuditReplayed</c>).
    /// - Phạm vi capstone: endpoint ghi nhận yêu cầu; phần re-publish per-service hoàn thiện khi onboard từng service (Phase 3-5).
    ///
    /// Use case:
    /// - Read-store bị hỏng/xoá nhầm → dựng lại từ source.
    /// - Bổ sung dữ liệu thiếu sau sự cố broker/consumer.
    /// </remarks>
    /// <param name="command">Phạm vi replay: <c>service</c> (null = tất cả), <c>from</c>, <c>to</c> (UTC, optional).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Xác nhận đã nhận yêu cầu replay.</returns>
    /// <response code="202">Đã nhận yêu cầu replay — xử lý bất đồng bộ (re-ingestion chạy nền).</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpPost("replay")]
    [ProducesResponseType(typeof(CommonResponse<object>), 202)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> Replay([FromQuery] AuditReplayCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// GDPR — ẩn danh (REDACT) thông tin cá nhân (PII) của 1 account trong read-store, KHÔNG xoá dòng (giữ toàn vẹn audit).
    /// </summary>
    /// <remarks>
    /// Thực thi "quyền được lãng quên" (GDPR right-to-be-forgotten): thay PII (tên hiển thị, IP...) của 1 account thành
    /// <c>[REDACTED]</c> trong <c>audit_aggregate</c>.
    ///
    /// Nguyên tắc bảo toàn audit:
    /// - <b>KHÔNG xoá dòng</b> — giữ <c>event_id</c> + <c>action_code</c> + timestamp để bảo toàn tính toàn vẹn / chuỗi audit.
    /// - Bảng source-of-truth ở từng service <b>KHÔNG bị redact</b> (legal hold cho regulator).
    /// - Hành động này tự ghi meta-audit <c>AccountDataRedacted</c> (severity <c>Security</c>).
    ///
    /// Quyền truy cập:
    /// - Chỉ role <c>Admin</c>. Thiếu token → 401; sai role → 403.
    ///
    /// Query parameter:
    /// - <c>accountId</c>: account cần ẩn danh PII — bắt buộc, khác <c>Guid.Empty</c> (rỗng → 400, lỗi field trong <c>listErrors</c>).
    ///
    /// Cách hoạt động:
    /// - Controller → <see cref="AuditRedactCommand"/> → handler validate <c>accountId</c> → bulk <c>UPDATE</c> các row có
    ///   <c>actor_account_id = accountId OR target_id = accountId</c>, set display/ip = <c>[REDACTED]</c>.
    /// - Trả số dòng bị ảnh hưởng trong <c>message</c>.
    ///
    /// Use case:
    /// - Xử lý yêu cầu GDPR/quyền riêng tư của user.
    /// - Ẩn PII khi tài khoản bị xoá nhưng vẫn cần giữ dấu vết audit.
    /// </remarks>
    /// <param name="accountId">Account cần ẩn danh PII (bắt buộc, khác <c>Guid.Empty</c>).</param>
    /// <param name="ct">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Kết quả redact + số dòng bị ảnh hưởng (trong message).</returns>
    /// <response code="200">Đã redact — số dòng bị ảnh hưởng nằm trong <c>message</c>.</response>
    /// <response code="400">Thiếu/không hợp lệ <c>accountId</c> — chi tiết field trong <c>listErrors</c>.</response>
    /// <response code="401">Chưa đăng nhập / token không hợp lệ / hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpPost("redact")]
    [ProducesResponseType(typeof(CommonResponse<object>), 200)]
    [ProducesResponseType(typeof(CommonResponse<object>), 400)]
    [ProducesResponseType(typeof(CommonResponse<object>), 401)]
    [ProducesResponseType(typeof(CommonResponse<object>), 403)]
    public async Task<IActionResult> Redact([FromQuery] Guid accountId, CancellationToken ct)
    {
        var result = await _mediator.Send(new AuditRedactCommand { AccountId = accountId }, ct);
        return StatusCode(result.StatusCode, result);
    }

    private static string Csv(string? v) =>
        string.IsNullOrEmpty(v) ? "" : v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
}
