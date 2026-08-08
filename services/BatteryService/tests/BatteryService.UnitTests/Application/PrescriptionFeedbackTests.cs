using BatteryService.Application.CQRS.Command.Alert;
using BatteryService.Application.CQRS.Handler.Alert;
using BatteryService.Application.Interfaces;
using BatteryService.Domain.Entities;
using BatteryService.Domain.Enums;
using BatteryService.UnitTests.Helpers;
using FluentAssertions;
using Moq;
using Xunit;
using AlertEntity = BatteryService.Domain.Entities.Alert;

namespace BatteryService.UnitTests.Application;

/// <summary>
/// GH-778 — vòng phản hồi prescription bị đứt.
///
/// <para>
/// Proto có sẵn <c>prescription_id = 23</c> và AI có sẵn <c>POST /prescribe/feedback</c>, nhưng cả
/// hai client Battery đều BỎ trường đó khi map ⇒ id chết ngay tại ranh giới bridge, và không có
/// endpoint nào gửi phản hồi. Hậu quả: kỹ thuật viên đọc được lời khuyên nhưng không nói lại được
/// nó đúng hay sai, nên AI lặp lại cùng một lời khuyên sai mãi.
/// </para>
/// </summary>
public class PrescriptionFeedbackTests
{
    private static readonly Guid CustomerA = Guid.NewGuid();
    private static readonly Guid CustomerB = Guid.NewGuid();

    private readonly Mock<IAiPrescriptionFeedbackClient> _ai = new();

    public PrescriptionFeedbackTests()
    {
        _ai.Setup(c => c.SubmitFeedbackAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiFeedbackOutcome.Recorded);
    }

    private static (MockUnitOfWorkBuilder Uow, AlertEntity Alert, Guid AssetId) Seed(
        string? prescriptionId = "presc-123", Guid? ownerId = null)
    {
        var assetId = Guid.NewGuid();
        var alert = new AlertEntity
        {
            Id = Guid.NewGuid(),
            BatteryAssetId = assetId,
            AnomalyType = AnomalyTypeEnum.SohDegradation,
            Severity = AlertSeverityEnum.Critical,
            Status = AlertStatusEnum.Open,
            DetectedAt = DateTime.UtcNow,
            AiPrescriptionId = prescriptionId,
        };
        var uow = new MockUnitOfWorkBuilder()
            .WithAlerts(alert)
            .WithBatteryAssets(new BatteryAsset
            {
                Id = assetId,
                SerialNumber = "BAT-778",
                CustomerId = ownerId ?? CustomerA,
                Status = BatteryStatusEnum.Active,
            });
        return (uow, alert, assetId);
    }

