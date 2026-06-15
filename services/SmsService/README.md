# SmsService — SMS Forwarder Gateway

SmsService là **SMS Gateway Hub trung tâm** của hệ thống:

- Nhận `SendSmsCommand` từ các service khác (Auth / Battery / Ticket / Notification) qua RabbitMQ.
- Queue vào DB riêng `sms_db` (Postgres, xmin concurrency).
- Push qua SignalR cho app Flutter `sms_fowarder` (Android có SIM thật) gửi tin nhắn.
- Báo kết quả `Sent` / `Failed` ngược lại qua Outbox event (`SmsDeliveryReportEvent` / `SmsFailedEvent`).
- Quản lý device gateway: cấp/thu hồi API key (BCrypt), daily limit, rate limit, audit log.

> **Spec đầy đủ** xem `overall.md` §68 + `sms_fowarder/backend-sms-fowarder.md`.

---

## Kiến trúc

```
┌─ AuthService (OTP)         ─┐
├─ BatteryService (alert)    ─┤  publish SendSmsCommand qua Outbox
├─ TicketService  (notify)   ─┤
└─ NotificationService       ─┘
                  │
                  ▼
   ════ RabbitMQ "SendSmsCommand" ════
                  │
                  ▼
       ┌──── SmsService ────┐
       │ Consumer (Inbox)   │
       │      │             │
       │ QueueSms Handler   │
       │      │             │
       │ sms_messages       │ ← (Pending)
       │      │             │
       │ ISmsGatewayNotifier│ ← SignalR push
       │      │             │
       └──────┼─────────────┘
              │
   ───── primary: SignalR push  ─── fallback: REST polling ───
              │
              ▼
      Flutter app sms_fowarder
              │
   GET  /api/sms-gateway/messages/pending  (claim batch)
   POST /api/sms-gateway/messages/report   (Sent/Failed)
   POST /api/sms-gateway/heartbeat
              │
              ▼
   Handlers + Outbox publish (SmsDeliveryReportEvent / SmsFailedEvent)
              │
              ▼
    Subscribers (Auth callback OTP sent, ...)
```

---

## Endpoint

### Flutter app (auth: `GatewayApiKey` scheme — BCrypt API key + `X-Device-Code`)

| Method | Path                                       | Mô tả                                |
|--------|--------------------------------------------|--------------------------------------|
| GET    | `/api/sms-gateway/messages/pending?limit=` | Claim batch SMS Pending → Sending    |
| POST   | `/api/sms-gateway/messages/report`         | Báo `Sent` / `Failed` (idempotent)   |
| POST   | `/api/sms-gateway/heartbeat`               | Cập nhật `LastSeenAt`                |
| Hub    | `/hubs/sms-gateway`                        | Realtime `NewPendingSms` + `BatchRevoked` |

Headers REST:
```
Authorization: Bearer <api-key-plaintext>
X-Device-Code: <device-code>
Content-Type: application/json
```

SignalR query (handshake WS):
```
{backendUrl}/hubs/sms-gateway?deviceCode={code}&access_token={apiKey}
```

### Admin (auth: JWT Bearer, role `Admin`)

| Method | Path                                          | Mô tả                                  |
|--------|-----------------------------------------------|----------------------------------------|
| POST   | `/api/admin/sms-gateway/devices`              | Tạo device + trả `apiKey` plaintext **1 lần** |
| GET    | `/api/admin/sms-gateway/devices`              | List devices (mặc định include revoked) |
| DELETE | `/api/admin/sms-gateway/devices/{id}`         | Thu hồi device (idempotent)            |
| POST   | `/api/admin/sms-gateway/messages/{id}/cancel` | Huỷ SMS Pending/Sending                |

Mọi response wrap trong `CommonResponse<T>` (chung quy ước hệ thống).

---

## State machine SMS

```
Pending  → Sending → Sent
Pending  → Sending → Failed (RetryCount + 1 < MaxRetryCount → Pending; ≥ → Failed final)
Pending  → Cancelled (admin)
Sending  → Pending (StaleSmsReaper revert sau 5 phút)
Sent → Sent (cột message bị Redactor xoá sau 24h)
```

---

## Quyết định kiến trúc (đã chốt)

| # | Mục | Quyết định |
|---|-----|-----------|
| 1 | DB | Postgres riêng `sms_db` (key `SmsDb`) |
| 2 | Concurrency | `xmin` (Postgres native) trên `sms_messages` + `sms_gateway_devices` |
| 3 | API key | BCrypt workFactor 11; plaintext chỉ trả 1 lần khi tạo |
| 4 | CQRS | MediatR — `Application/CQRS/Command/{Group}/...`, `Application/CQRS/Handler/{Group}/...` (theo AuthService) |
| 5 | Inbox | Redis `IInboxStore.ProcessOnceAsync` — dedup `(consumerName, messageId)` |
| 6 | Outbox | Custom pattern copy nguyên từ `AuthService.Infrastructure.BackgroundJobs.OutboxRelayBackgroundService` |
| 7 | Stale claim | 5 phút (đồng nhất ở `ClaimPending` lẫn `StaleSmsReaper`) |
| 8 | Daily limit | Có (mặc định 100/device/ngày) |
| 9 | Plaintext message | Lưu + TTL redactor xoá sau 24h khi `Sent` |
| 10 | Report idempotency | State-machine check `Status == Sending` + `GatewayDeviceCode` khớp |
| 11 | Rate limit | 60 req/phút/device (partition theo `X-Device-Code`) |
| 12 | Auth Flutter | Custom scheme `GatewayApiKey` |
| 13 | Auth Admin | JWT Bearer (scheme từ AuthService) |
| 14 | snake_case | Manual `HasColumnName(...)` trong từng Configuration |

