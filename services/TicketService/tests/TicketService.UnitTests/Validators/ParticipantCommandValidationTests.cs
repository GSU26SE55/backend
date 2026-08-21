using TicketService.Application.CQRS.Command.Participants;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

/// <summary>
/// Luật validate của nhóm Participant.
///
/// <para>Luật quan trọng nhất là danh sách <c>ManuallyAssignableTypes</c>: chỉ Collaborator, Watcher
/// và Delegate được gán tay. Các loại còn lại (Owner, PrimaryAssignee, PreviousAssignee) do luồng
/// nghiệp vụ tự sinh — cho phép gán tay sẽ tạo ra ticket có hai Owner hoặc người xử lý chính được
/// gán ngoài quy trình phân công.</para>
/// </summary>
public class ParticipantAddCommandValidationTests
{
    private static ParticipantAddCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        UserRole = ActorRoleEnum.Staff,
        ParticipantType = ParticipantTypeEnum.Collaborator,
        ActorUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyTicketId_Fails()
    {
        var c = Valid();
        c.TicketId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
    }

    [Fact]
    public async Task EmptyUserId_Fails()
    {
        var c = Valid();
        c.UserId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Theory]
    [InlineData(ParticipantTypeEnum.Collaborator)]
    [InlineData(ParticipantTypeEnum.Watcher)]
    [InlineData(ParticipantTypeEnum.Delegate)]
    public async Task ManuallyAssignableType_Passes(ParticipantTypeEnum type)
    {
        var c = Valid();
        c.ParticipantType = type;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "ParticipantType");
    }

    /// <summary>Loại do hệ thống sinh không được gán tay.</summary>
    [Fact]
    public async Task SystemAssignedType_Fails()
    {
        var c = Valid();
        c.ParticipantType = ParticipantTypeEnum.Owner;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ParticipantType");
    }

    /// <summary>Giá trị enum ngoài dải cũng bị chặn.</summary>
    [Fact]
    public async Task UndefinedType_Fails()
    {
        var c = Valid();
        c.ParticipantType = (ParticipantTypeEnum)999;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ParticipantType");
    }
}

public class ParticipantUpdateRoleCommandValidationTests
{
    private static ParticipantUpdateRoleCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        ParticipantType = ParticipantTypeEnum.Watcher,
        ActorUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyIds_Fail()
    {
        var c = Valid();
        c.TicketId = Guid.Empty;
        c.UserId = Guid.Empty;

        var r = await c.ValidateAsync();
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task SystemAssignedType_Fails()
    {
        var c = Valid();
        c.ParticipantType = ParticipantTypeEnum.Owner;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "ParticipantType");
    }

    [Theory]
    [InlineData(ParticipantTypeEnum.Collaborator)]
    [InlineData(ParticipantTypeEnum.Delegate)]
    public async Task ManuallyAssignableType_Passes(ParticipantTypeEnum type)
    {
        var c = Valid();
        c.ParticipantType = type;

        (await c.ValidateAsync()).ListErrors.Should().NotContain(e => e.Field == "ParticipantType");
    }
}

public class ParticipantBulkAddCommandValidationTests
{
    private static ParticipantBulkAddItem Item() => new(
        UserId: Guid.NewGuid(),
        UserRole: ActorRoleEnum.Staff,
        ParticipantType: ParticipantTypeEnum.Collaborator,
        CanPost: true,
        CanViewInternal: false);

    private static ParticipantBulkAddCommand Valid() => new()
    {
        TicketId = Guid.NewGuid(),
        Participants = [Item()],
        ActorUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task Valid_Passes() => (await Valid().ValidateAsync()).IsSuccess.Should().BeTrue();

    [Fact]
    public async Task EmptyTicketId_Fails()
    {
        var c = Valid();
        c.TicketId = Guid.Empty;

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "TicketId");
    }

    [Fact]
    public async Task EmptyParticipantList_Fails()
    {
        var c = Valid();
        c.Participants = [];

        var r = await c.ValidateAsync();
        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "Participants");
    }

    [Fact]
    public async Task ItemWithEmptyUserId_Fails()
    {
        var c = Valid();
        c.Participants = [Item() with { UserId = Guid.Empty }];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Participants[0].UserId");
    }

    /// <summary>
    /// Cùng một người xuất hiện hai lần trong lô: lần thứ hai bị báo trùng theo đúng chỉ số,
    /// tránh việc chèn hai bản ghi participant cho một người.
    /// </summary>
    [Fact]
    public async Task DuplicateUserIdInList_Fails()
    {
        var id = Guid.NewGuid();
        var c = Valid();
        c.Participants = [Item() with { UserId = id }, Item() with { UserId = id }];

        var r = await c.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field == "Participants[1].UserId"
            && e.Detail.Contains("Duplicate"));
        r.ListErrors.Should().NotContain(e => e.Field == "Participants[0].UserId");
    }

    [Fact]
    public async Task ItemWithSystemAssignedType_Fails()
    {
        var c = Valid();
        c.Participants = [Item() with { ParticipantType = ParticipantTypeEnum.Owner }];

        (await c.ValidateAsync()).ListErrors.Should().Contain(e => e.Field == "Participants[0].ParticipantType");
    }

    /// <summary>Lỗi được gắn theo chỉ số từng phần tử để client biết dòng nào hỏng.</summary>
    [Fact]
    public async Task MultipleInvalidItems_ReportErrorPerIndex()
    {
        var c = Valid();
        c.Participants =
        [
            Item() with { UserId = Guid.Empty },
            Item() with { ParticipantType = ParticipantTypeEnum.Owner }
        ];

        var r = await c.ValidateAsync();

        r.ListErrors.Should().Contain(e => e.Field.StartsWith("Participants[0]"));
        r.ListErrors.Should().Contain(e => e.Field.StartsWith("Participants[1]"));
    }
}

public class ParticipantRemoveAndLeaveValidationTests
{
    [Fact]
    public async Task Remove_Valid_Passes()
    {
        var r = await new ParticipantRemoveCommand
        {
            TicketId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Remove_EmptyIds_Fail()
    {
        var r = await new ParticipantRemoveCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.StatusCode.Should().Be(400);
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
        r.ListErrors.Should().Contain(e => e.Field == "UserId");
    }

    [Fact]
    public async Task SelfLeave_Valid_Passes()
    {
        var r = await new ParticipantSelfLeaveCommand { TicketId = Guid.NewGuid() }.ValidateAsync();

        r.IsSuccess.Should().BeTrue();
    }

    /// <summary>Người rời là chính người gọi (lấy từ token) nên chỉ TicketId cần kiểm.</summary>
    [Fact]
    public async Task SelfLeave_EmptyTicketId_Fails()
    {
        var r = await new ParticipantSelfLeaveCommand().ValidateAsync();

        r.IsSuccess.Should().BeFalse();
        r.ListErrors.Should().Contain(e => e.Field == "TicketId");
    }
}
