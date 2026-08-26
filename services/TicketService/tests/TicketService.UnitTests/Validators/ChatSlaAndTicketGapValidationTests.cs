using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.CQRS.Command.SLAs;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Đóng nốt các lớp <c>IValidatable</c> còn lại của TicketService: nhóm Chat, hợp nhất ticket,
/// quyết định leo thang, và khoảng thời gian không tính SLA.
/// </summary>
public class ChatReactionRemoveCommandValidationTests
{
    private static ChatReactionRemoveCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        ChatId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        UserRole = ActorRoleEnum.Staff,
        ReactionType = ReactionTypeEnum.ThumbsUp
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var r = await new ChatReactionRemoveCommand { ReactionType = ReactionTypeEnum.ThumbsUp }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "ChatId");
        r.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    /// <summary>Giá trị enum ngoài dải định nghĩa bị từ chối.</summary>
    [Fact]
    public async Task UndefinedReactionType_Fails()
    {
        var c = Valid();
        c.ReactionType = (ReactionTypeEnum)999;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ReactionType");
    }
}

public class ChatAttachmentRemoveCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new ChatAttachmentRemoveCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            AttachmentId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Xoá đính kèm là thao tác không hoàn tác, cả ba id đều phải hợp lệ.</summary>
    [Fact]
    public async Task EmptyIds_Fail()
    {
        var r = await new ChatAttachmentRemoveCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "ChatId");
        r.ListErrors.Should().Contain(e => e.Field == "AttachmentId");
    }

    [Fact]
    public async Task EmptyAttachmentIdOnly_Fails()
    {
        var r = await new ChatAttachmentRemoveCommand
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid()
        }.ValidateAsync();

        r.ListErrors.Should().ContainSingle(e => e.Field == "AttachmentId");
    }
}

public class ChatMarkAsReadCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new ChatMarkAsReadCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var r = await new ChatMarkAsReadCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "UserId");
    }
}

public class TicketMergeCommandValidationTests
{
    private static TicketMergeCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        TargetTicketId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var r = await new TicketMergeCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "TargetTicketId");
    }

    /// <summary>
    /// Hợp nhất một ticket vào chính nó sẽ tạo vòng tham chiếu, nên bị chặn ngay ở command.
    /// </summary>
    [Fact]
    public async Task MergeIntoItself_Fails()
    {
        var id = Guid.NewGuid();
        var r = await new TicketMergeCommand { TicketId = id, TargetTicketId = id }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "TargetTicketId"
            && e.Detail.Contains("into itself"));
    }

    /// <summary>
    /// Khi cả hai id đều rỗng thì chúng bằng nhau, nhưng luật "merge chính nó" không được kích hoạt
    /// — nếu không sẽ báo thừa một lỗi khó hiểu chồng lên lỗi id rỗng.
    /// </summary>
    [Fact]
    public async Task BothEmpty_DoesNotReportMergeIntoItself()
    {
        var r = await new TicketMergeCommand().ValidateAsync();

        r.ListErrors.Should().NotContain(e => e.Detail.Contains("into itself"));
    }
}

public class TicketEscalationDecisionCommandValidationTests
{
    [Fact]
    public async Task Valid_Passes()
    {
        var r = await new TicketEscalationDecisionCommand
        {
            TicketId = Guid.NewGuid(),
            Reason = "Approved: customer confirmed the outage window."
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyTicketId_Fails()
    {
        var r = await new TicketEscalationDecisionCommand { Reason = "Rejected." }.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
    }

    /// <summary>Quyết định của Manager luôn phải kèm lý do vì được ghi vào lịch sử ticket.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingReason_Fails(string reason)
    {
        var r = await new TicketEscalationDecisionCommand
        {
            TicketId = Guid.NewGuid(),
            Reason = reason
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Reason");
    }
}

/// <summary>
/// Khoảng thời gian không tính SLA (nghỉ lễ, ngừng dịch vụ theo kế hoạch). Luật nằm ở lớp cơ sở
/// <see cref="SlaNonWorkingPeriodWriteCommand"/> nên được kiểm qua cả hai lệnh dẫn xuất.
/// </summary>
public class SlaNonWorkingPeriodCommandValidationTests
{
    private static CreateSlaNonWorkingPeriodCommand ValidCreate() => new()
    {
        StartDate = new DateOnly(2026, 4, 30),
        EndDate = new DateOnly(2026, 5, 3),
        Reason = "Reunification Day holiday",
        ActorId = Guid.NewGuid()
    };

    [Fact]
    public async Task Create_Valid_Passes() => (await ValidCreate().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task Create_MissingStartDate_Fails()
    {
        var c = ValidCreate();
        c.StartDate = default;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "StartDate");
    }

    [Fact]
    public async Task Create_MissingEndDate_Fails()
    {
        var c = ValidCreate();
        c.EndDate = default;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "EndDate");
    }

    /// <summary>Khoảng đảo ngược bị chặn; một ngày (start == end) là hợp lệ.</summary>
    [Fact]
    public async Task Create_EndBeforeStart_Fails()
    {
        var c = ValidCreate();
        c.EndDate = c.StartDate.AddDays(-1);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "EndDate"
            && e.Detail.Contains("on or after"));
    }

    [Fact]
    public async Task Create_SingleDayPeriod_Passes()
    {
        var c = ValidCreate();
        c.EndDate = c.StartDate;

        (await c.ValidateAsync()).IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Khi một trong hai mốc bị bỏ trống thì không so sánh thứ tự — tránh báo thừa lỗi
    /// "end trước start" chồng lên lỗi thiếu ngày.
    /// </summary>
    [Fact]
    public async Task Create_MissingDates_DoesNotReportOrderError()
    {
        var c = ValidCreate();
        c.StartDate = default;
        c.EndDate = default;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Detail.Contains("on or after"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_MissingReason_Fails(string reason)
    {
        var c = ValidCreate();
        c.Reason = reason;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Reason");
    }

    [Fact]
    public async Task Create_ReasonTooLong_Fails()
    {
        var c = ValidCreate();
        c.Reason = new string('r', 501);

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Reason"
            && e.Detail.Contains("500"));
    }

    /// <summary>Người thực hiện lấy từ token — thiếu nghĩa là lệnh không gắn được vào ai.</summary>
    [Fact]
    public async Task Create_MissingActorId_Fails()
    {
        var c = ValidCreate();
        c.ActorId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ActorId");
    }

    /// <summary>Lệnh cập nhật dùng chung luật của lớp cơ sở.</summary>
    [Fact]
    public async Task Update_Valid_Passes()
    {
        var r = await new UpdateSlaNonWorkingPeriodCommand
        {
            Id = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 2),
            EndDate = new DateOnly(2026, 9, 2),
            Reason = "National Day",
            ActorId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_InvalidRange_Fails()
    {
        var r = await new UpdateSlaNonWorkingPeriodCommand
        {
            Id = Guid.NewGuid(),
            StartDate = new DateOnly(2026, 9, 5),
            EndDate = new DateOnly(2026, 9, 2),
            Reason = "National Day",
            ActorId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "EndDate");
    }
}

/// <summary>
/// Luật validate của lệnh Customer tự chọn lịch bảo trì định kỳ (GH-1244).
///
/// <para><c>ScheduledStartAt</c> là <c>DateTimeOffset</c>: giá trị mặc định nghĩa là client không gửi
/// mốc thời gian kèm offset, và lịch không có offset sẽ lệch múi giờ khi quy về UTC.</para>
/// </summary>
