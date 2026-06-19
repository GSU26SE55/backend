using SharedContracts.Common.Responses;

namespace NotificationService.Application.DTOs.Response.DeviceToken;

/// <summary>Response cho register/unregister — Data = Id của device token.</summary>
public class DeviceTokenActionResponse : CommonResponse<Guid> { }

/// <summary>Response cho list my devices.</summary>
public class DeviceTokenListResponse : CommonResponse<List<DeviceTokenDto>> { }