    private SubmitPrescriptionFeedbackCommandHandler Handler(
        MockUnitOfWorkBuilder uow, IBatteryCurrentUserService? user = null)
        => new(uow.Build(), _ai.Object, user ?? TestBatteryCurrentUserService.Staff());

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    public async Task Feedback_IsForwardedToAi_WithTheStoredPrescriptionId(string status)
    {
        var (uow, alert, _) = Seed();

        var resp = await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = status },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
        resp.StatusCode.Should().Be(200);
        _ai.Verify(c => c.SubmitFeedbackAsync(
            "presc-123", status, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Feedback_StatusIsNormalisedToLowercase()
    {
        // Hợp đồng AI dùng Literal["accepted","edited","rejected"] — gửi "Accepted" sẽ bị 422 và
        // người dùng cuối nhận một lỗi không đọc được.
        var (uow, alert, _) = Seed();

        await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "  ACCEPTED  " },
            CancellationToken.None);

        _ai.Verify(c => c.SubmitFeedbackAsync(
            It.IsAny<string>(), "accepted", It.IsAny<IReadOnlyList<string>?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlertWithoutPrescription_Is409_NotFoundWouldMislead()
    {
        // Alert CÓ THẬT, chỉ là chưa từng được prescribe. Trả 404 sẽ khiến người dùng đi tìm một
        // alert đang hiện ra ngay trước mắt họ.
        var (uow, alert, _) = Seed(prescriptionId: null);

        var resp = await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "accepted" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(409);
        _ai.Verify(c => c.SubmitFeedbackAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnknownAlert_Is404()
    {
        var (uow, _, _) = Seed();

        var resp = await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = Guid.NewGuid(), Status = "accepted" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AiForgotThePrescription_Is410_NotRetryable()
    {
        // 410 ≠ 503: hết hạn thì thử lại vô ích. Gộp cả hai thành 5xx sẽ khiến client retry mãi.
        _ai.Setup(c => c.SubmitFeedbackAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiFeedbackOutcome.NotFound);
        var (uow, alert, _) = Seed();

        var resp = await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "rejected" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(410);
    }

    [Fact]
    public async Task AiUnreachable_Is503_Retryable()
    {
        _ai.Setup(c => c.SubmitFeedbackAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiFeedbackOutcome.Unavailable);
        var (uow, alert, _) = Seed();

        var resp = await Handler(uow).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "accepted" },
            CancellationToken.None);

        resp.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task CustomerCannotGiveFeedbackOnAnotherTenantsAlert()
    {
        var (uow, alert, _) = Seed(ownerId: CustomerB);

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA)).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "accepted" },
            CancellationToken.None);

        // 404 chứ không 403 — 403 xác nhận alert đó có thật (khớp GH-722/GH-774).
        resp.StatusCode.Should().Be(404);
        _ai.Verify(c => c.SubmitFeedbackAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<string>?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CustomerOwningTheAlert_CanGiveFeedback()
    {
        var (uow, alert, _) = Seed(ownerId: CustomerA);

        var resp = await Handler(uow, TestBatteryCurrentUserService.Customer(CustomerA)).Handle(
            new SubmitPrescriptionFeedbackCommand { AlertId = alert.Id, Status = "accepted" },
            CancellationToken.None);

        resp.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("khong-hop-le")]
    [InlineData("APPROVED")]
    public async Task InvalidStatus_IsRejectedByValidation(string status)
    {
        var command = new SubmitPrescriptionFeedbackCommand { AlertId = Guid.NewGuid(), Status = status };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == nameof(command.Status));
    }

    [Theory]
    [InlineData("khong-hop-le", null)]
    [InlineData("edited", null)]
    public async Task ValidationFailure_SetsStatusCode400_NotZero(string status, List<string>? steps)
    {
        // Controller trả `StatusCode(result.StatusCode, result)`. StatusCode mặc định là 0, nên
        // bỏ sót dòng gán này khiến Kestrel ghi ra "HTTP/1.1 0": client nhận BadStatusLine và
        // gateway dịch thành 502 "Upstream không phản hồi hợp lệ" — gõ sai `status` trông y hệt
        // lúc AI sập, và listErrors không bao giờ tới được người dùng.
        var command = new SubmitPrescriptionFeedbackCommand
        {
            AlertId = Guid.NewGuid(),
            Status = status,
            EditedSteps = steps,
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task EditedWithoutSteps_IsRejectedByValidation()
    {
        // "edited" mà không kèm bước nào thì AI không có gì để học — nhận vào chỉ tạo bản ghi rỗng.
        var command = new SubmitPrescriptionFeedbackCommand
        {
            AlertId = Guid.NewGuid(),
            Status = "edited",
            EditedSteps = new List<string> { "  " }
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == nameof(command.EditedSteps));
    }

    [Fact]
    public async Task EditedWithSteps_PassesValidation_AndForwardsSteps()
    {
        var (uow, alert, _) = Seed();
        var command = new SubmitPrescriptionFeedbackCommand
        {
            AlertId = alert.Id,
            Status = "edited",
            EditedSteps = new List<string> { "Bước 1 đã sửa", "   ", "Bước 2 đã sửa" },
        };

        (await command.ValidateAsync()).IsSuccess.Should().BeTrue();
        await Handler(uow).Handle(command, CancellationToken.None);

        // Bước rỗng bị loại trước khi gửi — gửi chuỗi trắng sang AI là dạy nó một bước vô nghĩa.
        _ai.Verify(c => c.SubmitFeedbackAsync(
            It.IsAny<string>(), "edited",
            It.Is<IReadOnlyList<string>?>(list => list != null && list.Count == 2),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
