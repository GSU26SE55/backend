using BatteryService.Application.Common.Models;
using BatteryService.Infrastructure.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-780 — <c>Ai:MinReadings</c> đóng hai vai xung khắc nhau.
///
/// <para>
/// AI từ chối thẳng mọi payload khác 30 dòng (<c>predict.py</c>:
/// <c>if len(v) != WINDOW_SIZE: raise</c>) vì 30 là hình dạng cửa sổ đã nướng vào chính trọng số
/// model. Nhưng BE dùng cùng một giá trị cấu hình cho CẢ ngưỡng "đủ lịch sử chưa" LẪN số dòng gửi
/// đi. Đặt <c>Ai:MinReadings = 29</c> hay <c>31</c> là qua được ngưỡng rồi gửi payload sai hình
/// dạng: REST trả 422, gRPC trả INVALID_ARGUMENT, worker nhận null và prediction DỪNG HẲN — mà
/// nhìn cấu hình thì không thấy gì sai.
/// </para>
/// </summary>
public class AiWindowSizeContractTests
{
    /// <summary>
    /// Ghim theo <c>ai-module/src/core/config.py</c>: <c>WINDOW_SIZE = 30</c>. Hai kho khác nhau
    /// nên không tham chiếu chéo được; đây là chỗ duy nhất phát hiện một bên đổi mà bên kia không.
    /// </summary>
    [Fact]
    public void WindowSize_MatchesTheAiModelContract()
    {
        AiOptions.WindowSize.Should().Be(30);
    }

    [Fact]
    public void WindowSize_IsACompileTimeConstant_NotAConfigurableProperty()
    {
        // Nếu ai đó biến nó thành property có setter thì lỗ hổng quay lại y nguyên: vận hành lại
        // chỉnh được một con số vốn thuộc về trọng số model.
        typeof(AiOptions).GetProperty(nameof(AiOptions.WindowSize))
            .Should().BeNull("WindowSize phải là hằng số, không phải option cấu hình được");

        typeof(AiOptions).GetField(nameof(AiOptions.WindowSize))!.IsLiteral
            .Should().BeTrue();
    }

    private static IServiceProvider BuildProvider(int minReadings, int maxScan = 60)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Enabled"] = "true",
                ["Ai:MinReadings"] = minReadings.ToString(),
                ["Ai:MaxScanReadings"] = maxScan.ToString(),
                ["Ai:IntervalMinutes"] = "5",
                ["Ai:TimeoutSeconds"] = "5",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<AiOptions>()
            .Bind(config.GetSection(AiOptions.SectionName))
            .Validate(o => o.MinReadings >= AiOptions.WindowSize,
                $"Ai:MinReadings must be >= {AiOptions.WindowSize}")
            .Validate(o => o.MaxScanReadings >= o.MinReadings,
                "Ai:MaxScanReadings must be >= Ai:MinReadings")
            .Validate(o => o.IntervalMinutes > 0, "Ai:IntervalMinutes must be greater than 0.")
            .Validate(o => o.TimeoutSeconds > 0, "Ai:TimeoutSeconds must be greater than 0.")
            .ValidateOnStart();
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(29)]
    [InlineData(1)]
    [InlineData(0)]
    public void MinReadingsBelowWindowSize_IsRejectedAtStartup(int minReadings)
    {
        // Ca 29 chính là ca issue nêu: qua ngưỡng nhưng không bao giờ dựng nổi 30 dòng.
        var act = () => BuildProvider(minReadings).GetRequiredService<IOptions<AiOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MinReadings*");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(60)]
    public void MinReadingsAtOrAboveWindowSize_IsAccepted(int minReadings)
    {
        // 31 hợp lệ: đòi NHIỀU lịch sử hơn là chuyện vận hành bình thường — miễn payload vẫn đúng
        // 30 dòng (xem SohPredictionBackgroundServiceTests).
        var act = () => BuildProvider(minReadings, maxScan: Math.Max(60, minReadings))
            .GetRequiredService<IOptions<AiOptions>>().Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void MaxScanBelowMinReadings_IsRejected()
    {
        // Quét về ít hơn ngưỡng thì không bao giờ đủ mẫu — job im lặng bỏ qua mọi pin.
        var act = () => BuildProvider(minReadings: 40, maxScan: 30)
            .GetRequiredService<IOptions<AiOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .WithMessage("*MaxScanReadings*");
    }

    [Fact]
    public void DefaultOptions_AreValid()
    {
        // Chống hồi quy: mặc định phải qua được chính bộ kiểm mình vừa thêm, nếu không service
        // không lên nổi khi chưa cấu hình gì.
        var defaults = new AiOptions();

        defaults.MinReadings.Should().BeGreaterThanOrEqualTo(AiOptions.WindowSize);
        defaults.MaxScanReadings.Should().BeGreaterThanOrEqualTo(defaults.MinReadings);
        defaults.IntervalMinutes.Should().BeGreaterThan(0);
        defaults.TimeoutSeconds.Should().BeGreaterThan(0);
    }
}
