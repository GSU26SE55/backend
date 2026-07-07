using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedContracts.Interfaces;
using TicketService.Application.CQRS.Command.ChatAi;
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
    private readonly ILanguageDetectionService _langDetector;
    private readonly IChatAuthorizationService _chatAuth;
    private readonly IPiiDetector _piiDetector;
    private readonly ILogger<ChatTranslateCommandHandler> _logger;

    private const string CacheKeyPrefix = "chat-translation";
    private const string LockKeyPrefix = "translation-lock";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(60);

    public ChatTranslateCommandHandler(
        ITicketUnitOfWork uow,
        IChatTextAiClient aiClient,
        ICacheService cache,
        ILanguageDetectionService langDetector,
        IChatAuthorizationService chatAuth,
        IPiiDetector piiDetector,
        ILogger<ChatTranslateCommandHandler> logger)
    {
        _uow = uow;
        _aiClient = aiClient;
        _cache = cache;
        _langDetector = langDetector;
        _chatAuth = chatAuth;
        _piiDetector = piiDetector;
        _logger = logger;
    }

    public async Task<ChatTranslateResponse> Handle(ChatTranslateCommand request, CancellationToken ct)
    {
        // --- 1. Validate ---
        if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            return Fail("Target language is required.");

        if (request.TargetLanguage.Length > 5)
            return Fail("Target language code must not exceed 5 characters.");

        var targetLang = request.TargetLanguage.ToLowerInvariant();

        // --- 2. Load ticket + chat (BOLA: TicketId in WHERE clause) ---
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

        // --- 3. Security ---
        var canAccess = await _chatAuth.CanAccessTicketAsync(
            request.TicketId, request.CurrentUserId, request.CurrentUserRoles);
        if (!canAccess)
            return Fail("Access denied.", 403);

        if (chat.IsInternal)
        {
            var canViewInternal = await _chatAuth.CanViewInternalChatsAsync(
                request.TicketId, request.CurrentUserId, request.CurrentUserRoles);
            if (!canViewInternal)
                return Fail("You do not have permission to translate internal messages.", 403);
        }

        // --- 4. Redis Cache-Aside ---
        var cacheKey = $"{CacheKeyPrefix}:{request.ChatId}:{targetLang}";
        var cached = await _cache.GetAsync<string>(cacheKey, ct);
        if (cached != null)
        {
            return new ChatTranslateResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new ChatTranslateDTO
                {
                    TranslatedBody = cached,
                    TargetLanguage = targetLang,
                    OriginalLanguage = chat.OriginalLanguage,
                    Provider = _aiClient.TranslationProvider.ToString(),
                    FromCache = true,
                }
            };
        }

        // --- 5. DB check ---
        var existing = await _uow.TicketChatTranslations
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.ChatId == request.ChatId && t.TargetLanguage == targetLang)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            await AddUserLinkIfMissingAsync(existing.Id, request.CurrentUserId);
            await _cache.SetAsync(cacheKey, existing.TranslatedBody, CacheTtl, ct);
            return OkResponse(existing, chat.OriginalLanguage, fromCache: false);
        }

        // --- 6. Mask PII + Lingua: skip AI if source == target ---
        var (maskedBody, maskKey) = await _piiDetector.MaskAsync(chat.Body, ct);

        var detectedLocal = _langDetector.Detect(maskedBody);
        if (detectedLocal != "und" && detectedLocal == targetLang)
        {
            return new ChatTranslateResponse
            {
                IsSuccess = true,
                StatusCode = 200,
                Data = new ChatTranslateDTO
                {
                    TranslatedBody = chat.Body,
                    TargetLanguage = targetLang,
                    OriginalLanguage = detectedLocal,
                    Provider = "None",
                    FromCache = false,
                }
            };
        }

        // --- 7. Mutex Lock (soft lock via Redis) ---
        var lockKey = $"{LockKeyPrefix}:{request.ChatId}:{targetLang}";
        var lockHeld = await _cache.GetAsync<string>(lockKey, ct);
        if (lockHeld != null)
        {
            // Another request is translating — check DB once more before giving up
            var retry = await _uow.TicketChatTranslations
                .GetAllAsync()
                .Where(t => !t.IsDeleted && t.ChatId == request.ChatId && t.TargetLanguage == targetLang)
                .FirstOrDefaultAsync(ct);

            if (retry != null)
            {
                await _cache.SetAsync(cacheKey, retry.TranslatedBody, CacheTtl, ct);
                return OkResponse(retry, chat.OriginalLanguage, fromCache: false);
            }

            return Fail("Translation is being processed. Please try again in a moment.", 202);
        }

        await _cache.SetAsync(lockKey, "1", LockTtl, ct);

        // --- 8. AI: translate masked body (Lingua result if known, fallback to OriginalLanguage, else AI detects) ---
        var knownSource = detectedLocal != "und"
            ? detectedLocal
            : !string.IsNullOrEmpty(chat.OriginalLanguage) && chat.OriginalLanguage != "und"
                ? chat.OriginalLanguage
                : null;

        string translatedBody;
        string detectedLang;
        try
        {
            (translatedBody, detectedLang) = await _aiClient.TranslateWithDetectAsync(maskedBody, targetLang, knownSource, ct);
            translatedBody = await _piiDetector.UnmaskAsync(translatedBody, maskKey, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "RATE_LIMITED")
        {
            await _cache.RemoveAsync(lockKey, ct);
            _logger.LogWarning("[ChatTranslate] AI rate limit hit for chat {ChatId}", request.ChatId);
            return Fail("AI service is busy, please try again shortly.", 429);
        }
        catch (Exception ex)
        {
            await _cache.RemoveAsync(lockKey, ct);
            _logger.LogWarning(ex, "[ChatTranslate] Translation AI unavailable for chat {ChatId}", request.ChatId);
            return Fail("Translation service unavailable.");
        }

        // --- 9. Save DB + user link ---
        var translation = new TicketChatTranslation
        {
            Id = Guid.NewGuid(),
            ChatId = request.ChatId,
            TargetLanguage = targetLang,
            TranslatedBody = translatedBody,
            Provider = _aiClient.TranslationProvider,
            TranslatedAt = DateTime.UtcNow,
            Chat = chat,
        };

        var userLink = new TicketChatTranslationUser
        {
            Id = Guid.NewGuid(),
            TranslationId = translation.Id,
            UserId = request.CurrentUserId,
        };

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.TicketChatTranslations.AddAsync(translation);
            await _uow.ChatTranslationUsers.AddAsync(userLink);
            await _uow.CommitTransactionAsync();
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Race condition: another request already saved — load and return it
            await _uow.RollbackTransactionAsync();
            await _cache.RemoveAsync(lockKey, ct);

            var race = await _uow.TicketChatTranslations
                .GetAllAsync()
                .Where(t => !t.IsDeleted && t.ChatId == request.ChatId && t.TargetLanguage == targetLang)
                .FirstOrDefaultAsync(ct);

            if (race != null)
            {
                await _cache.SetAsync(cacheKey, race.TranslatedBody, CacheTtl, ct);
                return OkResponse(race, chat.OriginalLanguage, fromCache: false);
            }

            return Fail("Failed to save translation.");
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            await _cache.RemoveAsync(lockKey, ct);
            _logger.LogError(ex, "[ChatTranslate] Failed to persist translation for chat {ChatId}", request.ChatId);
            return Fail("Failed to save translation.");
        }

        // --- 10. Cache + release lock ---
        await _cache.SetAsync(cacheKey, translatedBody, CacheTtl, ct);
        await _cache.RemoveAsync(lockKey, ct);

        // Update OriginalLanguage on the chat entity if not yet set (best-effort — non-critical)
        if (string.IsNullOrEmpty(chat.OriginalLanguage) && detectedLang != "und")
        {
            try
            {
                chat.OriginalLanguage = detectedLang;
                _uow.TicketChats.UpdateAsync(chat);
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ChatTranslate] Failed to update OriginalLanguage for chat {ChatId}", request.ChatId);
            }
        }

        return OkResponse(translation, detectedLang != "und" ? detectedLang : chat.OriginalLanguage, fromCache: false);
    }

    private async Task AddUserLinkIfMissingAsync(Guid translationId, Guid userId)
    {
        var alreadyLinked = await _uow.ChatTranslationUsers
            .GetAllAsync()
            .AnyAsync(u => !u.IsDeleted && u.TranslationId == translationId && u.UserId == userId);

        if (alreadyLinked)
            return;

        await _uow.BeginTransactionAsync();
        try
        {
            await _uow.ChatTranslationUsers.AddAsync(new TicketChatTranslationUser
            {
                Id = Guid.NewGuid(),
                TranslationId = translationId,
                UserId = userId,
            });
            await _uow.CommitTransactionAsync();
        }
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            _logger.LogWarning(ex, "[ChatTranslate] Failed to add user link for translation {TranslationId}", translationId);
        }
    }

    private static ChatTranslateResponse OkResponse(TicketChatTranslation t, string? originalLanguage, bool fromCache)
        => new()
        {
            IsSuccess = true,
            StatusCode = 200,
            Data = new ChatTranslateDTO
            {
                TranslatedBody = t.TranslatedBody,
                TargetLanguage = t.TargetLanguage,
                OriginalLanguage = originalLanguage,
                Provider = t.Provider.ToString(),
                FromCache = fromCache,
            }
        };

    private static ChatTranslateResponse Fail(string message, int statusCode = 200)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
