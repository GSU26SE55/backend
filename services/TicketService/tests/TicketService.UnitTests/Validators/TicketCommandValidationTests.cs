using FluentAssertions;
using TicketService.Application.CQRS.Command.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Validators;

public class TicketCommandValidationTests
{
    #region Triage & Assign & Basic Commands
    [Fact]
    public async Task TicketTriageCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketTriageCommand
        {
            TicketId = Guid.Empty,
            Impact = (ImpactScopeEnum)99,
            Urgency = (UrgencyLevelEnum)99
        };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Impact");
        result.ListErrors.Should().Contain(x => x.Field == "Urgency");
    }

    [Fact]
    public async Task TicketAssignCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketAssignCommand { TicketId = Guid.Empty, StaffId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "StaffId");
    }

    [Fact]
    public async Task TicketRejectCommand_EmptyReason_ReturnsErrors()
    {
        var command = new TicketRejectCommand { TicketId = Guid.NewGuid(), Reason = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }
    #endregion

    #region Lifecycle Commands
    [Fact]
    public async Task TicketCreateCommand_EmptyData_ReturnsErrors()
    {
        var command = new TicketCreateCommand { Title = "", Description = "", CustomerId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "Title");
        result.ListErrors.Should().Contain(x => x.Field == "Description");
        result.ListErrors.Should().Contain(x => x.Field == "CustomerId");
    }

    [Fact]
    public async Task TicketResolveCommand_EmptySummary_ReturnsErrors()
    {
        var command = new TicketResolveCommand { TicketId = Guid.Empty, ResolutionSummary = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "ResolutionSummary");
    }

    [Fact]
    public async Task TicketHoldCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketHoldCommand { TicketId = Guid.Empty, Reason = (PauseReasonEnum)99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketResumeCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketResumeCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketStartCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketStartCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketApproveCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketApproveCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketReassignCommand_EmptyIds_ReturnsErrors()
    {
        var command = new TicketReassignCommand { TicketId = Guid.Empty, NewStaffId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "NewStaffId");
    }

    [Fact]
    public async Task TicketEscalateForceCommand_EmptyId_ReturnsErrors()
    {
        var command = new TicketEscalateForceCommand { TicketId = Guid.Empty };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
    }

    [Fact]
    public async Task TicketEscalateRequestCommand_InvalidData_ReturnsErrors()
    {
        var command = new TicketEscalateRequestCommand { TicketId = Guid.Empty, Reason = (EscalationReasonEnum)99 };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "TicketId");
        result.ListErrors.Should().Contain(x => x.Field == "Reason");
    }

    [Fact]
    public async Task TicketAutoCreateFromAlertCommand_EmptyData_ReturnsErrors()
    {
        var command = new TicketAutoCreateFromAlertCommand { OriginAlertId = Guid.Empty, AnomalyCategory = "" };
        var result = await command.ValidateAsync();
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(x => x.Field == "OriginAlertId");
        result.ListErrors.Should().Contain(x => x.Field == "AnomalyCategory");
    }
    #endregion
}
