using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.Common.Models;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatAdd;

public class ChatAddCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    // Heuristic — chỉ cover whitespace + emoji range phổ biến (BMP symbol/dingbat + surrogate pair khối emoji),
    // không exhaustive toàn bộ Unicode emoji (#518 — Simplicity First).
    private static readonly Regex WhitespaceOrEmojiOnlyRegex = new(
        "^[\\s\\u2600-\\u27BF\\u2190-\\u21FF\\u2B00-\\u2BFF\\uD83C-\\uDBFF\\uDC00-\\uDFFF\\uFE0F\\u200D]*$",
        RegexOptions.Compiled);

    [JsonIgnore]
    public Guid TicketId { get; set; }
    [JsonIgnore]
    public Guid UserId { get; set; }
    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;
    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    public required string Body { get; set; }
    public bool IsInternal { get; set; }
    public bool RequestCustomerInfo { get; set; }
    public ChatBodyFormatEnum BodyFormat { get; set; } = ChatBodyFormatEnum.PlainText;
    public List<ChatAttachmentInput>? Attachments { get; set; }
    public List<ChatMentionInput>? Mentions { get; set; }
    public List<GroupMentionInput>? GroupMentions { get; set; }

    private static readonly HashSet<string> ValidRoleIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        { "manager", "staff", "admin", "customer" };
    private static readonly HashSet<string> ValidTeamIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        { "tier1-staff", "tier2-staff", "tier3-staff" };

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (string.IsNullOrWhiteSpace(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung bình luận không được để trống." });
        // ValidateAsync() không nhận DI nên không inject được IOptions<ChatOptions> tại đây —
        // dùng hằng số ChatOptions.MaxBodyLengthDefault làm nguồn duy nhất, tránh lặp số tay.
        // Nếu appsettings.json "Chat:MaxBodyLength" override khác giá trị này, validate ở đây
        // KHÔNG phản ánh giá trị override — chỉ chặn theo default.
        else if (Body.Length > ChatOptions.MaxBodyLengthDefault)
            response.ListErrors.Add(new Errors { Field = "Body", Detail = $"Nội dung bình luận tối đa {ChatOptions.MaxBodyLengthDefault} ký tự." });
        else if (WhitespaceOrEmojiOnlyRegex.IsMatch(Body))
            response.ListErrors.Add(new Errors { Field = "Body", Detail = "Nội dung không được chỉ chứa khoảng trắng hoặc emoji." });

        if (Attachments != null && Attachments.Any())
        {
            for (int i = 0; i < Attachments.Count; i++)
            {
                var att = Attachments[i];
                if (att.FileId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileId", Detail = "FileId không được để trống." });
                if (string.IsNullOrWhiteSpace(att.FileName))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].FileName", Detail = "FileName không được để trống." });
                if (string.IsNullOrWhiteSpace(att.ContentType))
                    response.ListErrors.Add(new Errors { Field = $"Attachments[{i}].ContentType", Detail = "ContentType không được để trống." });
            }
        }

        if (Mentions != null && Mentions.Any())
        {
            for (int i = 0; i < Mentions.Count; i++)
            {
                var mention = Mentions[i];
                if (mention.UserId == Guid.Empty)
                    response.ListErrors.Add(new Errors { Field = $"Mentions[{i}].UserId", Detail = "UserId không được để trống." });
                if (string.IsNullOrWhiteSpace(mention.DisplayName))
                    response.ListErrors.Add(new Errors { Field = $"Mentions[{i}].DisplayName", Detail = "DisplayName không được để trống." });
            }
        }

        if (GroupMentions != null && GroupMentions.Any())
        {
            for (int i = 0; i < GroupMentions.Count; i++)
            {
                var gm = GroupMentions[i];
                if (gm.GroupType != "role" && gm.GroupType != "team")
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupType", Detail = "GroupType phải là 'role' hoặc 'team'." });
                else if (gm.GroupType == "role" && !ValidRoleIdentifiers.Contains(gm.GroupIdentifier))
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupIdentifier", Detail = "GroupIdentifier cho role phải là 'manager', 'staff', 'admin', hoặc 'customer'." });
                else if (gm.GroupType == "team" && !ValidTeamIdentifiers.Contains(gm.GroupIdentifier))
                    response.ListErrors.Add(new Errors { Field = $"GroupMentions[{i}].GroupIdentifier", Detail = "GroupIdentifier cho team phải là 'tier1-staff', 'tier2-staff', hoặc 'tier3-staff'." });
            }
        }

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}

public record ChatAttachmentInput(
    Guid FileId,
    string FileName,
    string ContentType,
    long SizeBytes
);

public record ChatMentionInput(
    Guid UserId,
    string DisplayName
);

public record GroupMentionInput(
    string GroupType,       // "role" | "team"
    string GroupIdentifier  // role: "manager"|"staff"|"admin"|"customer"  team: "tier1-staff"|"tier2-staff"|"tier3-staff"
);
