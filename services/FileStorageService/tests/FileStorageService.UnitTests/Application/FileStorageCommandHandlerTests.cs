using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Handler;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;

namespace FileStorageService.UnitTests.Application;

public class FileStorageCommandHandlerTests
{
    [Fact]
    public async Task UploadFile_NullFile_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var handler = new UploadFileCommandHandler(storage.Object);

        var result = await handler.Handle(new UploadFileCommand { File = null }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        storage.Verify(
            x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteFile_ValidObjectKey_CallsStorage_AndReturns204()
    {
        var storage = new Mock<IObjectStorageService>();
        var handler = new DeleteFileCommandHandler(storage.Object);

        var result = await handler.Handle(new DeleteFileCommand { ObjectKey = "avatars/a.png" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(204);
        storage.Verify(x => x.DeleteAsync("avatars/a.png", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrl_ValidObjectKey_ReturnsUrl()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.GetPresignedUrlAsync("docs/a.pdf", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("http://localhost:9000/solar-battery-files/docs/a.pdf");
        var handler = new GetPresignedUrlQueryHandler(storage.Object);

        var result = await handler.Handle(new GetPresignedUrlQuery
        {
            ObjectKey = "docs/a.pdf",
            ExpiresInMinutes = 10
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().Be("http://localhost:9000/solar-battery-files/docs/a.pdf");
    }

    [Fact]
    public async Task DownloadFile_BlankObjectKey_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileDownloadResponse());
        var handler = new DownloadFileQueryHandler(storage.Object);

        var result = await handler.Handle(new DownloadFileQuery { ObjectKey = " " }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
