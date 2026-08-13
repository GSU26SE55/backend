using FluentAssertions;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.StateMachine;

public class TicketStateMachineTests
{
    private readonly ITicketStateMachine _sut = new TicketStateMachine(new TransitionRuleProvider());

    private static Ticket CreateTicket(
        TicketStatusEnum status,
        Guid? primaryHandlerStaffId = null,
        Guid? customerId = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = $"T-{Guid.NewGuid():N}"[..10],
            Title = "Test ticket",
            Description = "Test description",
            Status = status,
            PrimaryHandlerStaffId = primaryHandlerStaffId,
            CustomerId = customerId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };

    [Theory]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.InProgress, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.Pending, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Pending, TicketStatusEnum.InProgress, ActorRoleEnum.System)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.ReAssign, ActorRoleEnum.System)]
    [InlineData(TicketStatusEnum.Request, TicketStatusEnum.InProgress, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Request, TicketStatusEnum.ReAssign, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.ReAssign, TicketStatusEnum.InProgress, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.ReAssign, TicketStatusEnum.Pending, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Completed, TicketStatusEnum.Closed, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Completed, TicketStatusEnum.InProgress, ActorRoleEnum.Manager)]
    public void CanTransition_AuthorizedLifecycleTransition_IsAllowed(
        TicketStatusEnum from,
        TicketStatusEnum to,
        ActorRoleEnum actorRole)
    {
        var result = _sut.CanTransition(CreateTicket(from), to, actorRole, Guid.NewGuid());

        result.IsAllowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Pending)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Request)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Completed)]
    [InlineData(TicketStatusEnum.Pending, TicketStatusEnum.InProgress)]
    public void CanTransition_PrimaryStaffTransition_IsAllowed(
        TicketStatusEnum from,
        TicketStatusEnum to)
    {
        var staffId = Guid.NewGuid();
        var result = _sut.CanTransition(CreateTicket(from, staffId), to, ActorRoleEnum.Staff, staffId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_NonPrimaryStaffTransition_IsDenied()
    {
        var ticket = CreateTicket(TicketStatusEnum.InProgress, Guid.NewGuid());

        var result = _sut.CanTransition(
            ticket,
            TicketStatusEnum.Completed,
            ActorRoleEnum.Staff,
            Guid.NewGuid());

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("PrimaryHandler");
    }

    [Fact]
    public void CanTransition_ClosedToOpen_AllowsOwner()
    {
        var customerId = Guid.NewGuid();
        var result = _sut.CanTransition(
            CreateTicket(TicketStatusEnum.Closed, customerId: customerId),
            TicketStatusEnum.Open,
            ActorRoleEnum.Customer,
            customerId);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_ClosedToOpen_DeniesDifferentCustomer()
    {
        var result = _sut.CanTransition(
            CreateTicket(TicketStatusEnum.Closed, customerId: Guid.NewGuid()),
            TicketStatusEnum.Open,
            ActorRoleEnum.Customer,
            Guid.NewGuid());

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void CanTransition_ClosedRejected_IsTerminal()
    {
        var result = _sut.CanTransition(
            CreateTicket(TicketStatusEnum.ClosedRejected),
            TicketStatusEnum.Open,
            ActorRoleEnum.Admin,
            Guid.NewGuid());

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("terminal");
    }

    [Fact]
    public async Task ExecuteAsync_Completed_SetsResolutionMetadata()
    {
        var staffId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.InProgress, staffId);
        var context = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Staff,
            ActorUserId = staffId,
            Payload = new Dictionary<string, object?> { ["ResolutionSummary"] = "Completed safely" }
        };

        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Completed, context, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Completed);
        ticket.ResolvedByStaffId.Should().Be(staffId);
        ticket.ResolutionSummary.Should().Be("Completed safely");
        ticket.ResolvedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_Closed_SetsApprovalAndClosureMetadata()
    {
        var managerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.Completed, Guid.NewGuid());

        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Closed, new TransitionContext
        {
            ActorRole = ActorRoleEnum.Manager,
            ActorUserId = managerId
        }, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Closed);
        ticket.ApprovedByManagerId.Should().Be(managerId);
        ticket.ApprovedAt.Should().NotBeNull();
        ticket.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_Reopen_IncrementsReopenCount()
    {
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.Closed, customerId: customerId);

        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Open, new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer,
            ActorUserId = customerId,
            Payload = new Dictionary<string, object?> { ["ReopenReason"] = "Issue returned" }
        }, CancellationToken.None);

        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.ReopenCount.Should().Be(1);
        ticket.Reason.Should().Be("Issue returned");
    }

    [Fact]
    public async Task ExecuteAsync_DeniedTransition_DoesNotMutateTicket()
    {
        var ticket = CreateTicket(TicketStatusEnum.Open);
        var originalUpdatedAt = ticket.UpdatedAt;

        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Completed, new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer,
            ActorUserId = ticket.CustomerId
        }, CancellationToken.None);

        result.IsAllowed.Should().BeFalse();
        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.UpdatedAt.Should().Be(originalUpdatedAt);
    }
}
