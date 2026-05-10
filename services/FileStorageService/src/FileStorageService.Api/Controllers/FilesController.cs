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
/// Service này chịu trách nhiệm upload file, tải file trực tiếp, tạo presigned URL và xóa file theo <c>objectKey</c>.
/// </summary>
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
    /// Upload một file lên object storage.
    /// </summary>
    /// <remarks>
    /// Endpoint này nhận request dạng <c>multipart/form-data</c> gồm file cần lưu và tên thư mục logic.
    ///
    /// Cách hoạt động:
    /// - Hệ thống kiểm tra request có file hay không.
    /// - File được kiểm tra theo cấu hình lưu trữ hiện tại, gồm dung lượng tối đa và danh sách phần mở rộng được phép.
    /// - Nếu hợp lệ, file được upload vào bucket đang cấu hình.
    /// - Tên file trong storage không giữ nguyên tên gốc; hệ thống tạo một tên mới bằng GUID để tránh trùng file.
    /// - <c>objectKey</c> trả về là định danh cần lưu lại ở các service khác để download, tạo presigned URL hoặc xóa file sau này.
    ///
    /// Form-data:
    /// - <c>file</c>: file cần upload. Đây là field bắt buộc.
    /// - <c>folderName</c>: thư mục logic để nhóm file, ví dụ <c>avatars</c>, <c>reports</c>, <c>warranty-documents</c>. Nếu bỏ trống thì dùng <c>default</c>.
    ///
    /// Kết quả thành công trả về HTTP 201 kèm thông tin file đã lưu:
    /// - <c>objectKey</c>: khóa định danh file trong object storage.
    /// - <c>fileName</c>: tên file gốc do client gửi lên.
    /// - <c>contentType</c>: MIME type của file.
    /// - <c>size</c>: kích thước file theo byte.
    /// - <c>publicUrl</c>: URL public nếu hệ thống có cấu hình PublicBaseUrl; ngược lại có thể là null.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu không gửi file.
    /// - Lỗi validation hoặc exception nếu file rỗng, vượt quá dung lượng tối đa, thiếu phần mở rộng, hoặc phần mở rộng không được cho phép.
    /// </remarks>
    /// <param name="request">Thông tin upload file được gửi bằng form-data.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>Thông tin file sau khi upload thành công, bao gồm <c>objectKey</c> để dùng cho các endpoint khác.</returns>
    /// <response code="201">Upload file thành công và trả về thông tin file đã lưu.</response>
    /// <response code="400">Request không hợp lệ, ví dụ thiếu file hoặc dữ liệu upload không đạt điều kiện validation.</response>
    /// <response code="500">Có lỗi khi ghi file vào object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(CommonResponse<FileUploadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Upload([FromForm] UploadFileRequest request, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new UploadFileCommand
        {
            File = request.File,
            FolderName = request.FolderName
        }, cancellationToken);

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
    /// - File được đọc từ bucket đang cấu hình.
    /// - Nếu đọc thành công, API trả về nội dung file với <c>Content-Type</c> và tên file phù hợp.
    ///
    /// Khi thành công, response không bọc trong <c>CommonResponse</c> mà trả về file stream trực tiếp.
    /// Client nên xử lý response như một file download, không phải JSON.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu thiếu <c>objectKey</c>.
    /// - HTTP 404 hoặc lỗi từ provider lưu trữ nếu file không tồn tại trong bucket.
    /// - HTTP 500 nếu object storage không khả dụng hoặc có lỗi hệ thống.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file trong object storage.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>Binary stream của file nếu tìm thấy; JSON lỗi nếu request không hợp lệ hoặc không tải được file.</returns>
    /// <response code="200">Tải file thành công. Response body là nội dung binary của file.</response>
    /// <response code="400">Thiếu hoặc truyền <c>objectKey</c> không hợp lệ.</response>
    /// <response code="404">Không tìm thấy file tương ứng trong object storage.</response>
    /// <response code="500">Có lỗi khi đọc file từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpGet("download")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<FileDownloadResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Download([FromQuery] string objectKey, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DownloadFileQuery { ObjectKey = objectKey }, cancellationToken);
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
    /// - Tạo URL tạm thời dùng HTTP GET.
    /// - URL hết hạn sau khoảng thời gian đã truyền vào <c>expiresInMinutes</c>.
    ///
    /// Lưu ý bảo mật:
    /// - Bất kỳ ai có URL trong thời gian còn hiệu lực đều có thể tải file.
    /// - Không nên log hoặc chia sẻ presigned URL ở nơi công khai.
    /// - Với file nhạy cảm, nên dùng thời gian hết hạn ngắn.
    ///
    /// Response thành công là <c>CommonResponse&lt;string&gt;</c>, trong đó <c>data</c> là presigned URL.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file trong object storage.</param>
    /// <param name="expiresInMinutes">Thời gian hiệu lực của URL, tính theo phút. Giá trị hợp lệ: 1 đến 1440.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>URL tạm thời để tải file trực tiếp từ object storage.</returns>
    /// <response code="200">Tạo presigned URL thành công.</response>
    /// <response code="400">Thiếu <c>objectKey</c> hoặc <c>expiresInMinutes</c> nằm ngoài khoảng hợp lệ.</response>
    /// <response code="500">Có lỗi khi tạo URL từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpGet("presigned-url")]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
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
    /// Xóa file khỏi object storage bằng <c>objectKey</c>.
    /// </summary>
    /// <remarks>
    /// Endpoint này xóa object tương ứng trong bucket lưu trữ.
    /// Client cần truyền đúng <c>objectKey</c> đã nhận được từ endpoint upload hoặc đang lưu trong database nghiệp vụ.
    ///
    /// Tham số query:
    /// - <c>objectKey</c>: khóa định danh file cần xóa. Ví dụ <c>reports/9b1c...ef.pdf</c>.
    ///
    /// Cách hoạt động:
    /// - Hệ thống kiểm tra <c>objectKey</c> không được rỗng.
    /// - <c>objectKey</c> được chuẩn hóa để chặn path traversal.
    /// - Gửi lệnh xóa object tới storage provider.
    ///
    /// Kết quả thành công:
    /// - API trả HTTP 204 No Content.
    /// - Response body rỗng theo chuẩn của HTTP 204.
    ///
    /// Lưu ý nghiệp vụ:
    /// - Endpoint này chỉ xóa file vật lý trong object storage.
    /// - Nếu service khác đang lưu metadata hoặc quan hệ tới file, service đó cần tự cập nhật/xóa dữ liệu liên quan.
    /// - Sau khi xóa, các <c>objectKey</c> hoặc presigned URL cũ không còn dùng để tải file được nữa.
    ///
    /// Các lỗi thường gặp:
    /// - HTTP 400 nếu thiếu <c>objectKey</c>.
    /// - HTTP 500 nếu object storage không khả dụng hoặc có lỗi hệ thống.
    /// </remarks>
    /// <param name="objectKey">Khóa định danh file cần xóa trong object storage.</param>
    /// <param name="cancellationToken">Token hủy request khi client ngắt kết nối hoặc server cần dừng xử lý.</param>
    /// <returns>HTTP 204 nếu xóa thành công.</returns>
    /// <response code="204">Xóa file thành công, response body rỗng.</response>
    /// <response code="400">Thiếu hoặc truyền <c>objectKey</c> không hợp lệ.</response>
    /// <response code="500">Có lỗi khi xóa file từ object storage hoặc lỗi hệ thống ngoài dự kiến.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(CommonResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Delete([FromQuery] string objectKey, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new DeleteFileCommand { ObjectKey = objectKey }, cancellationToken);
        return StatusCode(result.StatusCode);
    }
}
