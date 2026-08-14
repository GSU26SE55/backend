using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using SharedContracts.Grpc.FileInternal;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.BackgroundJobs;

namespace TicketService.IntegrationTests.Services;

/// <summary>
/// GH-790 — <c>VirusScanWorker</c> THẬT tải file qua kênh gRPC nội bộ THẬT.
/// </summary>
/// <remarks>
/// <para>
/// Worker cũ gọi <c>GET /api/files/{id}/download</c> mà không gắn token, trong khi endpoint đó có
/// <c>[Authorize]</c> ⇒ luôn 401. Tiêu chí nghiệm thu đòi "integration test chạy thật Ticket worker
/// ↔ authorized FileStorage endpoint", nên ở đây dựng máy chủ gRPC thật (Kestrel, HTTP/2) phục vụ
/// đúng hợp đồng <c>file_internal.proto</c>, rồi cho worker chạy qua một
/// <see cref="FileInternal.FileInternalClient"/> thật.
/// </para>
/// <para>
/// Mock client sẽ chỉ chứng minh "worker gọi một hàm nào đó" — không nói gì về việc dây có thông
/// hay không, mà dây không thông chính là toàn bộ nội dung của issue này.
/// </para>
/// </remarks>
public class VirusScanOverInternalChannelTests : IAsyncLifetime
{
    private IHost _server = null!;
    private GrpcChannel _channel = null!;
    private FakeFileServer _fileServer = null!;

