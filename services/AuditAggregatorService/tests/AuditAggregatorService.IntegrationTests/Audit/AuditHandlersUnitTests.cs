using AuditAggregatorService.Application.CQRS.Command.Audit;
using AuditAggregatorService.Application.CQRS.Handler.Audit;
using AuditAggregatorService.Application.CQRS.Query.Audit;
using AuditAggregatorService.Application.Interfaces;
using AuditAggregatorService.Domain.Entities;
using FluentAssertions;
using MockQueryable.Moq;
using Moq;
using SharedKernels.Interfaces;
using Xunit;

namespace AuditAggregatorService.IntegrationTests.Audit;

/// <summary>
/// Unit test cho CQRS query handlers sau khi refactor sang UnitOfWork (Sprint audit).
/// Mock <see cref="IAuditAggregatorUnitOfWork"/> → <see cref="IGenericRepository{T}"/> với MockQueryable (hỗ trợ EF async).
/// Redact (ExecuteUpdate) + idempotency insert được cover ở integration test (cần DB thật).
/// </summary>
public class AuditHandlersUnitTests
{
    private static AuditAggregate Agg(Guid? eventId = null, Guid? actorId = null, Guid? correlationId = null,
        string severity = "Info", DateTime? occurredAt = null) => AuditAggregate.FromEvent(
        eventId ?? Guid.NewGuid(), "AuthService", "LoginSucceeded", "Authentication", severity,
        "Account", null, "x@example.com", actorId, "Admin", "Admin User", "127.0.0.1", "ua",
        true, null, null, null, correlationId, null, occurredAt ?? DateTime.UtcNow, DateTime.UtcNow);

    private static IAuditAggregatorUnitOfWork UowWith(IEnumerable<AuditAggregate> data)
    {
        var repo = new Mock<IGenericRepository<AuditAggregate>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(data.AsQueryable().BuildMock());
        repo.Setup(r => r.GetAllAsync()).Returns(data.AsQueryable().BuildMock());
        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.Setup(u => u.AuditAggregates).Returns(repo.Object);
        return uow.Object;
    }

