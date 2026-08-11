using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Auth;

/// <summary>
/// Disable 2FA — yêu cầu re-auth bằng <see cref="Password"/> + <see cref="TotpCode"/> để chống
/// session hijack (attacker có cookie/JWT vẫn không disable được vì không biết password) và
/// chống stolen device (attacker có device vẫn không biết password).
/// </summary>
public class Disable2FACommand : IRequest<CommonResponse<string>>, IValidatable<CommonResponse<string>>
{
    /// <summary>Lấy từ JWT — body/query KHÔNG được override (BindNever) + JSON KHÔNG bind (JsonIgnore).</summary>
    [JsonIgnore]
    [BindNever]
    public Guid AccountId { get; set; }

    public string Password { get; set; } = string.Empty;
    public string TotpCode { get; set; } = string.Empty;

    public Task<CommonResponse<string>> ValidateAsync()
    {
        var response = new CommonResponse<string>();

        // AccountId từ JWT — short-circuit với 401 + Message-only.
        if (AccountId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.StatusCode = 401;
            response.Message = "Invalid session.";
            return Task.FromResult(response);
        }

        // Body field validation → ListErrors.
        if (string.IsNullOrWhiteSpace(Password))
            response.ListErrors.Add(new Errors { Field = "Password", Detail = "Password is required." });
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
