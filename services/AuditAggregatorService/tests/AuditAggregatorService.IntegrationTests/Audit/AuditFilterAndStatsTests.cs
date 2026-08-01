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
/// Lấp hai chỗ mà bộ test cũ bỏ trống:
///
/// <list type="number">
///   <item><b><c>AuditAggregateQueryExtensions.ApplyFilters</c></b> — 10 nhánh <c>if</c>, trước đây
///   chỉ vài nhánh được chạm. Mỗi nhánh chưa chạy là một bộ lọc chưa ai chứng minh là lọc đúng;
///   lọc sai ở màn hình forensic nghĩa là điều tra viên nhìn nhầm dữ liệu.</item>
///   <item><b><c>AuditGetStatsQueryHandler</c></b> — ba nhánh gộp (<c>service</c> / <c>action</c> /
///   mặc định <c>severity</c>) và hai bộ lọc thời gian.</item>
/// </list>
///
/// <para>Dùng mock <c>IQueryable</c> (MockQueryable) chứ không DB thật: đây là logic LINQ thuần,
/// không có gì phụ thuộc provider. Phần cần DB thật (giao dịch, unique, phân vùng) đã có bộ test
/// riêng.</para>
/// </summary>
public class AuditFilterAndStatsTests
{
    private static readonly Guid ActorA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ActorB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid TargetA = Guid.Parse("cccccccc-0000-0000-0000-000000000003");
    private static readonly Guid CorrA = Guid.Parse("dddddddd-0000-0000-0000-000000000004");

    private static readonly DateTime T0 = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AuditAggregate Agg(
        string service = "AuthService", string action = "LoginSucceeded",
        string category = "Authentication", string severity = "Info",
        Guid? actor = null, Guid? target = null, Guid? correlation = null,
        bool success = true, DateTime? occurredAt = null) =>
        AuditAggregate.FromEvent(
            Guid.NewGuid(), service, action, category, severity,
            "Account", target, "x@example.com",
            actor, "Admin", "Admin User", "127.0.0.1", "ua",
            success, null, null, null,
            correlation, null, occurredAt ?? T0, T0);

    private static IAuditAggregatorUnitOfWork UowWith(IEnumerable<AuditAggregate> data)
    {
        var repo = new Mock<IGenericRepository<AuditAggregate>>();
        repo.Setup(r => r.GetAllAsync(It.IsAny<bool>())).Returns(data.AsQueryable().BuildMock());
        repo.Setup(r => r.GetAllAsync()).Returns(data.AsQueryable().BuildMock());
        var uow = new Mock<IAuditAggregatorUnitOfWork>();
        uow.Setup(u => u.AuditAggregates).Returns(repo.Object);
        return uow.Object;
    }

    // ─────────────────────────────────────────────────────────── ApplyFilters

