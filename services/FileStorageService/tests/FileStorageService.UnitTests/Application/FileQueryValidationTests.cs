using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Query;

namespace FileStorageService.UnitTests.Application;

/// <summary>
/// Luật validate của các query/command nhận <c>ObjectKey</c> do client truyền.
///
/// <para>Điểm đáng test nhất là chặn <c>".."</c>: ObjectKey đi thẳng vào đường dẫn object trên
/// storage, nên một key chứa <c>..</c> là mưu toan path traversal ra ngoài thư mục cho phép.</para>
/// </summary>
public class ObjectKeyValidationTests
{
    [Fact]
    public async Task DownloadFileQuery_ValidKey_Passes()
    {
        var r = await new DownloadFileQuery { ObjectKey = "tickets/2026/photo.jpg" }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadFileQuery_MissingKey_Fails(string key)
    {
        var r = await new DownloadFileQuery { ObjectKey = key }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey" && e.Detail.Contains("required"));
    }

    /// <summary>Path traversal bị chặn ở mọi vị trí trong chuỗi.</summary>
    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("tickets/../../secret.pem")]
    [InlineData("a..b")]
    public async Task DownloadFileQuery_TraversalKey_Fails(string key)
    {
        var r = await new DownloadFileQuery { ObjectKey = key }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey" && e.Detail.Contains(".."));
    }

    [Fact]
    public async Task DeleteFileCommand_ValidKey_Passes()
    {
        var r = await new DeleteFileCommand { ObjectKey = "tickets/2026/photo.jpg" }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFileCommand_MissingKey_Fails()
    {
        var r = await new DeleteFileCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey");
    }

    /// <summary>Xoá là thao tác không hoàn tác được, nên chặn traversal ở đây càng quan trọng.</summary>
    [Fact]
    public async Task DeleteFileCommand_TraversalKey_Fails()
    {
        var r = await new DeleteFileCommand { ObjectKey = "../../prod/backup.sql" }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey" && e.Detail.Contains(".."));
    }

    [Fact]
    public async Task GetPresignedUrlQuery_Valid_Passes()
    {
        var r = await new GetPresignedUrlQuery
        {
            ObjectKey = "tickets/2026/photo.jpg",
            ExpiresInMinutes = 15
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetPresignedUrlQuery_TraversalKey_Fails()
    {
        var r = await new GetPresignedUrlQuery { ObjectKey = "../secret" }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey");
    }

    /// <summary>Hạn của URL ký sẵn nằm trong 1 phút đến 1 ngày.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1441)]
    public async Task GetPresignedUrlQuery_ExpiryOutOfRange_Fails(int minutes)
    {
        var r = await new GetPresignedUrlQuery
        {
            ObjectKey = "tickets/photo.jpg",
            ExpiresInMinutes = minutes
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "ExpiresInMinutes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task GetPresignedUrlQuery_ExpiryAtBoundary_Passes(int minutes)
    {
        var r = await new GetPresignedUrlQuery
        {
            ObjectKey = "tickets/photo.jpg",
            ExpiresInMinutes = minutes
        }.ValidateAsync();

        r.ListErrors.Should().NotContain(e => e.Field == "ExpiresInMinutes");
    }

    /// <summary>Key rỗng VÀ hạn sai cùng lúc phải sinh đủ hai lỗi, không dừng ở lỗi đầu.</summary>
    [Fact]
    public async Task GetPresignedUrlQuery_BothInvalid_ReportsBothErrors()
    {
        var r = await new GetPresignedUrlQuery { ObjectKey = "", ExpiresInMinutes = 0 }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "ObjectKey");
        r.ListErrors.Should().Contain(e => e.Field == "ExpiresInMinutes");
    }
}

public class FileIdQueryValidationTests
{
    [Fact]
    public async Task GetFileMetadataQuery_ValidId_Passes()
    {
        var r = await new GetFileMetadataQuery { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetFileMetadataQuery_EmptyId_Fails()
    {
        var r = await new GetFileMetadataQuery().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Id" && e.Detail.Contains("Invalid FileId"));
    }

    [Fact]
    public async Task DownloadFileByIdQuery_ValidId_Passes()
    {
        var r = await new DownloadFileByIdQuery { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFileByIdQuery_EmptyId_Fails()
    {
        var r = await new DownloadFileByIdQuery().ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Fact]
    public async Task GetFilePresignedUrlByIdQuery_Valid_Passes()
    {
        var r = await new GetFilePresignedUrlByIdQuery
        {
            Id = Guid.NewGuid(),
            ExpiresInMinutes = 60
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetFilePresignedUrlByIdQuery_EmptyId_Fails()
    {
        var r = await new GetFilePresignedUrlByIdQuery { ExpiresInMinutes = 15 }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    public async Task GetFilePresignedUrlByIdQuery_ExpiryOutOfRange_Fails(int minutes)
    {
        var r = await new GetFilePresignedUrlByIdQuery
        {
            Id = Guid.NewGuid(),
            ExpiresInMinutes = minutes
        }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "ExpiresInMinutes");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1440)]
    public async Task GetFilePresignedUrlByIdQuery_ExpiryAtBoundary_Passes(int minutes)
    {
        var r = await new GetFilePresignedUrlByIdQuery
        {
            Id = Guid.NewGuid(),
            ExpiresInMinutes = minutes
        }.ValidateAsync();

        r.ListErrors.Should().NotContain(e => e.Field == "ExpiresInMinutes");
    }

    /// <summary>Mặc định 15 phút là giá trị hợp lệ khi client không truyền.</summary>
    [Fact]
    public async Task GetFilePresignedUrlByIdQuery_DefaultExpiry_Passes()
    {
        var r = await new GetFilePresignedUrlByIdQuery { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }
}
