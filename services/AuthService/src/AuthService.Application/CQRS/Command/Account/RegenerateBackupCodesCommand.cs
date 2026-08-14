using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Auth;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

/// <summary>
/// Sinh lại 8 backup codes — yêu cầu TOTP để chứng minh user vẫn giữ device. Xóa codes cũ trước, return codes mới plain 1 lần.
/// </summary>
public class RegenerateBackupCodesCommand : IRequest<CommonResponse<BackupCodesDto>>, IValidatable<CommonResponse<BackupCodesDto>>
{
    /// <summary>Lấy từ JWT — body/query KHÔNG được override (BindNever) + JSON KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    public string TotpCode { get; set; } = string.Empty;

    public Task<CommonResponse<BackupCodesDto>> ValidateAsync()
    {
        var response = new CommonResponse<BackupCodesDto>();

        // AccountId từ JWT — short-circuit với 401 + Message-only.
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 401;
            response.Message = "Invalid session.";
            return Task.FromResult(response);
        }

        // Body field validation → ListErrors.
        if (string.IsNullOrWhiteSpace(TotpCode))
            response.ListErrors.Add(new Errors { Field = "TotpCode", Detail = "TotpCode is required." });
        else if (TotpCode.Length != 6 || !TotpCode.All(char.IsDigit))
            response.ListErrors.Add(new Errors { Field = "TotpCode", Detail = "TotpCode must be 6 digits." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid data.";
        }
        return Task.FromResult(response);
    }
}
