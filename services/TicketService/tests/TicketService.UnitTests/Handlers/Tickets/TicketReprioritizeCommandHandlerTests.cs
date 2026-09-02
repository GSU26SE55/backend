using FluentAssertions;
using Moq;
using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Application.CQRS.Handler.Tickets;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.Tickets;

/// <summary>
/// Quy tắc theo User Guide §3.8/§3.9: mức ưu tiên suy ra từ Impact × Urgency (không nhập
/// thẳng) và chỉ đổi được khi phiếu CHƯA giao cho nhân viên (status Open). Phiếu đang xử lý
/// thì dùng chuyển cấp (§3.12) chứ không đổi mức / dời hạn.
/// </summary>
public class TicketReprioritizeCommandHandlerTests
{
    [Fact]
    public async Task Handle_ActiveIncident_IsRejectedAndKeepsUrgent()
    {
        // Declare Incident dừng SLA timer và bắn lệnh cô lập pin. Hạ cấp lại về P1/P2/P3
        // sẽ hồi sinh timer và bỏ lại cờ incident treo, nên phải bị chặn hẳn.
        var episodeId = Guid.NewGuid();
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.Urgent);
        ticket.IsIncident = true;
        ticket.ActiveIncidentEpisodeId = episodeId;
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var logger = new Mock<IActivityLogger>();
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, logger.Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Manager tries to downgrade an incident",
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        ticket.Priority.Should().Be(TicketPriorityEnum.Urgent);
        ticket.IsIncident.Should().BeTrue();
        ticket.ActiveIncidentEpisodeId.Should().Be(episodeId);
        logger.Verify(x => x.LogAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<ActorRoleEnum>(), It.IsAny<string>(),
            It.IsAny<ActivityActionEnum>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Handle_EnvironmentalTicketWithoutActiveEpisode_CanBeReprioritized()
    {
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        ticket.IsIncident = true;
        ticket.Origin = TicketOriginEnum.AutoFromEnvironment;
        ticket.ActiveIncidentEpisodeId = null;
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket]);
        var handler = CreateHandler(
            uow.Object,
            new Mock<ISlaCalculator>().Object,
            new Mock<IActivityLogger>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Environmental impact reassessed",
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        ticket.IsIncident.Should().BeTrue();
        ticket.ActiveIncidentEpisodeId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OpenTicket_DerivesPriorityFromMatrixAndAudits()
    {
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var logger = new Mock<IActivityLogger>();
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, logger.Object);

        // Site × High → P1 theo bảng §3.9.
        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "The whole site is affected",
            ManagerId = Guid.NewGuid(),
            ManagerName = "Manager"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        ticket.Priority.Should().Be(TicketPriorityEnum.P1Critical);
        // Impact/Urgency phải được lưu lại, nếu không chúng sẽ mâu thuẫn với Priority hiển thị.
        ticket.ImpactScope.Should().Be(ImpactScopeEnum.Site);
        ticket.UrgencyLevel.Should().Be(UrgencyLevelEnum.High);
        logger.Verify(x => x.LogAsync(ticket.Id, It.IsAny<Guid>(), ActorRoleEnum.Manager, "Manager",
            ActivityActionEnum.PriorityAssigned, "P3Normal",
            "P1Critical (Impact: Site, Urgency: High)", "The whole site is affected"), Times.Once);
    }

    [Fact]
    public async Task Handle_OpenTicketWithTimer_UpdatesTicketPriorityAndPreservesResponseTimer()
    {
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        var startedAt = DateTime.UtcNow.AddHours(-2);
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Type = SlaTimerTypeEnum.Response,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = startedAt,
            OriginalDueAt = startedAt.AddHours(72),
            DueAt = startedAt.AddHours(72),
            TotalPausedMinutes = 30,
            Status = SlaTimerStatusEnum.Running
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, slaTimerSeed: new[] { timer });
        var calculator = new Mock<ISlaCalculator>();
        var handler = CreateHandler(uow.Object, calculator.Object, new Mock<IActivityLogger>().Object);

