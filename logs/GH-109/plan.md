# Plan — GH-109: NotificationDispatcher + preference + quiet hours + Critical bypass

## Metadata
- **Status:** SHIPPED | **Role:** BE | **Ngày:** 2026-06-19
- **Issue:** #109 — https://github.com/GSU26SE55/backend/issues/109
- **Sprint:** Sprint 6 (deadline 2026-08-08)

## Mục tiêu
Implement `NotificationDispatcher` — lớp orchestration trung tâm của NotificationService:
1. Route notification đến đúng channel dựa trên `NotificationType → Channel matrix`
2. Áp dụng user preference (`PushEnabled`, `EmailEnabled`, `SmsEnabled`, quiet hours)
3. Bypass quiet hours cho notification Critical (smoke/water/battery critical)
4. CQRS + Controller cho GET/PUT `/api/v1/notification-preferences`

> **Prerequisite:** Local dev branch cần merge origin/dev trước khi implement (NotificationService hiện chỉ có trên origin/dev, không có trong working directory local).

## Scope
**Trong scope:**
- `INotificationDispatcher` interface + `NotificationDispatcher` implementation
- `DispatchRequest` input model
- Preference CQRS: `GetNotificationPreferenceQuery`, `UpdateNotificationPreferenceCommand` + handlers
- `PreferencesController` (GET/PUT `/api/v1/notification-preferences`)
- DTOs cho preference: `NotificationPreferenceDto`, `NotificationPreferenceResponse`
- Redis cache 5min cho preference (key `notif_pref:{userId}`)
- Cache invalidation khi PUT preference
- DI registration cho `INotificationDispatcher`
- Unit tests cho dispatcher (quiet hours, critical bypass, channel routing, cache)

