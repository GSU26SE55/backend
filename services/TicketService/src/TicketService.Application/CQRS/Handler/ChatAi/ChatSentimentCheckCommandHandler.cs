using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.ChatAi;

public class ChatSentimentCheckCommandHandler : IRequestHandler<ChatSentimentCheckCommand, ChatSentimentCheckResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatTextAiClient _aiClient;
    private readonly IPiiDetector _piiDetector;
    private readonly ITicketChatRealtimeNotifier _notifier;
    private readonly ChatOptions _opts;

    public ChatSentimentCheckCommandHandler(
        ITicketUnitOfWork uow,
        IChatTextAiClient aiClient,
        IPiiDetector piiDetector,
        ITicketChatRealtimeNotifier notifier,
        IOptions<ChatOptions> opts)
    {
        _uow = uow;
        _aiClient = aiClient;
        _piiDetector = piiDetector;
        _notifier = notifier;
        _opts = opts.Value;
    }

    public async Task<ChatSentimentCheckResponse> Handle(ChatSentimentCheckCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.Id == request.TicketId)
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return new ChatSentimentCheckResponse { IsSuccess = false, StatusCode = 200, Message = "Ticket not found" };

        var customerChats = await _uow.TicketChats
            .GetAllAsync()
            .Where(c => !c.IsDeleted && c.TicketId == request.TicketId
                        && c.AuthorRole == ActorRoleEnum.Customer
                        && !c.IsInternal)
            .OrderByDescending(c => c.CreatedAt)
            .Take(_opts.Ai.SentimentAnalysisMaxChats)
            .Select(c => new { c.CreatedAt, c.Body })
            .ToListAsync(ct);

        if (customerChats.Count == 0)
            return new ChatSentimentCheckResponse { IsSuccess = false, StatusCode = 200, Message = "No customer chats to analyze" };

        var rawContext = string.Join("\n", customerChats
            .OrderBy(c => c.CreatedAt)
            .Select(c => $"[{c.CreatedAt:HH:mm}] {c.Body}"));

        var (maskedContext, _) = await _piiDetector.MaskAsync(rawContext, ct);

        double score;
        try
        {
            score = await _aiClient.AnalyzeSentimentAsync(maskedContext, ct);
        }
        catch (Exception)
        {
            return new ChatSentimentCheckResponse { IsSuccess = false, StatusCode = 200, Message = "AI service unavailable" };
        }

        score = Math.Clamp(score, -1.0, 1.0);
        var label = score switch
        {
            >= 0.3 => "Positive",
            >= -0.3 => "Neutral",
            >= -0.7 => "Negative",
            _ => "Critical"
        };

        var isAlertSent = false;
        if (score < _opts.Ai.SentimentAlertThreshold)
        {
            try
            {
                await _notifier.NotifySentimentAlertAsync(request.TicketId, score, label, ct);
                isAlertSent = true;
            }
            catch (Exception)
            {
                // Alert thất bại không chặn response
            }
        }

        return new ChatSentimentCheckResponse
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ChatSentimentCheckDTO
            {
                Score = score,
                Label = label,
                IsAlertSent = isAlertSent,
            }
        };
    }
}
