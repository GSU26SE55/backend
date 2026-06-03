using FileStorageService.Domain.Enums;

namespace FileStorageService.Api.Requests;

/// <summary>
/// Dữ liệu gửi lên khi client cần upload một file vào dịch vụ lưu trữ.
/// Request này phải được gửi bằng content type <c>multipart/form-data</c>.
/// </summary>
public class UploadFileRequest
{
    /// <summary>
    /// File cần upload. Đây là field bắt buộc trong form-data.
    /// File rỗng, file vượt quá dung lượng cấu hình, hoặc file có phần mở rộng không nằm trong danh sách cho phép sẽ bị từ chối.
    /// </summary>
    public IFormFile? File { get; set; }

    /// <summary>
    /// Tên thư mục logic dùng để nhóm file trong object storage.
    /// Nếu không truyền, hệ thống dùng giá trị mặc định là <c>default</c>.
    /// Giá trị này sẽ trở thành phần đầu của <c>objectKey</c>, ví dụ <c>avatars/...</c> hoặc <c>reports/...</c>.
    /// </summary>
    public string FolderName { get; set; } = "default";

    /// <summary>
    /// Mục đích nghiệp vụ của file để service khác có thể phân quyền/hiển thị đúng ngữ cảnh.
    /// </summary>
    public FilePurposeEnum Purpose { get; set; } = FilePurposeEnum.Other;
}
