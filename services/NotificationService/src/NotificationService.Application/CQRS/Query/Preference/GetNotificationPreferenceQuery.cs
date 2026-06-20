using MediatR;
using NotificationService.Application.DTOs.Response.Preference;

namespace NotificationService.Application.CQRS.Query.Preference;

public class GetNotificationPreferenceQuery : IRequest<NotificationPreferenceResponse>
{
    public Guid UserId { get; set; }
}
