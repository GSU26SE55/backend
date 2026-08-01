using System.Reflection;
using FluentAssertions;
using SharedContracts.Audit;
using TicketService.Application.CQRS.Notification.Audit;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Handlers.Audit;

/// <summary>
/// Sprint audit #AUDIT-24 + Sprint Chat DoD — chặn trôi giữa <see cref="TicketAuditActionEnum"/>
/// (nguồn sự thật phía TicketService) và <see cref="ActionCodes.Ticket"/> (hằng dùng chung cho
/// AuditAggregator + FE dropdown).
///
/// Vì sao cần: `TicketAuditTrailNotification.For` lấy action code bằng <c>action.ToString()</c>.
/// Thêm giá trị enum mà quên khai hằng tương ứng thì KHÔNG có lỗi biên dịch — chỉ tới lúc
/// AuditAggregator/FE lọc theo action mới phát hiện, mà lúc đó vết audit đã ghi sai/thiếu rồi.
/// Đây đúng kiểu lỗi đã xảy ra với <c>NotificationCategoryMap</c> (thiếu 2 type Blog).
/// </summary>
public class TicketAuditActionCodeTests
{
    private static IReadOnlyDictionary<string, string> TicketActionConstants =>
        typeof(ActionCodes.Ticket)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void EveryEnumValue_HasMatchingActionCodeConstant()
    {
        var constants = TicketActionConstants;

        var missing = Enum.GetNames<TicketAuditActionEnum>()
            .Where(name => !constants.ContainsKey(name))
            .ToList();

        missing.Should().BeEmpty(
            "mọi TicketAuditActionEnum phải có hằng tương ứng trong ActionCodes.Ticket");
    }

    [Fact]
    public void EveryActionCodeConstant_HasMatchingEnumValue()
    {
        var enumNames = Enum.GetNames<TicketAuditActionEnum>().ToHashSet();

        var orphan = TicketActionConstants.Keys
            .Where(name => !enumNames.Contains(name))
            .ToList();

        orphan.Should().BeEmpty(
            "hằng thừa trong ActionCodes.Ticket nghĩa là FE/aggregator lọc theo action không bao giờ có dữ liệu");
    }

    [Fact]
    public void ActionCodeConstantValue_EqualsItsName()
    {
        // `For()` sinh code bằng `action.ToString()` — nếu giá trị hằng khác tên hằng thì chuỗi ghi
        // vào DB và chuỗi FE gửi lên sẽ lệch nhau.
        foreach (var (name, value) in TicketActionConstants)
            value.Should().Be(name, $"ActionCodes.Ticket.{name} phải có giá trị trùng tên");
    }

    [Theory]
    [InlineData(TicketAuditActionEnum.ChatCreated)]
    [InlineData(TicketAuditActionEnum.ChatEdited)]
    [InlineData(TicketAuditActionEnum.ChatDeleted)]
    [InlineData(TicketAuditActionEnum.ChatPinned)]
    [InlineData(TicketAuditActionEnum.ChatUnpinned)]
    [InlineData(TicketAuditActionEnum.ChatReacted)]
    [InlineData(TicketAuditActionEnum.ChatMentioned)]
    public void ChatActions_MapToKnownCategoryAndSeverity(TicketAuditActionEnum action)
    {
        var notification = TicketAuditTrailNotification.For(action, Guid.NewGuid());

        notification.ActionCode.Should().Be(action.ToString());
        AuditCategories.All.Should().Contain(notification.ActionCategory,
            "category phải nằm trong tập đóng — aggregator validate exact-match (#AUDIT-17)");
        Severities.All.Should().Contain(notification.Severity,
            "severity phải nằm trong tập đóng — aggregator validate exact-match (#AUDIT-17)");
    }
}
