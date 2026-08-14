using System.Linq.Expressions;
using FileStorageService.Application.Authorization;
using FileStorageService.Application.CQRS.Command;
using FileStorageService.Application.CQRS.Handler;
using FileStorageService.Application.CQRS.Query;
using FileStorageService.Application.DTOs;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernels.Interfaces;

namespace FileStorageService.UnitTests.Application;

public class FileStorageCommandHandlerTests
{
    /// <summary>Magic bytes PNG — nội dung phải khớp đuôi file thì upload mới qua được validation.</summary>
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    /// <summary>
    /// Ảnh HEIC (ftyp box, brand <c>heic</c>) — đúng thứ iPhone sinh ra khi đặt tên file là <c>.JPEG</c>.
    /// </summary>
    private static readonly byte[] HeicBytes =
    [
        0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
        (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0x00, 0x00, 0x00, 0x00,
        (byte)'m', (byte)'i', (byte)'f', (byte)'1', (byte)'h', (byte)'e', (byte)'i', (byte)'c'
    ];

    [Fact]
    public async Task UploadFile_NullFile_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

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
                PngBytes.Length,
                "avatars",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ObjectKey = "avatars/abc.png",
                FileName = "avatar.png",
                ContentType = "image/png",
                Size = PngBytes.Length,
                PublicUrl = "http://localhost:9090/solar-battery-files/avatars/abc.png"
            });

        var (uow, files) = BuildFileStorageUnitOfWork();
        var currentUserId = Guid.NewGuid();
        var auth = BuildFileAuthorizationService(currentUserId: currentUserId);
        files
            .Setup(x => x.AddAsync(It.IsAny<UploadedFile>()))
            .Callback<UploadedFile>(file => fileId = file.Id)
            .Returns(Task.CompletedTask);

        await using var stream = new MemoryStream(PngBytes);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

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
            file.Status == FileStatusEnum.Ready &&
            file.CreatedBy == currentUserId)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadFile_DisallowedExtensionForPurpose_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            Purpose = FilePurposeEnum.Avatar
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(error => error.Field == "file");
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
    public async Task UploadFile_HeicContentNamedJpeg_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        await using var stream = new MemoryStream(HeicBytes);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "IMG_6945.JPEG")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            FolderName = "kb-blog",
            Purpose = FilePurposeEnum.KbImage
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().Contain(error => error.Field == "file" && error.Detail.Contains("HEIC"));
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
    public async Task UploadFile_DeclaredContentTypeWrong_StoresContentTypeDetectedFromBytes()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse { ObjectKey = "avatars/abc.png", FileName = "avatar.png" });

        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        await using var stream = new MemoryStream(PngBytes);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            FolderName = "avatars",
            Purpose = FilePurposeEnum.Avatar
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        storage.Verify(
            x => x.UploadAsync(
                It.IsAny<Stream>(),
                "avatar.png",
                "image/png",
                It.IsAny<long>(),
                "avatars",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadFile_ExtensionWithoutMagicBytes_SkipsContentCheck()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse { ObjectKey = "firmware/abc.bin", FileName = "firmware.bin" });

        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        await using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08]);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "firmware.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            FolderName = "firmware",
            Purpose = FilePurposeEnum.Firmware
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
    }

    [Fact]
    public async Task UploadFile_TooLarge_Returns413_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        await using var stream = new MemoryStream();
        var formFile = new FormFile(stream, 0, FileUploadPolicy.MaxFileSizeBytes + 1, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            Purpose = FilePurposeEnum.Avatar
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(413);
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
    public async Task UploadFile_UnauthorizedPurpose_Returns403_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService(canUpload: false);
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "firmware.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new UploadFileCommand
        {
            File = formFile,
            Purpose = FilePurposeEnum.Firmware
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
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
    public async Task UploadFile_MetadataSaveFails_DeletesUploadedObject()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                "avatar.png",
                "image/png",
                PngBytes.Length,
                "avatars",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ObjectKey = "avatars/abc.png",
                FileName = "avatar.png",
                ContentType = "image/png",
                Size = PngBytes.Length
            });

        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));

        await using var stream = new MemoryStream(PngBytes);
        var formFile = new FormFile(stream, 0, stream.Length, "file", "avatar.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var handler = new UploadFileCommandHandler(storage.Object, uow.Object, auth.Object);

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
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "avatars/a.png",
            OriginalFileName = "a.png",
            ContentType = "image/png",
            FolderName = "avatars",
            Purpose = FilePurposeEnum.Avatar,
            Status = FileStatusEnum.Ready
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, files) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new DeleteFileCommandHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DeleteFileCommand { ObjectKey = "avatars/a.png" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(204);
        file.Status.Should().Be(FileStatusEnum.Deleted);
        storage.Verify(x => x.DeleteAsync("avatars/a.png", It.IsAny<CancellationToken>()), Times.Once);
        files.Verify(x => x.DeleteAsync(file), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPresignedUrl_ValidObjectKey_ReturnsUrl()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "docs/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "docs",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Ready
        };
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.GetPresignedUrlAsync("docs/a.pdf", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("http://localhost:9000/solar-battery-files/docs/a.pdf");
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new GetPresignedUrlQueryHandler(storage.Object, uow.Object, auth.Object);

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
    public async Task GetPresignedUrl_QuarantinedObjectKey_Returns409_AndDoesNotCallStorage()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "docs/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "docs",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Quarantined
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new GetPresignedUrlQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new GetPresignedUrlQuery
        {
            ObjectKey = "docs/a.pdf",
            ExpiresInMinutes = 10
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        storage.Verify(
            x => x.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadFile_BlankObjectKey_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        storage
            .Setup(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileDownloadResponse());
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        var handler = new DownloadFileQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DownloadFileQuery { ObjectKey = " " }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadFile_QuarantinedObjectKey_Returns409_AndDoesNotCallStorage()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "reports/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "reports",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Quarantined
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new DownloadFileQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DownloadFileQuery { ObjectKey = "reports/a.pdf" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadFile_DeletedObjectKey_Returns404_AndDoesNotCallStorage()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "reports/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "reports",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Deleted
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new DownloadFileQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DownloadFileQuery { ObjectKey = "reports/a.pdf" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetPresignedUrl_InvalidExpiresInMinutes_Returns400_AndDoesNotCallStorage()
    {
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork();
        var auth = BuildFileAuthorizationService();
        var handler = new GetPresignedUrlQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new GetPresignedUrlQuery
        {
            ObjectKey = "reports/a.pdf",
            ExpiresInMinutes = 0
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        storage.Verify(
            x => x.GetPresignedUrlAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DownloadFileById_Processing_Returns409_AndDoesNotCallStorage()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "reports/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "reports",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Processing
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService();
        var handler = new DownloadFileByIdQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DownloadFileByIdQuery { Id = file.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DownloadFileById_Unauthorized_Returns403_AndDoesNotCallStorage()
    {
        var file = new UploadedFile
        {
            Id = Guid.NewGuid(),
            ObjectKey = "reports/a.pdf",
            OriginalFileName = "a.pdf",
            ContentType = "application/pdf",
            FolderName = "reports",
            Purpose = FilePurposeEnum.Other,
            Status = FileStatusEnum.Ready
        };
        var storage = new Mock<IObjectStorageService>();
        var (uow, _) = BuildFileStorageUnitOfWork([file]);
        var auth = BuildFileAuthorizationService(canRead: false);
        var handler = new DownloadFileByIdQueryHandler(storage.Object, uow.Object, auth.Object);

        var result = await handler.Handle(new DownloadFileByIdQuery { Id = file.Id }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        storage.Verify(x => x.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (Mock<IFileStorageUnitOfWork> uow, Mock<IGenericRepository<UploadedFile>> files)
        BuildFileStorageUnitOfWork(IEnumerable<UploadedFile>? seed = null)
    {
        var files = new Mock<IGenericRepository<UploadedFile>>();
        files.Setup(x => x.GetAllAsync()).Returns(new TestAsyncEnumerable<UploadedFile>(seed ?? Array.Empty<UploadedFile>()));

        var uow = new Mock<IFileStorageUnitOfWork>();
        uow.SetupGet(x => x.UploadedFiles).Returns(files.Object);
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        return (uow, files);
    }

    private static Mock<IFileAuthorizationService> BuildFileAuthorizationService(
        bool canUpload = true,
        bool canRead = true,
        bool canDelete = true,
        Guid? currentUserId = null)
    {
        var auth = new Mock<IFileAuthorizationService>();
        auth.SetupGet(x => x.CurrentUserId).Returns(currentUserId ?? Guid.NewGuid());
        auth.Setup(x => x.CanUpload(It.IsAny<FilePurposeEnum>())).Returns(canUpload);
        auth.Setup(x => x.CanRead(It.IsAny<UploadedFile>())).Returns(canRead);
        auth.Setup(x => x.CanDelete(It.IsAny<UploadedFile>())).Returns(canDelete);
        return auth;
    }

    private sealed class TestAsyncQueryProvider<TEntity> : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal TestAsyncQueryProvider(IQueryProvider inner)
        {
            _inner = inner;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            return new TestAsyncEnumerable<TEntity>(expression);
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new TestAsyncEnumerable<TElement>(expression);
        }

        public object? Execute(Expression expression)
        {
            return _inner.Execute(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return _inner.Execute<TResult>(expression);
        }

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default)
        {
            var expectedResultType = typeof(TResult).GetGenericArguments()[0];
            var executionResult = typeof(IQueryProvider)
                .GetMethod(nameof(IQueryProvider.Execute), 1, [typeof(Expression)])!
                .MakeGenericMethod(expectedResultType)
                .Invoke(_inner, [expression]);

            return (TResult)typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(expectedResultType)
                .Invoke(null, [executionResult])!;
        }
    }

    private sealed class TestAsyncEnumerable<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public TestAsyncEnumerable(IEnumerable<T> enumerable)
            : base(enumerable)
        {
        }

        public TestAsyncEnumerable(Expression expression)
            : base(expression)
        {
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new TestAsyncEnumerator<T>(this.AsEnumerable().GetEnumerator());
        }

        IQueryProvider IQueryable.Provider => new TestAsyncQueryProvider<T>(this);
    }

    private sealed class TestAsyncEnumerator<T> : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;

        public TestAsyncEnumerator(IEnumerator<T> inner)
        {
            _inner = inner;
        }

        public T Current => _inner.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            return new ValueTask<bool>(_inner.MoveNext());
        }

        public ValueTask DisposeAsync()
        {
            _inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
