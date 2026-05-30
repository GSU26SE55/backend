using FluentAssertions;
using TicketService.Application.StateMachine;
using TicketService.Application.StateMachine.Rules;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.StateMachine;

public class TicketStateMachineTests
{
    private readonly ITicketStateMachine _sut;
    private readonly ITransitionRuleProvider _ruleProvider;

    public TicketStateMachineTests()
    {
        _ruleProvider = new TransitionRuleProvider();
        _sut = new TicketStateMachine(_ruleProvider);
    }

    private static Ticket CreateTicket(TicketStatusEnum status, Guid? assignedStaffId = null, Guid? customerId = null)
    {
        return new Ticket
        {
            Id = Guid.NewGuid(),
            Code = "T-" + Guid.NewGuid().ToString()[..8],
            Title = "Test Ticket",
            Description = "Test Description",
            Status = status,
            AssignedStaffId = assignedStaffId,
            CustomerId = customerId ?? Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
    }

    #region Group 1: Valid Transitions (20 cases)

    [Theory]
    [InlineData(TicketStatusEnum.New, TicketStatusEnum.Open, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.New, TicketStatusEnum.Open, ActorRoleEnum.System)]
    [InlineData(TicketStatusEnum.New, TicketStatusEnum.Approved, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.Approved, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Approved, TicketStatusEnum.Assigned, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Approved, TicketStatusEnum.Escalated, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Approved, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Escalated, TicketStatusEnum.Assigned, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Escalated, TicketStatusEnum.Incident, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Escalated, TicketStatusEnum.ClosedRejected, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Incident, TicketStatusEnum.Assigned, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Resolved, TicketStatusEnum.ClosedPendingRate, ActorRoleEnum.Manager)]
    [InlineData(TicketStatusEnum.Resolved, TicketStatusEnum.InProgress, ActorRoleEnum.Manager)]
    public void CanTransition_ValidManagerSystemTransitions_ReturnsAllowed(
        TicketStatusEnum from, TicketStatusEnum to, ActorRoleEnum actor)
    {
        // Arrange
        var ticket = CreateTicket(from);

        // Act
        var result = _sut.CanTransition(ticket, to, actor, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Reason.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData(TicketStatusEnum.Assigned, TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.WaitingCustomer)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.WaitingParts)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.WaitingOnsiteSchedule)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Resolved)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Escalated)]
    public void CanTransition_ValidStaffTransitions_MustBeAssignedStaff(
        TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var assignedStaffId = Guid.NewGuid();
        var ticket = CreateTicket(from, assignedStaffId);

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Staff, assignedStaffId);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Reason.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData(TicketStatusEnum.WaitingCustomer, ActorRoleEnum.Staff)]
    [InlineData(TicketStatusEnum.WaitingCustomer, ActorRoleEnum.System)]
    [InlineData(TicketStatusEnum.WaitingParts, ActorRoleEnum.Staff)]
    [InlineData(TicketStatusEnum.WaitingParts, ActorRoleEnum.System)]
    [InlineData(TicketStatusEnum.WaitingOnsiteSchedule, ActorRoleEnum.Staff)]
    [InlineData(TicketStatusEnum.WaitingOnsiteSchedule, ActorRoleEnum.System)]
    public void CanTransition_WaitingToInProgress_AllowsStaffOrSystem(
        TicketStatusEnum from, ActorRoleEnum actor)
    {
        // Arrange
        var staffId = Guid.NewGuid();
        var ticket = CreateTicket(from, staffId);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.InProgress, actor, staffId);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_AssignedToEscalated_SystemOnly()
    {
        // Arrange
        var ticket = CreateTicket(TicketStatusEnum.Assigned, Guid.NewGuid());

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Escalated, ActorRoleEnum.System, Guid.Empty);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_ClosedPendingRateToClosed_CustomerOwner()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.Customer, customerId);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_ClosedPendingRateToOpen_CustomerWithin7Days()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-3); // Within 7 days

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, customerId);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void CanTransition_ClosedPendingRateToClosed_SystemAfter7Days()
    {
        // Arrange
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-8);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.System, Guid.Empty);

        // Assert
        result.IsAllowed.Should().BeTrue();
    }

    #endregion

    #region Group 2: Invalid Actor Roles (12 cases)

    [Theory]
    [InlineData(TicketStatusEnum.New, TicketStatusEnum.Open)] // Only Manager/System
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.Assigned)] // Only Manager
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Resolved)] // Only Staff
    [InlineData(TicketStatusEnum.Resolved, TicketStatusEnum.ClosedPendingRate)] // Only Manager
    public void CanTransition_CustomerInvalidActions_ReturnsFalse(
        TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(from, customerId: customerId);

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Customer, customerId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(TicketStatusEnum.Approved, TicketStatusEnum.Assigned)]
    [InlineData(TicketStatusEnum.Resolved, TicketStatusEnum.ClosedPendingRate)]
    public void CanTransition_StaffCannotDoManagerActions_ReturnsFalse(
        TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var staffId = Guid.NewGuid();
        var ticket = CreateTicket(from, staffId);

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Staff, staffId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Manager");
    }

    [Theory]
    [InlineData(TicketStatusEnum.Assigned, TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Resolved)]
    public void CanTransition_ManagerCannotStartOrResolve_ReturnsFalse(
        TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var managerId = Guid.NewGuid();
        var ticket = CreateTicket(from, Guid.NewGuid()); // Different staff assigned

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Manager, managerId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Assigned Staff");
    }

    [Fact]
    public void CanTransition_WrongStaffCannotStart_ReturnsFalse()
    {
        // Arrange
        var assignedStaffId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.Assigned, assignedStaffId);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, otherStaffId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Assigned Staff");
    }

    [Fact]
    public void CanTransition_WrongCustomerCannotClose_ReturnsFalse()
    {
        // Arrange
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerA);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Closed, ActorRoleEnum.Customer, customerB);

        // Assert
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void CanTransition_WrongCustomerCannotReopen_ReturnsFalse()
    {
        // Arrange
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerA);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-2);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, customerB);

        // Assert
        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public void CanTransition_WrongStaffCannotResolve_ReturnsFalse()
    {
        // Arrange
        var currentStaffId = Guid.NewGuid();
        var oldStaffId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.InProgress, currentStaffId);
        ticket.EscalatedAt = DateTime.UtcNow.AddHours(-1); // Marked as escalated

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Resolved, ActorRoleEnum.Staff, oldStaffId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Assigned Staff");
    }

    #endregion

    #region Group 3: Business Rules & Edge Cases (8 cases)

    [Fact]
    public void CanTransition_ReopenAfter7Days_ReturnsFalse()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-8); // After 7 days

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, customerId);

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("7 days");
    }

    [Fact]
    public void CanTransition_ReopenExactly7Days_ReturnsFalse()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-7).AddMinutes(-1); // Just over 7 days

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Customer, customerId);

        // Assert
        result.IsAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(TicketStatusEnum.Open)]
    [InlineData(TicketStatusEnum.Assigned)]
    [InlineData(TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.Resolved)]
    public void CanTransition_FromClosed_AlwaysFails(TicketStatusEnum target)
    {
        // Arrange
        var ticket = CreateTicket(TicketStatusEnum.Closed);

        // Act
        var result = _sut.CanTransition(ticket, target, ActorRoleEnum.Manager, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("closed");
    }

    [Theory]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.InProgress)] // Must go through Assigned
    [InlineData(TicketStatusEnum.Assigned, TicketStatusEnum.Resolved)] // Must go through InProgress
    public void CanTransition_InvalidPath_ReturnsFalse(TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var ticket = CreateTicket(from, Guid.NewGuid());

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Manager, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Cannot transition");
    }

    [Fact]
    public void CanTransition_NullAssignedStaff_CannotStart()
    {
        // Arrange
        var ticket = CreateTicket(TicketStatusEnum.Assigned, assignedStaffId: null);

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.InProgress, ActorRoleEnum.Staff, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeFalse();
    }

    #endregion

    #region Group 4: ExecuteAsync & Metadata Updates (8 cases)

    [Fact]
    public async Task ExecuteAsync_Resolved_SetsMetadata()
    {
        // Arrange
        var staffId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.InProgress, staffId);
        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Staff,
            ActorUserId = staffId,
            Payload = new Dictionary<string, object?> { { "ResolutionSummary", "Fixed" } }
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Resolved, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Resolved);
        ticket.ResolvedAt.Should().NotBeNull();
        ticket.ResolvedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        ticket.ResolvedByStaffId.Should().Be(staffId);
    }

    [Fact]
    public async Task ExecuteAsync_Approved_SetsMetadata()
    {
        // Arrange
        var managerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.Open);
        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Manager,
            ActorUserId = managerId
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Approved, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Approved);
        ticket.ApprovedAt.Should().NotBeNull();
        ticket.ApprovedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        ticket.ApprovedByManagerId.Should().Be(managerId);
    }

    [Fact]
    public async Task ExecuteAsync_ClosedPendingRate_SetsApprovedAt()
    {
        // Arrange
        var managerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.Resolved, Guid.NewGuid());
        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Manager,
            ActorUserId = managerId
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.ClosedPendingRate, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        ticket.ApprovedAt.Should().NotBeNull();
        ticket.ApprovedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_Closed_SetsClosedAt()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer,
            ActorUserId = customerId,
            Payload = new Dictionary<string, object?> { { "Rating", 5 } }
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Closed, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        ticket.ClosedAt.Should().NotBeNull();
        ticket.ClosedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_Reopen_IncrementsReopenCount()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-2);
        ticket.ReopenCount = 0;

        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer,
            ActorUserId = customerId,
            Payload = new Dictionary<string, object?> { { "ReopenReason", "Issue not fixed" } }
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Open, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeTrue();
        ticket.Status.Should().Be(TicketStatusEnum.Open);
        ticket.ReopenCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleReopens_IncrementsCorrectly()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate, customerId: customerId);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-1);
        ticket.ReopenCount = 2; // Already reopened twice

        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer,
            ActorUserId = customerId
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Open, ctx, CancellationToken.None);

        // Assert
        ticket.ReopenCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_AnyTransition_UpdatesUpdatedAt()
    {
        // Arrange
        var managerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.New);
        var oldUpdatedAt = DateTime.UtcNow.AddSeconds(-1);
        ticket.UpdatedAt = oldUpdatedAt;

        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Manager,
            ActorUserId = managerId
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Open, ctx, CancellationToken.None);

        // Assert
        ticket.UpdatedAt.Should().NotBeNull();
        ticket.UpdatedAt!.Value.Should().BeAfter(oldUpdatedAt);
    }

    [Fact]
    public async Task ExecuteAsync_FailedTransition_DoesNotUpdateTicket()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var ticket = CreateTicket(TicketStatusEnum.InProgress, Guid.NewGuid());
        var originalStatus = ticket.Status;
        var originalUpdatedAt = ticket.UpdatedAt;

        var ctx = new TransitionContext
        {
            ActorRole = ActorRoleEnum.Customer, // Invalid actor
            ActorUserId = customerId
        };

        // Act
        var result = await _sut.ExecuteAsync(ticket, TicketStatusEnum.Resolved, ctx, CancellationToken.None);

        // Assert
        result.IsAllowed.Should().BeFalse();
        ticket.Status.Should().Be(originalStatus); // Unchanged
        ticket.UpdatedAt.Should().Be(originalUpdatedAt); // Unchanged
    }

    #endregion

    #region Group 5: Admin Override Transitions (Updated)

    [Theory]
    [InlineData(TicketStatusEnum.New, TicketStatusEnum.Open)]
    [InlineData(TicketStatusEnum.Open, TicketStatusEnum.Approved)]
    [InlineData(TicketStatusEnum.Approved, TicketStatusEnum.Assigned)]
    [InlineData(TicketStatusEnum.Assigned, TicketStatusEnum.InProgress)]
    [InlineData(TicketStatusEnum.InProgress, TicketStatusEnum.Resolved)]
    [InlineData(TicketStatusEnum.Resolved, TicketStatusEnum.ClosedPendingRate)]
    [InlineData(TicketStatusEnum.ClosedPendingRate, TicketStatusEnum.Closed)]
    [InlineData(TicketStatusEnum.Escalated, TicketStatusEnum.Incident)]
    public void CanTransition_AdminOverride_AlwaysReturnsAllowed(
        TicketStatusEnum from, TicketStatusEnum to)
    {
        // Arrange
        var ticket = CreateTicket(from);
        // Ngay cả khi không phải là Staff được assign hay Customer chủ sở hữu
        ticket.AssignedStaffId = Guid.NewGuid();
        ticket.CustomerId = Guid.NewGuid();

        // Act
        var result = _sut.CanTransition(ticket, to, ActorRoleEnum.Admin, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Reason.Should().BeNullOrEmpty();
    }

    [Fact]
    public void CanTransition_AdminReopenAfter7Days_ReturnsAllowed()
    {
        // Arrange
        var ticket = CreateTicket(TicketStatusEnum.ClosedPendingRate);
        ticket.ApprovedAt = DateTime.UtcNow.AddDays(-10); // Quá 7 ngày

        // Act
        var result = _sut.CanTransition(ticket, TicketStatusEnum.Open, ActorRoleEnum.Admin, Guid.NewGuid());

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Reason.Should().BeNullOrEmpty();
    }

    #endregion
}
