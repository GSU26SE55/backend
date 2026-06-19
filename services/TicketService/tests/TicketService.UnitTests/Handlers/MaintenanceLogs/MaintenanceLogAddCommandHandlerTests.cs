using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.MaintenanceLogAdd;
using TicketService.Application.CQRS.Handler.MaintenanceLogs;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.MaintenanceLogs;

public class MaintenanceLogAddCommandHandlerTests
{
    private readonly Mock<IActivityLogger> _logger = new();

    [Fact]
    public async Task Handle_ValidRequest_AddsMaintenanceLog()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = new Ticket
        {
            Id = ticketId,
            Code = "TKT-001",
            Title = "Test Ticket",
            Description = "Test Description"
        };

        var (uow, _, _, _, _, _, _, _, attachments, logs, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new MaintenanceLogAddCommand
        {
            TicketId = ticketId,
            StaffId = staffId,
            LogType = MaintenanceLogTypeEnum.OnSite,
            Summary = "On-site repair",
            DiagnosisDetails = "Broken panel",
            ActionsTaken = "Replaced panel",
            DurationMinutes = 120,
            ResolutionNote = "Resolved",
            StartedAt = DateTime.UtcNow.AddHours(-2),
            CompletedAt = DateTime.UtcNow,
            Attachments = new List<MaintenanceAttachmentInput>
            {
                new MaintenanceAttachmentInput(Guid.NewGuid(), "report.pdf", "application/pdf", 2048)
            }
        };

        var handler = new MaintenanceLogAddCommandHandler(uow.Object, _logger.Object);

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(201);

        logs.Verify(x => x.AddAsync(It.Is<MaintenanceLog>(l =>
            l.TicketId == ticketId &&
            l.Summary == "On-site repair" &&
            l.AttachmentFileIds.Count == 1)), Times.Once);

        attachments.Verify(x => x.AddAsync(It.Is<TicketAttachment>(a =>
            a.TicketId == ticketId &&
            a.FileName == "report.pdf")), Times.Once);

        _logger.Verify(x => x.LogAsync(
            ticketId,
            staffId,
            ActorRoleEnum.Staff,
            "Staff",
            ActivityActionEnum.MaintenanceLogged,
            null,
            $"[{MaintenanceLogTypeEnum.OnSite}] On-site repair",
            It.IsAny<string>()), Times.Once);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Validate_EmptySummary_ReturnsError()
    {
        // Arrange
        var command = new MaintenanceLogAddCommand
        {
            TicketId = Guid.NewGuid(),
            StaffId = Guid.NewGuid(),
            LogType = MaintenanceLogTypeEnum.OnSite,
            Summary = "",
            StartedAt = DateTime.UtcNow
        };

        // Act
        var result = await command.ValidateAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Summary");
    }
}
