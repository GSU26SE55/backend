using FileStorageService.Api.Grpc;
using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Entities;
using FileStorageService.Domain.Enums;
using Grpc.Core;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Grpc.FileInternal;
using SharedKernels.Interfaces;

namespace FileStorageService.IntegrationTests.ObjectStorage;

/// <summary>
/// GH-790 — rpc <c>DownloadFile</c> phải lấy đúng nội dung từ kho lưu trữ THẬT.
/// </summary>
/// <remarks>
/// <para>
/// Phía TicketService đã có test chạy worker thật qua một máy chủ gRPC dựng trong test. Nhưng máy
/// chủ đó là bản giả — nó không nói gì về việc bản hiện thực THẬT của FileStorage có đọc đúng object
/// từ MinIO hay không. Hai nửa hợp đồng phải được kiểm riêng thì mới không còn chỗ hở.
/// </para>
/// <para>
/// Dùng lại MinIO thật của <see cref="MinioFixture"/> (GH-788) và
/// <c>S3CompatibleFileStorageService</c> thật; chỉ có tầng dữ liệu là giả lập, vì bảng metadata
/// không phải thứ đang được kiểm ở đây.
/// </para>
/// </remarks>
public sealed class InternalDownloadRpcTests : IClassFixture<MinioFixture>
{
    private readonly MinioFixture _minio;

    public InternalDownloadRpcTests(MinioFixture minio) => _minio = minio;

    /// <summary>Thu lại các mảnh mà rpc ghi ra, để so với nội dung gốc.</summary>
    private sealed class CollectingStreamWriter : IServerStreamWriter<DownloadFileReply>
    {
        public List<DownloadFileReply> Replies { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(DownloadFileReply message)
        {
            Replies.Add(message);
            return Task.CompletedTask;
        }

        public byte[] Payload => Replies.SelectMany(r => r.Chunk.ToByteArray()).ToArray();
    }

    private static ServerCallContext Context() => TestServerCallContext.Create();

    /// <summary>Bối cảnh gRPC tối giản — rpc chỉ dùng tới <c>CancellationToken</c>.</summary>
    private sealed class TestServerCallContext : ServerCallContext
    {
        private TestServerCallContext() { }
        public static TestServerCallContext Create() => new();

        protected override string MethodCore => "DownloadFile";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(5);
        protected override Metadata RequestHeadersCore => [];
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = [];
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
            => Task.CompletedTask;
    }

    private FileInternalGrpcService BuildService(params UploadedFile[] rows)
    {
        var repo = new Mock<IGenericRepository<UploadedFile>>();
        repo.Setup(r => r.GetAllAsync()).Returns(rows.AsQueryable().BuildMock());

        var uow = new Mock<IFileStorageUnitOfWork>();
        uow.SetupGet(u => u.UploadedFiles).Returns(repo.Object);

        return new FileInternalGrpcService(uow.Object, _minio.NewStorageService());
    }

    private static UploadedFile Row(string objectKey, long size, FileStatusEnum status = FileStatusEnum.Ready) => new()
    {
        Id = Guid.NewGuid(),
        ObjectKey = objectKey,
        OriginalFileName = "tai-lieu.txt",
        ContentType = "text/plain",
        Size = size,
        FolderName = "ticket-attachments",
        Purpose = FilePurposeEnum.TicketAttachment,
        Status = status,
    };

    [Fact]
    public async Task DownloadFile_StreamsTheExactBytesFromStorage()
    {
        // Nội dung dài hơn một mảnh để kiểm cả việc chia và ghép — cắt sai thì file tới tay worker
        // vẫn "có dữ liệu" nhưng khác bản gốc, và ClamAV quét nhầm thứ.
        var content = string.Concat(Enumerable.Repeat("noi dung dinh kem can quet virus. ", 400));
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        const string key = "ticket-attachments/gh790-download.txt";
        await _minio.PutAsync(key, content);

        var writer = new CollectingStreamWriter();
        var row = Row(key, bytes.Length);

        await BuildService(row).DownloadFile(
            new DownloadFileRequest { FileId = row.Id.ToString() }, writer, Context());

        writer.Payload.Should().Equal(bytes);
        writer.Replies[0].ContentType.Should().Be("text/plain", "mảnh đầu mang metadata");
        writer.Replies[0].FileName.Should().Be("tai-lieu.txt");
        writer.Replies[0].TotalSize.Should().Be(bytes.Length);
    }

    [Fact]
    public async Task DownloadFile_UnknownId_IsNotFound()
    {
        var service = BuildService();

        var act = async () => await service.DownloadFile(
            new DownloadFileRequest { FileId = Guid.NewGuid().ToString() }, new CollectingStreamWriter(), Context());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadFile_MalformedId_IsNotFound_NotACrash()
    {
        var service = BuildService();

        var act = async () => await service.DownloadFile(
            new DownloadFileRequest { FileId = "khong-phai-guid" }, new CollectingStreamWriter(), Context());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadFile_QuarantinedFile_IsRefused()
    {
        // File đã bị cách ly thì KHÔNG được phát ra ngoài, kể cả cho kênh nội bộ.
        const string key = "ticket-attachments/gh790-quarantined.txt";
        await _minio.PutAsync(key, "noi dung");
        var row = Row(key, 8, FileStatusEnum.Quarantined);

        var act = async () => await BuildService(row).DownloadFile(
            new DownloadFileRequest { FileId = row.Id.ToString() }, new CollectingStreamWriter(), Context());

        (await act.Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
    }

    [Fact]
    public async Task DownloadFile_SoftDeletedRow_IsNotFound()
    {
        const string key = "ticket-attachments/gh790-deleted.txt";
        await _minio.PutAsync(key, "noi dung");
        var row = Row(key, 8);
        row.IsDeleted = true;

        var act = async () => await BuildService(row).DownloadFile(
            new DownloadFileRequest { FileId = row.Id.ToString() }, new CollectingStreamWriter(), Context());

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.NotFound);
    }

    [Fact]
    public async Task DownloadForTranscription_StillWorks_SoTheVoicePathIsNotBroken()
    {
        // rpc mới được THÊM chứ không thay rpc cũ; voice transcription vẫn phải chạy nguyên vẹn.
        const string key = "ticket-attachments/gh790-voice.txt";
        await _minio.PutAsync(key, "ghi am");
        var row = Row(key, 6);

        var writer = new CollectingStreamWriter();
        await BuildService(row).DownloadForTranscription(
            new DownloadForTranscriptionRequest { FileId = row.Id.ToString() }, writer, Context());

        System.Text.Encoding.UTF8.GetString(writer.Payload).Should().Be("ghi am");
    }
}