        // SingleAsset × High → P2.
        await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.SingleAsset,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Only one battery but urgent"
        }, CancellationToken.None);

        ticket.Priority.Should().Be(TicketPriorityEnum.P2High);
        // Response SLA timer giữ nguyên mức ưu tiên ban đầu lúc tạo phiếu
        timer.Priority.Should().Be(TicketPriorityEnum.P3Normal);
        timer.DueAt.Should().Be(startedAt.AddHours(72));
        timer.Status.Should().Be(SlaTimerStatusEnum.Running);
        timer.BreachAt.Should().BeNull();
    }

    [Theory]
    [InlineData(TicketStatusEnum.Pending)]
    [InlineData(TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.Request)]
    [InlineData(TicketStatusEnum.ReAssign)]
    [InlineData(TicketStatusEnum.Completed)]
    [InlineData(TicketStatusEnum.Closed)]
    public async Task Handle_TicketNotOpen_IsRejected(TicketStatusEnum status)
    {
        var ticket = Ticket(status, TicketPriorityEnum.P3Normal);
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Review"
        }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        ticket.Priority.Should().Be(TicketPriorityEnum.P3Normal);
    }

    [Fact]
    public async Task Handle_AssignedTicket_DoesNotTouchSlaDeadline()
    {
        // §3.8: SLA breach thì bổ sung nhân lực, KHÔNG gia hạn. Phiếu đã giao mà đổi được
        // mức ưu tiên đồng nghĩa dời được deadline — đây là hồi quy cần chặn.
        var ticket = Ticket(TicketStatusEnum.InProgress, TicketPriorityEnum.P3Normal);
        var dueAt = DateTime.UtcNow.AddHours(10);
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = DateTime.UtcNow.AddHours(-2),
            OriginalDueAt = dueAt,
            DueAt = dueAt,
            Status = SlaTimerStatusEnum.Running
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket }, slaTimerSeed: new[] { timer });
        var calculator = new Mock<ISlaCalculator>();
        var handler = CreateHandler(uow.Object, calculator.Object, new Mock<IActivityLogger>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.MultiSite,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Raise the priority"
        }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
        timer.DueAt.Should().Be(dueAt);
        timer.Priority.Should().Be(TicketPriorityEnum.P3Normal);
        calculator.Verify(x => x.CalculateDueDate(It.IsAny<DateTime>(), It.IsAny<TicketPriorityEnum>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MergedDuplicate_IsRejected()
    {
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        ticket.CloseReason = TicketCloseReasonEnum.MergedDuplicate;
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Review"
        }, CancellationToken.None);

        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ValidateAsync_InvalidReprioritizeRequest_ReturnsValidationErrors()
    {
        var command = new TicketReprioritizeCommand
        {
            TicketId = Guid.Empty,
            Impact = (ImpactScopeEnum)999,
            Urgency = (UrgencyLevelEnum)999,
            Reason = " "
        };

        var result = await command.ValidateAsync();

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().HaveCount(4);
    }

    [Fact]
    public async Task Handle_SoftDeletedTicket_IsExcluded()
    {
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        ticket.IsDeleted = true;
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: new[] { ticket });
        var handler = CreateHandler(uow.Object, new Mock<ISlaCalculator>().Object, new Mock<IActivityLogger>().Object);

        var result = await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.Site,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Priority reassessment"
        }, CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_OpenTicket_NewPriorityWindowAlreadyElapsed_ResetsWindowFromReprioritizeTime()
    {
        // Bug regression: P3→P2, ticket open 45h. CalculateResponseDueDate(startedAt, P2) = startedAt+24h
        // which lies in the past. Handler must reset StartedAt to nowUtc and grant a fresh P2 window
        // so SlaTimerBackgroundService does not see DueAt<=now and escalate immediately.
        var startedAt = DateTime.UtcNow.AddHours(-45);
        var ticket = Ticket(TicketStatusEnum.Open, TicketPriorityEnum.P3Normal);
        var timer = new SlaTimer
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Priority = TicketPriorityEnum.P3Normal,
            StartedAt = startedAt,
            OriginalDueAt = startedAt.AddHours(72),
            DueAt = startedAt.AddHours(72),
            Status = SlaTimerStatusEnum.Running
        };
        var (uow, _, _, _, _, _, _) = MockTicketUnitOfWork.Build(ticketSeed: [ticket], slaTimerSeed: [timer]);

        var calculator = new Mock<ISlaCalculator>();
        // First call: old startedAt → result is in the past (P2 24h window already elapsed)
        var staleDueAt = startedAt.AddHours(24); // = UtcNow - 21h → past
        calculator.Setup(x => x.CalculateResponseDueDate(startedAt, TicketPriorityEnum.P2High))
                  .Returns(staleDueAt);
        // Second call: fresh anchor after reset → future
        var freshDueAt = DateTime.UtcNow.AddHours(24);
        calculator.Setup(x => x.CalculateResponseDueDate(
                      It.Is<DateTime>(d => d > startedAt), TicketPriorityEnum.P2High))
                  .Returns(freshDueAt);

        var handler = CreateHandler(uow.Object, calculator.Object, new Mock<IActivityLogger>().Object);

        await handler.Handle(new TicketReprioritizeCommand
        {
            TicketId = ticket.Id,
            Impact = ImpactScopeEnum.SingleAsset,
            Urgency = UrgencyLevelEnum.High,
            Reason = "Situation worsened"
        }, CancellationToken.None);

        timer.DueAt.Should().Be(startedAt.AddHours(72), "Response SLA timer due date is preserved from ticket creation");
        timer.StartedAt.Should().Be(startedAt, "Response SLA timer startedAt is preserved from ticket creation");
        timer.Priority.Should().Be(TicketPriorityEnum.P3Normal, "Response SLA timer priority is preserved from ticket creation");
        ticket.Priority.Should().Be(TicketPriorityEnum.P2High, "Ticket priority is updated for resolution stage");
    }

    private static TicketReprioritizeCommandHandler CreateHandler(
        TicketService.Application.Interfaces.Repositories.ITicketUnitOfWork uow,
        ISlaCalculator calculator,
        IActivityLogger activityLogger)
        => new(uow, calculator, new TicketService.Infrastructure.Implements.Utils.PriorityCalculator(), activityLogger);

    private static Ticket Ticket(TicketStatusEnum status, TicketPriorityEnum priority) => new() { Id = Guid.NewGuid(), Code = "TKT-1", Title = "Test", Description = "Test", Status = status, Priority = priority };
}
