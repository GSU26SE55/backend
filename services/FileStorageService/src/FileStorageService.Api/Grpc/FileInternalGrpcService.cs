using FileStorageService.Application.Interfaces;
using FileStorageService.Domain.Enums;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Grpc.FileInternal;

namespace FileStorageService.Api.Grpc;

public sealed class FileInternalGrpcService : FileInternal.FileInternalBase
{
    private const int ChunkSize = 64 * 1024;
    private readonly IFileStorageUnitOfWork _uow;
    private readonly IObjectStorageService _storage;

    public FileInternalGrpcService(IFileStorageUnitOfWork uow, IObjectStorageService storage)
    {
        (_uow, _storage) = (uow, storage);
    }

    public override Task DownloadForTranscription(DownloadForTranscriptionRequest request,
        IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
        => StreamFileAsync(request.FileId, responseStream, context);

    /// <summary>
    /// GH-790 — kênh tải file nội bộ dùng chung giữa các service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VirusScanWorker</c> của TicketService trước đây gọi thẳng
    /// <c>GET /api/files/{id}/download</c> — endpoint có <c>[Authorize]</c> — mà không gắn token nào.
    /// Kết quả luôn là 401; worker ghi <c>VirusScanStatus=Failed</c>, và vì nó chỉ quét bản ghi
    /// <c>Pending</c> nên bản ghi đó không bao giờ được thử lại: đính kèm kẹt vĩnh viễn ở 202
    /// "đang quét, thử lại sau".
    /// </para>
    /// <para>
    /// Kênh gRPC nội bộ chạy trên cổng riêng, không đi qua tầng JWT dành cho người dùng cuối, và đã
    /// được dùng sẵn cho voice transcription — nên đây là đường service-to-service ĐÃ CÓ, không phải
    /// cơ chế mới bịa ra cho riêng virus scan.
    /// </para>
    /// </remarks>
    public override Task DownloadFile(DownloadFileRequest request,
        IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
        => StreamFileAsync(request.FileId, responseStream, context);

    private async Task StreamFileAsync(string rawFileId,
        IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
    {
        if (!Guid.TryParse(rawFileId, out var fileId))
            throw new RpcException(new Status(StatusCode.NotFound, "File not found."));

        var file = await _uow.UploadedFiles.GetAllAsync().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId && !x.IsDeleted, context.CancellationToken);
        if (file is null || file.Status == FileStatusEnum.Deleted)
            throw new RpcException(new Status(StatusCode.NotFound, "File not found."));
        if (file.Status is FileStatusEnum.Quarantined or FileStatusEnum.Processing)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "File is not available for download."));

        var download = await _storage.DownloadAsync(file.ObjectKey, context.CancellationToken);
        await using var stream = download.Stream;
        var buffer = new byte[ChunkSize];
        var first = true;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), context.CancellationToken)) > 0)
        {
            await responseStream.WriteAsync(new DownloadFileReply
            {
                Chunk = Google.Protobuf.ByteString.CopyFrom(buffer, 0, read),
                ContentType = first ? file.ContentType : string.Empty,
                FileName = first ? file.OriginalFileName : string.Empty,
                TotalSize = first ? file.Size : 0
            });
            first = false;
        }
    }
}
