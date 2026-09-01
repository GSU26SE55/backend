using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Handler.Ticket;
using TicketService.Application.CQRS.Query.Ticket;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Queries;

public class TicketGetListQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly Mock<ITicketCurrentUserService> _mockCurrentUser = new();
    private readonly TicketGetListQueryHandler _handler;

    public TicketGetListQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _mockCurrentUser.Setup(x => x.UserId).Returns(Guid.NewGuid().ToString());
        _mockCurrentUser.Setup(x => x.Role).Returns("Admin");

        var mockChatReads = new Mock<IGenericRepository<TicketChatRead>>();
        mockChatReads.Setup(r => r.GetAllAsync()).Returns(Array.Empty<TicketChatRead>().BuildMock());
        _mockUow.Setup(x => x.TicketChatReads).Returns(mockChatReads.Object);

        var mockChats = new Mock<IGenericRepository<TicketChat>>();
        mockChats.Setup(r => r.GetAllAsync()).Returns(Array.Empty<TicketChat>().BuildMock());
        _mockUow.Setup(x => x.TicketChats).Returns(mockChats.Object);

        _handler = new TicketGetListQueryHandler(
            _mockUow.Object, _mockCurrentUser.Object,
            new TicketService.Infrastructure.Implements.Utils.SlaCalculator());
    }

    private static Ticket MakeTicket(
        TicketStatusEnum status = TicketStatusEnum.Pending,
        TicketPriorityEnum? priority = null,
        TicketCategoryEnum category = TicketCategoryEnum.Other,
        string title = "Test Ticket",
        string code = "T-001",
        Guid? batteryAssetId = null,
        bool isDeleted = false,
        DateTime? createdAt = null) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            BatteryAssetId = batteryAssetId ?? Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Title = title,
            Description = "description",
            Category = category,
            Priority = priority,
            Status = status,
            Origin = TicketOriginEnum.ManualByCustomer,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsDeleted = isDeleted
        };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(() => new TestAsyncEnumerable<Ticket>(tickets));

    [Fact]
    public async Task Handle_ReturnsOnlyNonDeletedTickets()
    {
        SetupMock([MakeTicket(), MakeTicket(isDeleted: true)]);

        var result = await _handler.Handle(new TicketGetListQuery { PageNumber = 1, PageSize = 10 }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(1);
        result.Data.TotalItems.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FilterByStatus_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(TicketStatusEnum.Open),
            MakeTicket(TicketStatusEnum.Pending),
            MakeTicket(TicketStatusEnum.InProgress)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Status = TicketStatusEnum.Open,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Status.Should().Be(TicketStatusEnum.Open);
    }

    [Fact]
    public async Task Handle_AdminDefaultList_IncludesOpenTickets()
    {
        _mockCurrentUser.Setup(x => x.Role).Returns("Admin");
        SetupMock([
            MakeTicket(TicketStatusEnum.Open, code: "OPEN"),
            MakeTicket(TicketStatusEnum.Pending, code: "PENDING")
        ]);

        var result = await _handler.Handle(
            new TicketGetListQuery { PageNumber = 1, PageSize = 10 }, default);

        result.Data!.Items.Select(ticket => ticket.Code)
            .Should().BeEquivalentTo("OPEN", "PENDING");
    }

    [Fact]
    public async Task Handle_ManagerDefaultList_ExcludesOpenTickets()
    {
        _mockCurrentUser.Setup(x => x.Role).Returns("Manager");
        SetupMock([
            MakeTicket(TicketStatusEnum.Open, code: "OPEN"),
            MakeTicket(TicketStatusEnum.Pending, code: "PENDING")
        ]);

        var result = await _handler.Handle(
            new TicketGetListQuery { PageNumber = 1, PageSize = 10 }, default);

        result.Data!.Items.Should().ContainSingle().Which.Code.Should().Be("PENDING");
    }

    /// <summary>
    /// Màn so sánh trước khi gộp ticket cần MỌI trạng thái trong một lượt gọi: ticket do AI gợi ý
    /// là trùng lặp thường vẫn đang Open chờ triage. Trước khi có cờ này FE phải gọi endpoint hai
    /// lần rồi tự nối kết quả, khiến phân trang và sắp xếp sai vì mỗi lượt lấy riêng một trang.
    /// </summary>
    [Fact]
    public async Task Handle_ManagerWithIncludeOpen_IncludesOpenTickets()
    {
        _mockCurrentUser.Setup(x => x.Role).Returns("Manager");
        SetupMock([
            MakeTicket(TicketStatusEnum.Open, code: "OPEN"),
            MakeTicket(TicketStatusEnum.Pending, code: "PENDING")
        ]);

        var result = await _handler.Handle(
            new TicketGetListQuery { PageNumber = 1, PageSize = 10, IncludeOpen = true }, default);

        result.Data!.Items.Select(ticket => ticket.Code)
            .Should().BeEquivalentTo("OPEN", "PENDING");
    }

    /// <summary>
    /// Lọc Status tường minh luôn thắng IncludeOpen — cờ này chỉ bỏ bộ lọc ẩn Open MẶC ĐỊNH,
    /// nó không được phép nới rộng một truy vấn mà người dùng đã thu hẹp có chủ đích.
    /// </summary>
    [Fact]
    public async Task Handle_IncludeOpenWithExplicitStatus_StatusFilterWins()
    {
        _mockCurrentUser.Setup(x => x.Role).Returns("Manager");
        SetupMock([
            MakeTicket(TicketStatusEnum.Open, code: "OPEN"),
            MakeTicket(TicketStatusEnum.Pending, code: "PENDING")
        ]);

        var result = await _handler.Handle(
            new TicketGetListQuery
            {
                PageNumber = 1,
                PageSize = 10,
                IncludeOpen = true,
                Status = TicketStatusEnum.Pending
            }, default);

        result.Data!.Items.Should().ContainSingle().Which.Code.Should().Be("PENDING");
    }

    [Fact]
    public async Task Handle_FilterByKeyword_MatchesTitleCaseInsensitive()
    {
        SetupMock([
            MakeTicket(title: "Battery Overheating Issue"),
            MakeTicket(title: "Charging Problem")
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Keyword = "overheat",
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
        result.Data.Items[0].Title.Should().Contain("Overheating");
    }

    [Fact]
    public async Task Handle_FilterByPriority_ReturnsMatchingOnly()
    {
        SetupMock([
            MakeTicket(priority: TicketPriorityEnum.P1Critical),
            MakeTicket(priority: TicketPriorityEnum.P2High),
            MakeTicket(priority: TicketPriorityEnum.P3Normal)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Priority = TicketPriorityEnum.P1Critical,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_FilterByBatteryAssetId_ReturnsMatchingOnly()
    {
        var targetId = Guid.NewGuid();
        SetupMock([MakeTicket(batteryAssetId: targetId), MakeTicket()]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            BatteryAssetId = targetId,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(TicketSourceFilterEnum.Customer, "CUSTOMER")]
    [InlineData(TicketSourceFilterEnum.AiPredicted, "AI-ALERT,AI-CASCADE")]
    [InlineData(TicketSourceFilterEnum.Environmental, "ENVIRONMENTAL")]
    [InlineData(TicketSourceFilterEnum.PeriodicMaintenance, "PERIODIC")]
    public async Task Handle_FilterBySource_ReturnsOnlyMatchingTickets(
        TicketSourceFilterEnum source,
        string expectedCodes)
    {
        var customer = MakeTicket(code: "CUSTOMER");
        var aiAlert = MakeTicket(code: "AI-ALERT");
        aiAlert.Origin = TicketOriginEnum.AutoFromAlert;
        var aiCascade = MakeTicket(code: "AI-CASCADE");
        aiCascade.Origin = TicketOriginEnum.System;
        // Ticket môi trường nay mang origin RIÊNG — không còn dùng ké System/AutoFromAlert.
        var environmental = MakeTicket(code: "ENVIRONMENTAL");
        environmental.Origin = TicketOriginEnum.AutoFromEnvironment;
        environmental.EnvironmentalIncidentId = Guid.NewGuid();
        var periodic = MakeTicket(code: "PERIODIC");
        periodic.Origin = TicketOriginEnum.System;
        periodic.PeriodicMaintenanceDueAtUtc = DateTime.UtcNow.AddDays(7);

        SetupMock([customer, aiAlert, aiCascade, environmental, periodic]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Source = source,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Select(item => item.Code)
            .Should().BeEquivalentTo(expectedCodes.Split(','));
    }

    [Fact]
    public async Task Handle_IsDescendingTrue_OrdersNewestFirst()
    {
        var now = DateTime.UtcNow;
        SetupMock([
            MakeTicket(code: "OLD", createdAt: now.AddHours(-2)),
            MakeTicket(code: "NEW", createdAt: now)
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            IsDescending = true,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.Data!.Items[0].Code.Should().Be("NEW");
        result.Data.Items[1].Code.Should().Be("OLD");
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectPage()
    {
        SetupMock(Enumerable.Range(1, 5).Select(_ => MakeTicket()).ToList());

        var result = await _handler.Handle(new TicketGetListQuery
        {
            PageNumber = 2,
            PageSize = 3
        }, default);

        result.Data!.Items.Should().HaveCount(2);
        result.Data.TotalItems.Should().Be(5);
        result.Data.PageNumber.Should().Be(2);
        result.Data.PageSize.Should().Be(3);
    }

    /// <summary>
    /// Ticket InProgress kèm SlaTimer ở trạng thái cho trước. Mọi ticket đều InProgress để bộ
    /// lọc Status mặc định (ẩn Open) không xen vào phép kiểm SLA.
    /// </summary>
    private static Ticket MakeSlaTicket(string code, SlaTimerStatusEnum status, DateTime? warningSentAt)
    {
        // Priority phải là P1/P2/P3 thật ở CẢ ticket lẫn timer: SlaCalculator.GetSlaWorkingDays
        // ném ArgumentOutOfRange với giá trị mặc định (0) khi map sang SlaTimerDTO.
        var ticket = MakeTicket(TicketStatusEnum.InProgress, TicketPriorityEnum.P2High, code: code);
        ticket.SlaTimers.Add(new SlaTimer
        {
            TicketId = ticket.Id,
            Type = SlaTimerTypeEnum.Resolution,
            Priority = TicketPriorityEnum.P2High,
            StartedAt = DateTime.UtcNow.AddHours(-4),
            DueAt = DateTime.UtcNow.AddHours(1),
            OriginalDueAt = DateTime.UtcNow.AddHours(1),
            Status = status,
            WarningSentAt = warningSentAt,
            CurrentPauseStartedAt = status == SlaTimerStatusEnum.Paused ? DateTime.UtcNow : null
        });
        return ticket;
    }

    [Theory]
    [InlineData(SlaFilterEnum.Paused, "PAUSED")]
    [InlineData(SlaFilterEnum.Warning, "WARNING")]
    [InlineData(SlaFilterEnum.Breached, "BREACHED")]
    public async Task Handle_FilterBySla_ReturnsOnlyThatState(SlaFilterEnum sla, string expectedCode)
    {
        SetupMock([
            MakeSlaTicket("PAUSED", SlaTimerStatusEnum.Paused, warningSentAt: null),
            MakeSlaTicket("WARNING", SlaTimerStatusEnum.Running, warningSentAt: DateTime.UtcNow.AddMinutes(-10)),
            // Đã breach nhưng WarningSentAt VẪN còn (background job không xoá khi chuyển Breached).
            // Đây là ca dễ sai nhất: lọc Warning chỉ theo WarningSentAt thì ticket này lọt vào cả hai.
            MakeSlaTicket("BREACHED", SlaTimerStatusEnum.Breached, warningSentAt: DateTime.UtcNow.AddHours(-1)),
            MakeSlaTicket("RUNNING", SlaTimerStatusEnum.Running, warningSentAt: null),
            // Chưa chạy đồng hồ → không thuộc bộ lọc SLA nào.
            MakeTicket(TicketStatusEnum.InProgress, code: "NOTIMER")
        ]);

        var result = await _handler.Handle(new TicketGetListQuery
        {
            Sla = sla,
            PageNumber = 1,
            PageSize = 10
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle().Which.Code.Should().Be(expectedCode);
    }

    [Fact]
    public async Task Handle_NoSlaFilter_ReturnsEveryTicket()
    {
        SetupMock([
            MakeSlaTicket("PAUSED", SlaTimerStatusEnum.Paused, warningSentAt: null),
            MakeSlaTicket("BREACHED", SlaTimerStatusEnum.Breached, warningSentAt: null),
            MakeTicket(TicketStatusEnum.InProgress, code: "NOTIMER")
        ]);

        var result = await _handler.Handle(new TicketGetListQuery { PageNumber = 1, PageSize = 10 }, default);

        result.Data!.Items.Should().HaveCount(3);
    }
}
