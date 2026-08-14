namespace TicketService.Application.Interfaces.Services;

/// <summary>
/// Thay thế thuật ngữ kỹ thuật (SOH, SLA, BMS...) bằng placeholder [[term_N]] trước khi
/// gửi AI dịch, restore lại sau khi nhận kết quả — đảm bảo AI không dịch hoặc biến dạng term (#639).
/// </summary>
public interface ITechnicalTermMasker
{
    (string MaskedText, IReadOnlyDictionary<string, string> TermMap) Mask(string text);

    string Unmask(string text, IReadOnlyDictionary<string, string> termMap);
}
