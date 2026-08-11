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
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Invalid account Id." });

        if (!Enum.IsDefined(typeof(AccountStatusEnum), Status))
            response.ListErrors.Add(new Errors { Field = "Status", Detail = "Invalid status." });

        if (!string.IsNullOrEmpty(Reason) && Reason.Length > 500)
            response.ListErrors.Add(new Errors { Field = "Reason", Detail = "Reason must not exceed 500 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
