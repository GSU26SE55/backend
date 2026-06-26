using MediatR;
using TicketService.Application.CQRS.Command.ChatExportPdf;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.Chats;

/// <summary>Delegates PDF generation to IPdfExporter. #568.</summary>
public class ChatExportPdfCommandHandler : IRequestHandler<ChatExportPdfCommand, Stream>
{
    private readonly IPdfExporter _pdfExporter;

    public ChatExportPdfCommandHandler(IPdfExporter pdfExporter)
    {
        _pdfExporter = pdfExporter;
    }

    public Task<Stream> Handle(ChatExportPdfCommand request, CancellationToken ct)
        => _pdfExporter.ExportChatPdfAsync(request.TicketId, request.ViewerRole, ct);
}
