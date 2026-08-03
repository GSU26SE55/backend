using MediatR;
using NotificationService.Application.DTOs.Response.Notification;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;

namespace NotificationService.Application.CQRS.Query.NotificationTemplate;

/// <summary>Chi tiết một template theo Id (kể cả bản không active — để xem lại phiên bản cũ).</summary>
public class NotificationTemplateGetByIdQuery
    : IRequest<NotificationTemplateResponse>, IValidatable<NotificationTemplateResponse>
{
    public Guid Id { get; set; }

    public Task<NotificationTemplateResponse> ValidateAsync()
    {
        var response = new NotificationTemplateResponse();

        if (Id == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = "Id", Detail = "Id template không hợp lệ." });

        if (response.ListErrors.Count > 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.Message = "Dữ liệu đầu vào không hợp lệ.";
        }

        return Task.FromResult(response);
    }
}
