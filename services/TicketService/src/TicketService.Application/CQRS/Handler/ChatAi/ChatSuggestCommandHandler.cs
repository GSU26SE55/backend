using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.CQRS.Command.ChatSuggest;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.ChatAi;

public class ChatSuggestCommandHandler : IRequestHandler<ChatSuggestCommand, ChatSuggestResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IPiiDetector _piiDetector;
    private readonly IChatAiSuggestionClient _aiClient;
    private readonly ChatOptions _opts;

    public ChatSuggestCommandHandler(
        ITicketUnitOfWork uow,
        IPiiDetector piiDetector,
        IChatAiSuggestionClient aiClient,
        IOptions<ChatOptions> opts)
    {
        _uow = uow;
        _piiDetector = piiDetector;
        _aiClient = aiClient;
        _opts = opts.Value;
    }

    public async Task<ChatSuggestResponse> Handle(ChatSuggestCommand request, CancellationToken ct)
    {
        var ticket = await _uow.Tickets
            .GetAllAsync()
            .Include(t => t.Chats)
            .Where(t => !t.IsDeleted && t.Id == request.TicketId)
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return new ChatSuggestResponse { IsSuccess = false, Message = "Ticket not found" };

        var recentChats = ticket.Chats
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .Take(5)
            .Reverse()
            .Select(c => $"[{c.CreatedAt:HH:mm}] {c.Body}")
            .ToList();

        var rawContext = BuildContext(ticket.Title, ticket.Description, ticket.Category.ToString(), recentChats);

        var (maskedContext, _) = await _piiDetector.MaskAsync(rawContext, ct);

        IReadOnlyList<string> suggestions;
        try
        {
            suggestions = await _aiClient.SuggestAsync(
                maskedContext,
                request.Intent,
                ticket.Category.ToString(),
                _opts.Ai.MaxSuggestionsPerCall,
                ct);
        }
        catch (Exception)
        {
            return new ChatSuggestResponse { IsSuccess = false, Message = "AI service unavailable" };
        }

        if (suggestions.Count == 0)
            return new ChatSuggestResponse { IsSuccess = false, Message = "AI service unavailable" };

        var entity = new ChatAiSuggestion
        {
            TicketId = ticket.Id,
            Ticket = ticket,
            SuggestedAt = DateTime.UtcNow,
            Intent = request.Intent,
            Suggestions = suggestions.ToList(),
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.ChatAiSuggestions.AddAsync(entity);
            await _uow.CommitTransactionAsync();
        }
        catch (Exception)
        {
            await _uow.RollbackTransactionAsync();
            return new ChatSuggestResponse { IsSuccess = false, Message = "Failed to save suggestion" };
        }

        return new ChatSuggestResponse
        {
            IsSuccess = true,
            Data = new ChatSuggestDTO
            {
                SuggestionId = entity.Id.ToString(),
                Suggestions = entity.Suggestions,
            }
        };
    }

    private static string BuildContext(string title, string description, string category, IEnumerable<string> recentChats)
    {
        var chatsSection = recentChats.Any()
            ? string.Join("\n", recentChats)
            : "(chưa có chat)";

        return $"[Ticket] {title}\n[Mô tả] {description}\n[Danh mục] {category}\n[Chat gần đây]\n{chatsSection}";
    }
}
