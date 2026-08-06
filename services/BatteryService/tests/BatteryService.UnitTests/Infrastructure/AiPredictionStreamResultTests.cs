using BatteryService.Application.Common.Models;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Enums;
using FluentAssertions;

namespace BatteryService.UnitTests.Infrastructure;

/// <summary>
/// C10 — ngữ nghĩa kết quả của lượt dự đoán hàng loạt qua bidi stream.
/// </summary>
/// <remarks>
/// Bidi stream KHÔNG có lỗi theo từng message: một cửa sổ sai làm abort CẢ stream sau k−1
/// response. Nên "thiếu kết quả" phải phân biệt được với "pin không có vấn đề" — đọc nhầm hai
/// thứ này thì dashboard hiển thị pin CHƯA ĐƯỢC CHẤM y như pin khoẻ mạnh.
/// </remarks>
public class AiPredictionStreamResultTests
{
    private static AiPredictionResult Sample() => new(
        SohPercent: 88m,
        Confidence: 0.9m,
        Classification: AnomalyClassificationEnum.Normal,
        AnomalyScore: 0.1m,
        AnomalyConfidence: 0.1m,
        RulCyclesEstimate: 100,
        Priority: "P3",
        ModelVersion: "1.6",
        LatencyMs: 40);

    [Fact]
    public void IsComplete_True_OnlyWhenAllRequestedCameBack()
    {
        var r = new AiPredictionStreamResult(new[] { Sample(), Sample() }, RequestedCount: 2, AbortReason: null);
        r.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void IsComplete_False_WhenStreamAbortedMidway()
    {
        // 3 pin gửi đi, chỉ 1 kết quả về vì cửa sổ thứ 2 sai hình dạng.
        var r = new AiPredictionStreamResult(
            new[] { Sample() }, RequestedCount: 3, AbortReason: "InvalidArgument: readings must have 30 timesteps");

        r.IsComplete.Should().BeFalse();
        r.Predictions.Should().HaveCount(1);
        r.AbortReason.Should().NotBeNull();
    }

    [Fact]
    public void IsComplete_False_WhenCountShort_EvenWithoutAbortReason()
    {
        // Thiếu kết quả mà không có lý do vẫn là KHÔNG hoàn tất. Nếu chỉ xét AbortReason thì
        // một stream đóng sớm im lặng sẽ bị coi là thành công — đúng ca khó phát hiện nhất.
        var r = new AiPredictionStreamResult(new[] { Sample() }, RequestedCount: 2, AbortReason: null);
        r.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void EmptyBatch_IsComplete()
    {
        var r = new AiPredictionStreamResult(Array.Empty<AiPredictionResult>(), 0, null);
        r.IsComplete.Should().BeTrue("không gửi gì thì không thiếu gì");
    }
}
