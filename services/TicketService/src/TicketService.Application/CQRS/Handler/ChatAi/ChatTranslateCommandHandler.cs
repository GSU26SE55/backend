using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ChatTranslate;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.ChatAi;

public class ChatTranslateCommandHandler : IRequestHandler<ChatTranslateCommand, ChatTranslateResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatTextAiClient _aiClient;
    private readonly ICacheService _cache;
    private readonly ILogger<ChatTranslateCommandHandler> _logger;

    private static readonly TimeSpan TranslationCacheTtl = TimeSpan.FromDays(30);

    public ChatTranslateCommandHandler(
        ITicketUnitOfWork uow,
        IChatTextAiClient aiClient,
        ICacheService cache,
        ILogger<ChatTranslateCommandHandler> logger)
    {
        _uow = uow;
        _aiClient = aiClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ChatTranslateResponse> Handle(ChatTranslateCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            return Fail("Target language is required.");

        if (request.TargetLanguage.Length > 5)
            return Fail("Target language code must not exceed 5 characters.");

        var targetLang = request.TargetLanguage.ToLowerInvariant();

        var ticket = await _uow.Tickets
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.Id == request.TicketId)
            .FirstOrDefaultAsync(ct);

        if (ticket == null)
            return Fail("Ticket not found.");

        var chat = await _uow.TicketChats
            .GetAllAsync()
            .Where(c => !c.IsDeleted && c.Id == request.ChatId && c.TicketId == request.TicketId)
            .FirstOrDefaultAsync(ct);

        if (chat == null)
            return Fail("Chat not found.");

        // 1. Redis cache hit
        var cacheKey = $"chat_translation:{request.ChatId:N}:{targetLang}";
        var cached = await _cache.GetAsync<ChatTranslateDTO>(cacheKey, ct);
        if (cached != null)
        {
            cached.FromCache = true;
            return new ChatTranslateResponse { IsSuccess = true, StatusCode = 200, Data = cached };
        }

        // 2. DB hit
        var existing = await _uow.TicketChatTranslations
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.ChatId == request.ChatId && t.TargetLanguage == targetLang)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            var dto = BuildDto(existing, chat.OriginalLanguage, fromCache: false);
            await _cache.SetAsync(cacheKey, dto, TranslationCacheTtl, ct);
            dto.FromCache = false;
            return new ChatTranslateResponse { IsSuccess = true, StatusCode = 200, Data = dto };
        }

        // 3. AI translation
        string translatedBody;
        try
        {
            translatedBody = await _aiClient.TranslateAsync(chat.Body, targetLang, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Translation AI service unavailable for chat {ChatId}", request.ChatId);
            return Fail("Translation service unavailable.");
        }

        // 4. Persist to DB
        var translation = new TicketChatTranslation
        {
            ChatId = request.ChatId,
            TargetLanguage = targetLang,
            TranslatedBody = translatedBody,
            Provider = TranslationProviderEnum.GeminiAi,
            TranslatedAt = DateTime.UtcNow,
            Chat = chat,
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.TicketChatTranslations.AddAsync(translation);
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            _logger.LogError(ex, "Failed to persist translation for chat {ChatId}", request.ChatId);
            return Fail("Failed to save translation.");
        }

        // 5. Cache result
        var result = BuildDto(translation, chat.OriginalLanguage, fromCache: false);
        await _cache.SetAsync(cacheKey, result, TranslationCacheTtl, ct);

        return new ChatTranslateResponse { IsSuccess = true, StatusCode = 200, Data = result };
    }

    private static ChatTranslateDTO BuildDto(TicketChatTranslation t, string? originalLanguage, bool fromCache)
        => new()
        {
            TranslatedBody = t.TranslatedBody,
            TargetLanguage = t.TargetLanguage,
            OriginalLanguage = originalLanguage,
            Provider = t.Provider.ToString(),
            FromCache = fromCache,
        };

    private static ChatTranslateResponse Fail(string message)
        => new() { IsSuccess = false, StatusCode = 200, Message = message };
}
