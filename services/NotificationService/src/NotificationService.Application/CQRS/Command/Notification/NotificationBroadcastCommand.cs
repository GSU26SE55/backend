using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Application.Templates;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Notification;

/// <summary>
/// Sprint 6.4 NOTI4-07 — gửi một thông báo cho nhiều nhóm và/hoặc nhiều cá nhân trong <b>một</b> lệnh.
///
/// <para>Trước sprint này endpoint gửi tay nhận đúng <b>một</b> <c>Guid UserId</c>; muốn báo cho 20
/// người thì phải bấm 20 lần, và 20 lần đó không có gì nối chúng lại thành một sự kiện.</para>
///
/// <para><b>Cho phép trộn nhóm với cá nhân.</b> "Gửi cho nhóm Quản lý và thêm anh A" là một lần
/// gửi, không phải hai — người vừa ở nhóm vừa được thêm đích danh cũng chỉ nhận một lần.</para>
/// </summary>
public class NotificationBroadcastCommand
    : IRequest<NotificationBroadcastResponse>, IValidatable<NotificationBroadcastResponse>
{
    /// <summary>Khớp giới hạn cột <c>title varchar(200)</c> của cả batch lẫn notification.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>Khớp giới hạn cột <c>body varchar(2000)</c>.</summary>
    public const int MaxBodyLength = 2000;

    /// <summary>
    /// Trần số nhóm/cá nhân nhắm tới trong một lệnh. Chặn payload rác, không phải luật nghiệp vụ.
    /// </summary>
    public const int MaxTargets = 200;

    public NotificationTypeEnum Type { get; set; }

    /// <summary>Các kênh muốn gửi. Trùng lặp được gộp lại.</summary>
    public List<NotificationChannelEnum> Channels { get; set; } = new();

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>JSON object tuỳ chọn — deep link, entity ref. Phải là object hợp lệ nếu có.</summary>
    public string? PayloadJson { get; set; }

    /// <summary>
    /// 03/08/2026 — render nội dung qua <b>mẫu thông báo</b> thay vì dùng thẳng
    /// <see cref="Title"/>/<see cref="Body"/>.
    ///
    /// <para><c>false</c> (mặc định) ⇒ chữ admin gõ là thứ được gửi đi, y nguyên.</para>
    ///
    /// <para><c>true</c> ⇒ dispatcher tra mẫu theo cặp <c>(Loại × Kênh)</c> và render với
    /// <see cref="PayloadJson"/>. Đây là lý do phải qua mẫu chứ không "đổ nội dung vào ô soạn":
    /// <b>mỗi kênh có mẫu riêng</b> — bản SMS được nén ngắn lại vì tính tiền theo đoạn — nên một
    /// lần gửi 3 kênh cho ra 3 nội dung khác nhau, thứ mà một ô nhập duy nhất không làm được.</para>
    ///
    /// <para><see cref="Title"/>/<see cref="Body"/> vẫn bắt buộc và trở thành <b>nội dung dự phòng</b>:
    /// kênh nào không có mẫu khớp, hoặc mẫu hỏng cú pháp, thì rơi về chúng — không chặn việc gửi.</para>
    /// </summary>
    public bool UseTemplate { get; set; }

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    /// <summary>Nhóm nhắm tới. Có thể để rỗng nếu đã chỉ định <see cref="UserIds"/>.</summary>
    public List<Guid> GroupIds { get; set; } = new();

    /// <summary>Cá nhân nhắm tới ngoài các nhóm. Có thể để rỗng nếu đã chỉ định <see cref="GroupIds"/>.</summary>
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationBroadcastResponse> ValidateAsync()
    {
        var response = new NotificationBroadcastResponse();
        var errors = response.ListErrors;

        if (!Enum.IsDefined(typeof(NotificationTypeEnum), Type))
            errors.Add(new Errors { Field = "Type", Detail = "Invalid notification type." });

        if (Channels.Count == 0)
        {
            errors.Add(new Errors { Field = "Channels", Detail = "At least one channel must be selected." });
        }
        else if (Channels.Any(c => !Enum.IsDefined(typeof(NotificationChannelEnum), c)))
        {
            errors.Add(new Errors { Field = "Channels", Detail = "The channel list contains an invalid value." });
        }

        if (string.IsNullOrWhiteSpace(Title))
            errors.Add(new Errors { Field = "Title", Detail = "Title is required." });
        else if (Title.Trim().Length > MaxTitleLength)
            errors.Add(new Errors { Field = "Title", Detail = $"Title must be at most {MaxTitleLength} characters." });

        if (string.IsNullOrWhiteSpace(Body))
            errors.Add(new Errors { Field = "Body", Detail = "Body is required." });
        else if (Body.Trim().Length > MaxBodyLength)
            errors.Add(new Errors { Field = "Body", Detail = $"Body must be at most {MaxBodyLength} characters." });

        if (GroupIds.Count == 0 && UserIds.Count == 0)
        {
            errors.Add(new Errors
            {
                Field = "GroupIds",
                Detail = "At least one group or recipient must be selected.",
            });
        }
        else if (GroupIds.Count + UserIds.Count > MaxTargets)
        {
            errors.Add(new Errors
            {
                Field = "GroupIds",
                Detail = $"A maximum of {MaxTargets} groups and individuals per send.",
            });
        }

        if (GroupIds.Any(id => id == Guid.Empty) || UserIds.Any(id => id == Guid.Empty))
            errors.Add(new Errors { Field = "GroupIds", Detail = "The list contains an empty id." });

        // Cột payload_json là jsonb — chuỗi không phải JSON hợp lệ sẽ làm vỡ INSERT ở tận tầng DB,
        // lúc đó lỗi trả về là 500 khó hiểu thay vì 400 chỉ đúng ô nhập.
        if (!string.IsNullOrWhiteSpace(PayloadJson))
        {
            try
            {
                using var parsed = JsonDocument.Parse(PayloadJson);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add(new Errors
                    {
                        Field = "PayloadJson",
                        Detail = "Payload must be a JSON object.",
                    });
                }
            }
            catch (JsonException)
            {
                errors.Add(new Errors { Field = "PayloadJson", Detail = "Payload is not valid JSON." });
            }
        }

        // Chọn dùng mẫu mà khai biến lạ thì Handlebars render ra RỖNG chứ không báo lỗi — đúng cái
        // bẫy mà cả Sprint 6.5 sinh ra để chặn. Chặn ngay ở đây, kèm danh sách biến hợp lệ.
        if (UseTemplate && !string.IsNullOrWhiteSpace(PayloadJson))
        {
            var allowed = NotificationTemplateVariables.AllowedFor(Type);
            try
            {
                using var parsed = JsonDocument.Parse(PayloadJson);
                if (parsed.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var unknown = parsed.RootElement.EnumerateObject()
                        .Select(p => p.Name)
                        .Where(name => !allowed.Contains(name))
                        .ToList();

                    if (unknown.Count > 0)
                    {
                        errors.Add(new Errors
                        {
                            Field = "PayloadJson",
                            Detail = $"These variables are not usable for this notification type: {string.Join(", ", unknown)}. "
                                   + $"Allowed variables: {string.Join(", ", allowed.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))}.",
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // Lỗi cú pháp JSON đã được báo ở khối trên, không nhân đôi thông báo.
            }
        }

        if (ActorUserId == Guid.Empty)
        {
            errors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Unable to determine the actor from the token.",
            });
        }

        if (errors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
