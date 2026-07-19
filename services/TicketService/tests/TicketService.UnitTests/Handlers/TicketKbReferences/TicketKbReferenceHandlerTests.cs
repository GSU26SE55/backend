using System.Text.Json;
using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.CQRS.Handler.TicketKbReferences;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Utils;

namespace TicketService.UnitTests.Handlers.TicketKbReferences;

public class TicketKbReferenceHandlerTests
{
    private readonly Mock<ITicketCurrentUserService> _currentUserMock = new();

    private static Ticket BuildTicket(TicketStatusEnum status = TicketStatusEnum.InProgress, Guid? assignedStaffId = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2026-0001",
            Status = status,
            Category = TicketCategoryEnum.Charging,
            Title = "Test Ticket",
            Description = "Test",
            AssignedStaffId = assignedStaffId
        };

    private static KnowledgeBaseArticle BuildArticle(bool isInternalOnly = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Code = "KB-2026-0001",
            Title = "Test Article",
            Category = TicketCategoryEnum.Charging,
            Symptoms = JsonDocument.Parse("\"s\""),
            DiagnosisSteps = JsonDocument.Parse("\"d\""),
            SolutionSteps = JsonDocument.Parse("\"sol\""),
            Status = KbArticleStatusEnum.Published,
            IsInternalOnly = isInternalOnly
        };

    // ────────────────────────────────────────────────
    // Add tests
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Add_WhenTicketNotFound_Returns404()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = Guid.NewGuid(),
            KbArticleId = Guid.NewGuid(),
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Add_WhenArticleNotFound_Returns404()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = Guid.NewGuid(),
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Add_WhenAlreadyExists_RestoresAndUpdates()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket();
        var article = BuildArticle();
        var existingRef = new TicketKbReference
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            KbArticleCode = article.Code,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            ReferencedByUserId = Guid.NewGuid(),
            IsDeleted = true
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article },
            kbRefSeed: new[] { existingRef });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            Note = "Updated Note"
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.UpdateAsync(It.Is<TicketKbReference>(
            x => x.TicketId == ticket.Id &&
                 x.KbArticleId == article.Id &&
                 x.IsDeleted == false &&
                 x.Note == "Updated Note")), Times.Once);
    }

    [Fact]
    public async Task Add_WhenValid_ReturnsSuccess()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket();
        var article = BuildArticle();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ProvidedToCustomer,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.AddAsync(It.Is<TicketKbReference>(
            x => x.TicketId == ticket.Id &&
                 x.KbArticleId == article.Id &&
                 x.KbArticleCode == article.Code)), Times.Once);
    }

    [Fact]
    public async Task Add_InternalOnlyArticle_ProvidedToCustomer_Returns422()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket();
        var article = BuildArticle(isInternalOnly: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ProvidedToCustomer,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422); // vi phạm rule nghiệp vụ — không phải lỗi quyền
        kbRefs.Verify(r => r.AddAsync(It.IsAny<TicketKbReference>()), Times.Never);
        kbRefs.Verify(r => r.UpdateAsync(It.IsAny<TicketKbReference>()), Times.Never);
    }

    [Fact]
    public async Task Add_InternalOnlyArticle_ConsultedDuringResolve_Returns200()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket();
        var article = BuildArticle(isInternalOnly: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.AddAsync(It.IsAny<TicketKbReference>()), Times.Once);
    }

    [Theory]
    [InlineData(KbReferenceTypeEnum.GeneratedAfterResolve)]
    [InlineData(KbReferenceTypeEnum.ProvidedToCustomer)]
    public async Task Add_ResolvedTicket_AfterResolveTypes_Returns200(KbReferenceTypeEnum referenceType)
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket(TicketStatusEnum.Resolved);
        var article = BuildArticle();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = referenceType,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.AddAsync(It.IsAny<TicketKbReference>()), Times.Once);
    }

    [Fact]
    public async Task Add_ResolvedTicket_ConsultedDuringResolve_Returns409()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket(TicketStatusEnum.Resolved);
        var article = BuildArticle();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409); // xung đột trạng thái ticket — không phải lỗi quyền
    }

    [Theory]
    [InlineData(TicketStatusEnum.ClosedPendingRate)]
    [InlineData(TicketStatusEnum.Closed)]
    public async Task Add_ClosedTicket_AfterResolveTypes_StillReturns409(TicketStatusEnum status)
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket(status);
        var article = BuildArticle();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.GeneratedAfterResolve,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409); // từ ClosedPendingRate trở đi chặn mọi type — xung đột trạng thái
    }

    [Fact]
    public async Task Add_ResolvedTicket_ProvidedToCustomer_InternalOnlyArticle_StillReturns422()
    {
        _currentUserMock.Setup(s => s.Role).Returns("Admin");
        var ticket = BuildTicket(TicketStatusEnum.Resolved);
        var article = BuildArticle(isInternalOnly: true);

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ProvidedToCustomer,
            CurrentUserId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(422); // guard nghiệp vụ internal-only vẫn thắng dù state Resolved cho phép type này
        kbRefs.Verify(r => r.AddAsync(It.IsAny<TicketKbReference>()), Times.Never);
    }

    [Fact]
    public async Task Add_WhenStaffNotAssigned_Returns403()
    {
        var staffId = Guid.NewGuid();
        var otherStaffId = Guid.NewGuid();
        var ticket = BuildTicket(assignedStaffId: otherStaffId);
        var article = BuildArticle();

        _currentUserMock.Setup(s => s.Role).Returns("Staff");

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ProvidedToCustomer,
            CurrentUserId = staffId
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Add_WhenStaffAssigned_Returns200()
    {
        var staffId = Guid.NewGuid();
        var ticket = BuildTicket(assignedStaffId: staffId);
        var article = BuildArticle();

        _currentUserMock.Setup(s => s.Role).Returns("Staff");

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object, _currentUserMock.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ProvidedToCustomer,
            CurrentUserId = staffId
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.AddAsync(It.Is<TicketKbReference>(
            x => x.TicketId == ticket.Id &&
                 x.KbArticleId == article.Id)), Times.Once);
    }

    // ────────────────────────────────────────────────
    // Remove tests
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Remove_WhenReferenceNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var handler = new RemoveTicketKbReferenceCommandHandler(uow.Object);

        var result = await handler.Handle(new RemoveTicketKbReferenceCommand
        {
            ReferenceId = Guid.NewGuid()
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Remove_WhenValid_SoftDeletes()
    {
        var reference = new TicketKbReference
        {
            Id = Guid.NewGuid(),
            TicketId = Guid.NewGuid(),
            KbArticleId = Guid.NewGuid(),
            KbArticleCode = "KB-2026-0001",
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            ReferencedByUserId = Guid.NewGuid()
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs, _) = MockTicketUnitOfWork.BuildExtended(
            kbRefSeed: new[] { reference });
        var handler = new RemoveTicketKbReferenceCommandHandler(uow.Object);

        var result = await handler.Handle(new RemoveTicketKbReferenceCommand
        {
            ReferenceId = reference.Id
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        kbRefs.Verify(r => r.DeleteAsync(It.Is<TicketKbReference>(x => x.Id == reference.Id)), Times.Once);
    }
}