    [Fact]
    public async Task SearchHandler_ReturnsPagedData_200()
    {
        var uow = UowWith(new[] { Agg(), Agg(), Agg() });
        var result = await new AuditSearchQueryHandler(uow)
            .Handle(new AuditSearchQuery { PageNumber = 1, PageSize = 50 }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.TotalItems.Should().Be(3);
        result.Data.Items.Should().HaveCount(3);
    }

    // ---- A+E (#AUDIT-17): validate filter tập-đóng severity/category → 400 thay vì 200 rỗng âm thầm ----

    [Fact]
    public async Task SearchHandler_InvalidSeverity_Returns400_WithFieldError()
    {
        var uow = UowWith(Array.Empty<AuditAggregate>());
        var result = await new AuditSearchQueryHandler(uow)
            .Handle(new AuditSearchQuery { Severity = "Crit1cal" }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Data.Should().BeNull();
        result.ListErrors.Should().ContainSingle(e => e.Field == "severity");
    }

    [Fact]
    public async Task SearchHandler_WrongCaseSeverity_Returns400()
    {
        // "critical" sai hoa-thường → exact-match reject (E xử lý luôn vấn đề case, không cần B).
        var uow = UowWith(Array.Empty<AuditAggregate>());
        var result = await new AuditSearchQueryHandler(uow)
            .Handle(new AuditSearchQuery { Severity = "critical" }, CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().ContainSingle(e => e.Field == "severity");
    }

    [Fact]
    public async Task SearchHandler_InvalidCategory_Returns400()
    {
        var uow = UowWith(Array.Empty<AuditAggregate>());
        var result = await new AuditSearchQueryHandler(uow)
            .Handle(new AuditSearchQuery { Category = "NotARealCategory" }, CancellationToken.None);

        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().ContainSingle(e => e.Field == "category");
    }

    [Fact]
    public async Task SearchHandler_ValidSeverityAndCategory_PassesTo200()
    {
        var uow = UowWith(new[] { Agg(severity: "Critical") });
        var result = await new AuditSearchQueryHandler(uow)
            .Handle(new AuditSearchQuery { Severity = "Critical", Category = "Authentication", PageNumber = 1, PageSize = 50 },
                CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public void Validator_BothInvalid_CollectsAllErrors()
    {
        var errors = AuditSearchValidator.Validate(
            new AuditSearchQuery { Severity = "xx", Category = "yy" });

        errors.Should().HaveCount(2);
        errors.Should().Contain(e => e.Field == "severity");
        errors.Should().Contain(e => e.Field == "category");
    }

    [Fact]
    public void Validator_NullOrEmptyFilters_NoErrors()
    {
        AuditSearchValidator.Validate(new AuditSearchQuery()).Should().BeEmpty();
        AuditSearchValidator.Validate(new AuditSearchQuery { Severity = "", Category = " " }).Should().BeEmpty();
    }

    [Fact]
    public async Task GetByEventIdHandler_Found_200()
    {
        var id = Guid.NewGuid();
        var uow = UowWith(new[] { Agg(eventId: id), Agg() });
        var result = await new AuditGetByEventIdQueryHandler(uow)
            .Handle(new AuditGetByEventIdQuery { EventId = id }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.EventId.Should().Be(id.ToString());
    }

    [Fact]
    public async Task GetByEventIdHandler_NotFound_404()
    {
        var uow = UowWith(new[] { Agg() });
        var result = await new AuditGetByEventIdQueryHandler(uow)
            .Handle(new AuditGetByEventIdQuery { EventId = Guid.NewGuid() }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task CorrelationHandler_FiltersByCorrelation_200()
    {
        var corr = Guid.NewGuid();
        var uow = UowWith(new[] { Agg(correlationId: corr), Agg(correlationId: corr), Agg() });
        var result = await new AuditGetByCorrelationQueryHandler(uow)
            .Handle(new AuditGetByCorrelationQuery { CorrelationId = corr }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task TimelineHandler_MatchesActorOrTarget_200()
    {
        var acc = Guid.NewGuid();
        var uow = UowWith(new[] { Agg(actorId: acc), Agg() });
        var result = await new AuditGetAccountTimelineQueryHandler(uow)
            .Handle(new AuditGetAccountTimelineQuery { AccountId = acc, Limit = 100 }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task StatsHandler_GroupsBySeverity_200()
    {
        var uow = UowWith(new[] { Agg(severity: "Info"), Agg(severity: "Info"), Agg(severity: "Critical") });
        var result = await new AuditGetStatsQueryHandler(uow)
            .Handle(new AuditGetStatsQuery { GroupBy = "severity" }, CancellationToken.None);

        result.StatusCode.Should().Be(200);
        result.Data.Should().Contain(s => s.Key == "Info" && s.Count == 2);
        result.Data.Should().Contain(s => s.Key == "Critical" && s.Count == 1);
    }

    [Fact]
    public async Task ExportHandler_StreamsFilteredData()
    {
        var uow = UowWith(new[] { Agg(severity: "Critical"), Agg(severity: "Info") });
        var stream = await new AuditExportQueryHandler(uow)
            .Handle(new AuditExportQuery { Severity = "Critical" }, CancellationToken.None);

        var rows = new List<string>();
        await foreach (var dto in stream)
            rows.Add(dto.Severity);
        rows.Should().OnlyContain(s => s == "Critical");
        rows.Should().HaveCount(1);
    }

    [Fact]
    public async Task RedactHandler_EmptyAccount_400_WithFieldError()
    {
        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        var result = await new AuditRedactCommandHandler(uow.Object)
            .Handle(new AuditRedactCommand { AccountId = Guid.Empty }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ListErrors.Should().ContainSingle(e => e.Field == "accountId");
        uow.Verify(u => u.AuditAggregates, Times.Never);
    }

    [Fact]
    public async Task ReplayHandler_Accepted_202()
    {
        var result = await new AuditReplayCommandHandler()
            .Handle(new AuditReplayCommand { Service = "AuthService" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
    }
}
