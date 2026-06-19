using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Application.Validation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Admin;

/// <summary>
/// #AUTH-47: Admin merge 2 account thuộc cùng 1 người thật (vd Alice có local account + Google OAuth account).
///
/// Side effects:
/// - <see cref="SecondaryAccountId"/> bị soft-delete + set MergedIntoId = PrimaryAccountId.
/// - Tất cả RefreshToken active của secondary → revoked (force logout).
/// - GoogleId của secondary (nếu có) → transfer sang primary (nếu primary chưa có).
/// - AccountProfile/StaffProfile của secondary → migrate sang primary (nếu primary chưa có).
/// - AuditLog của secondary giữ nguyên (audit history immutable per AUTH-29), KHÔNG move.
/// - Insert AccountMergeLog dedicated row (audit + rollback snapshot).
///
/// Quy tắc safety:
/// - PrimaryAccountId != SecondaryAccountId.
/// - Cả 2 phải tồn tại + không IsDeleted + chưa từng được merge (MergedIntoId == null).
/// - Reason bắt buộc, max 1000 chars.
/// - Chỉ Admin role được call (controller [Authorize(Policy="AdminOnly")]).
/// </summary>
public class MergeAccountCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    /// <summary>Primary account id — resolved từ URL path, KHÔNG bind từ body.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid PrimaryAccountId { get; set; }

    /// <summary>Secondary account id — bind từ request body (user input).</summary>
    public Guid SecondaryAccountId { get; set; }

    /// <summary>Reason — bind từ request body (user input bắt buộc).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Admin thực hiện merge — resolved từ JWT, KHÔNG bind từ user input.</summary>
    [JsonIgnore]
    [BindNever]
    public Guid PerformedBy { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();
        if (PrimaryAccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "PrimaryAccountId", Detail = "PrimaryAccountId là bắt buộc." });
        if (SecondaryAccountId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "SecondaryAccountId", Detail = "SecondaryAccountId là bắt buộc." });
        if (PrimaryAccountId == SecondaryAccountId)
            response.ListErrors.Add(new Errors { Field = "SecondaryAccountId", Detail = "Không thể merge account vào chính nó." });
        if (string.IsNullOrWhiteSpace(Reason))
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Reason là bắt buộc — bắt buộc audit." });
        else if (Reason.Length > 1000)
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Reason tối đa 1000 ký tự." });
        if (PerformedBy == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "PerformedBy", Detail = "Phải có admin actor." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu không hợp lệ.";
        }
        return Task.FromResult(response);
    }
}
