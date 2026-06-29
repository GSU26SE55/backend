using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;
using TicketService.Infrastructure.Persistence;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>
/// QuestPDF implementation of IPdfExporter. #568.
/// Layout: ticket header → chat timeline (oldest first) → attachment listing.
/// Customer viewer: IsInternal=true chats are excluded.
/// </summary>
public class QuestPdfChatExporter : IPdfExporter
{
    // PDF export is read-only (AsNoTracking) — DbContext injected directly to bypass UoW overhead.
    private readonly TicketDbContext _db;

    public QuestPdfChatExporter(TicketDbContext db)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        _db = db;
    }

    public async Task<Stream> ExportChatPdfAsync(Guid ticketId, ActorRoleEnum viewerRole, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId && !t.IsDeleted, ct)
            ?? throw new InvalidOperationException("Ticket not found.");

        var chatsQuery = _db.TicketChats
            .AsNoTracking()
            .Where(c => c.TicketId == ticketId && !c.IsDeleted);

        if (viewerRole == ActorRoleEnum.Customer)
            chatsQuery = chatsQuery.Where(c => !c.IsInternal);

        var chats = await chatsQuery.OrderBy(c => c.CreatedAt).ToListAsync(ct);

        var ms = new MemoryStream();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text($"Ticket #{ticket.Code}").Bold().FontSize(16);
                    col.Item().Text($"Status: {ticket.Status}  |  Priority: {ticket.Priority}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    col.Item().Text($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                    col.Item().LineHorizontal(1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    if (chats.Count == 0)
                    {
                        col.Item().Text("Không có tin nhắn nào.").Italic();
                        return;
                    }

                    foreach (var chat in chats)
                    {
                        col.Item().Padding(4).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Column(chatCol =>
                        {
                            chatCol.Item().Row(row =>
                            {
                                row.RelativeItem().Text($"{chat.AuthorDisplayName ?? chat.AuthorUserId.ToString()} [{chat.AuthorRole}]")
                                    .Bold().FontSize(9);
                                row.ConstantItem(120).AlignRight().Text(chat.CreatedAt.ToString("yyyy-MM-dd HH:mm"))
                                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                            });

                            if (chat.IsInternal)
                                chatCol.Item().Text("[INTERNAL]").FontColor(Colors.Orange.Darken2).FontSize(8).Italic();

                            var body = chat.IsRedacted ? "[REDACTED — GDPR erasure]" : chat.Body;
                            chatCol.Item().PaddingTop(2).Text(body).FontSize(9);

                            if (chat.AttachmentFileIds.Count > 0)
                                chatCol.Item().PaddingTop(2)
                                    .Text($"Attachments: {chat.AttachmentFileIds.Count} file(s)")
                                    .FontSize(8).FontColor(Colors.Blue.Darken1);
                        });

                        col.Item().Height(4);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Trang ").FontSize(8);
                    t.CurrentPageNumber().FontSize(8);
                    t.Span(" / ").FontSize(8);
                    t.TotalPages().FontSize(8);
                });
            });
        }).GeneratePdf(ms);

        ms.Position = 0;
        return ms;
    }
}
