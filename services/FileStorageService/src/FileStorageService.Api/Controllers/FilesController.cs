using FileStorageService.Api.Requests;
using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SharedContracts.Common.Responses;

namespace FileStorageService.Api.Controllers;

/// <summary>
/// Nhóm API quản lý file trong object storage.
/// Service này chịu trách nhiệm upload file, lưu metadata, tải file trực tiếp, tạo presigned URL và xóa file.
/// </summary>
/// <remarks>
/// FileStorageService hiện hỗ trợ hai cách truy cập:
/// - Các endpoint cũ dùng <c>objectKey</c> để tương thích với flow hiện tại.
/// - Các endpoint mới dùng <c>fileId</c> để các service khác chỉ cần lưu id metadata, không phải phụ thuộc trực tiếp vào đường dẫn object storage.
///
/// Flow khuyến nghị cho các service nghiệp vụ:
/// - Upload binary lên endpoint <c>POST /api/files/upload</c>.
/// - Lưu <c>fileId</c> trả về trong domain service, ví dụ <c>AccountProfile.AvatarFileId</c>.
/// - Khi cần hiển thị/tải file, gọi endpoint theo <c>fileId</c> như metadata, presigned-url hoặc download.
///
/// Service này không xử lý resize ảnh, strip EXIF, virus scan hoặc lifecycle archival ở Sprint 1.
/// Các trạng thái như Processing/Quarantined được chuẩn bị cho pipeline xử lý file ở các sprint sau.
/// </remarks>
[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FilesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Upload 1 file lên object storage (MinIO) — accept multipart form-data, generate UUID filename + lưu metadata DB; trả về fileId + presigned download URL.
    /// </summary>
    /// <remarks>
    /// Endpoint này nhận request dạng <c>multipart/form-data</c> gồm file cần lưu, tên thư mục logic và mục đích sử dụng file.
    ///
    /// Cách hoạt động:
    /// - Hệ thống kiểm tra request có file hay không.
    /// - File được kiểm tra theo purpose, gồm dung lượng tối đa 20 MB và danh sách phần mở rộng được phép.
    /// - Nếu hợp lệ, file được upload vào bucket đang cấu hình.
    /// - Tên file trong storage không giữ nguyên tên gốc; hệ thống tạo một tên mới bằng GUID để tránh trùng file.
    /// - Sau khi upload binary thành công, hệ thống tạo record <c>UploadedFile</c> trong database metadata.
    /// - Response trả về cả <c>fileId</c> và <c>objectKey</c>. Các service mới nên ưu tiên lưu <c>fileId</c>.
    /// - Nếu lưu metadata thất bại sau khi upload binary thành công, handler sẽ cố gắng xóa object vừa upload để tránh file mồ côi.
    ///
    /// Form-data:
    /// - <c>file</c>: file cần upload. Đây là field bắt buộc.
    /// - <c>folderName</c>: thư mục logic để nhóm file, ví dụ <c>avatars</c>, <c>reports</c>, <c>warranty-documents</c>. Nếu bỏ trống thì dùng <c>default</c>.
    /// - <c>purpose</c>: mục đích sử dụng file, ví dụ <c>Avatar</c>, <c>TicketAttachment</c>, <c>MaintenancePhoto</c>, <c>KbImage</c>, <c>Firmware</c> hoặc <c>Other</c>.
    ///
    /// Kết quả thành công trả về HTTP 201 kèm thông tin file đã lưu:
    /// - <c>fileId</c>: id metadata ổn định để các service khác tham chiếu.
    /// - <c>objectKey</c>: khóa định danh file trong object storage, vẫn trả về để tương thích với endpoint cũ.
    /// - <c>fileName</c>: tên file gốc do client gửi lên.
    /// - <c>contentType</c>: MIME type của file.
    /// - <c>size</c>: kích thước file theo byte.
    /// - <c>publicUrl</c>: URL public nếu hệ thống có cấu hình PublicBaseUrl; ngược lại có thể là null.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu không gửi file, file rỗng, thiếu phần mở rộng hoặc phần mở rộng không được cho phép theo purpose.
    /// - HTTP 403 nếu role hiện tại không được upload purpose yêu cầu, ví dụ Firmware không phải Admin.
    /// - HTTP 413 nếu file vượt quá 20 MB.
    /// </remarks>
    /// <param name="request">Thông tin upload file được gửi bằng form-data.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>Thông tin file sau khi upload thành công, bao gồm <c>fileId</c> và <c>objectKey</c>.</returns>
    /// <response code="201">Upload file thành công và trả về thông tin file đã lưu.</response>
    /// <response code="400">Request không hợp lệ, ví dụ thiếu file hoặc dữ liệu upload không đạt điều kiện validation.</response>
    /// <response code="403">Không đủ quyền upload với purpose yêu cầu.</response>
    /// <response code="413">File vượt quá giới hạn 20 MB.</response>
    /// <response code="500">Có lỗi khi ghi file vào object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(21 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new UploadFileCommand
        {
            File = request.File,
            FolderName = request.FolderName,
            Purpose = request.Purpose
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Lấy metadata của file theo fileId (filename gốc, size, content-type, uploader, uploadedAt) — KHÔNG trả content. Dùng cho UI hiển thị info trước khi download.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi client hoặc service khác cần biết thông tin mô tả file mà không tải binary.
    /// Đây là endpoint đọc bảng <c>uploaded_files</c>, không gọi object storage để lấy nội dung file.
    ///
    /// Path parameter:
    /// - <c>id</c>: <c>fileId</c> nhận được từ response upload.
    ///
    /// Dữ liệu metadata trả về gồm:
    /// - <c>fileId</c>, <c>objectKey</c>, <c>fileName</c>.
    /// - <c>contentType</c>, <c>size</c>, <c>folderName</c>.
    /// - <c>purpose</c> và <c>status</c> để service khác biết file dùng cho nghiệp vụ nào và hiện có tải được không.
    /// - <c>publicUrl</c> nếu hệ thống có cấu hình public base URL.
    /// - Audit fields như created/updated nếu DTO đang expose.
    ///
    /// Cách hoạt động:
    /// - Validate <c>fileId</c> khác empty GUID.
    /// - Chỉ trả file chưa bị xóa. File có trạng thái Deleted hoặc đã bị soft-delete sẽ trả 404.
    /// - Enforce quyền truy cập theo owner/role/purpose của file.
    /// - Không trả binary stream, không tạo presigned URL và không thay đổi trạng thái file.
    ///
    /// Use case:
    /// - FE hiển thị tên file, dung lượng và loại file trong màn hình profile/ticket.
    /// - AuthService hoặc TicketService kiểm tra fileId đã có metadata trước khi gắn vào entity nghiệp vụ.
    /// </remarks>
    /// <param name="id">FileId metadata cần đọc.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><c>CommonResponse</c> chứa <see cref="FileMetadataResponse"/> nếu tìm thấy file.</returns>
    /// <response code="200">Lấy metadata file thành công.</response>
    /// <response code="400">FileId không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Không có quyền xem metadata file này.</response>
    /// <response code="404">Không tìm thấy metadata file hoặc file đã bị xóa.</response>
    [HttpGet("{id:guid}/metadata")]
    [ProducesResponseType(typeof(CommonResponse<FileMetadataResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<FileMetadataResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<FileMetadataResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<FileMetadataResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<FileMetadataResponse>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMetadata(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetFileMetadataQuery { Id = id }, cancellationToken);
        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tải nội dung file về trực tiếp bằng <c>objectKey</c>.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi client muốn nhận binary stream của file ngay trong response.
    /// Khác với presigned URL, server sẽ đọc file từ object storage rồi trả stream về cho client.
    ///
    /// Tham số query:
    /// - <c>objectKey</c>: khóa định danh file trong storage, thường lấy từ response của endpoint upload. Ví dụ <c>avatars/4f2c...a9.png</c>.
    ///
    /// Cách hoạt động:
    /// - Hệ thống kiểm tra <c>objectKey</c> không được rỗng.
    /// - <c>objectKey</c> được chuẩn hóa để loại bỏ ký tự slash ở đầu và chặn path traversal như <c>..</c>.
    /// - Service lookup metadata DB, enforce owner/status, rồi mới đọc file từ bucket đang cấu hình.
    /// - Nếu đọc thành công, API trả về nội dung file với <c>Content-Type</c> và tên file phù hợp.
    ///
    /// Khi thành công, response không bọc trong <c>CommonResponse</c> mà trả về file stream trực tiếp.
    /// Client nên xử lý response như một file download, không phải JSON.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu thiếu <c>objectKey</c>.
    /// - HTTP 403 nếu file không thuộc quyền truy cập của account hiện tại.
    /// - HTTP 404 nếu không tìm thấy metadata hoặc file đã bị xóa.
    /// - HTTP 409 nếu file đang Processing hoặc Quarantined.
    /// - HTTP 500 nếu object storage không khả dụng hoặc có lỗi hệ thống.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file trong object storage.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>Binary stream của file nếu tìm thấy; JSON lỗi nếu request không hợp lệ hoặc không tải được file.</returns>
    /// <response code="200">Tải file thành công. Response body là nội dung binary của file.</response>
    /// <response code="400">Thiếu hoặc truyền <c>objectKey</c> không hợp lệ.</response>
    /// <response code="403">Không có quyền tải file này.</response>
    /// <response code="404">Không tìm thấy metadata hoặc file đã bị xóa.</response>
    /// <response code="409">File đang Processing hoặc Quarantined.</response>
    /// <response code="500">Có lỗi khi đọc file từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpGet("download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Download([FromQuery] string objectKey, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DownloadFileQuery { ObjectKey = objectKey }, cancellationToken);
        if (!result.IsSuccess || result.Data is null)
            return StatusCode(result.StatusCode, result);

        return File(result.Data.Stream, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>
    /// Tải nội dung file trực tiếp theo fileId — backend stream từ object storage qua API; phù hợp file nhỏ < 5MB. File lớn hơn dùng GetPresignedUrl.
    /// </summary>
    /// <remarks>
    /// Endpoint này là phiên bản metadata-aware của endpoint download cũ theo <c>objectKey</c>.
    /// Client chỉ cần biết <c>fileId</c>; service sẽ tự đọc metadata để tìm <c>objectKey</c> tương ứng trong object storage.
    ///
    /// Path parameter:
    /// - <c>id</c>: <c>fileId</c> nhận được từ response upload hoặc được lưu trong service nghiệp vụ.
    ///
    /// Cách hoạt động:
    /// - Validate <c>fileId</c> khác empty GUID.
    /// - Tìm record <c>UploadedFile</c> chưa bị xóa.
    /// - Enforce quyền truy cập theo owner/role/purpose.
    /// - Nếu file đang ở trạng thái <c>Processing</c> hoặc <c>Quarantined</c>, trả 409 và không tải binary.
    /// - Nếu hợp lệ, dùng <c>objectKey</c> trong metadata để đọc stream từ object storage.
    /// - Response thành công trả binary stream trực tiếp với content type và file name phù hợp.
    ///
    /// Khi dùng endpoint này:
    /// - FE nên xử lý response như file download hoặc image source, không parse JSON khi status 200.
    /// - Với avatar, AuthService trả <c>displayAvatarUrl</c> dạng <c>/api/files/{fileId}/download</c>.
    ///
    /// Lỗi thường gặp:
    /// - HTTP 400 nếu fileId không hợp lệ.
    /// - HTTP 403 nếu file không thuộc quyền truy cập của account hiện tại.
    /// - HTTP 404 nếu không tìm thấy metadata hoặc file đã bị xóa.
    /// - HTTP 409 nếu file đang xử lý hoặc bị cách ly.
    /// </remarks>
    /// <param name="id">FileId cần tải binary.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>Binary stream của file nếu tải thành công; JSON lỗi nếu request không hợp lệ hoặc file không thể tải.</returns>
    /// <response code="200">Tải file thành công. Response body là nội dung binary của file.</response>
    /// <response code="400">FileId không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Không có quyền tải file này.</response>
    /// <response code="404">Không tìm thấy file.</response>
    /// <response code="409">File đang xử lý hoặc bị cách ly và không thể tải.</response>
    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadById(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DownloadFileByIdQuery { Id = id }, cancellationToken);
        if (!result.IsSuccess || result.Data is null)
            return StatusCode(result.StatusCode, result);

        return File(result.Data.Stream, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>
    /// Tạo presigned URL để client tải file trực tiếp từ object storage trong thời gian giới hạn.
    /// </summary>
    /// <remarks>
    /// Endpoint này phù hợp khi client cần tải file mà không muốn server proxy toàn bộ nội dung file.
    /// API trả về một URL tạm thời đã được ký bởi storage provider. Client dùng URL này để gọi trực tiếp tới object storage.
    ///
    /// Tham số query:
    /// - <c>objectKey</c>: khóa định danh file trong storage, thường lấy từ response upload.
    /// - <c>expiresInMinutes</c>: thời gian hiệu lực của URL tính theo phút. Mặc định là 15 phút.
    ///
    /// Quy tắc validation:
    /// - <c>objectKey</c> là bắt buộc.
    /// - <c>expiresInMinutes</c> phải nằm trong khoảng từ 1 đến 1440 phút.
    ///
    /// Cách hoạt động:
    /// - Hệ thống chuẩn hóa và kiểm tra <c>objectKey</c>.
    /// - Service lookup metadata DB, enforce owner/status, rồi mới cấp URL.
    /// - Tạo URL tạm thời dùng HTTP GET.
    /// - URL hết hạn sau khoảng thời gian đã truyền vào <c>expiresInMinutes</c>.
    ///
    /// Lưu ý bảo mật:
    /// - Bất kỳ ai có URL trong thời gian còn hiệu lực đều có thể tải file.
    /// - Không nên log hoặc chia sẻ presigned URL ở nơi công khai.
    /// - Với file nhạy cảm, nên dùng thời gian hết hạn ngắn.
    /// - Nếu file bị quarantine sau khi URL đã cấp, URL hiện hành vẫn sống đến khi hết hạn.
    ///
    /// Response thành công là <c>CommonResponse&lt;string&gt;</c>, trong đó <c>data</c> là presigned URL.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file trong object storage.</param>
    /// <param name="expiresInMinutes">Thời gian hiệu lực của URL, tính theo phút. Giá trị hợp lệ: 1 đến 1440.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>URL tạm thời để tải file trực tiếp từ object storage.</returns>
    /// <response code="200">Tạo presigned URL thành công.</response>
    /// <response code="400">Thiếu <c>objectKey</c> hoặc <c>expiresInMinutes</c> nằm ngoài khoảng hợp lệ.</response>
    /// <response code="403">Không có quyền tạo presigned URL cho file này.</response>
    /// <response code="404">Không tìm thấy metadata hoặc file đã bị xóa.</response>
    /// <response code="409">File đang Processing hoặc Quarantined.</response>
    /// <response code="500">Có lỗi khi tạo URL từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpGet("presigned-url")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPresignedUrl(
        [FromQuery] string objectKey,
        [FromQuery] int expiresInMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPresignedUrlQuery
        {
            ObjectKey = objectKey,
            ExpiresInMinutes = expiresInMinutes
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Tạo presigned URL để FE/Mobile tải file trực tiếp từ MinIO (bypass backend) — TTL 1h default, dùng cho file lớn để giảm tải backend bandwidth.
    /// </summary>
    /// <remarks>
    /// Endpoint này dùng khi client cần tải file trực tiếp từ object storage nhưng chỉ đang giữ <c>fileId</c>.
    /// Service sẽ resolve <c>fileId</c> sang <c>objectKey</c> rồi tạo URL tạm thời bằng storage provider.
    ///
    /// Path parameter:
    /// - <c>id</c>: <c>fileId</c> cần tạo presigned URL.
    ///
    /// Query string:
    /// - <c>expiresInMinutes</c>: thời gian hiệu lực của URL, tính bằng phút. Mặc định 15 phút, hợp lệ từ 1 đến 1440.
    ///
    /// Cách hoạt động:
    /// - Validate <c>fileId</c> và <c>expiresInMinutes</c>.
    /// - Tìm metadata file chưa bị xóa.
    /// - Enforce quyền truy cập theo owner/role/purpose.
    /// - Nếu file đang ở trạng thái <c>Processing</c> hoặc <c>Quarantined</c>, trả 409 và không cấp URL.
    /// - Tạo URL tạm thời để client gọi trực tiếp object storage bằng HTTP GET.
    ///
    /// Lưu ý bảo mật:
    /// - Presigned URL là bearer URL; ai có URL trong thời gian còn hiệu lực đều có thể tải file.
    /// - Không log URL này ở client hoặc server nếu file nhạy cảm.
    /// - Nếu file bị quarantine sau khi URL đã cấp, URL hiện hành vẫn sống đến khi hết hạn.
    /// - Với avatar hoặc file nhỏ cần kiểm soát auth qua gateway, có thể dùng endpoint download theo fileId thay vì presigned URL.
    /// </remarks>
    /// <param name="id">FileId cần tạo presigned URL.</param>
    /// <param name="expiresInMinutes">Thời gian hiệu lực của URL, tính theo phút. Giá trị hợp lệ: 1 đến 1440.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns><c>CommonResponse</c> chứa URL tạm thời nếu tạo thành công.</returns>
    /// <response code="200">Tạo presigned URL thành công.</response>
    /// <response code="400">FileId hoặc expiresInMinutes không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Không có quyền tạo presigned URL cho file này.</response>
    /// <response code="404">Không tìm thấy file.</response>
    /// <response code="409">File đang xử lý hoặc bị cách ly và không thể tải.</response>
    [HttpGet("{id:guid}/presigned-url")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetPresignedUrlById(
        Guid id,
        [FromQuery] int expiresInMinutes = 15,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetFilePresignedUrlByIdQuery
        {
            Id = id,
            ExpiresInMinutes = expiresInMinutes
        }, cancellationToken);

        return StatusCode(result.StatusCode, result);
    }

    /// <summary>
    /// Xoá file bằng objectKey (raw MinIO path) — endpoint legacy cho migration; production khuyến nghị dùng DeleteById có audit trail.
    /// </summary>
    /// <remarks>
    /// Endpoint legacy này lookup metadata theo objectKey, enforce quyền xóa, xóa object trong bucket và soft-delete metadata.
    /// Client cần truyền đúng <c>objectKey</c> đã nhận được từ endpoint upload hoặc đang lưu trong database nghiệp vụ.
    ///
    /// Tham số query:
    /// - <c>objectKey</c>: khóa định danh file cần xóa. Ví dụ <c>reports/9b1c...ef.pdf</c>.
    ///
    /// Cách hoạt động:
    /// - Hệ thống kiểm tra <c>objectKey</c> không được rỗng.
    /// - <c>objectKey</c> được chuẩn hóa để chặn path traversal.
    /// - Lookup metadata DB và enforce quyền xóa theo owner/role/purpose.
    /// - Gửi lệnh xóa object tới storage provider.
    /// - Đánh dấu metadata là Deleted/soft-delete.
    ///
    /// Kết quả thành công:
    /// - API trả HTTP 204 No Content.
    /// - Response body rỗng theo chuẩn của HTTP 204.
    ///
    /// Lưu ý nghiệp vụ:
    /// - FE/service mới nên dùng endpoint theo fileId thay vì objectKey.
    /// - Nếu service khác đang lưu quan hệ tới file, service đó cần clear reference trước rồi mới gọi FileStorage cleanup.
    /// - Sau khi xóa, các <c>objectKey</c> hoặc presigned URL cũ không còn dùng để tải file được nữa.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu thiếu <c>objectKey</c>.
    /// - HTTP 403 nếu không có quyền xóa file này.
    /// - HTTP 404 nếu không tìm thấy metadata hoặc file đã bị xóa.
    /// - HTTP 500 nếu object storage không khả dụng hoặc có lỗi hệ thống.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file cần xóa trong object storage.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>HTTP 204 nếu xóa thành công.</returns>
    /// <response code="204">Xóa file thành công, response body rỗng.</response>
    /// <response code="400">Thiếu hoặc truyền <c>objectKey</c> không hợp lệ.</response>
    /// <response code="403">Không có quyền xóa file này.</response>
    /// <response code="404">Không tìm thấy metadata hoặc file đã bị xóa.</response>
    /// <response code="500">Có lỗi khi xóa file từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete([FromQuery] string objectKey, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteFileCommand { ObjectKey = objectKey }, cancellationToken);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, result);

        return StatusCode(StatusCodes.Status204NoContent);
    }

    /// <summary>
    /// Xoá file theo fileId — xoá metadata DB + xoá object trong MinIO. Soft delete (giữ row metadata với IsDeleted=true) cho audit.
    /// </summary>
    /// <remarks>
    /// Endpoint này là phiên bản metadata-aware của endpoint xóa file cũ theo <c>objectKey</c>.
    /// Client hoặc service nghiệp vụ chỉ cần truyền <c>fileId</c>; FileStorageService tự tìm <c>objectKey</c> trong metadata.
    ///
    /// Path parameter:
    /// - <c>id</c>: <c>fileId</c> cần xóa.
    ///
    /// Cách hoạt động:
    /// - Validate <c>fileId</c> khác empty GUID.
    /// - Tìm record <c>UploadedFile</c> chưa bị xóa.
    /// - Enforce quyền xóa theo owner/role/purpose.
    /// - Gửi lệnh xóa object vật lý trong object storage theo <c>objectKey</c>.
    /// - Đánh dấu metadata file là Deleted/soft-delete để các endpoint metadata, download và presigned-url không trả file này nữa.
    ///
    /// Kết quả thành công:
    /// - API trả HTTP 204 No Content.
    /// - Response body rỗng theo chuẩn HTTP 204.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Endpoint này không tự gỡ tham chiếu ở service khác. Service sở hữu resource phải clear reference trước, rồi gọi FileStorage cleanup.
    /// - Nếu xóa object storage thành công nhưng lưu metadata thất bại, request sẽ fail theo exception hiện tại và cần retry/cleanup thủ công.
    /// - Pipeline quarantine/virus scan chưa nằm trong Sprint 1; trạng thái Deleted là nền cho lifecycle sau này.
    /// </remarks>
    /// <param name="id">FileId cần xóa.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server dừng xử lý.</param>
    /// <returns>HTTP 204 nếu xóa thành công.</returns>
    /// <response code="204">Xóa file thành công, response body rỗng.</response>
    /// <response code="400">FileId không hợp lệ.</response>
    /// <response code="401">Chưa đăng nhập hoặc access token không hợp lệ/hết hạn.</response>
    /// <response code="403">Không có quyền xóa file này.</response>
    /// <response code="404">Không tìm thấy file hoặc file đã bị xóa.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteById(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteFileByIdCommand { Id = id }, cancellationToken);
        if (!result.IsSuccess)
            return StatusCode(result.StatusCode, result);

        return StatusCode(StatusCodes.Status204NoContent);
    }
}