**Ngoài scope:**
- `IUserResolver` / `UserResolver` (fetch email/phone từ AuthService) — defer Sprint 7
- Template rendering (HBS) — issue #111
- 15 consumers (#107) — issue riêng
- Notification batching / snooze (§49.2, §49.3) — Sprint 6+ backlog
- SSE real-time — Sprint 6 scope riêng

## Files

| File | Action | Ghi chú |
|------|--------|---------|
| `services/NotificationService/src/NotificationService.Application/Services/INotificationDispatcher.cs` | create | Interface + `DispatchRequest` model |
| `services/NotificationService/src/NotificationService.Application/Services/NotificationDispatcher.cs` | create | Core logic: matrix + preference + quiet hours |
| `services/NotificationService/src/NotificationService.Api/Controllers/PreferencesController.cs` | create | GET/PUT `/api/v1/notification-preferences` |
| `services/NotificationService/src/NotificationService.Application/CQRS/Query/Preference/GetNotificationPreferenceQuery.cs` | create | Query by current userId |
| `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Preference/GetNotificationPreferenceQueryHandler.cs` | create | Load từ DB, tạo default nếu chưa có |
| `services/NotificationService/src/NotificationService.Application/CQRS/Command/Preference/UpdateNotificationPreferenceCommand.cs` | create | Validation included |
| `services/NotificationService/src/NotificationService.Application/CQRS/Handler/Preference/UpdateNotificationPreferenceCommandHandler.cs` | create | Upsert + invalidate cache |
| `services/NotificationService/src/NotificationService.Application/DTOs/Response/Preference/NotificationPreferenceDto.cs` | create | — |
| `services/NotificationService/src/NotificationService.Application/DTOs/Response/Preference/NotificationPreferenceResponse.cs` | create | `CommonResponse<NotificationPreferenceDto>` |
| `services/NotificationService/tests/NotificationService.UnitTests/Services/NotificationDispatcherTests.cs` | create | Unit tests cho dispatcher |
| `services/NotificationService/src/NotificationService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs` | modify | Register `INotificationDispatcher, NotificationDispatcher` |

## Approach

### 1. `INotificationDispatcher` interface

```csharp
public interface INotificationDispatcher
{
    Task DispatchAsync(DispatchRequest request, CancellationToken ct = default);
}

public class DispatchRequest
{
    public NotificationTypeEnum Type { get; set; }
    public List<RecipientInfo> Recipients { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public bool BypassQuietHours { get; set; }
    public Guid? EntityId { get; set; }
    public string? EntityType { get; set; }
}

public class RecipientInfo
{
    public Guid UserId { get; set; }
    public string? Email { get; set; }       // optional — null → skip email channel
    public string? PhoneNumber { get; set; } // optional — null → skip sms channel
}
```

### 2. `NotificationDispatcher` logic

```
DispatchAsync(request):
  channels = TypeChannelMatrix[request.Type]   // lookup dictionary
  for each recipient in request.Recipients:
    pref = LoadPreference(recipient.UserId)    // ICacheService, key "notif_pref:{userId}", TTL 5min
    isQuiet = IsQuietHours(pref)               // TimeZoneInfo.FindSystemTimeZoneById(pref.TimeZone)
    bypass = request.BypassQuietHours || IsCriticalType(request.Type)

    for each channel in channels:
      if !IsChannelEnabled(pref, channel) → skip
      if isQuiet && !bypass && channel != InApp → skip   // defer to InApp only during quiet hours

      notification = new Notification { UserId, Type, Channel, Status=Pending, Title, Body, ... }
      await _unitOfWork.Notifications.AddAsync(notification)
      await _unitOfWork.SaveChangesAsync()

      sendReq = BuildSendRequest(notification, recipient, channel)
      result = await channel.SendAsync(sendReq, ct)

      notification.Status = result.Success ? Sent : Failed
      notification.SentAt = result.Success ? UtcNow : null
      notification.FailureReason = result.ErrorMessage
      _unitOfWork.Notifications.UpdateAsync(notification)
      await _unitOfWork.SaveChangesAsync()
```

### 3. Type → Channel matrix (static dictionary)

```csharp
private static readonly Dictionary<NotificationTypeEnum, NotificationChannelEnum[]> TypeChannelMatrix = new()
{
    [NotificationTypeEnum.TicketCreated]               = [InApp, Push],
    [NotificationTypeEnum.TicketAssigned]              = [InApp, Push, Email],
    [NotificationTypeEnum.TicketStatusChanged]         = [InApp, Push],
    [NotificationTypeEnum.TicketResolved]              = [InApp, Push, Email],
    [NotificationTypeEnum.TicketClosed]                = [InApp, Push],
    [NotificationTypeEnum.TicketEscalated]             = [InApp, Push],
    [NotificationTypeEnum.SlaWarning]                  = [InApp, Push],
    [NotificationTypeEnum.SlaBreached]                 = [InApp, Push, Email, Sms],
    [NotificationTypeEnum.BatteryAnomalyDetected]      = [InApp, Push, Email],
    [NotificationTypeEnum.EnvironmentalIncidentDetected] = [InApp, Push, Email, Sms],
    [NotificationTypeEnum.EnvironmentalIncidentResolved] = [InApp, Push],
    [NotificationTypeEnum.IncidentDeclared]            = [InApp, Push, Email, Sms],
    [NotificationTypeEnum.AccountActivated]            = [InApp, Email],
    [NotificationTypeEnum.AdminInvite]                 = [Email],
    [NotificationTypeEnum.BatteryAlertEscalationPending] = [InApp, Push],
    [NotificationTypeEnum.AlertTicketSagaFailed]       = [InApp, Push],
    [NotificationTypeEnum.IotDeviceWentOffline]        = [InApp, Push],
    [NotificationTypeEnum.System]                      = [InApp],
};
```

### 4. Critical types (bypass quiet hours tự động)

```csharp
private static readonly HashSet<NotificationTypeEnum> CriticalTypes =
[
    NotificationTypeEnum.EnvironmentalIncidentDetected,
    NotificationTypeEnum.IncidentDeclared,
    NotificationTypeEnum.BatteryAlertEscalationPending,
    NotificationTypeEnum.AlertTicketSagaFailed,
    NotificationTypeEnum.SlaBreached,
];
```

### 5. Quiet hours check

```csharp
// pref.QuietHoursStart == null → không có quiet hours
// Dùng TimeZoneInfo.FindSystemTimeZoneById(pref.TimeZone) + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz)
// Nếu start > end → wraps midnight (e.g. 22:00-07:00)
```

### 6. Push channel: load device tokens

```csharp
// Mỗi recipient có thể có nhiều device tokens
var tokens = _unitOfWork.DeviceTokens.GetAllAsync()
    .Where(dt => dt.UserId == recipient.UserId && !dt.IsDeleted)
    .ToListAsync();
// Tạo 1 Notification record per token → gọi ExpoPushChannel per token
```

### 7. Preference endpoints

```
GET /api/v1/notification-preferences   → load của current user (UserId từ JWT)
PUT /api/v1/notification-preferences   → upsert, invalidate cache
```

`GetNotificationPreferenceQueryHandler`: nếu chưa có record → tạo default và trả về (không ghi DB).
`UpdateNotificationPreferenceCommandHandler`: upsert, invalidate `notif_pref:{userId}`.

## Edge Cases

| Case | Xử lý |
|------|-------|
| User chưa có preference record | `GetHandler` trả về default values, `UpdateHandler` tạo mới (INSERT) |
| Quiet hours start > end (wraps midnight, e.g. 22:00–07:00) | So sánh: `now < end || now >= start` |
| `QuietHoursStart == null` | Skip quiet hours check hoàn toàn |
| Channel disabled trong preference | Skip channel đó, không log error |
| Recipient không có email (null) → email channel | Skip silently, log Debug |
| Recipient không có device token → push channel | Skip, không tạo notification record |
| `DispatchRequest.Recipients` rỗng | Return sớm, không làm gì |
| `TypeChannelMatrix` không có type | Default `[InApp]` |

## Success Criteria

| Tiêu chí | Cách verify |
|----------|------------|
| Quiet hours: push bị skip, InApp không bị skip | Unit test: mock pref với QuietHoursStart=22, End=07; assert push channel không được gọi |
| Critical bypass: push vẫn gửi khi quiet hours active + Critical type | Unit test: mock pref + CriticalType → channel.SendAsync được gọi |
| Preference GET/PUT 200 OK | `dotnet test` integration test hoặc manual swagger |
| Cache invalidation khi PUT | Unit test: verify `ICacheService.RemoveAsync("notif_pref:{userId}")` được gọi |
| Unit test coverage ≥ 80% | `dotnet test --collect:"XPlat Code Coverage"` |

## Steps

- [x] Bước 0: Merge origin/dev vào local dev (`git merge origin/dev`) để có NotificationService files — 2026-06-19
- [x] Bước 1: Tạo `INotificationDispatcher.cs` + `DispatchRequest.cs` (Application/Services/) — 2026-06-19
- [x] Bước 2: Tạo `NotificationDispatcher.cs` với đầy đủ logic (matrix + quiet hours + critical bypass + push tokens) — 2026-06-19
- [x] Bước 3: Tạo `NotificationPreferenceDto.cs` + `NotificationPreferenceResponse.cs` — 2026-06-19
- [x] Bước 4: Tạo `GetNotificationPreferenceQuery` + Handler (default nếu chưa có record) — 2026-06-19
- [x] Bước 5: Tạo `UpdateNotificationPreferenceCommand` + Handler (upsert + invalidate cache) — 2026-06-19
- [x] Bước 6: Tạo `PreferencesController.cs` (GET/PUT) — 2026-06-19
- [x] Bước 7: Modify `ManageDependencyInjection.cs` — register `INotificationDispatcher` — 2026-06-19
- [ ] Bước 8: Tạo `NotificationDispatcherTests.cs` (quiet hours, critical bypass, preference routing, cache)
- [x] Bước 9: `dotnet build` + `dotnet test` → PASS — 2026-06-19

## Ghi chú kỹ thuật

- `ICacheService` đã registered trong `SharedInfrastructure.DependencyInjection.ManageDependencyInjection` — inject trực tiếp vào `NotificationDispatcher`
- Push tokens load từ `_unitOfWork.DeviceTokens` — 1 user có thể có nhiều devices → tạo nhiều notification records (1 per token)
- `NotificationDispatcher` đặt ở `Application/Services/` (không phải Infrastructure) vì nó orchestrate nhưng không cần EF core trực tiếp — chỉ cần `INotificationUnitOfWork` + `ICacheService` + `IEnumerable<INotificationChannel>` (inject từ Infrastructure)
- `TimeZoneInfo.FindSystemTimeZoneById` — IANA timezone IDs (e.g. "Asia/Ho_Chi_Minh") hoạt động trên Linux/.NET 6+ với package `TimeZoneConverter` hoặc dùng `TimeZoneInfo.TryFindSystemTimeZoneById`
- `INotificationChannel` inject như `IEnumerable<INotificationChannel>` → dispatcher resolve đúng channel bằng `.FirstOrDefault(c => c.ChannelType == channel)`
