using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.ChatAi;

public class ChatSummarizeCommandHandler : IRequestHandler<ChatSummarizeCommand, ChatSummarizeResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatTextAiClient _aiClient;
    private readonly IPiiDetector _piiDetector;
    private readonly ChatOptions _opts;
    private readonly ILogger<ChatSummarizeCommandHandler> _logger;

    public ChatSummarizeCommandHandler(
        ITicketUnitOfWork uow,
        IChatTextAiClient aiClient,
        IPiiDetector piiDetector,
        IOptions<ChatOptions> opts,
        ILogger<ChatSummarizeCommandHandler> logger)
    {
        _uow = uow;
        _aiClient = aiClient;
        _piiDetector = piiDetector;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task<ChatSummarizeResponse> Handle(ChatSummarizeCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.Id == request.TicketId)
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return new ChatSummarizeResponse { IsSuccess = false, StatusCode = 200, Message = "Ticket not found" };

        var chats = await _uow.TicketChats
            .GetAllAsync()
            .Where(c => !c.IsDeleted && c.TicketId == request.TicketId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new { c.CreatedAt, c.Body, c.AuthorRole, c.AuthorDisplayName })
            .ToListAsync(ct);

        if (chats.Count == 0)
            return new ChatSummarizeResponse { IsSuccess = false, StatusCode = 200, Message = "No chats to summarize" };

        var rawContext = string.Join("\n", chats.Select(c =>
            $"[{c.CreatedAt:HH:mm}] [{c.AuthorRole}]: {c.Body}"));

        var (maskedContext, _) = await _piiDetector.MaskAsync(rawContext, ct);

        string summary;
        try
        {
            summary = await _aiClient.SummarizeAsync(maskedContext, _opts.Ai.SummarizeLinesCount, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "RATE_LIMITED")
        {
            _logger.LogWarning("[ChatSummarize] AI rate limit hit for ticket {TicketId}", request.TicketId);
            return new ChatSummarizeResponse { IsSuccess = false, StatusCode = 429, Message = "AI service is busy, please try again in a few seconds." };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ChatSummarize] AI call failed for ticket {TicketId}", request.TicketId);
            return new ChatSummarizeResponse { IsSuccess = false, StatusCode = 200, Message = "AI service unavailable" };
        }

        return new ChatSummarizeResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ChatSummarizeDTO { Summary = summary }
        };
    }
}
