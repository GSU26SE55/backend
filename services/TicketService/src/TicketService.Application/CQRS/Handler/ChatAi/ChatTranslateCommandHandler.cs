using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TicketService.Application.CQRS.Command.ChatAi;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Domain.Entities;

namespace TicketService.Application.CQRS.Handler.ChatAi;

public class ChatTranslateCommandHandler : IRequestHandler<ChatTranslateCommand, ChatTranslateResponse>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IChatTextAiClient _aiClient;
    private readonly ILogger<ChatTranslateCommandHandler> _logger;

    public ChatTranslateCommandHandler(
        ITicketUnitOfWork uow,
        IChatTextAiClient aiClient,
        ILogger<ChatTranslateCommandHandler> logger)
    {
        _uow = uow;
        _aiClient = aiClient;
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

        // 1. DB check — tìm bản dịch đã có cho ngôn ngữ này
        var existing = await _uow.TicketChatTranslations
            .GetAllAsync()
            .Where(t => !t.IsDeleted && t.ChatId == request.ChatId && t.TargetLanguage == targetLang)
            .FirstOrDefaultAsync(ct);

        if (existing != null)
        {
            // Tái sử dụng bản dịch — thêm user link nếu chưa có
            var alreadyLinked = await _uow.ChatTranslationUsers
                .GetAllAsync()
                .AnyAsync(u => !u.IsDeleted && u.TranslationId == existing.Id && u.UserId == request.CurrentUserId, ct);

            if (!alreadyLinked)
            {
                await _uow.BeginTransactionAsync();
                try
                {
                    await _uow.ChatTranslationUsers.AddAsync(new TicketChatTranslationUser
                    {
                        Id = Guid.NewGuid(),
                        TranslationId = existing.Id,
                        UserId = request.CurrentUserId,
                    });
                    await _uow.CommitTransactionAsync();
                }
                catch (Exception ex)
                {
                    await _uow.RollbackTransactionAsync();
                    _logger.LogWarning(ex, "[ChatTranslate] Failed to add user link for translation {TranslationId}", existing.Id);
                    // Non-fatal — vẫn trả về bản dịch
                }
            }

            return new ChatTranslateResponse { IsSuccess = true, StatusCode = 200, Data = BuildDto(existing, chat.OriginalLanguage) };
        }

        // 2. Gọi AI dịch
        string translatedBody;
        try
        {
            translatedBody = await _aiClient.TranslateAsync(chat.Body, targetLang, ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "RATE_LIMITED")
        {
            _logger.LogWarning("[ChatTranslate] AI rate limit hit for chat {ChatId}", request.ChatId);
            return Fail("AI service đang bận, vui lòng thử lại sau ít giây.", 429);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ChatTranslate] Translation AI unavailable for chat {ChatId}", request.ChatId);
            return Fail("Translation service unavailable.");
        }

        // 3. Lưu bản dịch mới + user link trong 1 transaction
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
        catch (Exception ex)
        {
            await _uow.RollbackTransactionAsync();
            _logger.LogError(ex, "[ChatTranslate] Failed to persist translation for chat {ChatId}", request.ChatId);
            return Fail("Failed to save translation.");
        }

        return new ChatTranslateResponse { IsSuccess = true, StatusCode = 200, Data = BuildDto(translation, chat.OriginalLanguage) };
    }

    private static ChatTranslateDTO BuildDto(TicketChatTranslation t, string? originalLanguage)
        => new()
        {
            TranslatedBody = t.TranslatedBody,
            TargetLanguage = t.TargetLanguage,
            OriginalLanguage = originalLanguage,
            Provider = t.Provider.ToString(),
            FromCache = false,
        };

    private static ChatTranslateResponse Fail(string message, int statusCode = 200)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
