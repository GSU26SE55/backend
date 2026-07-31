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

    public override async Task DownloadForTranscription(DownloadForTranscriptionRequest request,
        IServerStreamWriter<DownloadFileReply> responseStream, ServerCallContext context)
    {
        if (!Guid.TryParse(request.FileId, out var fileId))
            throw new RpcException(new Status(StatusCode.NotFound, "File not found."));

        var file = await _uow.UploadedFiles.GetAllAsync().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == fileId && !x.IsDeleted, context.CancellationToken);
        if (file is null || file.Status == FileStatusEnum.Deleted)
            throw new RpcException(new Status(StatusCode.NotFound, "File not found."));
        if (file.Status is FileStatusEnum.Quarantined or FileStatusEnum.Processing)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "File is not available for transcription."));

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
