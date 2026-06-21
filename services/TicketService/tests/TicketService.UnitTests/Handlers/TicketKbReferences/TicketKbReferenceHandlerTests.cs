using FluentAssertions;
using Moq;
using TicketService.Application.CQRS.Command.TicketKbReferences;
using TicketService.Application.CQRS.Handler.TicketKbReferences;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;
using TicketService.UnitTests.Helpers;

namespace TicketService.UnitTests.Handlers.TicketKbReferences;

public class TicketKbReferenceHandlerTests
{
    private static Ticket BuildTicket(TicketStatusEnum status = TicketStatusEnum.InProgress)
        => new()
        {
            Id = Guid.NewGuid(),
            Code = "TKT-2026-0001",
            Status = status,
            Category = TicketCategoryEnum.Charging,
            Title = "Test Ticket",
            Description = "Test"
        };

    private static KnowledgeBaseArticle BuildArticle()
        => new()
        {
            Id = Guid.NewGuid(),
            Code = "KB-2026-0001",
            Title = "Test Article",
            Category = TicketCategoryEnum.Charging,
            Symptoms = "s",
            DiagnosisSteps = "d",
            SolutionSteps = "sol",
            Status = KbArticleStatusEnum.Published
        };

    // ────────────────────────────────────────────────
    // Add tests
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Add_WhenTicketNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object);

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
        var ticket = BuildTicket();
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object);

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
    public async Task Add_WhenDuplicate_Returns400()
    {
        var ticket = BuildTicket();
        var article = BuildArticle();
        var existingRef = new TicketKbReference
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            KbArticleCode = article.Code,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve,
            ReferencedByUserId = Guid.NewGuid()
        };

        var (uow, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article },
            kbRefSeed: new[] { existingRef });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object);

        var result = await handler.Handle(new AddTicketKbReferenceCommand
        {
            TicketId = ticket.Id,
            KbArticleId = article.Id,
            ReferenceType = KbReferenceTypeEnum.ConsultedDuringResolve
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Add_WhenValid_ReturnsSuccess()
    {
        var ticket = BuildTicket();
        var article = BuildArticle();

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs) = MockTicketUnitOfWork.BuildExtended(
            ticketSeed: new[] { ticket },
            kbSeed: new[] { article });
        var handler = new AddTicketKbReferenceCommandHandler(uow.Object);

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

    // ────────────────────────────────────────────────
    // Remove tests
    // ────────────────────────────────────────────────

    [Fact]
    public async Task Remove_WhenReferenceNotFound_Returns404()
    {
        var (uow, _, _, _, _, _, _, _, _, _, _, _, _) = MockTicketUnitOfWork.BuildExtended();
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

        var (uow, _, _, _, _, _, _, _, _, _, _, _, kbRefs) = MockTicketUnitOfWork.BuildExtended(
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
