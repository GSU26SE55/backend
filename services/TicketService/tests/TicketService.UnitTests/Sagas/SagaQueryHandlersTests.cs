using FluentAssertions;
using Moq;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Handler.Sagas;
using TicketService.Application.CQRS.Query.Sagas;
using TicketService.Application.DTOs.Response.Saga;
using TicketService.Application.Interfaces.Services;

namespace TicketService.UnitTests.Sagas;

/// <summary>
/// Sprint 5B #239 — Get list / by-id query handlers.
/// </summary>
public class SagaQueryHandlersTests
{
    [Fact]
    public async Task GetById_NotFound_ShouldReturn404()
    {
        var alertId = Guid.NewGuid();
        var svc = new Mock<IAlertTicketSagaQueryService>();
        svc.Setup(s => s.GetByAlertIdAsync(alertId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AlertTicketSagaDTO?)null);

        var handler = new GetAlertTicketSagaByIdQueryHandler(svc.Object);
        var result = await handler.Handle(new GetAlertTicketSagaByIdQuery { AlertId = alertId }, default);

        result.StatusCode.Should().Be(404);
        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetById_Found_ShouldReturnDto()
    {
        var alertId = Guid.NewGuid();
        var dto = new AlertTicketSagaDTO { AlertId = alertId.ToString(), CurrentState = "Completed" };
        var svc = new Mock<IAlertTicketSagaQueryService>();
        svc.Setup(s => s.GetByAlertIdAsync(alertId, It.IsAny<CancellationToken>())).ReturnsAsync(dto);

        var handler = new GetAlertTicketSagaByIdQueryHandler(svc.Object);
        var result = await handler.Handle(new GetAlertTicketSagaByIdQuery { AlertId = alertId }, default);

        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.CurrentState.Should().Be("Completed");
    }

    [Fact]
    public async Task GetList_WithItems_ShouldReturnPaginated()
    {
        var svc = new Mock<IAlertTicketSagaQueryService>();
        var items = new List<AlertTicketSagaDTO>
        {
            new() { AlertId = Guid.NewGuid().ToString(), CurrentState = "Failed" },
            new() { AlertId = Guid.NewGuid().ToString(), CurrentState = "Failed" }
        };
        svc.Setup(s => s.QueryAsync(
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaginationResponse<AlertTicketSagaDTO> { Items = items.ToList(), TotalItems = 2, PageNumber = 1, PageSize = 50 });

        var handler = new GetAlertTicketSagasQueryHandler(svc.Object);
        var result = await handler.Handle(new GetAlertTicketSagasQuery { IsFailed = true }, default);

        result.StatusCode.Should().Be(200);
        result.Data!.TotalItems.Should().Be(2);
        result.Data.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetList_DefaultsPageSize_WhenInvalid()
    {
        var svc = new Mock<IAlertTicketSagaQueryService>();
        var captured = 0;
        svc.Setup(s => s.QueryAsync(
                It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<bool?>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<string?, Guid?, Guid?, Guid?, DateTime?, DateTime?, bool?, int, int, bool, CancellationToken>(
                (_, _, _, _, _, _, _, _, ps, _, _) => captured = ps)
            .ReturnsAsync(new PaginationResponse<AlertTicketSagaDTO> { Items = new List<AlertTicketSagaDTO>(), TotalItems = 0, PageNumber = 1, PageSize = 50 });

        var handler = new GetAlertTicketSagasQueryHandler(svc.Object);
        await handler.Handle(new GetAlertTicketSagasQuery { PageSize = 999, PageNumber = 0 }, default);

        captured.Should().Be(50); // default
    }
}