    public async Task InitializeAsync()
    {
        _fileServer = new FakeFileServer();

        _server = await new HostBuilder()
            .ConfigureWebHostDefaults(web => web
                // Cổng 0 = để hệ điều hành chọn cổng rảnh, tránh đụng nhau khi cả bộ test chạy song
                // song. Phải là 127.0.0.1 chứ không phải "localhost": Kestrel không cho gán cổng
                // động khi bind theo tên localhost.
                .UseKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0,
                    o => o.Protocols = HttpProtocols.Http2))
                .ConfigureServices(s =>
                {
                    s.AddGrpc();
                    s.AddSingleton(_fileServer);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapGrpcService<FakeFileServer>());
                }))
            .StartAsync();

        _channel = GrpcChannel.ForAddress(ServerAddress());
    }

    public async Task DisposeAsync()
    {
        _channel?.Dispose();
        if (_server is not null)
        {
            await _server.StopAsync();
            _server.Dispose();
        }
    }

    private string ServerAddress()
    {
        var addresses = _server.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses;
        return addresses.First();
    }

    /// <summary>Máy chủ gRPC phục vụ đúng hợp đồng chung, có thể ép trả lỗi để kiểm chiều hỏng.</summary>
    private class FakeFileServer : FileInternal.FileInternalBase
    {
        public byte[] Payload { get; set; } = [];
        public StatusCode? ForcedError { get; set; }
        public int DownloadCalls { get; private set; }

        public override async Task DownloadFile(DownloadFileRequest request,
            IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
        {
            DownloadCalls++;

            if (ForcedError is { } code)
                throw new RpcException(new Status(code, "ép lỗi trong test"));

            // Chia nhỏ để kiểm cả việc worker ghép các mảnh lại đúng thứ tự.
            const int chunk = 3;
            for (var offset = 0; offset < Payload.Length; offset += chunk)
            {
                var size = Math.Min(chunk, Payload.Length - offset);
                await responseStream.WriteAsync(new DownloadFileReply
                {
                    Chunk = ByteString.CopyFrom(Payload, offset, size),
                    ContentType = offset == 0 ? "application/pdf" : string.Empty,
                    FileName = offset == 0 ? "tai-lieu.pdf" : string.Empty,
                    TotalSize = offset == 0 ? Payload.Length : 0,
                });
            }
        }
    }

    private static TicketAttachment Attachment() => new()
    {
        Id = Guid.NewGuid(),
        FileId = Guid.NewGuid(),
        FileName = "tai-lieu.pdf",
        TicketId = Guid.NewGuid(),
        UploadedByUserId = Guid.NewGuid(),
        ContentType = "application/pdf",
        SizeBytes = 1024,
        VirusScanStatus = VirusScanStatusEnum.Pending,
        CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        Ticket = null!,
    };

    private (VirusScanWorker Worker, Mock<IClamAvClient> ClamAv, List<byte[]> Scanned) BuildWorker(
        TicketAttachment attachment, VirusScanStatusEnum scanResult = VirusScanStatusEnum.Clean)
    {
        var list = new List<TicketAttachment> { attachment };

        var repo = new Mock<IGenericRepository<TicketAttachment>>();
        repo.Setup(r => r.GetAllAsync()).Returns(() => list.AsQueryable().BuildMock());
        repo.Setup(r => r.UpdateAsync(It.IsAny<TicketAttachment>()));

        var uow = new Mock<ITicketUnitOfWork>();
        uow.Setup(u => u.TicketAttachments).Returns(repo.Object);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var scanned = new List<byte[]>();
        var clamAv = new Mock<IClamAvClient>();
        clamAv.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Callback<Stream, string, CancellationToken>((s, _, _) =>
              {
                  using var ms = new MemoryStream();
                  s.CopyTo(ms);
                  scanned.Add(ms.ToArray());
              })
              .ReturnsAsync(scanResult);

        var services = new ServiceCollection();
        services.AddSingleton(uow.Object);
        services.AddSingleton(clamAv.Object);
        services.AddSingleton(new FileInternal.FileInternalClient(_channel));
        var provider = services.BuildServiceProvider();

        var worker = new VirusScanWorker(
            NullLogger<VirusScanWorker>.Instance,
            provider,
            Options.Create(new ChatOptions
            {
                Features = new ChatOptions.FeaturesSection { EnableVirusScan = true },
                VirusScan = new ChatOptions.VirusScanSection
                {
                    BatchSize = 10,
                    MaxAttempts = 3,
                    RetryBackoffSeconds = 60,
                    ScanTimeoutSeconds = 600,
                },
            }));

        return (worker, clamAv, scanned);
    }

    [Fact]
    public async Task CleanFile_IsDownloadedOverGrpc_AndMarkedClean()
    {
        // Kịch bản chính của tiêu chí nghiệm thu: Pending → (tải qua kênh nội bộ) → Clean.
        var payload = System.Text.Encoding.UTF8.GetBytes("noi dung dinh kem sach de kiem tra");
        _fileServer.Payload = payload;

        var attachment = Attachment();
        var (worker, _, scanned) = BuildWorker(attachment);

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        _fileServer.DownloadCalls.Should().Be(1, "phải đi qua kênh gRPC nội bộ, không phải REST có [Authorize]");
        scanned.Should().ContainSingle();
        scanned[0].Should().Equal(payload, "các mảnh stream phải được ghép lại nguyên vẹn, đúng thứ tự");
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Clean);
    }

    [Fact]
    public async Task InfectedFile_IsMarkedInfected()
    {
        _fileServer.Payload = System.Text.Encoding.UTF8.GetBytes("EICAR-gia-lap");

        var attachment = Attachment();
        var (worker, _, _) = BuildWorker(attachment, VirusScanStatusEnum.Infected);

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Infected);
    }

    [Fact]
    public async Task ServerRejectingTheCall_DoesNotStrandTheAttachment()
    {
        // Tái hiện đúng hình dạng của lỗi cũ (401 → hỏng) nhưng ở tầng gRPC. Điều phải đúng: bản ghi
        // quay lại hàng đợi để lượt sau thử tiếp, KHÔNG rơi thẳng vào Failed rồi nằm đó vĩnh viễn.
        _fileServer.ForcedError = StatusCode.PermissionDenied;

        var attachment = Attachment();
        var (worker, clamAv, _) = BuildWorker(attachment);

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Pending);
        attachment.VirusScanAttempts.Should().Be(1);
        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "tải hỏng thì không được đưa gì cho ClamAV");
    }

    [Fact]
    public async Task EmptyStreamFromServer_IsAFailure_NotACleanVerdict()
    {
        // Máy chủ trả 0 byte mà worker báo "sạch" nghĩa là đính kèm được đánh dấu an toàn trong khi
        // chưa ai quét nội dung thật của nó.
        _fileServer.Payload = [];

        var attachment = Attachment();
        var (worker, clamAv, _) = BuildWorker(attachment);

        await worker.ScanPendingAttachmentsAsync(CancellationToken.None);

        clamAv.Verify(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Pending);
    }

    [Fact]
    public async Task RepeatedServerFailures_EndInFailed_NotAnEndlessLoop()
    {
        _fileServer.ForcedError = StatusCode.Unavailable;

        var attachment = Attachment();
        var (worker, _, _) = BuildWorker(attachment);

        // Ba lượt liên tiếp; xoá mốc thời gian để bỏ qua giãn cách — ở đây đang kiểm trần số lần thử,
        // không phải kiểm giãn cách (đã có test riêng ở tầng unit).
        for (var i = 0; i < 3; i++)
        {
            attachment.VirusScanLastAttemptAt = null;
            await worker.ScanPendingAttachmentsAsync(CancellationToken.None);
        }

        attachment.VirusScanAttempts.Should().Be(3);
        attachment.VirusScanStatus.Should().Be(VirusScanStatusEnum.Failed);
    }
}
