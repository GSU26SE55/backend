using System.Text.Json;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Application.CQRS.Handler.Notification;
using NotificationService.Domain.Enums;
using NotificationService.UnitTests.Helpers;
using NotificationEntity = NotificationService.Domain.Entities.Notification;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// Unit test cho <see cref="CreateNotificationCommandHandler"/>.
/// Mỗi test tương ứng 1 UTCID trong GSU26SE55_Unit_Test_Report.xlsx — sheet CreateNotification.
///
/// <para>Trọng tâm là cờ <c>BypassQuietHours</c> (Sprint IoT-2 #IoT2-31): nó phải được merge vào
/// <c>PayloadJson</c> để dispatcher đọc được. Ba nhánh payload — JSON object hợp lệ, rỗng, và
/// chuỗi không parse được — cho ra ba kết quả khác nhau, nên tách thành ba test riêng.</para>
/// </summary>
public class CreateNotificationCommandHandlerTests
{
    private static CreateNotificationCommand BuildCommand(
        bool bypassQuietHours = false,
        string? payloadJson = null,
        string title = "Tiêu đề",
        string body = "Nội dung")
        => new()
        {
            UserId = Guid.NewGuid(),
            Type = NotificationTypeEnum.System,
            Channel = NotificationChannelEnum.InApp,
            Title = title,
            Body = body,
            PayloadJson = payloadJson,
            BypassQuietHours = bypassQuietHours,
        };

    /// <summary>
    /// Dựng handler và trả kèm callback lấy entity đã được AddAsync — cần để soi PayloadJson thực tế.
    /// </summary>
    private static (CreateNotificationCommandHandler handler, Func<NotificationEntity?> captured) CreateHandler()
    {
        var (uow, _, notifications) = MockNotificationUnitOfWork.Build();

        NotificationEntity? added = null;
        notifications
            .Setup(r => r.AddAsync(It.IsAny<NotificationEntity>()))
            .Callback<NotificationEntity>(e => added = e)
            .Returns(Task.CompletedTask);

        return (new CreateNotificationCommandHandler(uow.Object), () => added);
    }

    /// <summary>UTCID01 — BypassQuietHours=false: PayloadJson giữ nguyên, Title/Body được Trim().</summary>
    [Fact]
    public async Task Handle_WithoutBypass_CreatesPendingNotificationAndKeepsPayload()
    {
        var (handler, captured) = CreateHandler();
        var command = BuildCommand(
            bypassQuietHours: false,
            payloadJson: """{"ticketId":"abc"}""",
            title: "  Tiêu đề  ",
            body: "  Nội dung  ");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);
        result.Message.Should().Be("Notification created successfully.");

        var entity = captured();
        entity.Should().NotBeNull();
        entity!.Status.Should().Be(NotificationStatusEnum.Pending);
        entity.Title.Should().Be("Tiêu đề");
        entity.Body.Should().Be("Nội dung");
        entity.PayloadJson.Should().Be("""{"ticketId":"abc"}""");
        result.Data.Should().Be(entity.Id);
    }

    /// <summary>UTCID02 — BypassQuietHours=true trên JSON object hợp lệ: thêm field, giữ field cũ.</summary>
    [Fact]
    public async Task Handle_BypassWithValidJsonObject_MergesFlagAndKeepsExistingFields()
    {
        var (handler, captured) = CreateHandler();
        var command = BuildCommand(bypassQuietHours: true, payloadJson: """{"ticketId":"abc"}""");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        using var doc = JsonDocument.Parse(captured()!.PayloadJson!);
        doc.RootElement.GetProperty("bypassQuietHours").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("ticketId").GetString().Should().Be("abc");
    }

    /// <summary>UTCID03 — BypassQuietHours=true khi payload rỗng/null: tạo JSON object mới chỉ có cờ.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_BypassWithEmptyPayload_CreatesObjectWithFlagOnly(string? payload)
    {
        var (handler, captured) = CreateHandler();
        var command = BuildCommand(bypassQuietHours: true, payloadJson: payload);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        using var doc = JsonDocument.Parse(captured()!.PayloadJson!);
        doc.RootElement.GetProperty("bypassQuietHours").GetBoolean().Should().BeTrue();
        doc.RootElement.EnumerateObject().Should().HaveCount(1);
    }

    /// <summary>
    /// UTCID04 — BypassQuietHours=true nhưng payload không phải JSON object:
    /// handler không được ném lỗi, phải wrap lại thành { bypassQuietHours, original }.
    /// </summary>
    [Fact]
    public async Task Handle_BypassWithUnparsablePayload_WrapsOriginalWithoutThrowing()
    {
        var (handler, captured) = CreateHandler();
        var command = BuildCommand(bypassQuietHours: true, payloadJson: "khong-phai-json");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        using var doc = JsonDocument.Parse(captured()!.PayloadJson!);
        doc.RootElement.GetProperty("bypassQuietHours").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("original").GetString().Should().Be("khong-phai-json");
    }

    /// <summary>
    /// UTCID05 — EntityType có khoảng trắng thừa được Trim(), EntityId được giữ nguyên;
    /// notification luôn khởi tạo ở trạng thái Pending để dispatcher xử lý sau.
    /// </summary>
    [Fact]
    public async Task Handle_TrimsEntityTypeAndPersistsEntityId()
    {
        var (handler, captured) = CreateHandler();
        var entityId = Guid.NewGuid();
        var command = BuildCommand();
        command.EntityType = "  Ticket  ";
        command.EntityId = entityId;

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var entity = captured()!;
        entity.EntityType.Should().Be("Ticket");
        entity.EntityId.Should().Be(entityId);
        entity.Status.Should().Be(NotificationStatusEnum.Pending);
        entity.UserId.Should().Be(command.UserId);
    }
}
