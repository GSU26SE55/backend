using System.Text.Json.Serialization;
using MediatR;
using NotificationService.Application.DTOs.Response.Preference;
using NotificationService.Domain.Enums;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Command.Preference;

/// <summary>Sprint 6.3 NOTI3-04 (#704) — một dòng cần cập nhật trong ma trận.</summary>
public class CategoryPreferenceItem
{
    public NotificationCategoryEnum Category { get; set; }
    public bool PushEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public bool SmsEnabled { get; set; }
    public bool InAppEnabled { get; set; }
}

/// <summary>
/// Sprint 6.3 NOTI3-04 (#704) — cập nhật ma trận nhóm × kênh.
///
/// Ghi theo kiểu **vá từng dòng**: chỉ những nhóm có trong <see cref="Items"/> mới bị đổi, các nhóm
/// còn lại giữ nguyên. Ghi đè toàn bộ sẽ khiến hai tab mở song song xoá lẫn thiết lập của nhau.
/// </summary>
public class UpdateNotificationCategoryPreferenceCommand
    : IRequest<NotificationPreferenceMatrixResponse>, IValidatable<NotificationPreferenceMatrixResponse>
{
    /// <summary>Set từ JWT claim, không nhận từ body.</summary>
    [JsonIgnore]
    public Guid UserId { get; set; }

    public List<CategoryPreferenceItem> Items { get; set; } = new();

    public Task<NotificationPreferenceMatrixResponse> ValidateAsync()
    {
        var response = new NotificationPreferenceMatrixResponse();

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "Unable to determine the user." });

        if (Items.Count == 0)
            response.ListErrors.Add(new Errors { Field = "Items", Detail = "At least one category must be provided to update." });

        foreach (var item in Items)
        {
            if (!Enum.IsDefined(typeof(NotificationCategoryEnum), item.Category))
            {
                response.ListErrors.Add(new Errors
                {
                    Field = "Items.Category",
                    Detail = $"Category '{(int)item.Category}' is invalid.",
                });
            }
        }

        // Cùng một nhóm gửi hai lần thì không biết dòng nào thắng — từ chối thay vì đoán.
        var duplicated = Items.GroupBy(i => i.Category).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        foreach (var category in duplicated)
        {
            response.ListErrors.Add(new Errors
            {
                Field = "Items.Category",
                Detail = $"Category '{category}' appears more than once in the same request.",
            });
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
