using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.Extensions.Logging;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// Transcode audio về m4a/AAC bằng ffmpeg (qua FFMpegCore). ffmpeg binary phải có trong PATH
/// (Docker runtime cài qua apt; local dev cài thủ công).
/// </summary>
public class FfmpegAudioTranscoder : IAudioTranscoder
{
    private static readonly HashSet<string> AlreadyM4aMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/aac",
    };

    private readonly ILogger<FfmpegAudioTranscoder> _logger;

    public FfmpegAudioTranscoder(ILogger<FfmpegAudioTranscoder> logger)
    {
        _logger = logger;
    }

    public async Task<(byte[] Bytes, string ContentType, string FileName)> ToM4aAsync(
        byte[] input,
        string sourceContentType,
        string sourceFileName,
        CancellationToken ct = default)
    {
        var targetName = ToM4aFileName(sourceFileName);

        // Đã là m4a/aac → không transcode lại, chỉ chuẩn hóa content-type + tên.
        if (AlreadyM4aMimeTypes.Contains(sourceContentType))
            return (input, "audio/mp4", targetName);

        using var inputStream = new MemoryStream(input, writable: false);
        using var outputStream = new MemoryStream();

        try
        {
            await RunFfmpegAsync(inputStream, outputStream, ct);
        }
        catch (Exception ex) when (ex is NullReferenceException or ObjectDisposedException)
        {
            // FFMpegCore ném NullReferenceException từ chính bên trong
            // `OutputPipeArgument.ProcessDataAsync` khi không dựng nổi pipe tới tiến trình ffmpeg —
            // quan sát được khi máy quá tải (chạy song song cả bộ test), không phải vì dữ liệu vào sai.
            // Để nguyên NRE thì log chỉ có "Object reference not set to an instance of an object" và
            // người đọc sẽ đi tìm biến null trong mã của mình. Đổi thành lỗi nói đúng chuyện đã xảy ra.
            throw new InvalidOperationException(
                $"Failed to run ffmpeg to convert '{sourceFileName}' to m4a "
                + "(the process failed to start or the pipe was closed early).", ex);
        }

        var output = outputStream.ToArray();

        // ffmpeg có thể thoát mã 0 mà đầu ra vẫn rỗng hoặc cụt (pipe bị ngắt, máy quá tải, encoder
        // bỏ cuộc giữa chừng). Trả về như không có chuyện gì nghĩa là lưu một tệp ghi âm 0 byte:
        // đính kèm trông hợp lệ, mở ra không có gì, và không ai biết mất từ lúc nào.
        // Ném ra ở đây để lỗi nổi lên đúng chỗ nó xảy ra, thay vì hiện ra như "voice hỏng" nhiều
        // ngày sau. 8 byte đầu là kích thước box + chữ ký 'ftyp' của container MP4/M4A.
        if (output.Length <= 8
            || System.Text.Encoding.ASCII.GetString(output, 4, 4) != "ftyp")
        {
            throw new InvalidOperationException(
                $"Audio transcoding to m4a failed: ffmpeg returned {output.Length} byte(s), which is not a "
                + $"valid MP4 container (source {sourceContentType}, {input.Length} byte(s)).");
        }

        _logger.LogInformation(
            "[AudioTranscoder] {Source} ({InBytes}B) → m4a ({OutBytes}B)",
            sourceContentType, input.Length, output.Length);

        return (output, "audio/mp4", targetName);
    }

    /// <summary>Chạy ffmpeg: đọc từ pipe vào, ghi ra pipe ra.</summary>
    private static Task RunFfmpegAsync(Stream inputStream, Stream outputStream, CancellationToken ct)
        => FFMpegArguments
            .FromPipeInput(new StreamPipeSource(inputStream))
            .OutputToPipe(new StreamPipeSink(outputStream), options => options
                .WithAudioCodec("aac")
                .WithAudioBitrate(128)
                // MP4/ipod ghi bảng chỉ mục (moov) ở CUỐI rồi tua ngược về đầu file để vá lại.
                // Đầu ra ở đây là pipe — KHÔNG tua ngược được — nên ffmpeg từ chối ngay từ khâu
                // ghi header: "muxer does not support non seekable output". Hệ quả: MỌI input
                // không phải m4a/aac đều ném exception, tức transcode voice chưa từng chạy được
                // (đã tái hiện trên cả ffmpeg 5.1.9 trong container lẫn 8.1.1 trên máy dev).
                //
                // frag_keyframe+empty_moov = MP4 phân mảnh: moov rỗng đặt ngay đầu, dữ liệu chia
                // thành fragment tự mô tả ⇒ ghi tuần tự được, không cần seek. Vẫn là .m4a hợp lệ,
                // trình phát thường + trình duyệt đều mở được.
                .WithCustomArgument("-movflags frag_keyframe+empty_moov")
                .ForceFormat("ipod")) // ipod muxer = m4a container (AAC trong MP4)
            .CancellableThrough(ct)
            .ProcessAsynchronously();

    private static string ToM4aFileName(string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return "voice-message.m4a";
        var dot = sourceFileName.LastIndexOf('.');
        var stem = dot > 0 ? sourceFileName[..dot] : sourceFileName;
        return $"{stem}.m4a";
    }
}
