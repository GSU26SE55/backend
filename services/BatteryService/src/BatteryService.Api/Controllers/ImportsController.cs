using System.Security.Claims;
using BatteryService.Application.CQRS.Command.Import;
using BatteryService.Application.CQRS.Query.Import;
using BatteryService.Application.DTOs.Import;
using BatteryService.Domain.Enums;
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

    public ImportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Tải file CSV mẫu cho một loại dữ liệu.</summary>
    /// <response code="200">Trả file CSV.</response>
    [HttpGet("templates/{entityType}")]
    [Authorize(Roles = "Admin")]
    // KHÔNG khai [Produces("text/csv")]: thuộc tính đó khoá luôn kiểu nội dung của nhánh lỗi, nên
    // một entityType không hợp lệ sẽ nhận 406 rỗng thay vì 400 kèm lý do — client không biết vì sao.
    // Nhánh thành công trả FileContentResult vốn tự đặt kiểu nội dung nên không cần thuộc tính này.
    public async Task<IActionResult> GetTemplate(ImportEntityTypeEnum entityType, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetImportTemplateQuery { EntityType = entityType }, cancellationToken);
        if (!result.IsSuccess || result.Data is null)
            return StatusCode(result.StatusCode, result);

        return File(result.Data.Content, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>Gửi file lên để đọc và kiểm định. Không ghi dữ liệu nghiệp vụ nào.</summary>
    /// <remarks>
    /// Gửi dạng multipart. Ba phần đều tuỳ chọn nhưng phải có ít nhất một:
    /// <c>customersFile</c>, <c>sitesFile</c>, <c>assetsFile</c>.
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
    public async Task<IActionResult> CreateBatch(
        IFormFile? customersFile,
        IFormFile? sitesFile,
        IFormFile? assetsFile,
        CancellationToken cancellationToken)
    {
        var command = new CreateImportBatchCommand
        {
            CustomersCsv = await ReadAsync(customersFile, cancellationToken),
            SitesCsv = await ReadAsync(sitesFile, cancellationToken),
            AssetsCsv = await ReadAsync(assetsFile, cancellationToken),
            FileName = BuildFileName(customersFile, sitesFile, assetsFile),
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

    private static string? BuildFileName(params IFormFile?[] files)
    {
        var names = files
            .Where(file => file is { Length: > 0 })
            .Select(file => file!.FileName)
            .ToList();

        if (names.Count == 0)
            return null;

        var joined = string.Join(", ", names);
        return joined.Length <= 255 ? joined : joined[..255];
    }
}