---

## Cấu trúc folder

```
services/SmsService/
├── src/
│   ├── SmsService.Domain/                    Entities + Enums (4 + 2)
│   ├── SmsService.Application/               CQRS + abstractions + consumers
│   │   ├── CQRS/Command/{Sms,Admin}/
│   │   ├── CQRS/Handler/{Sms,Admin}/
│   │   ├── CQRS/Query/{Sms,Admin}/
│   │   ├── Consumers/                        SendSmsCommand + SendPhoneOtp (backward-compat)
│   │   ├── Interfaces/Repositories/          ISmsUnitOfWork
│   │   ├── Interfaces/Services/              ISmsGatewayNotifier + IGatewayApiKeyHasher
│   │   ├── DTOs/Response/{Sms,Admin}/
│   │   ├── Common/                           PhoneNumberNormalizer
│   │   └── DependencyInjection/              ManageDependencyInjection (marker)
│   ├── SmsService.Infrastructure/
│   │   ├── Persistence/                      SmsDbContext + Factory + 4 Configurations
│   │   ├── Implements/Repositories/          SmsUnitOfWork
│   │   ├── Implements/Services/              OutboxMessagePublisher
│   │   ├── Security/                         BcryptHasher + GatewayApiKeyAuthHandler
│   │   ├── Realtime/                         SmsGatewayHub + SignalRSmsGatewayNotifier
│   │   ├── BackgroundJobs/                   OutboxRelay + StaleReaper + Redactor + OutboxOptions
│   │   └── Options/                          SmsOptions
│   └── SmsService.Api/                       Program.cs (13 step) + 2 controllers
└── tests/
    └── SmsService.UnitTests/
```

---

## Local development

### Setup

```bash
# DB
createdb sms_db
# Hoặc:
PGPASSWORD='Password12345@' psql -h localhost -U alex -d postgres -c 'CREATE DATABASE sms_db;'

# Connection string trong .env:
ConnectionStrings__SmsDb=Host=localhost;Port=5432;Database=sms_db;Username=alex;Password=Password12345@
```

### Migration

```bash
# Từ REPO ROOT (capstone/backend/)
dotnet ef migrations add <Name> \
    --project services/SmsService/src/SmsService.Infrastructure \
    --startup-project services/SmsService/src/SmsService.Api \
    --context SmsDbContext

dotnet ef database update \
    --project services/SmsService/src/SmsService.Infrastructure \
    --startup-project services/SmsService/src/SmsService.Api \
    --context SmsDbContext
```

### Chạy service

```bash
dotnet run --project services/SmsService/src/SmsService.Api
# → http://localhost:5xxx (Swagger UI ở /swagger)
```

### Tạo device + lấy API key (curl)

```bash
# 1) Login admin qua AuthService để lấy JWT
ADMIN_JWT=$(curl -X POST https://localhost:5002/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"<password>"}' | jq -r '.data.accessToken')

# 2) Tạo device — copy apiKey 1 lần duy nhất
curl -X POST https://localhost:5xxx/api/admin/sms-gateway/devices \
  -H "Authorization: Bearer $ADMIN_JWT" \
  -H "Content-Type: application/json" \
  -d '{"deviceName":"Phone A","deviceCode":"android-gateway-001","dailyLimit":100}' | jq
```

### Test gateway endpoint

```bash
API_KEY="<plaintext-từ-response-trên>"

curl https://localhost:5xxx/api/sms-gateway/messages/pending?limit=5 \
  -H "Authorization: Bearer $API_KEY" \
  -H "X-Device-Code: android-gateway-001"
```

---

## Tích hợp với service khác

```csharp
// Từ AuthService / BatteryService / TicketService / NotificationService:
await _messageProducer.PublishAsync(new SendSmsCommand(
    PhoneNumber:   "+84901234567",
    Message:       "Nội dung tin nhắn",
    SourceService: "auth",                  // | "battery" | "ticket" | "notification"
    CorrelationId: Guid.NewGuid(),          // hoặc dùng record Id để track
    Category:      "otp",                   // optional
    TargetDeviceCode: null                  // null = broadcast tới mọi device
), cancellationToken);
```

Subscribe kết quả (optional):

```csharp
public class MySmsReportConsumer : IConsumer<SmsDeliveryReportEvent>
{
    public Task Consume(ConsumeContext<SmsDeliveryReportEvent> context)
    {
        // sms id, correlation id, phone, source, sent at, gateway device code
        return Task.CompletedTask;
    }
}
```

Đăng ký consumer qua `AddMessageBus(... typeof(MySmsReportConsumer).Assembly)` trong service đó.

---

## Quan sát (metrics + logs)

- Prometheus endpoint `/metrics` exposed.
- Outbox: `outbox_processed_total{event_type}`, `outbox_failures_total{reason}`, `outbox_skipped_max_retry_total`, `outbox_pending`.
- Inbox: `inbox_processed_total{consumer}`, `inbox_skipped_duplicate_total{consumer}`.
- Log structured (JSON, có `CorrelationId`).
