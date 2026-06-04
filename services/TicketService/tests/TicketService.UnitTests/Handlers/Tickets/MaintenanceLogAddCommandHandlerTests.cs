using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Commands.MaintenanceLogAdd;
using TicketService.Application.Interfaces.Helpers;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.Tickets;

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

        var (uow, _, _, _, _, _, _, _, attachments, logs) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket }
        );

        var command = new MaintenanceLogAddCommand(
            ticketId,
            staffId,
            MaintenanceLogTypeEnum.OnSite,
            "On-site repair",
            "Broken panel",
            "Replaced panel",
            120,
            "Resolved",
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow,
            null,
            new List<MaintenanceAttachmentInput>
            {
                new MaintenanceAttachmentInput(Guid.NewGuid(), "report.pdf", "application/pdf", 2048)
            }
        );

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
        var command = new MaintenanceLogAddCommand(
            Guid.NewGuid(), Guid.NewGuid(), MaintenanceLogTypeEnum.OnSite, "", null, null, 0, null, DateTime.UtcNow, null, null);

        // Act
        var result = await command.ValidateAsync();

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ListErrors.Should().Contain(e => e.Field == "Summary");
    }
}
