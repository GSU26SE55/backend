namespace TicketService.Application.Interfaces.Services;

public interface IVoiceTranscriptionService
{
    /// <summary>
    /// Transcribe audio stream sang text bằng Gemini multimodal.
    /// Stream phải seekable (MemoryStream) — service sẽ đọc từ đầu.
    /// </summary>
    Task<string> TranscribeAsync(Stream audioStream, string mimeType, CancellationToken ct = default);
}
