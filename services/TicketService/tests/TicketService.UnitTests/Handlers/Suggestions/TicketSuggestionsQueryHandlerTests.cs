using FluentAssertions;
using Moq;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Handler.Suggestions;
using TicketService.Application.CQRS.Query.Suggestions;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Suggestions;

public class TicketStaffSuggestionsQueryHandlerTests
{
    private readonly Mock<IAiStaffSuggestClient> _ai = new();

    private static Ticket MakeTicket(Guid id) => new()
    {
        Id = id,
        Code = "TKT-001",
        Title = "Pin không sạc",
        Description = "Sạc không vào",
        Category = TicketCategoryEnum.Charging,
        Priority = TicketPriorityEnum.P2High,
        Status = TicketStatusEnum.Open,
        CustomerId = Guid.NewGuid(),
    };

    private static StaffAccount MakeStaff(Guid accountId, string role = "Staff", bool available = true) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        FullName = "Nguyễn Văn A",
        Email = "a@x.local",
        Role = role,
        Status = AccountStatusEnum.Active,
        IsAvailable = available,
        SkillTier = StaffSkillTierEnum.ModuleSpecialist,
        SkillCodes = new List<string> { "charging" },
        MaxConcurrentTickets = 8,
    };

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build();
        var handler = new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object);

        var res = await handler.Handle(
            new TicketStaffSuggestionsQuery { TicketId = Guid.NewGuid() }, CancellationToken.None);

        res.IsSuccess.Should().BeFalse();
        res.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ExcludesManagerAndAdminFromCandidates()
    {
        // staff_accounts chứa CẢ Manager/Admin — không lọc theo Role thì Manager tự thấy mình.
        var ticketId = Guid.NewGuid();
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { MakeTicket(ticketId) },
            staffSeed: new[]
            {
                MakeStaff(Guid.NewGuid()),
                MakeStaff(Guid.NewGuid(), role: "Manager"),
                MakeStaff(Guid.NewGuid(), role: "Admin"),
            });

        IReadOnlyList<AiStaffCandidate>? captured = null;
        _ai.Setup(x => x.SuggestStaffAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AiStaffCandidate>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, IReadOnlyList<AiStaffCandidate>, int, CancellationToken>(
                (_, _, _, c, _, _) => captured = c)
            .ReturnsAsync(new AiStaffSuggestResult(Array.Empty<AiStaffSuggestion>(), ""));

        await new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object)
            .Handle(new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_ExcludesUnavailableStaff()
    {
        var ticketId = Guid.NewGuid();
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { MakeTicket(ticketId) },
            staffSeed: new[] { MakeStaff(Guid.NewGuid(), available: false) });

        var handler = new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object);
        var res = await handler.Handle(
            new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.Data!.Items.Should().BeEmpty();
        res.Data.Note.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_WorkloadExcludesPreviousPrimaryHandler()
    {
        // PreviousPrimaryHandler = người ĐÃ bị chuyển giao; đếm cả họ là coi staff đầy tải oan.
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var otherTicketId = Guid.NewGuid();
        var otherTicket = MakeTicket(otherTicketId);

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { MakeTicket(ticketId), otherTicket },
            staffSeed: new[] { MakeStaff(staffId) },
            assignmentSeed: new[]
            {
                new TicketAssignment
                {
                    Id = Guid.NewGuid(), TicketId = otherTicketId, StaffId = staffId,
                    Role = AssignmentRoleEnum.PreviousPrimaryHandler, Ticket = otherTicket,
                },
            });

        IReadOnlyList<AiStaffCandidate>? captured = null;
        _ai.Setup(x => x.SuggestStaffAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AiStaffCandidate>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, IReadOnlyList<AiStaffCandidate>, int, CancellationToken>(
                (_, _, _, c, _, _) => captured = c)
            .ReturnsAsync(new AiStaffSuggestResult(Array.Empty<AiStaffSuggestion>(), ""));

        await new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object)
            .Handle(new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        captured!.Single().ActiveTickets.Should().Be(0);
    }

    [Fact]
    public async Task Handle_AiUnavailable_ReturnsFlagNotError()
    {
        // AI hỏng KHÔNG được chặn triage — phải phân biệt với "không có ai phù hợp".
        var ticketId = Guid.NewGuid();
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { MakeTicket(ticketId) },
            staffSeed: new[] { MakeStaff(Guid.NewGuid()) });

        _ai.Setup(x => x.SuggestStaffAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AiStaffCandidate>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AiStaffSuggestResult?)null);

        var res = await new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object)
            .Handle(new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        res.IsSuccess.Should().BeTrue();
        res.StatusCode.Should().Be(200);
        res.Data!.AiAvailable.Should().BeFalse();
        res.Data.Note.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Handle_NullPriority_SendsZero()
    {
        // Ticket chưa triage chưa có Priority — gửi 0 để AI bỏ qua lọc tier.
        var ticketId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId);
        ticket.Priority = null;

        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { ticket }, staffSeed: new[] { MakeStaff(Guid.NewGuid()) });

        var sentPriority = -1;
        _ai.Setup(x => x.SuggestStaffAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AiStaffCandidate>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, string, IReadOnlyList<AiStaffCandidate>, int, CancellationToken>(
                (_, p, _, _, _, _) => sentPriority = p)
            .ReturnsAsync(new AiStaffSuggestResult(Array.Empty<AiStaffSuggestion>(), ""));

        await new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object)
            .Handle(new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        sentPriority.Should().Be(0);
    }

    [Fact]
    public async Task Handle_EnrichesSuggestionWithStaffDetails()
    {
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(
            ticketSeed: new[] { MakeTicket(ticketId) }, staffSeed: new[] { MakeStaff(staffId) });

        _ai.Setup(x => x.SuggestStaffAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AiStaffCandidate>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiStaffSuggestResult(
                new[] { new AiStaffSuggestion(staffId.ToString(), "Nguyễn Văn A", 0.9, "khớp kỹ năng", true) },
                ""));

        var res = await new TicketStaffSuggestionsQueryHandler(uow.Object, _ai.Object)
            .Handle(new TicketStaffSuggestionsQuery { TicketId = ticketId }, CancellationToken.None);

        var item = res.Data!.Items.Single();
        item.SkillTier.Should().Be((int)StaffSkillTierEnum.ModuleSpecialist);
        item.SkillCodes.Should().Contain("charging");
        item.MaxConcurrentTickets.Should().Be(8);
        item.Reason.Should().Be("khớp kỹ năng");
    }
}
