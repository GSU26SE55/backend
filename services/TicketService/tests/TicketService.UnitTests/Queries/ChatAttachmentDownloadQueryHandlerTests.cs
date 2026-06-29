using Microsoft.Extensions.Options;
using SharedKernels.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Handler.Chats;
using TicketService.Application.CQRS.Query.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.UnitTests.Queries;

public class ChatAttachmentDownloadQueryHandlerTests
{
    private readonly Mock<ITicketUnitOfWork> _uow = new();
    private readonly Mock<IGenericRepository<Ticket>> _ticketsRepo = new();
    private readonly Mock<IGenericRepository<TicketAttachment>> _attachmentsRepo = new();

    public ChatAttachmentDownloadQueryHandlerTests()
    {
        _uow.Setup(u => u.Tickets).Returns(_ticketsRepo.Object);
        _uow.Setup(u => u.TicketAttachments).Returns(_attachmentsRepo.Object);
    }

    private ChatAttachmentDownloadQueryHandler CreateHandler(bool enableVirusScan = true) =>
        new(_uow.Object, Options.Create(new ChatOptions
        {
            Features = new ChatOptions.FeaturesSection { EnableVirusScan = enableVirusScan },
            VirusScan = new ChatOptions.VirusScanSection { FileStorageBaseUrl = "http://files" }
        }));

    private static Ticket MakeTicket(Guid id, Guid customerId, Guid? assignedStaffId = null) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test",
        Description = "desc",
        CustomerId = customerId,
        AssignedStaffId = assignedStaffId,
        Status = TicketStatusEnum.Open
    };

    private static TicketAttachment MakeAttachment(
        Guid id, Guid ticketId, Guid chatId,
        VirusScanStatusEnum status = VirusScanStatusEnum.Pending) => new()
        {
            Id = id,
            TicketId = ticketId,
            ChatId = chatId,
            FileId = id,
            FileName = "file.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            VirusScanStatus = status,
            Ticket = null!,
            UploadedByUserId = Guid.NewGuid()
        };

    private void SetupTickets(params Ticket[] tickets)
        => _ticketsRepo.Setup(r => r.GetAllAsync()).Returns(tickets.BuildMock());

    private void SetupAttachments(params TicketAttachment[] attachments)
        => _attachmentsRepo.Setup(r => r.GetAllAsync()).Returns(attachments.BuildMock());

    #region Ticket Not Found

    [Fact]
    public async Task Handle_TicketNotFound_Returns404()
    {
        SetupTickets();
        SetupAttachments();

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = Guid.NewGuid(),
            ChatId = Guid.NewGuid(),
            AttachmentId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Ticket not found");
    }

    #endregion

    #region Access Control

    [Fact]
    public async Task Handle_CustomerWithWrongId_Returns403()
    {
        var ticketId = Guid.NewGuid();
        SetupTickets(MakeTicket(ticketId, Guid.NewGuid())); // different customerId
        SetupAttachments();

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            AttachmentId = Guid.NewGuid(),
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.Message.Should().Be("Forbidden");
    }

    [Fact]
    public async Task Handle_AdminRole_BypassesOwnershipCheck()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, Guid.NewGuid()); // random customer
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, VirusScanStatusEnum.Clean);
        SetupTickets(ticket);
        SetupAttachments(attachment);

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = Guid.NewGuid(),
            ActorRoles = ["Admin"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task Handle_AssignedStaff_CanAccess()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ticket = MakeTicket(ticketId, Guid.NewGuid(), assignedStaffId: staffId);
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, VirusScanStatusEnum.Clean);
        SetupTickets(ticket);
        SetupAttachments(attachment);

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = staffId,
            ActorRoles = ["Staff"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
    }

    #endregion

    #region Attachment Not Found

    [Fact]
    public async Task Handle_AttachmentNotFound_Returns404()
    {
        var ticketId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments();

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = Guid.NewGuid(),
            AttachmentId = Guid.NewGuid(),
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        result.Message.Should().Be("Attachment not found");
    }

    #endregion

    #region Virus Scan Disabled

    [Fact]
    public async Task Handle_VirusScanDisabled_ReturnsDownloadUrlRegardlessOfStatus()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, VirusScanStatusEnum.Pending);
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments(attachment);

        var result = await CreateHandler(enableVirusScan: false).Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().Contain(attachment.FileId.ToString());
    }

    #endregion

    #region Virus Scan Status Cases

    [Fact]
    public async Task Handle_InfectedFile_Returns451()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments(MakeAttachment(attachmentId, ticketId, chatId, VirusScanStatusEnum.Infected));

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(451);
        result.Message.Should().Be("File is infected and cannot be downloaded");
    }

    [Fact]
    public async Task Handle_CleanFile_Returns200WithDownloadUrl()
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var attachment = MakeAttachment(attachmentId, ticketId, chatId, VirusScanStatusEnum.Clean);
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments(attachment);

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().Contain(attachment.FileId.ToString());
        result.Data.Should().StartWith("http://files");
    }

    [Theory]
    [InlineData(VirusScanStatusEnum.Pending)]
    [InlineData(VirusScanStatusEnum.Failed)]
    public async Task Handle_PendingOrFailed_Returns202(VirusScanStatusEnum status)
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments(MakeAttachment(attachmentId, ticketId, chatId, status));

        var result = await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
    }

    #endregion
}
