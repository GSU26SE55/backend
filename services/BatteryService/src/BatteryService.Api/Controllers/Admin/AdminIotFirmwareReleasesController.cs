using System.Security.Cryptography;
using BatteryService.Application.CQRS.Command.IotFirmware;
using BatteryService.Application.CQRS.Query.IotFirmware;
using BatteryService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SharedContracts.Common.Responses;

namespace BatteryService.Api.Controllers.Admin;

/// <summary>
/// Module admin quản lý <b>IotFirmwareRelease</b> — Sprint IoT-1 (#244).
/// Upload bản firmware mới, publish để rollout, archive khi rollback.
/// Toàn bộ endpoint chỉ dành cho Admin.
/// </summary>
/// <remarks>
/// Luồng OTA tổng quan:
/// <list type="number">
///   <item><description>Admin upload artifact (file .bin) lên FileStorageService → nhận URL.</description></item>
///   <item><description>Admin gọi <c>POST</c> ở đây với URL + SHA-256 + version + hardware revision. Có thể đặt <c>PublishImmediately = true</c> để publish ngay.</description></item>
///   <item><description>Admin gắn <c>TargetFirmwareReleaseId</c> vào <c>IotDevice</c> qua <c>PUT /api/admin/iot-devices/{id}</c>.</description></item>
///   <item><description>Device gọi <c>GET /api/iot-devices/firmware-check?currentVersion=...</c> → nếu khác target sẽ nhận artifact URL + checksum để download.</description></item>
///   <item><description>Device flash xong → <c>PUT /api/iot-devices/firmware-update-log/{id}</c> với <c>Status = Success/Failed</c>.</description></item>
/// </list>
///
/// Khác biệt giữa các status:
/// <list type="bullet">
///   <item><description><c>IsPublished = false</c>: bản draft, KHÔNG thể đặt làm target. Có thể edit (Sprint IoT-2) hoặc xóa.</description></item>
///   <item><description><c>IsPublished = true</c>, <c>IsArchived = false</c>: đang rollout, dùng làm target được.</description></item>
///   <item><description><c>IsArchived = true</c>: đã rollback / EOL. Device đã flash bản này vẫn chạy, nhưng KHÔNG được set làm target mới.</description></item>
/// </list>
/// </remarks>
[ApiController]
[Route("api/admin/iot-firmware-releases")]
[Produces("application/json")]
[ApiExplorerSettings(GroupName = "admin")]
[Authorize(Roles = "Admin")]
public class AdminIotFirmwareReleasesController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminIotFirmwareReleasesController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Liệt kê firmware releases (filter theo HardwareRevision + PublishedOnly) + pagination — UI dropdown 'chọn firmware để đặt làm target' lọc theo device hardware revision.
    /// </summary>
    /// <remarks>
    /// Query parameters:
    /// <list type="bullet">
    ///   <item><description><c>HardwareRevision</c>: tùy chọn, filter theo hardware version (vd <c>"v1.0-S3-MAX485"</c>).</description></item>
    ///   <item><description><c>PublishedOnly</c>: tùy chọn, <c>true</c> → chỉ trả các bản <c>IsPublished &amp;&amp; !IsArchived</c> (= các bản đang rollout).</description></item>
    ///   <item><description><c>Page</c>, <c>PageSize</c>: pagination chuẩn, clamp [1, 100].</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Filter <c>!IsDeleted</c> luôn áp dụng.</description></item>
    ///   <item><description>Sort theo <c>CreatedAt DESC</c> (mới nhất trên cùng).</description></item>
    ///   <item><description>Trả <see cref="PaginationResponse{T}"/> chứa <see cref="IotFirmwareReleaseDto"/>.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Endpoint <b>không</b> trả binary của artifact — chỉ URL. Device download trực tiếp qua HTTPS.</description></item>
    ///   <item><description>Khi UI cần dropdown "chọn firmware để đặt làm target", luôn pass <c>PublishedOnly = true</c> + filter <c>HardwareRevision</c> khớp device.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="query">Filter + pagination.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="PaginationResponse{T}"/> các <see cref="IotFirmwareReleaseDto"/>.</returns>
    /// <response code="200">Trả danh sách.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    [HttpGet]
    [ProducesResponseType(typeof(CommonResponse<PaginationResponse<IotFirmwareReleaseDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] GetIotFirmwareReleasesQuery query, CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo firmware release mới (upload metadata sau khi đã đẩy artifact lên FileStorage).
    /// </summary>
    /// <remarks>
    /// Body request:
    /// <list type="bullet">
    ///   <item><description><c>Version</c>: bắt buộc, SemVer <c>X.Y.Z</c> (vd <c>"1.2.3"</c>).</description></item>
    ///   <item><description><c>HardwareRevision</c>: bắt buộc, vd <c>"v1.0-S3-MAX485"</c>. <b>Phải khớp</b> với <c>IotDevice.HardwareRevision</c> để check được.</description></item>
    ///   <item><description><c>ArtifactUrl</c>: bắt buộc, absolute URL của file <c>.bin</c>. Khuyến nghị URL của FileStorageService (đã presign).</description></item>
    ///   <item><description><c>Sha256Checksum</c>: bắt buộc, 64 hex chars. Device dùng để verify download.</description></item>
    ///   <item><description><c>ArtifactSizeBytes</c>: bắt buộc, &gt; 0 và ≤ 50MB (giới hạn ESP32 partition).</description></item>
    ///   <item><description><c>ReleaseNotes</c>: tùy chọn, markdown changelog.</description></item>
    ///   <item><description><c>PublishImmediately</c>: tùy chọn, default <c>false</c>. <c>true</c> = publish ngay sau khi tạo (skip bước publish riêng).</description></item>
    /// </list>
    ///
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Validate field (400) → check trùng <c>(Version, HardwareRevision)</c> (409).</description></item>
    ///   <item><description>Persist với <c>IsPublished</c> theo flag, <c>PublishedAt</c> = <c>UtcNow</c> nếu publish ngay.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Index unique <c>(version, hardware_revision)</c> ở DB chặn trùng — chỉ cho 1 bản 1.2.3 trên cùng hardware.</description></item>
    ///   <item><description><c>ArtifactUrl</c> nên có presigned expiry &gt; 24h để device có queue nội bộ vẫn flash kịp.</description></item>
    ///   <item><description><b>Endpoint KHÔNG verify checksum file thực sự</b> — admin tự tính bằng <c>sha256sum file.bin</c> rồi paste. Device verify sau khi download.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="cmd">Metadata firmware.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotFirmwareReleaseDto"/> vừa tạo.</returns>
    /// <response code="201">Tạo thành công.</response>
    /// <response code="400">Dữ liệu không hợp lệ (xem <c>ListErrors</c>).</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="409"><c>(Version, HardwareRevision)</c> đã tồn tại.</response>
    [HttpPost]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateIotFirmwareReleaseCommand cmd, CancellationToken ct)
    {
        var result = await _mediator.Send(cmd, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Publish 1 firmware release để cho phép đặt làm target OTA.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm release; 404 nếu không có.</description></item>
    ///   <item><description>Nếu đã <c>IsArchived = true</c> → 409 (không publish lại bản đã rollback).</description></item>
    ///   <item><description>Set <c>IsPublished = true</c>. Nếu <c>PublishedAt</c> chưa có thì set <c>UtcNow</c> (giữ nguyên nếu đã publish trước đó).</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Publish KHÔNG tự gán device — admin vẫn phải <c>PUT /api/admin/iot-devices/{id}</c> với <c>TargetFirmwareReleaseId</c> để rollout.</description></item>
    ///   <item><description>Khuyến nghị rollout từng nhóm device nhỏ (canary) — Sprint IoT-2 sẽ thêm <c>rollout policy</c> theo % hoặc cohort.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id firmware release.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> chứa <see cref="IotFirmwareReleaseDto"/> sau publish.</returns>
    /// <response code="200">Publish thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy release.</response>
    /// <response code="409">Release đã <c>IsArchived</c>.</response>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<IotFirmwareReleaseDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new PublishIotFirmwareReleaseCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Archive 1 release — đánh dấu rollback / EOL, không cho đặt làm target nữa.
    /// </summary>
    /// <remarks>
    /// Cách hoạt động:
    /// <list type="bullet">
    ///   <item><description>Tìm release; 404 nếu không có.</description></item>
    ///   <item><description>Set <c>IsArchived = true</c>. <c>IsPublished</c> giữ nguyên để audit.</description></item>
    /// </list>
    ///
    /// Use case:
    /// <list type="bullet">
    ///   <item><description>Phát hiện bug trong bản đã publish → archive để chặn rollout thêm.</description></item>
    ///   <item><description>Đã có bản mới ổn định hơn, đánh dấu bản cũ EOL.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>Device đã flash bản này vẫn chạy bình thường — archive chỉ ảnh hưởng pipeline OTA tương lai.</description></item>
    ///   <item><description>Nếu device đang có <c>TargetFirmwareReleaseId</c> = release bị archive, lần <c>firmware-check</c> tiếp sẽ trả <c>updateAvailable = false</c> (do filter <c>!IsArchived</c>).</description></item>
    ///   <item><description>Để un-archive: Sprint IoT-2 sẽ có endpoint riêng — hiện tại phải sửa DB tay.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">Id firmware release.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <returns><see cref="CommonResponse{T}"/> thông báo archive thành công.</returns>
    /// <response code="200">Archive thành công.</response>
    /// <response code="401">Chưa đăng nhập / token hết hạn.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="404">Không tìm thấy release.</response>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new ArchiveIotFirmwareReleaseCommand { Id = id }, ct);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Sprint IoT-2 #IoT2-35 (S7-BE-04) — upload firmware binary (.bin) qua multipart form-data.
    /// </summary>
    /// <remarks>
    /// Backend xử lý 3 việc:
    /// <list type="number">
    ///   <item><description>Stream file vào storage volume (path config qua <c>Firmware:StorageRoot</c>, default <c>ContentRoot/firmware-storage</c>).</description></item>
    ///   <item><description>Tính SHA-256 in-stream (không buffer toàn file) để verify integrity.</description></item>
    ///   <item><description>Sanitize filename: <c>{hwRev}_{version}_{guid:N}.bin</c> — tránh path traversal.</description></item>
    /// </list>
    ///
    /// Body multipart fields:
    /// <list type="bullet">
    ///   <item><description><c>File</c>: file <c>.bin</c> (bắt buộc, ≤ 60MB).</description></item>
    ///   <item><description><c>Version</c>: SemVer X.Y.Z.</description></item>
    ///   <item><description><c>HardwareRevision</c>: hardware tương thích (vd <c>"v1.0-S3-MAX485"</c>).</description></item>
    ///   <item><description><c>IsRequired</c>: force update — device bắt buộc apply (default false).</description></item>
    ///   <item><description><c>Channel</c>: <c>Stable</c> hoặc <c>Beta</c> (default Stable).</description></item>
    ///   <item><description><c>ReleaseNotes</c>: markdown changelog (tùy chọn).</description></item>
    ///   <item><description><c>DeviceModel</c>: model device (vd <c>"ESP32-S3-WROOM-1"</c>, tùy chọn).</description></item>
    /// </list>
    ///
    /// Response 201 trả <see cref="FirmwareBinaryUploadDto"/> gồm <c>ArtifactUrl</c> + <c>Sha256Checksum</c> + <c>ArtifactSizeBytes</c>.
    /// Admin paste 3 giá trị này vào <c>POST /api/admin/iot-firmware-releases</c> để tạo metadata release chính thức.
    ///
    /// Storage backend:
    /// <list type="bullet">
    ///   <item><description><b>Dev/Staging</b>: local PVC mount <c>/data/firmware-storage</c> + serve static qua <c>/firmware-storage/{file}</c>.</description></item>
    ///   <item><description><b>Production</b>: set <c>Firmware:PublicBaseUrl</c> tới CDN/object storage URL.</description></item>
    /// </list>
    ///
    /// Lưu ý:
    /// <list type="bullet">
    ///   <item><description>RequestSizeLimit = 60MB (ESP32 partition tối đa 50MB + slack header).</description></item>
    ///   <item><description>File quá lớn → 413 Request Entity Too Large (ASP.NET default response).</description></item>
    ///   <item><description>SHA-256 tính ở backend — admin KHÔNG cần tự hash bằng <c>sha256sum</c>.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="form">Multipart form binding.</param>
    /// <param name="configuration">Config DI để lấy Firmware:StorageRoot + PublicBaseUrl.</param>
    /// <param name="env">Web host environment DI cho fallback path.</param>
    /// <param name="ct">Token hủy request.</param>
    /// <response code="201">Upload thành công, trả URL + SHA-256 + size.</response>
    /// <response code="400">File missing, extension ≠ .bin, hoặc field validation lỗi.</response>
    /// <response code="401">Chưa đăng nhập.</response>
    /// <response code="403">Không có role Admin.</response>
    /// <response code="413">File &gt; 60MB (ASP.NET response).</response>
    [HttpPost("upload-binary")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(60 * 1024 * 1024)] // 60MB cap (ESP32 partition ≤ 50MB + slack).
    [ProducesResponseType(typeof(CommonResponse<FirmwareBinaryUploadDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadBinary(
        [FromForm] FirmwareBinaryUploadForm form,
        [FromServices] IConfiguration configuration,
        [FromServices] IWebHostEnvironment env,
        CancellationToken ct)
    {
        if (form?.File is null || form.File.Length == 0)
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Firmware file is required.",
                ListErrors = new() { new() { Field = "File", Detail = "You must upload a .bin file." } }
            });
        }

        var extension = Path.GetExtension(form.File.FileName);
        if (!string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new CommonResponse<object>
            {
                IsSuccess = false,
                StatusCode = 400,
                Message = "Only .bin files are accepted.",
                ListErrors = new() { new() { Field = "File", Detail = "Extension must be .bin" } }
            });
        }

        // Lấy root path từ config; fallback: ContentRoot/firmware-storage.
        var root = configuration["Firmware:StorageRoot"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(env.ContentRootPath, "firmware-storage");
        Directory.CreateDirectory(root);

        // Tên file an toàn: {hwRev}_{version}_{guid}.bin (sanitize input).
        var safeHw = SanitizeSegment(form.HardwareRevision);
        var safeVer = SanitizeSegment(form.Version);
        var fileName = $"{safeHw}_{safeVer}_{Guid.NewGuid():N}.bin";
        var fullPath = Path.Combine(root, fileName);

        long size;
        string sha256;
        await using (var output = System.IO.File.Create(fullPath))
        await using (var input = form.File.OpenReadStream())
        {
            using var hasher = SHA256.Create();
            var buffer = new byte[81920];
            int read;
            long total = 0;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                hasher.TransformBlock(buffer, 0, read, null, 0);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                total += read;
            }
            hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            size = total;
            sha256 = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
        }

        // URL public: dùng PublicBaseUrl nếu set, không thì tương đối /firmware-storage/{file}.
        var publicBase = configuration["Firmware:PublicBaseUrl"];
        var artifactUrl = !string.IsNullOrWhiteSpace(publicBase)
            ? $"{publicBase.TrimEnd('/')}/{fileName}"
            : $"/firmware-storage/{fileName}";

        return StatusCode(201, new CommonResponse<FirmwareBinaryUploadDto>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "Firmware binary uploaded successfully. Use the URL + SHA-256 to create a release.",
            Data = new FirmwareBinaryUploadDto
            {
                ArtifactUrl = artifactUrl,
                Sha256Checksum = sha256,
                ArtifactSizeBytes = size,
                FileName = fileName,
                Version = form.Version,
                HardwareRevision = form.HardwareRevision,
                IsRequired = form.IsRequired,
                Channel = form.Channel,
                ReleaseNotes = form.ReleaseNotes,
                DeviceModel = form.DeviceModel
            }
        });
    }

    private static string SanitizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var span = value.Where(c => !invalid.Contains(c) && c != ' ' && c != '/').ToArray();
        return new string(span);
    }
}

/// <summary>Sprint IoT-2 #IoT2-35 — multipart form binding.</summary>
public class FirmwareBinaryUploadForm
{
    public IFormFile? File { get; set; }
    public string Version { get; set; } = string.Empty;
    public string HardwareRevision { get; set; } = string.Empty;

    // Spec fields per §52.7.
    public bool IsRequired { get; set; }
    public BatteryService.Domain.Enums.IotFirmwareChannelEnum Channel { get; set; } = BatteryService.Domain.Enums.IotFirmwareChannelEnum.Stable;
    public string? ReleaseNotes { get; set; }
    public string? DeviceModel { get; set; }
}
