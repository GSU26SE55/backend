using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Helpers;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatAddCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    /// <summary>
    /// Vai trò của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;
    /// <summary>
    /// Danh sách quyền hạn của người thực hiện.
    /// </summary>
    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    /// <summary>
    /// Nội dung chi tiết.
    /// </summary>
    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public bool RequestCustomerInfo { get; set; }
    /// <summary>
    /// Body format.
    /// </summary>
    public ChatBodyFormatEnum BodyFormat { get; set; } = ChatBodyFormatEnum.PlainText;
    public List<ChatAttachmentInput>? Attachments { get; set; }
    public List<ChatMentionInput>? Mentions { get; set; }
    /// <summary>
    /// Danh sách nhóm hoặc bộ phận được nhắc tên.
    /// </summary>
    public List<GroupMentionInput>? GroupMentions { get; set; }

    private static readonly HashSet<string> ValidRoleIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        { "manager", "staff", "admin", "customer" };
    private static readonly HashSet<string> ValidTeamIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        { "tier1-staff", "tier2-staff", "tier3-staff" };

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Invalid UserId." });

        ChatBodyPolicy.AddBodyErrors(response.ListErrors, Body);

        if (Attachments != null && Attachments.Any())
        {
            for (int i = 0; i < Attachments.Count; i++)
            {
                var att = Attachments[i];
                if (att.FileId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileId", Detail = "FileId is required." });
                if (string.IsNullOrWhiteSpace(att.FileName))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileName", Detail = "FileName is required." });
                if (string.IsNullOrWhiteSpace(att.ContentType))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].ContentType", Detail = "ContentType is required." });
            }
        }

        if (Mentions != null && Mentions.Any())
        {
            for (int i = 0; i < Mentions.Count; i++)
            {
                var mention = Mentions[i];
                if (mention.UserId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Mentions[{i}].UserId", Detail = "UserId is required." });
                if (string.IsNullOrWhiteSpace(mention.DisplayName))
                    response.ListErrors.Add(new Errors { Field = $"Mentions[{i}].DisplayName", Detail = "DisplayName is required." });
            }
        }

        if (GroupMentions != null && GroupMentions.Any())
        {
            for (int i = 0; i < GroupMentions.Count; i++)
            {
                var gm = GroupMentions[i];
                if (gm.GroupType != "role" && gm.GroupType != "team")
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupType", Detail = "GroupType must be 'role' or 'team'." });
                else if (gm.GroupType == "role" && !ValidRoleIdentifiers.Contains(gm.GroupIdentifier))
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupIdentifier", Detail = "GroupIdentifier for role must be 'manager', 'staff', 'admin', or 'customer'." });
                else if (gm.GroupType == "team" && !ValidTeamIdentifiers.Contains(gm.GroupIdentifier))
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupIdentifier", Detail = "GroupIdentifier for team must be 'tier1-staff', 'tier2-staff', or 'tier3-staff'." });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}

public record ChatAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Url = null
);

public record ChatMentionInput(
    Guid UserId,
    string DisplayName
);

public record GroupMentionInput(
    string GroupType,       // "role" | "team"
    string GroupIdentifier  // role: "manager"|"staff"|"admin"|"customer"  team: "tier1-staff"|"tier2-staff"|"tier3-staff"
);
