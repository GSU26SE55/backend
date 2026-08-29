using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;

namespace TicketService.Application.CQRS.Command.Tickets;

public class TicketRateCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    /// <summary>
    /// ID của Ticket liên quan.
    /// </summary>
    [JsonIgnore]
    public Guid TicketId { get; set; }

    /// <summary>
    /// Rating.
    /// </summary>
    public short Rating { get; set; }
    public string? RatingComment { get; set; }

    /// <summary>
    /// Customer id.
    /// </summary>
    [JsonIgnore]
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Customer name.
    /// </summary>
    [JsonIgnore]
    public string? CustomerName { get; set; }

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (TicketId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "TicketId", Detail = "Invalid TicketId." });

        if (Rating < 1 || Rating > 5)
            response.ListErrors.Add(new Errors { Field = "Rating", Detail = "Rating must be between 1 and 5 stars." });

        // Cột rating_comment là text không giới hạn, nên không có cap thì nhận được cả bài
        // văn qua API. Mobile đã chặn ở 500 — khớp lại để hai đường vào cùng luật.
        if (!string.IsNullOrWhiteSpace(RatingComment) && RatingComment.Trim().Length > 500)
            response.ListErrors.Add(new Errors { Field = "RatingComment", Detail = "Rating comment must be at most 500 characters." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Invalid input data.";
        }

        return Task.FromResult(response);
    }
}
