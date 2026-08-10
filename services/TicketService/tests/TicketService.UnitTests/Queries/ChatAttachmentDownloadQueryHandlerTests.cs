using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SharedContracts.Common.Responses;
using SharedKernels.Interfaces;
using SharedKernels.Security;
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

    /// <summary>GH-723 — khoá ký grant; phải trùng khoá mà FileStorageService dùng để xác minh.</summary>
    private const string TestSecretKey = "test-secret-key-for-file-access-grant-0123456789";

    private ChatAttachmentDownloadQueryHandler CreateHandler(bool enableVirusScan = true) =>
        new(_uow.Object, Options.Create(new ChatOptions
        {
            Features = new ChatOptions.FeaturesSection { EnableVirusScan = enableVirusScan },
            VirusScan = new ChatOptions.VirusScanSection { FileStorageBaseUrl = "http://files" }
        }),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:SecretKey"] = TestSecretKey })
            .Build());

    private static Ticket MakeTicket(Guid id, Guid customerId, Guid? PrimaryHandlerStaffId = null) => new()
    {
        Id = id,
        Code = "T-001",
        Title = "Test",
        Description = "desc",
        CustomerId = customerId,
        PrimaryHandlerStaffId = PrimaryHandlerStaffId,
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
        var ticket = MakeTicket(ticketId, Guid.NewGuid(), PrimaryHandlerStaffId: staffId);
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

    /// <summary>
    /// GH-723 — hai nửa phải khớp nhau: grant mà TicketService gắn vào URL phải được
    /// <c>FileAccessGrant.Validate</c> (thứ FileStorageService gọi) chấp nhận, đúng cặp
    /// (fileId, người gọi). Không có test này thì hai service có thể xanh riêng lẻ mà
    /// ghép vào vẫn 403.
    /// </summary>
    [Fact]
    public async Task Handle_CleanFile_UrlCarriesGrantAcceptedByFileStorage()
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

        var query = new Uri(result.Data!).Query.TrimStart('?');
        var grant = Uri.UnescapeDataString(
            query.Split('&').Single(p => p.StartsWith($"{FileAccessGrant.QueryParameterName}=", StringComparison.Ordinal))
                 [(FileAccessGrant.QueryParameterName.Length + 1)..]);

        FileAccessGrant.Validate(TestSecretKey, grant, attachment.FileId, actorId, DateTimeOffset.UtcNow)
            .Should().BeTrue("grant phải hợp lệ đúng cặp (fileId, người gọi)");

        // Và KHÔNG hợp lệ cho người khác / file khác.
        FileAccessGrant.Validate(TestSecretKey, grant, attachment.FileId, Guid.NewGuid(), DateTimeOffset.UtcNow)
            .Should().BeFalse();
        FileAccessGrant.Validate(TestSecretKey, grant, Guid.NewGuid(), actorId, DateTimeOffset.UtcNow)
            .Should().BeFalse();
    }

    /// <summary>
    /// GH-790 — chỉ những trạng thái CHƯA có kết luận mới là 202 "thử lại sau".
    /// </summary>
    [Theory]
    [InlineData(VirusScanStatusEnum.Pending)]
    [InlineData(VirusScanStatusEnum.Scanning)]
    public async Task Handle_ScanNotFinished_Returns202(VirusScanStatusEnum status)
    {
        var result = await DownloadWithScanStatusAsync(status);

        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(202);
    }

    /// <summary>
    /// GH-790 — <c>Failed</c> KHÔNG còn bị gộp vào 202.
    /// </summary>
    /// <remarks>
    /// Trước đây mọi trạng thái ngoài Clean/Infected đều trả 202 "đang quét, thử lại sau". Với một
    /// lượt quét đã hỏng hẳn thì đó là lời nói dối: client hỏi lại mãi mãi và không bao giờ nhận
    /// được file — đúng triệu chứng mà issue mô tả. 503 nói đúng chuyện đã xảy ra.
    /// </remarks>
    [Fact]
    public async Task Handle_ScanFailed_Returns503_NotAMisleading202()
    {
        var result = await DownloadWithScanStatusAsync(VirusScanStatusEnum.Failed);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(503);
    }

    private async Task<CommonResponse<string>> DownloadWithScanStatusAsync(VirusScanStatusEnum status)
    {
        var ticketId = Guid.NewGuid();
        var chatId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        SetupTickets(MakeTicket(ticketId, actorId));
        SetupAttachments(MakeAttachment(attachmentId, ticketId, chatId, status));

        return await CreateHandler().Handle(new ChatAttachmentDownloadQuery
        {
            TicketId = ticketId,
            ChatId = chatId,
            AttachmentId = attachmentId,
            ActorUserId = actorId,
            ActorRoles = ["Customer"]
        }, CancellationToken.None);
    }

    #endregion
}