    /// <summary>
    /// Bật ĐỒNG THỜI cả 10 bộ lọc. Một dòng khớp hết, các dòng còn lại mỗi dòng lệch đúng một tiêu
    /// chí — nên nếu bất kỳ nhánh <c>if</c> nào bị bỏ sót hoặc so sai trường, kết quả sẽ ra nhiều
    /// hơn một dòng và test đỏ.
    /// </summary>
    [Fact]
    public async Task ApplyFilters_AllTenFiltersTogether_MatchesExactlyTheOneRow()
    {
        var wanted = Agg(actor: ActorA, target: TargetA, correlation: CorrA,
            severity: "Critical", category: "Security", action: "PasswordChanged",
            service: "AuthService", success: true, occurredAt: T0.AddHours(5));

        var data = new[]
        {
            wanted,
            Agg(service: "BatteryService", actor: ActorA, target: TargetA, correlation: CorrA,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(action: "LoginFailed", actor: ActorA, target: TargetA, correlation: CorrA,
                severity: "Critical", category: "Security", occurredAt: T0.AddHours(5)),
            Agg(category: "DataAccess", actor: ActorA, target: TargetA, correlation: CorrA,
                severity: "Critical", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(severity: "Info", actor: ActorA, target: TargetA, correlation: CorrA,
                category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(actor: ActorB, target: TargetA, correlation: CorrA,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(actor: ActorA, target: Guid.NewGuid(), correlation: CorrA,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(actor: ActorA, target: TargetA, correlation: Guid.NewGuid(),
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(actor: ActorA, target: TargetA, correlation: CorrA, success: false,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddHours(5)),
            Agg(actor: ActorA, target: TargetA, correlation: CorrA,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddDays(-1)),
            Agg(actor: ActorA, target: TargetA, correlation: CorrA,
                severity: "Critical", category: "Security", action: "PasswordChanged", occurredAt: T0.AddDays(9)),
        };

        var result = await new AuditSearchQueryHandler(UowWith(data)).Handle(new AuditSearchQuery
        {
            Service = "AuthService",
            Action = "PasswordChanged",
            Category = "Security",
            Severity = "Critical",
            ActorId = ActorA,
            TargetId = TargetA,
            CorrelationId = CorrA,
            IsSuccess = true,
            From = T0,
            To = T0.AddDays(1),
            PageNumber = 1,
            PageSize = 50,
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalItems.Should().Be(1,
            "mỗi dòng nhiễu lệch đúng MỘT tiêu chí — ra nhiều hơn 1 nghĩa là có nhánh lọc không chạy");
        // DTO trả EventId dạng chuỗi (quy ước Guid → string ở tầng DTO).
        result.Data.Items.Single().EventId.Should().Be(wanted.EventId.ToString());
    }

    [Fact]
    public async Task ApplyFilters_NoFilter_ReturnsEverything()
    {
        var data = new[] { Agg(), Agg(service: "TicketService"), Agg(severity: "Warning") };

        var result = await new AuditSearchQueryHandler(UowWith(data))
            .Handle(new AuditSearchQuery { PageNumber = 1, PageSize = 50 }, CancellationToken.None);

        result.Data!.TotalItems.Should().Be(3);
    }

    /// <summary>
    /// Chuỗi rỗng / toàn khoảng trắng phải bị coi là "không lọc", không phải "lọc theo chuỗi rỗng".
    /// Nhầm chỗ này là màn hình forensic trả về rỗng mà không báo lỗi gì.
    /// </summary>
    [Fact]
    public async Task ApplyFilters_BlankStrings_AreTreatedAsNoFilter()
    {
        var data = new[] { Agg(), Agg(service: "TicketService") };

        var result = await new AuditSearchQueryHandler(UowWith(data)).Handle(new AuditSearchQuery
        {
            Service = "   ",
            Action = "",
            PageNumber = 1,
            PageSize = 50,
        }, CancellationToken.None);

        result.Data!.TotalItems.Should().Be(2);
    }

    /// <summary>Biên của khoảng thời gian phải là <b>đóng hai đầu</b> (>= From, &lt;= To).</summary>
    [Fact]
    public async Task ApplyFilters_TimeRange_IsInclusiveOnBothEnds()
    {
        var data = new[]
        {
            Agg(occurredAt: T0),                       // đúng mốc đầu
            Agg(occurredAt: T0.AddDays(1)),            // đúng mốc cuối
            Agg(occurredAt: T0.AddTicks(-1)),          // lệch 1 tick trước
            Agg(occurredAt: T0.AddDays(1).AddTicks(1)),// lệch 1 tick sau
        };

        var result = await new AuditSearchQueryHandler(UowWith(data)).Handle(new AuditSearchQuery
        {
            From = T0,
            To = T0.AddDays(1),
            PageNumber = 1,
            PageSize = 50,
        }, CancellationToken.None);

        result.Data!.TotalItems.Should().Be(2);
    }

    [Fact]
    public async Task ApplyFilters_IsSuccessFalse_MatchesOnlyFailures()
    {
        var data = new[] { Agg(success: true), Agg(success: false), Agg(success: false) };

        var result = await new AuditSearchQueryHandler(UowWith(data))
            .Handle(new AuditSearchQuery { IsSuccess = false, PageNumber = 1, PageSize = 50 },
                    CancellationToken.None);

        result.Data!.TotalItems.Should().Be(2, "IsSuccess=false KHÁC với không lọc — false không được coi là 'bỏ trống'");
    }

    // ─────────────────────────────────────────────────────────────── Stats

    [Fact]
    public async Task Stats_GroupByService_CountsPerService_SortedDescending()
    {
        var data = new[]
        {
            Agg(service: "AuthService"), Agg(service: "AuthService"), Agg(service: "AuthService"),
            Agg(service: "TicketService"), Agg(service: "TicketService"),
            Agg(service: "SmsService"),
        };

        var result = await new AuditGetStatsQueryHandler(UowWith(data))
            .Handle(new AuditGetStatsQuery { GroupBy = "service" }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Select(x => x.Key).Should().ContainInOrder("AuthService", "TicketService", "SmsService");
        result.Data.Single(x => x.Key == "AuthService").Count.Should().Be(3);
    }

    [Fact]
    public async Task Stats_GroupByAction_CountsPerAction()
    {
        var data = new[]
        {
            Agg(action: "LoginSucceeded"), Agg(action: "LoginSucceeded"),
            Agg(action: "LoginFailed"),
        };

        var result = await new AuditGetStatsQueryHandler(UowWith(data))
            .Handle(new AuditGetStatsQuery { GroupBy = "action" }, CancellationToken.None);

        result.Data!.Single(x => x.Key == "LoginSucceeded").Count.Should().Be(2);
        result.Data.Single(x => x.Key == "LoginFailed").Count.Should().Be(1);
    }

    /// <summary>
    /// <c>GroupBy</c> lạ hoặc bỏ trống phải rơi về gộp theo <c>severity</c>, KHÔNG được ném hay trả
    /// rỗng — tham số này đến thẳng từ query string do người dùng gõ.
    /// </summary>
    [Theory]
    [InlineData("severity")]
    [InlineData("SEVERITY")]
    [InlineData("khong-ton-tai")]
    [InlineData("")]
    public async Task Stats_UnknownOrBlankGroupBy_FallsBackToSeverity(string groupBy)
    {
        var data = new[] { Agg(severity: "Info"), Agg(severity: "Info"), Agg(severity: "Critical") };

        var result = await new AuditGetStatsQueryHandler(UowWith(data))
            .Handle(new AuditGetStatsQuery { GroupBy = groupBy }, CancellationToken.None);

        result.Data!.Should().HaveCount(2);
        result.Data.Single(x => x.Key == "Info").Count.Should().Be(2);
    }

    /// <summary>Gộp theo <c>Service</c> viết hoa-thường khác nhau vẫn phải nhận đúng nhánh.</summary>
    [Fact]
    public async Task Stats_GroupByService_IsCaseInsensitive()
    {
        var data = new[] { Agg(service: "AuthService"), Agg(service: "TicketService") };

        var result = await new AuditGetStatsQueryHandler(UowWith(data))
            .Handle(new AuditGetStatsQuery { GroupBy = "SeRvIcE" }, CancellationToken.None);

        result.Data!.Select(x => x.Key).Should().BeEquivalentTo("AuthService", "TicketService");
    }

    [Fact]
    public async Task Stats_TimeRange_FiltersBeforeGrouping()
    {
        var data = new[]
        {
            Agg(severity: "Info", occurredAt: T0),
            Agg(severity: "Info", occurredAt: T0.AddDays(10)),   // ngoài khoảng
            Agg(severity: "Critical", occurredAt: T0.AddHours(2)),
        };

        var result = await new AuditGetStatsQueryHandler(UowWith(data)).Handle(new AuditGetStatsQuery
        {
            From = T0,
            To = T0.AddDays(1),
            GroupBy = "severity",
        }, CancellationToken.None);

        result.Data!.Sum(x => x.Count).Should().Be(2, "dòng ngoài khoảng thời gian phải bị loại TRƯỚC khi gộp");
    }

    [Fact]
    public async Task Stats_EmptyStore_ReturnsEmptyList_Not500()
    {
        var result = await new AuditGetStatsQueryHandler(UowWith(Array.Empty<AuditAggregate>()))
            .Handle(new AuditGetStatsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().BeEmpty();
    }
}
