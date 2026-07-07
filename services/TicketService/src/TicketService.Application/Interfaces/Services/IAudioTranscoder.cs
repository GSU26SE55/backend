namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Chuẩn hóa file audio về m4a (AAC) — định dạng iOS/Android/Web đều phát được.
/// Web ghi .webm (Opus) mà iOS/AVFoundation không decode được → phải transcode.
/// </summary>
public interface IAudioTranscoder
{
    /// <summary>
    /// Transcode audio bytes sang m4a/AAC. Nếu source đã là mp4/m4a/aac thì trả nguyên bytes
    /// (không transcode lại). Trả về (Bytes, ContentType="audio/mp4", FileName="*.m4a").
    /// Ném exception nếu ffmpeg lỗi — caller tự quyết fallback.
    /// </summary>
    Task<(byte[] Bytes, string ContentType, string FileName)> ToM4aAsync(
        byte[] input,
        string sourceContentType,
        string sourceFileName,
        CancellationToken ct = default);
}
