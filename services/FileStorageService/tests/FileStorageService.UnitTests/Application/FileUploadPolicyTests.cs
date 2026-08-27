using FileStorageService.Application.Authorization;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;

namespace FileStorageService.UnitTests.Application;

/// <summary>
/// Kiểm tra trực tiếp <see cref="FileUploadPolicy"/> thay vì đi qua handler.
///
/// <para>Bộ test qua handler (<c>FileStorageCommandHandlerTests</c>) chỉ chạm được vài nhánh của policy
/// vì mỗi case phải dựng đủ storage/uow/authorization mock. Ở đây gọi thẳng <c>Validate</c> nên phủ được
/// từng luật một: file rỗng, thiếu đuôi file, và whitelist của TỪNG purpose — phần trước đây mới chỉ
/// kiểm một purpose duy nhất.</para>
/// </summary>
public class FileUploadPolicyTests
{
    private static FormFile MakeFile(string fileName, long length = 1024, string contentType = "application/octet-stream")
    {
        // Stream giả có độ dài mong muốn mà không cấp phát thật nhiều byte.
        var stream = new MemoryStream(new byte[Math.Min(length, 16)]);
        return new FormFile(stream, 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static CommonResponse<string> Validate(
        IFormFile? file,
        FilePurposeEnum purpose = FilePurposeEnum.Other,
        byte[]? header = null)
    {
        var response = new CommonResponse<string>();
        FileUploadPolicy.Validate(file, purpose, response, header);
        return response;
    }

    // ---------- File null / rỗng ----------

    [Fact]
    public void Validate_NullFile_Returns400()
    {
        var response = Validate(null);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Message.Should().Be("File is required.");
    }

    /// <summary>
    /// File dài 0 byte — nhánh trước đây chưa có test nào chạm tới.
    /// </summary>
    [Fact]
    public void Validate_EmptyFile_Returns400()
    {
        var response = Validate(MakeFile("photo.jpg", length: 0));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().Contain(e => e.Detail == "File is empty.");
    }

    // ---------- Kích thước ----------

    [Fact]
    public void Validate_FileOverLimit_Returns413()
    {
        var response = Validate(MakeFile("photo.jpg", FileUploadPolicy.MaxFileSizeBytes + 1));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(413);
        response.ListErrors.Should().Contain(e => e.Detail == "Maximum file size is 20 MB.");
    }

    /// <summary>Đúng 20 MB vẫn hợp lệ — biên trên là <c>&lt;=</c>, không phải <c>&lt;</c>.</summary>
    [Fact]
    public void Validate_FileExactlyAtLimit_IsAccepted()
    {
        var response = Validate(MakeFile("photo.jpg", FileUploadPolicy.MaxFileSizeBytes));

        response.IsSuccess.Should().BeTrue();
        response.ListErrors.Should().BeEmpty();
    }

    // ---------- Đuôi file ----------

    /// <summary>Tên file không có phần mở rộng — nhánh trước đây chưa được test.</summary>
    [Theory]
    [InlineData("README")]
    [InlineData("archive")]
    public void Validate_FileWithoutExtension_Returns400(string fileName)
    {
        var response = Validate(MakeFile(fileName));

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Message.Should().Be("File must have a valid extension.");
    }

    /// <summary>Đuôi file viết hoa vẫn được chấp nhận (so sánh không phân biệt hoa thường).</summary>
    [Fact]
    public void Validate_UppercaseExtension_IsAccepted()
    {
        var response = Validate(MakeFile("PHOTO.JPG"), FilePurposeEnum.Avatar);

        response.IsSuccess.Should().BeTrue();
    }

    // ---------- Whitelist theo từng purpose ----------

    /// <summary>Mỗi purpose chấp nhận một đuôi file đại diện của riêng nó.</summary>
    [Theory]
    [InlineData(FilePurposeEnum.Other, "report.pdf")]
    [InlineData(FilePurposeEnum.Other, "data.csv")]
    [InlineData(FilePurposeEnum.Avatar, "me.webp")]
    [InlineData(FilePurposeEnum.TicketAttachment, "note.mp3")]
    [InlineData(FilePurposeEnum.TicketAttachment, "scan.docx")]
    [InlineData(FilePurposeEnum.MaintenancePhoto, "before.png")]
    [InlineData(FilePurposeEnum.KbImage, "diagram.gif")]
    [InlineData(FilePurposeEnum.Firmware, "device.bin")]
    [InlineData(FilePurposeEnum.Firmware, "device.hex")]
    public void Validate_ExtensionAllowedForPurpose_IsAccepted(FilePurposeEnum purpose, string fileName)
    {
        var response = Validate(MakeFile(fileName), purpose);

        response.IsSuccess.Should().BeTrue();
        response.ListErrors.Should().BeEmpty();
    }

    /// <summary>
    /// Đuôi file hợp lệ ở purpose khác nhưng không thuộc purpose đang dùng thì vẫn bị chặn —
    /// đây mới là điều whitelist theo purpose bảo đảm.
    /// </summary>
    [Theory]
    [InlineData(FilePurposeEnum.Avatar, "report.pdf")]
    [InlineData(FilePurposeEnum.MaintenancePhoto, "photo.webp")]
    [InlineData(FilePurposeEnum.KbImage, "firmware.bin")]
    [InlineData(FilePurposeEnum.Firmware, "photo.jpg")]
    [InlineData(FilePurposeEnum.TicketAttachment, "sheet.xlsx")]
    public void Validate_ExtensionNotAllowedForPurpose_Returns400(FilePurposeEnum purpose, string fileName)
    {
        var response = Validate(MakeFile(fileName), purpose);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Message.Should().Be($"Invalid file format for purpose {purpose}.");
    }

    /// <summary>Purpose ngoài bảng ánh xạ rơi về whitelist của <c>Other</c>.</summary>
    [Fact]
    public void GetAllowedExtensions_UnknownPurpose_FallsBackToOther()
    {
        var unknown = (FilePurposeEnum)999;

        FileUploadPolicy.GetAllowedExtensions(unknown)
            .Should().BeEquivalentTo(FileUploadPolicy.GetAllowedExtensions(FilePurposeEnum.Other));
    }

    // ---------- Nội dung thật vs đuôi file ----------

    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Validate_ContentMatchesExtension_IsAccepted()
    {
        var response = Validate(MakeFile("logo.png"), FilePurposeEnum.KbImage, PngHeader);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_ContentDoesNotMatchExtension_Returns400()
    {
        // Nội dung PNG nhưng khai là .jpg
        var response = Validate(MakeFile("logo.jpg"), FilePurposeEnum.KbImage, PngHeader);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.Message.Should().Contain("does not match extension");
    }

    /// <summary>
    /// Đuôi file không có chữ ký nhận dạng ổn định (.txt, .csv, .bin…) thì bỏ qua bước so nội dung —
    /// nếu không, mọi file văn bản đều bị từ chối oan.
    /// </summary>
    [Theory]
    [InlineData(FilePurposeEnum.Other, "note.txt")]
    [InlineData(FilePurposeEnum.Firmware, "image.bin")]
    public void Validate_ExtensionWithoutSignature_SkipsContentCheck(FilePurposeEnum purpose, string fileName)
    {
        var response = Validate(MakeFile(fileName), purpose, PngHeader);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>File quá lớn giữ nguyên 413, không bị 400 của bước kiểm nội dung ghi đè.</summary>
    [Fact]
    public void Validate_OversizedFileWithMismatchedContent_Keeps413()
    {
        var response = Validate(
            MakeFile("logo.jpg", FileUploadPolicy.MaxFileSizeBytes + 1),
            FilePurposeEnum.KbImage,
            PngHeader);

        response.StatusCode.Should().Be(413);
    }
}
