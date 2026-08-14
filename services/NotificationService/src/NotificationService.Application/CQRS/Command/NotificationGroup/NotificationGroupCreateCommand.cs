using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.NotificationGroup;

/// <summary>
/// Sprint 6.4 NOTI4-02 — tạo một nhóm người nhận mới.
///
/// <para><b>Chỉ tạo được nhóm <c>Static</c> (thành viên tường minh) qua API.</b> Nhóm <c>Role</c>
/// chỉ do seeder sinh ra và đã phủ đủ cả 4 role (<c>Admin</c>/<c>Manager</c>/<c>Staff</c>/
/// <c>Customer</c>), nên không có nhu cầu tạo thêm. Cho phép tạo tay sẽ mở ra một loạt trạng thái
/// vô nghĩa phải xử lý: nhóm trỏ tới role không tồn tại, hai nhóm cùng role, nhóm role do người
/// dùng tạo nhưng lại không phải nhóm hệ thống. Không đáng.</para>
/// </summary>
public class NotificationGroupCreateCommand
    : IRequest<NotificationGroupActionResponse>, IValidatable<NotificationGroupActionResponse>
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Set từ JWT claim, không nhận từ body — dùng cho audit.</summary>
    [JsonIgnore]
    public Guid ActorUserId { get; set; }

    public Task<NotificationGroupActionResponse> ValidateAsync()
    {
        var response = new NotificationGroupActionResponse();
        NotificationGroupRules.ValidateNameAndDescription(response.ListErrors, Name, Description);
        NotificationGroupRules.ValidateActor(response.ListErrors, ActorUserId);

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}

/// <summary>
/// Quy tắc kiểm dữ liệu nhóm, dùng chung cho <c>create</c> và <c>update</c> — hai lệnh nhận cùng bộ
/// trường nên tách ra để không có chỗ nào quên một luật.
/// </summary>
internal static class NotificationGroupRules
{
    /// <summary>Khớp giới hạn cột DB <c>name varchar(128)</c>.</summary>
    public const int MaxNameLength = 128;

    /// <summary>Khớp giới hạn cột DB <c>description varchar(512)</c>.</summary>
    public const int MaxDescriptionLength = 512;

    public static void ValidateNameAndDescription(List<Errors> errors, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            errors.Add(new Errors { Field = "Name", Detail = "Group name is required." });
        else if (name.Trim().Length > MaxNameLength)
            errors.Add(new Errors { Field = "Name", Detail = $"Group name must be at most {MaxNameLength} characters." });

        if (description is not null && description.Trim().Length > MaxDescriptionLength)
            errors.Add(new Errors { Field = "Description", Detail = $"Description must be at most {MaxDescriptionLength} characters." });
    }

    public static void ValidateActor(List<Errors> errors, Guid actorUserId)
    {
        if (actorUserId == Guid.Empty)
        {
            errors.Add(new Errors
            {
                Field = "ActorUserId",
                Detail = "Unable to determine the actor from the token.",
            });
        }
    }

    /// <summary>
    /// Dạng chuẩn hoá dùng cho cột <c>normalized_name</c> mang partial unique index chống trùng tên
    /// không phân biệt hoa-thường. Dùng <c>ToUpperInvariant</c> (không phải <c>ToUpper</c>) để kết
    /// quả không phụ thuộc culture của máy chạy.
    /// </summary>
    public static string Normalize(string name) => name.Trim().ToUpperInvariant();
}
