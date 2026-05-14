using System.Text.Json.Serialization;
using AuthService.Application.DTOs.Response.Account;
using AuthService.Domain.Enums;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace AuthService.Application.CQRS.Command.Account;

public class ChangeAccountStatusCommand : IRequest<AccountActionResponse>, IValidatable<AccountActionResponse>
{
    [JsonIgnore]
    public Guid Id { get; set; }
    public AccountStatusEnum Status { get; set; }
    public string? Reason { get; set; }

    public Task<AccountActionResponse> ValidateAsync()
    {
        var response = new AccountActionResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id account không hợp lệ." });

        if (!Enum.IsDefined(typeof(AccountStatusEnum), Status))
            response.ListErrors.Add(new Errors { Field = "Status", Detail = "Trạng thái không hợp lệ." });

        if (!string.IsNullOrEmpty(Reason) && Reason.Length > 500)
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Lý do tối đa 500 ký tự." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
