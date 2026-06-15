using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.Notification;

public class NotificationResponse : CommonResponse<NotificationDto> { }

public class NotificationListResponse : CommonResponse<PaginationResponse<NotificationDto>> { }

public class NotificationActionResponse : CommonResponse<Guid> { }
