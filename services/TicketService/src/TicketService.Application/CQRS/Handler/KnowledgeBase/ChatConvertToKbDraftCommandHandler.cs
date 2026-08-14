using MediatR;
using Microsoft.EntityFrameworkCore;
using SharedContracts.Common.Responses;
using TicketService.Application.CQRS.Command.Chats;
using TicketService.Application.DTOs.Response.KnowledgeBases;
using TicketService.Application.Interfaces.Repositories;
using TicketService.Application.Interfaces.Services;
using TicketService.Application.Interfaces.Utils;
using TicketService.Application.Mapping;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Handler.KnowledgeBase;

public class ChatConvertToKbDraftCommandHandler : IRequestHandler<ChatConvertToKbDraftCommand, CommonResponse<KbArticleActionDTO>>
{
    private readonly ITicketUnitOfWork _uow;
    private readonly IKbCodeGenerator _codeGenerator;
    private readonly ITicketCurrentUserService _currentUser;

    public ChatConvertToKbDraftCommandHandler(
        ITicketUnitOfWork uow,
        IKbCodeGenerator codeGenerator,
        ITicketCurrentUserService currentUser)
    {
        _uow = uow;
        _codeGenerator = codeGenerator;
        _currentUser = currentUser;
    }

    public async Task<CommonResponse<KbArticleActionDTO>> Handle(ChatConvertToKbDraftCommand command, CancellationToken ct)
    {
        var role = _currentUser.Role;
        if (role != "Staff" && role != "Manager" && role != "Admin")
            return Fail(403, "Only Staff, Manager, or Admin can convert a chat into a KB draft.");

        var chat = await _uow.TicketChats.GetAllAsync()
            .Include(c => c.Ticket)
            .FirstOrDefaultAsync(c => c.Id == command.ChatId && c.TicketId == command.TicketId && !c.IsDeleted, ct);
        if (chat == null)
            return Fail(404, "Chat not found.");

        var category = command.Category ?? chat.Ticket.Category;
        var title = string.IsNullOrWhiteSpace(command.Title)
            ? $"[Draft from chat] {chat.Body.Substring(0, Math.Min(80, chat.Body.Length))}..."
            : command.Title;

        var code = await _codeGenerator.GenerateNextCodeAsync(ct);

        var article = new KnowledgeBaseArticle
        {
            Id = Guid.NewGuid(),
            Code = code,
            Category = category,
            Title = title,
            Content = KnowledgeBaseMapper.ToJsonDoc(chat.Body),
            Tags = new List<string>(),
            Status = KbArticleStatusEnum.Draft,
            Version = 0,
            ViewCount = 0,
            HelpfulCount = 0,
            CreatedByUserId = command.CurrentUserId,
            ReviewRequired = true,
            PendingReviewBy = command.CurrentUserId
        };

        var initialVersion = new KbArticleVersion
        {
            Id = Guid.NewGuid(),
            ArticleId = article.Id,
            MajorVersion = 1,
            MinorVersion = 0,
            Status = KbVersionStatusEnum.Pending,
            Title = article.Title,
            Content = article.Content,
            Tags = new List<string>(),
            ChangeDescription = $"Created from chat #{command.ChatId}",
            ChangedBy = command.CurrentUserId
        };

        await _uow.KnowledgeBaseArticles.AddAsync(article);
        await _uow.KbArticleVersions.AddAsync(initialVersion);
        await _uow.SaveChangesAsync(ct);

        return new CommonResponse<KbArticleActionDTO>
        {
            IsSuccess = true,
            StatusCode = 201,
            Message = "KB draft has been created from the chat content.",
            Data = new KbArticleActionDTO
            {
                Id = article.Id.ToString(),
                Code = article.Code,
                Status = article.Status
            }
        };
    }

    private static CommonResponse<KbArticleActionDTO> Fail(int statusCode, string message)
        => new() { IsSuccess = false, StatusCode = statusCode, Message = message };
}
