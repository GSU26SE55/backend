using BatteryService.Application.CQRS.Command.AnomalyClassification;
using BatteryService.Application.CQRS.Handler.AnomalyClassification;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// Sprint Bonus NS-26 (#666, F2) — Staff feedback cho AnomalyClassification.
/// </summary>
public class SubmitAnomalyClassificationFeedbackTests
{
    private static AnomalyClassification MakeClassification(Guid id) => new()
    {
        Id = id,
        BatteryAssetId = Guid.NewGuid(),
        Classification = AnomalyClassificationEnum.Failed,
        AnomalyScore = -0.35m,
        Confidence = 0.9m,
        ModelVersion = "1.0",
        ClassifiedAt = DateTime.UtcNow,
        LatencyMs = 42,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Feedback_SetsFieldsAndReturnsDto()
    {
        var id = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var entity = MakeClassification(id);
        var b = new MockUnitOfWorkBuilder().WithAnomalyClassifications(entity);
        var handler = new SubmitAnomalyClassificationFeedbackCommandHandler(b.Build());

        var res = await handler.Handle(new SubmitAnomalyClassificationFeedbackCommand
        {
            Id = id,
            Feedback = StaffFeedbackEnum.FalsePositive,
            StaffFeedbackByUserId = staffId
        }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(200);
        entity.StaffFeedback.Should().Be(StaffFeedbackEnum.FalsePositive);
        entity.StaffFeedbackByUserId.Should().Be(staffId);
        entity.StaffFeedbackAt.Should().NotBeNull();
        res.Data!.StaffFeedback.Should().Be(StaffFeedbackEnum.FalsePositive);
        b.AnomalyClassifications.Verify(r => r.UpdateAsync(entity), Times.Once);
    }

    [Fact]
    public async Task Feedback_NotFound_Returns404()
    {
        var b = new MockUnitOfWorkBuilder();
        var handler = new SubmitAnomalyClassificationFeedbackCommandHandler(b.Build());

        var res = await handler.Handle(new SubmitAnomalyClassificationFeedbackCommand
        {
            Id = Guid.NewGuid(),
            Feedback = StaffFeedbackEnum.Correct,
            StaffFeedbackByUserId = Guid.NewGuid()
        }, CancellationToken.None);

        // Lỗi business (không tìm thấy) → ghi ở Message, ListErrors rỗng → serialize thành null.
        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(404);
        res.Message.Should().NotBeNullOrEmpty();
        res.ListErrors.Should().BeEmpty();
        SerializedListErrors(res).Should().Be("null", "lỗi business → listErrors null trong JSON");
    }

    [Fact]
    public async Task Validate_InvalidFeedback_Returns400_FieldInListErrors()
    {
        var res = await new SubmitAnomalyClassificationFeedbackCommand
        {
            Id = Guid.NewGuid(),
            Feedback = (StaffFeedbackEnum)99
        }.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(400);
        res.ListErrors.Should().ContainSingle(e => e.Field == "Feedback" && !string.IsNullOrEmpty(e.Detail));
    }

    [Fact]
    public async Task Validate_EmptyId_Returns400_FieldInListErrors()
    {
        var res = await new SubmitAnomalyClassificationFeedbackCommand
        {
            Id = Guid.Empty,
            Feedback = StaffFeedbackEnum.Correct
        }.ValidateAsync();

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(400);
        res.ListErrors.Should().ContainSingle(e => e.Field == "Id" && !string.IsNullOrEmpty(e.Detail));
    }

    // Trích field "listErrors" từ JSON đã serialize (qua ErrorsListJsonConverter) — chứng minh
    // rỗng → null, có lỗi → array.
    private static string SerializedListErrors<T>(SharedContracts.Common.Responses.CommonResponse<T> res)
    {
        var opts = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
        using var doc = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(res, opts));
        return doc.RootElement.GetProperty("listErrors").GetRawText();
    }
}
