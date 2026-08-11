using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Query.ChatKbSuggestions;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class ChatKbSuggestionsQueryHandler : IRequestHandler<ChatKbSuggestionsQuery, CommonResponse<List<KbArticleSuggestDTO>>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbSuggestionService _kbSuggestionService;

    public ChatKbSuggestionsQueryHandler(ITicketUnitOfWork uow, IKbSuggestionService kbSuggestionService)
    {
        _uow = uow;
        _kbSuggestionService = kbSuggestionService;
    }

    public async Task<CommonResponse<List<KbArticleSuggestDTO>>> Handle(ChatKbSuggestionsQuery query, CancellationToken ct)
    {
        var chat = await _uow.TicketChats.GetAllAsync()
            .Include(c => c.Ticket)
            .FirstOrDefaultAsync(c => c.Id == query.ChatId && c.TicketId == query.TicketId && !c.IsDeleted, ct);

        if (chat == null)
            return Fail(404, "Chat not found.");

        var topN = Math.Clamp(query.TopN, 1, 10);
        var suggestions = await _kbSuggestionService.SuggestByChatBodyAsync(
            chat.Body,
            chat.Ticket.Category,
            topN,
            ct);

        return new CommonResponse<List<KbArticleSuggestDTO>>
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = suggestions.ToList()
        };
    }

    private static CommonResponse<List<KbArticleSuggestDTO>> Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
