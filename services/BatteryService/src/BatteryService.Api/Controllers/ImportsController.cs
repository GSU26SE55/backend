using System.Security.Claims;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Query.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Application.Import;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers;

/// <summary>
/// Nhập dữ liệu khách hàng và thiết bị do bên thứ ba bàn giao.
/// </summary>
/// <remarks>
/// <para>Luồng chuẩn gồm bốn bước:</para>
/// <list type="number">
///   <item><description>Tải file mẫu về, điền dữ liệu.</description></item>
///   <item><description>Gửi file lên — hệ thống đọc và kiểm định, <b>chưa ghi gì</b>.</description></item>
///   <item><description>Xem kết quả, tải danh sách dòng hỏng về sửa rồi gửi lại nếu cần.</description></item>
///   <item><description>Bấm ghi thật; theo dõi tiến độ; hoàn tác nếu nhầm.</description></item>
/// </list>
/// <para>
/// Phân quyền: chỉ Admin. Tiếp nhận dữ liệu bàn giao từ bên thứ ba là việc quản trị hệ thống —
/// nó tạo ra tài khoản khách hàng ở AuthService và có thể gỡ hàng loạt bản ghi khi hoàn tác, nên
/// không mở cho Manager.
/// </para>
/// </remarks>
[ApiController]
[Route("api/imports")]
[Produces("application/json")]
[Authorize]
public class ImportsController : ControllerBase
{
    /// <summary>
    /// Hạn kích thước cho mỗi lần gửi. Đủ cho vài chục nghìn dòng CSV, và đủ nhỏ để đọc thẳng vào
    /// bộ nhớ mà không cần ghi tạm ra đĩa.
    /// </summary>
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    private readonly IMediator _mediator;
    private readonly IImportWorkbookSplitter _workbookSplitter;

    public ImportsController(IMediator mediator, IImportWorkbookSplitter workbookSplitter)
    {
        _mediator = mediator;
        _workbookSplitter = workbookSplitter;
    }

    /// <summary>Tải file Excel mẫu — một file .xlsx, ba sheet: khách hàng, site, pin.</summary>
    /// <response code="200">Trả file .xlsx.</response>
    [HttpGet("templates")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetTemplate(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetImportTemplateQuery(), cancellationToken);
        if (!result.IsSuccess || result.Data is null)
            return StatusCode(result.StatusCode, result);

        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>Gửi file lên để đọc và kiểm định. Không ghi dữ liệu nghiệp vụ nào.</summary>
    /// <remarks>
    /// Gửi dạng multipart, một phần duy nhất tên <c>file</c> — một workbook .xlsx ba sheet
    /// (<c>1-Customers</c>, <c>2-Sites</c>, <c>3-Assets</c>, đúng tên và thứ tự trong file mẫu tải
    /// về). Một sheet không có dòng dữ liệu nào (ngoài dòng chú thích) coi như không tham gia lô này
    /// — giống hệt việc trước đây không đính kèm file cho loại đó.
    ///
    /// Thiết bị IoT KHÔNG nhập được qua đây: thiết bị do hệ thống cấp phát cùng khoá API và
    /// credential MQTT, nên chúng chỉ được tạo ở màn quản trị thiết bị.
    /// </remarks>
    /// <response code="201">Đã kiểm định xong; xem bộ đếm trong kết quả.</response>
    /// <response code="409">Đúng nội dung này đã được nạp trước đó.</response>
    /// <response code="422">File hỏng hoặc thiếu cột bắt buộc.</response>
    [HttpPost("batches")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(MaxUploadBytes)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> CreateBatch(IFormFile? file, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(file, cancellationToken);

        var splitResult = new ImportWorkbookSplitResult(null, null, null);
        if (bytes is not null)
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                splitResult = _workbookSplitter.Split(stream);
            }
            catch (Exception ex)
            {
                // Hỏng ở mức file (không phải .xlsx thật, hoặc bị hỏng khi tải lên) — không có lô
                // nào để dựng, nên trả lỗi thẳng ở đây thay vì đi qua CreateImportBatchCommand.
                return StatusCode(422, new CommonResponse<ImportBatchDto>
                {
                    IsSuccess = false,
                    StatusCode = 422,
                    Message = $"The file could not be read as an Excel workbook: {ex.Message}"
                });
            }
        }

        var command = new CreateImportBatchCommand
        {
            CustomersCsv = splitResult.CustomersCsv,
            SitesCsv = splitResult.SitesCsv,
            AssetsCsv = splitResult.AssetsCsv,
            FileName = file is { Length: > 0 } ? file.FileName : null,
            DryRun = true,
            RequestedBy = ResolveAccountId()
        };

        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Danh sách lô, mới nhất trước.</summary>
    [HttpGet("batches")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetBatches([FromQuery] GetImportBatchListQuery query, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Chi tiết một lô, kèm bộ đếm để vẽ tiến độ.</summary>
    [HttpGet("batches/{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetImportBatchByIdQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sửa trực tiếp giá trị một hoặc nhiều dòng rồi kiểm định lại cả lô — thay cho việc phải tải cả
    /// file .xlsx lên lại chỉ để sửa vài ô sai. Chỉ dùng được khi lô đang ở trạng thái ReadyToCommit.
    /// </summary>
    /// <response code="200">Đã kiểm định lại; xem bộ đếm mới trong kết quả.</response>
    /// <response code="409">Lô không ở trạng thái ReadyToCommit.</response>
    [HttpPut("batches/{id:guid}/rows")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateRows(
        Guid id, [FromBody] UpdateImportRowsCommand command, CancellationToken cancellationToken)
    {
        command.BatchId = id;
        var result = await _mediator.Send(command, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Các dòng của một lô; lọc được theo trạng thái và loại dữ liệu.</summary>
    [HttpGet("batches/{id:guid}/rows")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetRows(
        Guid id, [FromQuery] GetImportRowsQuery query, CancellationToken cancellationToken)
    {
        query.BatchId = id;
        var result = await _mediator.Send(query, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Tải danh sách dòng hỏng về dạng CSV, kèm cột lý do.</summary>
    [HttpGet("batches/{id:guid}/errors.csv")]
    [Authorize(Roles = "Admin")]
    // Cùng lý do như GetTemplate: giữ được nhánh lỗi trả JSON đọc hiểu được.
    public async Task<IActionResult> GetErrorsCsv(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetImportErrorsCsvQuery { BatchId = id }, cancellationToken);
        if (!result.IsSuccess || result.Data is null)
            return StatusCode(result.StatusCode, result);

        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>Chuyển lô sang ghi thật. Trả về ngay; tiến trình nền làm phần còn lại.</summary>
    /// <response code="202">Đã nhận; theo dõi tiến độ qua endpoint chi tiết lô.</response>
    [HttpPost("batches/{id:guid}/commit")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Commit(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new CommitImportBatchCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>Gỡ bỏ những gì lô đã tạo. Không đụng tới tài khoản khách hàng.</summary>
    [HttpPost("batches/{id:guid}/revert")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Revert(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RevertImportBatchCommand { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    private Guid? ResolveAccountId()
    {
        var raw = User.FindFirstValue("AccountId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static async Task<byte[]?> ReadAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return null;

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
