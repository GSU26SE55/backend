using System.Text.Json;
using NotificationService.Application.CQRS.Command.Notification;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers.Notification;

/// <summary>
/// Sửa 30/07/2026 — <c>POST /api/notifications</c> không nhắm được người nhận.
///
/// <c>CreateNotificationCommand.UserId</c> từng bị đánh <c>[JsonIgnore]</c> (sao chép nhầm từ
/// <c>MarkNotificationReadCommand</c>, nơi UserId lấy từ claim), trong khi controller KHÔNG gán nó
/// từ token. Kết quả: mọi bản ghi tạo qua REST đều mang <c>Guid.Empty</c> → dispatch worker đánh
/// <c>Failed</c> với lý do <c>empty_user_id</c>. Phát hiện khi test E2E.
///
/// Hai test đầu là **test chặn hồi quy trực tiếp**: nếu ai đó gắn lại <c>[JsonIgnore]</c> thì đỏ ngay.
/// </summary>
public class CreateNotificationUserIdTests
{
    private static readonly JsonSerializerOptions Camel = new(JsonSerializerDefaults.Web);

    private static CreateNotificationCommand Valid(Guid userId) => new()
    {
        UserId = userId,
        Type = NotificationTypeEnum.TicketCreated,
        Channel = NotificationChannelEnum.InApp,
        Title = "Title",
        Body = "Content",
    };

    /// <summary>Đây chính là lỗi: body có `userId` mà deserialize ra `Guid.Empty`.</summary>
    [Fact]
    public void Deserialize_BodyWithUserId_BindsIntoCommand()
    {
        var expected = Guid.NewGuid();
        var json = $$"""
            {
              "userId":  "{{expected}}",
              "type":    1,
              "channel": 4,
              "title":   "Title",
              "body":    "Content"
            }
            """;

        var cmd = JsonSerializer.Deserialize<CreateNotificationCommand>(json, Camel)!;

        cmd.UserId.Should().Be(expected,
            "gắn lại [JsonIgnore] sẽ khiến endpoint tạo bản ghi không có người nhận");
    }

    /// <summary>Chặn hồi quy ở tầng metadata, không phụ thuộc hành vi serializer.</summary>
    [Fact]
    public void UserId_MustNotBeMarkedJsonIgnore()
    {
        var prop = typeof(CreateNotificationCommand).GetProperty(nameof(CreateNotificationCommand.UserId))!;

        prop.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), inherit: true)
            .Should().BeEmpty("UserId đến từ body — controller không gán nó từ claim JWT");
    }

    /// <summary>
    /// Bản ghi không có người nhận là bản ghi chắc chắn thất bại ở dispatch.
    /// Từ chối sớm tốt hơn tạo ra một dòng rác rồi để worker đánh Failed.
    /// </summary>
    [Fact]
    public async Task Validate_EmptyUserId_Returns400()
    {
        var response = await Valid(Guid.Empty).ValidateAsync();

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(400);
        response.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task Validate_RealUserId_Passes()
    {
        var response = await Valid(Guid.NewGuid()).ValidateAsync();

        response.IsSuccess.Should().BeTrue();
        response.ListErrors.Should().BeEmpty();
    }

    /// <summary>Thiếu người nhận không được che mất các lỗi trường khác — gom hết trong 1 lần trả về.</summary>
    [Fact]
    public async Task Validate_CollectsAllErrors_NotJustUserId()
    {
        var cmd = new CreateNotificationCommand
        {
            UserId = Guid.Empty,
            Type = (NotificationTypeEnum)999,
            Channel = (NotificationChannelEnum)999,
            Title = "",
            Body = "",
        };

        var response = await cmd.ValidateAsync();

        response.ListErrors.Select(e => e.Field)
            .Should().Contain(new[] { "UserId", "Type", "Channel", "Title", "Body" });
    }

    /// <summary>
    /// 9 consumer gán UserId bằng code C# chứ không qua JSON — thay đổi này không được ảnh hưởng chúng.
    /// </summary>
    [Fact]
    public async Task ConsumerStyleConstruction_StillWorks()
    {
        var recipient = Guid.NewGuid();

        var cmd = new CreateNotificationCommand
        {
            UserId = recipient,
            Type = NotificationTypeEnum.SlaBreached,
            Channel = NotificationChannelEnum.Push,
            Title = "SLA breach",
            Body = "Ticket overdue",
            EntityType = "Ticket",
            EntityId = Guid.NewGuid(),
            BypassQuietHours = true,
        };

        cmd.UserId.Should().Be(recipient);
        (await cmd.ValidateAsync()).IsSuccess.Should().BeTrue();
    }
}
