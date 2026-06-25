using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Entities;
using TicketService.Domain.Enums;

namespace TicketService.Application.Common.Helpers;

public static class ChatReactionAggregateHelper
{
    public static TicketChatReactionsAggregateDTO Build(IEnumerable<TicketChatReaction> reactions)
    {
        var dto = new TicketChatReactionsAggregateDTO();

        foreach (var group in reactions.GroupBy(r => r.ReactionType))
        {
            var target = group.Key switch
            {
                ReactionTypeEnum.ThumbsUp => dto.ThumbsUp,
                ReactionTypeEnum.Acknowledged => dto.Acknowledged,
                ReactionTypeEnum.Resolved => dto.Resolved,
                ReactionTypeEnum.NeedMoreInfo => dto.NeedMoreInfo,
                ReactionTypeEnum.Disagree => dto.Disagree,
                _ => null
            };

            if (target == null)
                continue;

            target.Count = group.Count();
            target.Users = group.Select(r => new ChatReactionUserDTO
            {
                UserId = r.UserId.ToString(),
                Role = r.UserRole
            }).ToList();
        }

        return dto;
    }
}
