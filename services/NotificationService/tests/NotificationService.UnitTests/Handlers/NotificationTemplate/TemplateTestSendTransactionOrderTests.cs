using MassTransit;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.CQRS.Handler.NotificationTemplate;
using NotificationService.Domain.Enums;
using SharedContracts.Events;
using SharedContracts.Events.Root;

namespace NotificationService.UnitTests.Handlers.NotificationTemplate;

/// <summary>
/// GH-795 — gửi thử template phải COMMIT audit trước, publish sau.
/// </summary>
/// <remarks>
/// <para>
/// Thứ tự cũ là publish rồi mới ghi audit + <c>SaveChangesAsync</c>. Lần ghi DB hỏng sau khi broker
/// đã nhận event thì admin thấy HTTP 500 nhưng thư VẪN đi, và không có bản ghi kiểm toán nào để đối
/// chiếu. Admin bấm lại vì tưởng chưa gửi ⇒ thư thứ hai.
/// </para>
/// <para>
/// NotificationService không có outbox cho đường email này, nên cách đúng là commit trạng thái +
/// audit trước khi tạo tác động ra ngoài — đúng như phần Expected của issue.
/// </para>
/// </remarks>
public class TemplateTestSendTransactionOrderTests
{
    private static readonly Guid AdminId = Guid.Parse("eeeeeeee-1111-2222-3333-444444444444");

    private static NotificationTemplateTestSendCommand Command(Guid templateId) => new()
    {
        Id = templateId,
        ActorUserId = AdminId,
        ActorEmailFromClaim = "admin@x.com",
    };

    private static NotificationTemplateTestSendCommandHandler Build(TemplateHandlerHarness harness) =>
        new(harness.Uow.Object,
            harness.Renderer,
            harness.Publisher.Object,
            harness.Cache.Object,
            harness.Audit.Object,
            TemplateHandlerHarness.Logger<NotificationTemplateTestSendCommandHandler>());

    [Fact]
    public async Task AuditIsCommitted_BeforeTheEmailIsPublished()
    {
        // Kiểm THỨ TỰ, không chỉ kiểm "cả hai đều xảy ra": trạng thái cuối giống hệt nhau ở cả hai
        // thứ tự, nên chỉ ghi lại mốc thời điểm mới phân biệt được.
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        var steps = new List<string>();
        harness.Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .Callback(() => steps.Add("commit"))
               .ReturnsAsync(1);
        harness.Publisher.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
               .Callback(() => steps.Add("publish"))
               .Returns(Task.CompletedTask);

        var result = await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        steps.Should().Equal("commit", "publish");
    }

    [Fact]
    public async Task DatabaseFailure_MeansNoEmailIsPublished()
    {
        // Tiêu chí nghiệm thu: hỏng DB thì KHÔNG được có thư nào đi. Trước đây thư đã đi rồi mới tới
        // lượt DB hỏng, nên 500 trả về là một lời nói dối với người dùng.
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        harness.Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("DB down"));

        var act = async () => await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Publisher.Verify(
            p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SuccessfulSend_ProducesExactlyOneEmailEvent()
    {
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        var result = await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        harness.Publisher.Verify(
            p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);
        harness.Audit.Verify(a => a.WriteAsync(
            NotificationAuditActionEnum.TemplateTestSent,
            It.IsAny<Guid>(), It.IsAny<Guid>(), true,
            It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishedEmail_CarriesADeterministicId_SoARepublishIsDeduped()
    {
        // Nối với GH-792: MassTransit có thể phát lại event. ID ngẫu nhiên thì EmailService coi lần
        // phát lại là một việc mới và admin nhận thư thử lần thứ hai.
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        SendNotificationEmailEvent? sent = null;
        harness.Publisher.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
               .Callback<SendNotificationEmailEvent, CancellationToken>((e, _) => sent = e)
               .Returns(Task.CompletedTask);

        await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        sent.Should().NotBeNull();
        sent!.Id.Should().Be(DeterministicEventId.From(sent.NotificationId, "email"));
    }

    [Fact]
    public async Task BrokerFailure_IsReportedAsFailure_NotSilentSuccess()
    {
        // Chiều ngược lại của việc commit trước: broker hỏng thì audit đã ghi "đã gửi thử" mà thư
        // chưa đi. Trả 200 lúc này là để lại một dòng kiểm toán không có thư nào tương ứng.
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        harness.Publisher.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("broker down"));

        var result = await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task BrokerFailure_LeavesAFailureAuditRecord()
    {
        var template = TemplateHandlerHarness.Template();
        var harness = new TemplateHandlerHarness(template);

        harness.Publisher.Setup(p => p.Publish(It.IsAny<SendNotificationEmailEvent>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("broker down"));

        await Build(harness).Handle(Command(template.Id), CancellationToken.None);

        harness.Audit.Verify(a => a.WriteAsync(
            NotificationAuditActionEnum.TemplateTestSent,
            It.IsAny<Guid>(), It.IsAny<Guid>(), false,
            It.IsAny<string?>(), It.IsAny<IReadOnlyDictionary<string, object?>?>(), It.IsAny<CancellationToken>()),
            Times.Once, "dấu vết kiểm toán phải nói đúng sự thật: đã ghi nhận nhưng thư chưa đi");
    }
}
