using NotificationService.Application.CQRS.Query.NotificationTemplate;
using NotificationService.Application.CQRS.Command.NotificationGroup;
using NotificationService.Application.CQRS.Command.NotificationTemplate;
using NotificationService.Application.CQRS.Command.Preference;
using NotificationService.Domain.Enums;

namespace NotificationService.UnitTests.Handlers;

/// <summary>
/// Phủ nốt các command của NotificationService còn để trống ở tầng <c>ValidateAsync</c>.
///
/// <para>Đáng chú ý nhất là <see cref="UpdateNotificationPreferenceCommand"/>: chuỗi giờ im lặng
/// (<c>QuietHours</c>) do người dùng nhập được parse theo định dạng <c>HH:mm</c>. Nếu luật này
/// không chạy, một chuỗi sai định dạng sẽ đi thẳng xuống handler.</para>
/// </summary>
public class UpdateNotificationPreferenceCommandValidationTests
{
    private static UpdateNotificationPreferenceCommand Valid() => new()
    {
        UserId = Guid.NewGuid(),
        QuietHoursStart = "22:00",
        QuietHoursEnd = "07:30",
        TimeZone = "Asia/Ho_Chi_Minh"
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyUserId_Fails()
    {
        var c = Valid();
        c.UserId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    /// <summary>Chuỗi giờ sai định dạng bị chặn ngay ở command, không để handler tự vỡ.</summary>
    [Theory]
    [InlineData("25:00")]
    [InlineData("22:99")]
    [InlineData("10 giờ")]
    [InlineData("abc")]
    [InlineData("7:00 PM")]
    public async Task InvalidQuietHoursStart_Fails(string value)
    {
        var c = Valid();
        c.QuietHoursStart = value;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "QuietHoursStart");
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("--:--")]
    public async Task InvalidQuietHoursEnd_Fails(string value)
    {
        var c = Valid();
        c.QuietHoursEnd = value;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "QuietHoursEnd");
    }

    /// <summary>Giờ im lặng là tuỳ chọn — bỏ trống thì không kiểm định dạng.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EmptyQuietHours_Passes(string? value)
    {
        var c = Valid();
        c.QuietHoursStart = value;
        c.QuietHoursEnd = value;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    /// <summary>Các mốc biên hợp lệ của định dạng 24 giờ.</summary>
    [Theory]
    [InlineData("00:00")]
    [InlineData("23:59")]
    public async Task QuietHoursAtBoundary_Passes(string value)
    {
        var c = Valid();
        c.QuietHoursStart = value;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "QuietHoursStart");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingTimeZone_Fails(string tz)
    {
        var c = Valid();
        c.TimeZone = tz;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TimeZone");
    }

    [Fact]
    public async Task TimeZoneTooLong_Fails()
    {
        var c = Valid();
        c.TimeZone = new string('z', 101);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TimeZone");
    }
}

public class NotificationTemplateCreateCommandValidationTests
{
    private static NotificationTemplateCreateCommand Valid() => new()
    {
        Type = NotificationTypeEnum.System,
        Channel = NotificationChannelEnum.InApp,
        TitleTemplate = "Ticket {{code}} updated",
        BodyTemplate = "The ticket {{code}} moved to {{status}}.",
        ActorUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    /// <summary>Người thực hiện là bắt buộc vì thao tác này được ghi vào nhật ký kiểm toán.</summary>
    [Fact]
    public async Task EmptyActorUserId_Fails()
    {
        var c = Valid();
        c.ActorUserId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "ActorUserId");
    }
}

/// <summary>
/// Luật validate của lệnh thêm nhiều thành viên vào nhóm thông báo.
///
/// <para>Trần <c>MaxUserIdsPerRequest</c> không phải giới hạn nghiệp vụ mà là chặn payload rác:
/// mỗi id sinh một dòng INSERT trong cùng một transaction.</para>
/// </summary>
public class NotificationGroupAddMembersCommandValidationTests
{
    private static NotificationGroupAddMembersCommand Valid() => new()
    {
        GroupId = Guid.NewGuid(),
        UserIds = [Guid.NewGuid(), Guid.NewGuid()],
        ActorUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyUserIds_Fails()
    {
        var c = Valid();
        c.UserIds = [];

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "UserIds" && e.Detail.Contains("At least one"));
    }

    [Fact]
    public async Task TooManyUserIds_Fails()
    {
        var c = Valid();
        c.UserIds = Enumerable
            .Range(0, NotificationGroupAddMembersCommand.MaxUserIdsPerRequest + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "UserIds"
            && e.Detail.Contains("maximum"));
    }

    /// <summary>Đúng trần cho phép vẫn hợp lệ.</summary>
    [Fact]
    public async Task ExactlyMaxUserIds_Passes()
    {
        var c = Valid();
        c.UserIds = Enumerable
            .Range(0, NotificationGroupAddMembersCommand.MaxUserIdsPerRequest)
            .Select(_ => Guid.NewGuid())
            .ToList();

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ListContainingEmptyGuid_Fails()
    {
        var c = Valid();
        c.UserIds = [Guid.NewGuid(), Guid.Empty];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "UserIds"
            && e.Detail.Contains("empty id"));
    }

    /// <summary>Người thực hiện lấy từ JWT — thiếu nghĩa là request không xác định được chủ thể.</summary>
    [Fact]
    public async Task MissingActor_Fails()
    {
        var c = Valid();
        c.ActorUserId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ActorUserId");
    }
}

/// <summary>
/// Luật validate của hai query template. Id lấy từ route nên rỗng nghĩa là route sai hoặc client
/// tự dựng request — chặn ngay thay vì để handler truy vấn bằng Guid.Empty.
/// </summary>
public class NotificationTemplateQueryValidationTests
{
    [Fact]
    public async Task GetById_ValidId_Passes()
    {
        var r = await new NotificationTemplateGetByIdQuery { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
        r.ListErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_EmptyId_Fails()
    {
        var r = await new NotificationTemplateGetByIdQuery().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Id" && e.Detail.Contains("Invalid template Id"));
    }

    [Fact]
    public async Task Preview_ValidId_Passes()
    {
        var r = await new NotificationTemplatePreviewQuery { Id = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_EmptyId_Fails()
    {
        var r = await new NotificationTemplatePreviewQuery().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "Id");
    }

    /// <summary>SampleData là tuỳ chọn — không gửi thì render với model rỗng.</summary>
    [Fact]
    public async Task Preview_NullSampleData_Passes()
    {
        var r = await new NotificationTemplatePreviewQuery
        {
            Id = Guid.NewGuid(),
            SampleData = null
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }
}
