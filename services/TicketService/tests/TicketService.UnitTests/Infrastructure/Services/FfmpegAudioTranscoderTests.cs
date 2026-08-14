using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.UnitTests.Infrastructure.Services;

/// <summary>
/// Kiểm <see cref="FfmpegAudioTranscoder"/> CHẠY THẬT sau khi nâng
/// <c>FFMpegCore 5.1.0 → 5.4.0</c> (để thoát <c>System.Text.Json 7.0.2</c> — advisory High).
///
/// <para><b>Vì sao cần:</b> trước đây component này <b>không có test nào</b>, nên nâng
/// FFMpegCore chỉ được bảo chứng ở mức "biên dịch được". Mà rủi ro thật của FFMpegCore nằm ở
/// chỗ nó dựng dòng lệnh rồi spawn tiến trình <c>ffmpeg</c> — sai ở đó thì compiler không
/// bao giờ biết.</para>
///
/// <para><b>Phần chạy ffmpeg thật đã chuyển sang <c>TicketService.IntegrationTests</c></b>
/// (<c>FfmpegTranscodeTests</c>): nó spawn tiến trình hệ điều hành, mà stage unit chạy song song
/// mọi assembly nên khi máy quá tải FFMpegCore ném NullReferenceException từ trong ruột thư viện —
/// đỏ vì môi trường, che mất kết quả thật của thay đổi đang kiểm.
/// Những gì còn ở đây KHÔNG gọi ffmpeg nên chạy được ở mọi môi trường.</para>
/// </summary>
public class FfmpegAudioTranscoderTests
{
    private readonly FfmpegAudioTranscoder _sut = new(NullLogger<FfmpegAudioTranscoder>.Instance);



    [Theory]
    [InlineData("audio/mp4")]
    [InlineData("audio/m4a")]
    [InlineData("audio/x-m4a")]
    [InlineData("audio/aac")]
    public async Task ToM4a_AlreadyM4a_SkipsTranscode_AndReturnsInputUnchanged(string contentType)
    {
        // Nhánh này KHÔNG gọi ffmpeg nên chạy được ở mọi môi trường.
        var input = new byte[] { 1, 2, 3, 4, 5 };

        var (bytes, resultType, fileName) = await _sut.ToM4aAsync(
            input, contentType, "voice.m4a", CancellationToken.None);

        bytes.Should().BeSameAs(input, "đã đúng định dạng thì không được transcode lại");
        resultType.Should().Be("audio/mp4");
        fileName.Should().Be("voice.m4a");
    }

    [Theory]
    [InlineData("recording.wav", "recording.m4a")]
    [InlineData("a.b.ogg", "a.b.m4a")]
    [InlineData("no-extension", "no-extension.m4a")]
    [InlineData("", "voice-message.m4a")]
    public async Task ToM4a_NormalizesFileName(string source, string expected)
    {
        var (_, _, fileName) = await _sut.ToM4aAsync(
            new byte[] { 1 }, "audio/mp4", source, CancellationToken.None);

        fileName.Should().Be(expected);
    }
}
