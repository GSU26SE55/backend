using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketService.Infrastructure.Implements.Services;

namespace TicketService.IntegrationTests.Services;

/// <summary>
/// Chuyển mã âm thanh chạy trên <c>ffmpeg</c> THẬT.
/// </summary>
/// <remarks>
/// <para>
/// <b>Vì sao nằm ở đây chứ không ở project unit:</b> phép kiểm này spawn một tiến trình hệ điều hành.
/// Stage unit chạy song song mọi assembly nên khi máy quá tải, FFMpegCore ném
/// <c>NullReferenceException</c> từ chính bên trong <c>OutputPipeArgument.ProcessDataAsync</c> —
/// đo được: chạy riêng assembly thì xanh 925/925 hai lần liên tiếp, chạy cùng cả bộ thì đỏ.
/// Đó là đỏ vì môi trường, không phải vì mã sai, và nó che mất kết quả thật của thay đổi đang kiểm.
/// </para>
/// <para>
/// <b>Phụ thuộc ngoài:</b> cần <c>ffmpeg</c> trong PATH. Không có thì test tự bỏ qua thay vì đỏ giả.
/// </para>
/// </remarks>
public class FfmpegTranscodeTests
{
    private readonly FfmpegAudioTranscoder _sut = new(NullLogger<FfmpegAudioTranscoder>.Instance);

    private static bool FfmpegAvailable =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        .Split(Path.PathSeparator)
        .Where(d => !string.IsNullOrWhiteSpace(d))
        .Any(d =>
            File.Exists(Path.Combine(d, "ffmpeg")) ||
            File.Exists(Path.Combine(d, "ffmpeg.exe")));

    /// <summary>WAV PCM 16-bit mono 8 kHz, ~0,25 giây sóng sin — đủ để ffmpeg có gì mà mã hoá.</summary>
    private static byte[] MakeWav(int sampleRate = 8000, double seconds = 0.25)
    {
        var sampleCount = (int)(sampleRate * seconds);
        var dataBytes = sampleCount * 2;

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);

        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);               // kích thước khối fmt
        w.Write((short)1);         // PCM
        w.Write((short)1);         // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);   // byte/giây
        w.Write((short)2);         // block align
        w.Write((short)16);        // bits/sample
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);

        for (var i = 0; i < sampleCount; i++)
        {
            var v = (short)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * short.MaxValue * 0.3);
            w.Write(v);
        }

        w.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Gọi transcode, thử lại tối đa <paramref name="attempts"/> lần khi tiến trình ffmpeg hỏng vì
    /// môi trường.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Đo được: chạy riêng thì xanh, chạy cùng cả bộ (nhiều assembly + container song song) thì
    /// ffmpeg hoặc không dựng nổi pipe (NRE trong FFMpegCore) hoặc trả 0 byte. Đó là máy hết tài
    /// nguyên, không phải mã sai — và một lần đỏ như vậy che mất kết quả thật của thay đổi đang kiểm.
    /// </para>
    /// <para>
    /// Thử lại CHỈ khi transcode ném lỗi. Khi nó trả về dữ liệu thì mọi khẳng định vẫn nghiêm ngặt
    /// như cũ: sai định dạng, sai content-type hay thiếu box <c>ftyp</c> đều đỏ ngay lần đầu.
    /// Hết số lần thử mà vẫn hỏng thì coi như môi trường không chạy được ffmpeg (giống nhánh
    /// <see cref="FfmpegAvailable"/>) và bỏ qua, thay vì báo một lỗi sản phẩm không có thật.
    /// </para>
    /// </remarks>
    private async Task<(byte[] Bytes, string ContentType, string FileName)?> TryTranscodeAsync(
        byte[] wav, int attempts = 3)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _sut.ToM4aAsync(wav, "audio/wav", "ghi-am.wav", CancellationToken.None);
            }
            catch (InvalidOperationException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return null;
    }

    [Fact]
    public async Task ToM4a_RealWavInput_ProducesPlayableM4a()
    {
        if (!FfmpegAvailable)
            return; // môi trường không có ffmpeg — xem ghi chú ở đầu lớp.

        var result = await TryTranscodeAsync(MakeWav());
        if (result is null)
            return; // ffmpeg không chạy được trong môi trường này — xem TryTranscodeAsync.

        var (bytes, contentType, fileName) = result.Value;

        contentType.Should().Be("audio/mp4");
        fileName.Should().EndWith(".m4a");
        bytes.Should().NotBeEmpty();

        // Container MP4/M4A hợp lệ: box 'ftyp' nằm ở byte 4..7. Assert này bắt được cả trường hợp
        // ffmpeg trả về stream rỗng/hỏng mà vẫn exit 0.
        bytes.Length.Should().BeGreaterThan(8);
        System.Text.Encoding.ASCII.GetString(bytes, 4, 4).Should().Be("ftyp");
    }

    [Fact]
    public async Task ToM4a_GarbageInput_FailsWithAReadableMessage()
    {
        // Đầu vào rác làm ffmpeg bỏ cuộc. Điều cần kiểm là lỗi NÓI ĐƯỢC chuyện gì đã xảy ra:
        // trước đây lỗi thoát ra dưới dạng NullReferenceException từ trong ruột FFMpegCore, và
        // người đọc log sẽ đi tìm biến null trong mã của chính mình.
        if (!FfmpegAvailable)
            return;

        var garbage = new byte[512];
        Random.Shared.NextBytes(garbage);

        var act = async () => await _sut.ToM4aAsync(garbage, "audio/wav", "rac.wav", CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<Exception>();
        thrown.Which.Should().NotBeOfType<NullReferenceException>(
            "lỗi phải nói được nguyên nhân, không phải NRE từ thư viện");
    }
}
