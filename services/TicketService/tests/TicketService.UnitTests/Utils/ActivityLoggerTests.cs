using FluentAssertions;
using Moq;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Implements.Utils;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Utils;

public class ActivityLoggerTests
{
    [Fact]
    public async Task LogAsync_AddsActivityToRepository()
    {
        // Arrange
        var ticketId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var (uow, _, activities, _, _, _, _) = MockTicketUnitOfWork.Build();
        var logger = new ActivityLogger(uow.Object);

        // Act
        await logger.LogAsync(
            ticketId,
            actorId,
            ActorRoleEnum.Manager,
            "Manager A",
            ActivityActionEnum.Approved,
            "Old",
            "New",
            "Because I said so");

        // Assert
        activities.Verify(x => x.AddAsync(It.Is<TicketActivity>(a =>
            a.TicketId == ticketId &&
            a.ActorUserId == actorId &&
            a.ActorRole == ActorRoleEnum.Manager &&
            a.Action == ActivityActionEnum.Approved &&
            a.Reason == "Because I said so")), Times.Once);
    }
}
