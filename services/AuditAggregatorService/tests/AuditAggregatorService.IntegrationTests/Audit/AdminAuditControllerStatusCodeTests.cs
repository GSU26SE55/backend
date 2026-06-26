using AuditAggregatorService.Api.Controllers.Admin;
using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.DTOs;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SharedContracts.Common.Responses;
using Xunit;

namespace AuditAggregatorService.IntegrationTests.Audit;

/// <summary>
/// Unit test xác nhận controller map <c>result.StatusCode</c> (từ handler) → đúng HTTP status code
/// (<c>StatusCode(result.StatusCode, result)</c>). Mock <see cref="IMediator"/> — không cần app/DB.
/// </summary>
public class AdminAuditControllerStatusCodeTests
{
    private static (AdminAuditController ctrl, Mock<IMediator> mediator) Build()
    {
        var mediator = new Mock<IMediator>();
        return (new AdminAuditController(mediator.Object), mediator);
    }

    private static int StatusOf(IActionResult result) => ((ObjectResult)result).StatusCode!.Value;

    [Fact]
    public async Task Search_Returns200()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<PaginationResponse<AuditAggregateDto>> { StatusCode = 200 });

        StatusOf(await ctrl.Search(new AuditSearchQuery(), default)).Should().Be(200);
    }

    [Fact]
    public async Task GetByEventId_Returns200_WhenFound()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditGetByEventIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<AuditAggregateDto> { StatusCode = 200, Data = new AuditAggregateDto() });

        StatusOf(await ctrl.GetByEventId(Guid.NewGuid(), default)).Should().Be(200);
    }

    [Fact]
    public async Task GetByEventId_Returns404_WhenNotFound()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditGetByEventIdQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<AuditAggregateDto> { IsSuccess = false, StatusCode = 404 });

        StatusOf(await ctrl.GetByEventId(Guid.NewGuid(), default)).Should().Be(404);
    }

    [Fact]
    public async Task Stats_Returns200()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditGetStatsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<List<AuditStatsItemDto>> { StatusCode = 200 });

        StatusOf(await ctrl.Stats(null, null, "severity", default)).Should().Be(200);
    }

    [Fact]
    public async Task Replay_Returns202()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditReplayCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<object> { StatusCode = 202 });

        StatusOf(await ctrl.Replay(new AuditReplayCommand(), default)).Should().Be(202);
    }

    [Fact]
    public async Task Redact_Returns400_WhenFieldError()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditRedactCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<object> { IsSuccess = false, StatusCode = 400 });

        StatusOf(await ctrl.Redact(Guid.Empty, default)).Should().Be(400);
    }

    [Fact]
    public async Task Redact_Returns200_WhenOk()
    {
        var (ctrl, m) = Build();
        m.Setup(x => x.Send(It.IsAny<AuditRedactCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommonResponse<object> { StatusCode = 200 });

        StatusOf(await ctrl.Redact(Guid.NewGuid(), default)).Should().Be(200);
    }

    // ---- A+E (#AUDIT-17): /export validate filter tập-đóng NGAY Ở CONTROLLER (trước khi stream) ----

    private static async IAsyncEnumerable<AuditAggregateDto> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static (AdminAuditController ctrl, Mock<IMediator> mediator, MemoryStream body) BuildWithHttp()
    {
        var mediator = new Mock<IMediator>();
        var http = new DefaultHttpContext();
        var body = new MemoryStream();
        http.Response.Body = body;
        var ctrl = new AdminAuditController(mediator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (ctrl, mediator, body);
    }

    [Fact]
    public async Task Export_Returns400_WhenInvalidSeverity_WithoutCallingMediator()
    {
        var (ctrl, m, body) = BuildWithHttp();

        await ctrl.Export(new AuditExportQuery { Severity = "critical" }, "csv", default); // sai hoa-thường

        ctrl.Response.StatusCode.Should().Be(400);
        ctrl.Response.ContentType.Should().Be("application/json");
        // StreamWriter trong controller dispose Response.Body (đúng pattern); MemoryStream.ToArray() vẫn đọc được sau dispose.
        var json = System.Text.Encoding.UTF8.GetString(body.ToArray());
        json.Should().Contain("severity").And.Contain("listErrors");
        m.Verify(x => x.Send(It.IsAny<AuditExportQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_ValidFilter_PassesValidation_AndStreams()
    {
        var (ctrl, m, _) = BuildWithHttp();
        m.Setup(x => x.Send(It.IsAny<AuditExportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyStream());

        await ctrl.Export(new AuditExportQuery { Severity = "Critical", Category = "Authentication" }, "csv", default);

        ctrl.Response.StatusCode.Should().Be(200);
        m.Verify(x => x.Send(It.IsAny<AuditExportQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
