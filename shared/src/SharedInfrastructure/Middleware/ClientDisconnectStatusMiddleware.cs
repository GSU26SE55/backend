using Microsoft.AspNetCore.Http;

namespace SharedInfrastructure.Middleware;

/// <summary>
/// GH-800 — client tự đóng kết nối không phải lỗi máy chủ.
/// </summary>
/// <remarks>
/// <para>
/// Luồng SSE (telemetry cảm biến) sống lâu và client đóng lại là chuyện bình thường. Khi đó YARP
/// đánh trạng thái downstream thành <c>502</c>, và cả log lẫn Prometheus đều ghi nhận một lỗi 5xx
/// dù dữ liệu đã truyền xong. Hậu quả không nằm ở con số cho đẹp: tỉ lệ lỗi giả làm SLO sai và
/// che mất những cú 502 THẬT.
/// </para>
/// <para>
/// <c>499</c> là quy ước của nginx cho "client closed request" — không phải mã HTTP chuẩn, và
/// KHÔNG bao giờ được gửi ra ngoài (chỉ đặt khi phản hồi chưa bắt đầu, tức chưa có byte nào tới
/// client). Nó chỉ tồn tại để phân loại trong log và metric.
/// </para>
/// <para>
/// Đặt middleware này <b>bên trong</b> <c>UseHttpMetrics()</c>: bộ đếm
/// <c>http_requests_received_total</c> đọc <c>Response.StatusCode</c> sau khi phần còn lại của
/// pipeline chạy xong, nên phải sửa trạng thái trước lúc đó thì dashboard mới hết 5xx giả.
/// </para>
/// </remarks>
public class ClientDisconnectStatusMiddleware
{
    /// <summary>Quy ước nginx: client đóng kết nối trước khi máy chủ trả lời xong.</summary>
    public const int ClientClosedRequest = 499;

    private readonly RequestDelegate _next;

    public ClientDisconnectStatusMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        finally
        {
            if (ShouldRewrite(
                    context.RequestAborted.IsCancellationRequested,
                    context.Response.StatusCode,
                    context.Response.HasStarted))
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
        }
    }

    /// <summary>
    /// Có nên đổi trạng thái sang <see cref="ClientClosedRequest"/> không.
    /// </summary>
    /// <remarks>
    /// Tách thành hàm thuần để kiểm được từng nhánh mà không phải dựng cả pipeline HTTP.
    /// <list type="bullet">
    ///   <item>Client KHÔNG huỷ ⇒ giữ nguyên. Đây là điều kiện giữ lại những cú 502 thật: upstream
    ///   chết thì client vẫn đang chờ, không có huỷ nào cả.</item>
    ///   <item>Chỉ đổi khi trạng thái là 5xx: client huỷ giữa chừng một phản hồi 200 thì 200 vẫn là
    ///   mô tả đúng — dữ liệu đã truyền thật.</item>
    ///   <item>Phản hồi đã bắt đầu thì KHÔNG đổi được (ASP.NET ném lỗi), và cũng không nên: byte đã
    ///   ra khỏi máy chủ với mã trạng thái cũ rồi.</item>
    /// </list>
    /// </remarks>
    public static bool ShouldRewrite(bool clientAborted, int statusCode, bool responseHasStarted)
        => clientAborted && statusCode >= 500 && !responseHasStarted;
}
