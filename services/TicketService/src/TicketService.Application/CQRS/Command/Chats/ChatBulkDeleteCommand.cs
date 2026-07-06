using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Chats;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public class ChatBulkDeleteCommand : IRequest<ChatBulkDeleteResponse>, IValidatable<ChatBulkDeleteResponse>
{
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

    public List<Guid> ChatIds { get; set; } = new();

    private const int MaxBatchSize = 50;

    public Task<ChatBulkDeleteResponse> ValidateAsync()
    {
        var response = new ChatBulkDeleteResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "TicketId không hợp lệ." });

        if (UserId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "UserId", Detail = "UserId không hợp lệ." });

        if (ChatIds == null || ChatIds.Count == 0)
            response.ListErrors.Add(new Errors { Field = "ChatIds", Detail = "Danh sách ChatIds không được rỗng." });
        else if (ChatIds.Count > MaxBatchSize)
            response.ListErrors.Add(new Errors { Field = "ChatIds", Detail = $"Tối đa {MaxBatchSize} chat mỗi lần xóa." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
