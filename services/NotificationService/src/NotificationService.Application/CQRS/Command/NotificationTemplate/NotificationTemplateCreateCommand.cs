using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationTemplate;

/// <summary>
/// Tạo template ĐẦU TIÊN cho một cặp (Type × Channel) chưa có template nào.
///
/// <para>Cặp đã có template rồi thì phải dùng <c>revise</c> — sửa nội dung là sinh phiên bản mới,
/// không ghi đè bản cũ, để còn quay lui được khi bản mới sai chính tả đã gửi cho hàng trăm khách.</para>
/// </summary>
public class NotificationTemplateCreateCommand
    : IRequest<NotificationTemplateActionResponse>, IValidatable<NotificationTemplateActionResponse>
{
    public NotificationTypeEnum Type { get; set; }

    public NotificationChannelEnum Channel { get; set; }

    /// <summary>Template tiêu đề (cú pháp Handlebars <c>{{var}}</c>).</summary>
    public string TitleTemplate { get; set; } = string.Empty;

    /// <summary>Template nội dung.</summary>
    public string BodyTemplate { get; set; } = string.Empty;

    /// <summary>Set từ JWT claim, không nhận từ body — dùng cho audit.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationTemplateActionResponse> ValidateAsync()
    {
        var response = new NotificationTemplateActionResponse();
        NotificationTemplateContentRules.Validate(
            response.ListErrors, Type, Channel, TitleTemplate, BodyTemplate);

        if (ActorUserId == Guid.Empty)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Không xác định được người thực hiện từ token.",
            });
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

/// <summary>
/// Quy tắc kiểm nội dung template, dùng chung cho <c>create</c> và <c>revise</c> — hai lệnh này nhận
/// cùng một bộ trường nội dung nên tách ra để không có chỗ nào quên một luật.
/// </summary>
internal static class NotificationTemplateContentRules
{
    /// <summary>Khớp giới hạn cột DB: <c>title_template</c> 500, <c>body_template</c> 4000.</summary>
    public const int MaxTitleLength = 500;
    public const int MaxBodyLength = 4000;

    public static void Validate(
        List<Errors> errors,
        NotificationTypeEnum? type,
        NotificationChannelEnum? channel,
        string titleTemplate,
        string bodyTemplate)
    {
        if (type.HasValue && !Enum.IsDefined(typeof(NotificationTypeEnum), type.Value))
            errors.Add(new Errors { Field = "Type", Detail = "Type không hợp lệ." });

        if (channel.HasValue && !Enum.IsDefined(typeof(NotificationChannelEnum), channel.Value))
            errors.Add(new Errors { Field = "Channel", Detail = "Channel không hợp lệ." });

        if (string.IsNullOrWhiteSpace(titleTemplate))
            errors.Add(new Errors { Field = "TitleTemplate", Detail = "Tiêu đề không được trống." });
        else if (titleTemplate.Length > MaxTitleLength)
            errors.Add(new Errors { Field = "TitleTemplate", Detail = $"Tiêu đề tối đa {MaxTitleLength} ký tự." });

        if (string.IsNullOrWhiteSpace(bodyTemplate))
            errors.Add(new Errors { Field = "BodyTemplate", Detail = "Nội dung không được trống." });
        else if (bodyTemplate.Length > MaxBodyLength)
            errors.Add(new Errors { Field = "BodyTemplate", Detail = $"Nội dung tối đa {MaxBodyLength} ký tự." });
    }
}
