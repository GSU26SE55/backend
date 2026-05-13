using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Handler;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using SharedKernels.Interfaces;

namespace FileStorageService.UnitTests.Application;

public class FileStorageCommandHandlerTests
{
    [Fact]
    public async Task UploadFile_NullFile_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var handler = new UploadFileCommandHandler(storage.Object, uow.Object);

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
    public async Task UploadFile_ValidFile_PersistsMetadata_AndReturnsFileId()
    {
        var fileId = Guid.Empty;
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "avatar.png",
                "image/png",
                4,
                "avatars",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ObjectKey = "avatars/abc.png",
                FileName = "avatar.png",
                ContentType = "image/png",
                Size = 4,
                PublicUrl = "http://localhost:9090/solar-battery-files/avatars/abc.png"
            });

        var (uow, files) = BuildFileStorageUnitOfWork();
        files
            .Setup(x => x.AddAsync(It.IsAny<UploadedFile>()))
            .Callback<UploadedFile>(file => fileId = file.Id)
            .Returns(Task.CompletedTask);

        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            FolderName = "avatars",
            Purpose = FilePurposeEnum.Avatar
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Data!.FileId.Should().Be(fileId);
        result.Data.ObjectKey.Should().Be("avatars/abc.png");
        files.Verify(x => x.AddAsync(It.Is<UploadedFile>(file =>
            file.ObjectKey == "avatars/abc.png" &&
            file.Purpose == FilePurposeEnum.Avatar &&
            file.Status == FileStatusEnum.Ready)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFile_MetadataSaveFails_DeletesUploadedObject()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "avatar.png",
                "image/png",
                4,
                "avatars",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ObjectKey = "avatars/abc.png",
                FileName = "avatar.png",
                ContentType = "image/png",
                Size = 4
            });

        var (uow, _) = BuildFileStorageUnitOfWork();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object);

        var act = async () => await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            FolderName = "avatars",
            Purpose = FilePurposeEnum.Avatar
        }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        storage.Verify(x => x.DeleteAsync("avatars/abc.png", It.IsAny<CancellationToken>()), Times.Once);
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

    private static (Mock<IFileStorageUnitOfWork> uow, Mock<IGenericRepository<UploadedFile>> files)
        BuildFileStorageUnitOfWork(IEnumerable<UploadedFile>? seed = null)
    {
        var files = new Mock<IGenericRepository<UploadedFile>>();
        files.Setup(x => x.GetAllAsync()).Returns((seed ?? Array.Empty<UploadedFile>()).AsQueryable());

        var uow = new Mock<IFileStorageUnitOfWork>();
        uow.SetupGet(x => x.UploadedFiles).Returns(files.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return (uow, files);
    }
}
