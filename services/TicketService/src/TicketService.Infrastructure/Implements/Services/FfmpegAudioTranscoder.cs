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

        await FFMpegArguments
            .FromPipeInput(new StreamPipeSource(inputStream))
            .OutputToPipe(new StreamPipeSink(outputStream), options => options
                .WithAudioCodec("aac")
                .WithAudioBitrate(128)
                .ForceFormat("ipod")) // ipod muxer = m4a container (AAC trong MP4)
            .CancellableThrough(ct)
            .ProcessAsynchronously();

        _logger.LogInformation(
            "[AudioTranscoder] {Source} ({InBytes}B) → m4a ({OutBytes}B)",
            sourceContentType, input.Length, outputStream.Length);

        return (outputStream.ToArray(), "audio/mp4", targetName);
    }

    private static string ToM4aFileName(string sourceFileName)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return "voice-message.m4a";
        var dot = sourceFileName.LastIndexOf('.');
        var stem = dot > 0 ? sourceFileName[..dot] : sourceFileName;
        return $"{stem}.m4a";
    }
}
