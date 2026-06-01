using SharedKernels.Interfaces;
using TicketService.Application.CQRS.Query.TicketGetById;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Queries;

public class TicketGetByIdQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _mockUow = new();
    private readonly Mock<IGenericRepository<Ticket>> _mockRepo = new();
    private readonly TicketGetByIdQueryHandler _handler;

    public TicketGetByIdQueryHandlerTests()
    {
        _mockUow.Setup(x => x.Tickets).Returns(_mockRepo.Object);
        _handler = new TicketGetByIdQueryHandler(_mockUow.Object);
    }

    private static Ticket MakeTicket(Guid? customerId = null, Guid? assignedStaffId = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = "T-001",
        BatteryAssetId = Guid.NewGuid(),
        CustomerId = customerId ?? Guid.NewGuid(),
        AssignedStaffId = assignedStaffId,
        Title = "Test Ticket",
        Description = "desc",
        Category = TicketCategoryEnum.Other,
        Status = TicketStatusEnum.InProgress,
        Origin = TicketOriginEnum.ManualByCustomer,
        CreatedAt = DateTime.UtcNow,
        Activities = new List<TicketActivity>(),
        Comments = new List<TicketComment>(),
        MaintenanceLogs = new List<MaintenanceLog>()
    };

    private void SetupMock(List<Ticket> tickets)
        => _mockRepo.Setup(r => r.GetAllAsync()).Returns(tickets.BuildMockDbSet().Object);

    [Fact]
    public async Task Handle_AdminCanReadAnyTicket_Returns200()
    {
        var ticket = MakeTicket();
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = Guid.NewGuid(), ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data!.Id.Should().Be(ticket.Id.ToString());
    }

    [Fact]
    public async Task Handle_ManagerCanReadAnyTicket_Returns200()
    {
        var ticket = MakeTicket();
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = Guid.NewGuid(), ActorRoles = ["Manager"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_CustomerCanReadOwnTicket_Returns200()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = customerId, ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_CustomerCannotReadOtherCustomerTicket_Returns403()
    {
        var ticket = MakeTicket(customerId: Guid.NewGuid());
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = Guid.NewGuid(), ActorRoles = ["Customer"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_StaffCanReadAssignedTicket_Returns200()
    {
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(assignedStaffId: staffId);
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = staffId, ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_StaffCannotReadUnassignedTicket_Returns403()
    {
        var ticket = MakeTicket(assignedStaffId: Guid.NewGuid());
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = Guid.NewGuid(), ActorRoles = ["Staff"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupMock([]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = Guid.NewGuid(), ActorUserId = Guid.NewGuid(), ActorRoles = ["Admin"]
        }, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_InternalCommentsHiddenFromCustomer()
    {
        var customerId = Guid.NewGuid();
        var ticket = MakeTicket(customerId: customerId);
        ticket.Comments = new List<TicketComment>
        {
            new() { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorUserId = Guid.NewGuid(),
                    AuthorRole = ActorRoleEnum.Staff, Body = "Internal note", IsInternal = true,
                    CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), TicketId = ticket.Id, AuthorUserId = customerId,
                    AuthorRole = ActorRoleEnum.Customer, Body = "Public comment", IsInternal = false,
                    CreatedAt = DateTime.UtcNow }
        };
        SetupMock([ticket]);

        var result = await _handler.Handle(new TicketGetByIdQuery
        {
            Id = ticket.Id, ActorUserId = customerId, ActorRoles = ["Customer"]
        }, default);

        result.Data!.Comments.Should().HaveCount(1);
        result.Data.Comments[0].IsInternal.Should().BeFalse();
    }
}
