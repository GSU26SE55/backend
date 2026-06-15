# Flutter datasource patch — required for #SMS-41

> **Repo target:** `/Users/alex/Documents/capstone/sms_fowarder/` (Flutter — repo riêng, KHÔNG nằm trong backend này)
>
> **Lý do patch:** Backend trả `CommonResponse<T>` wrapper cho mọi REST endpoint:
> ```json
> { "isSuccess": true, "statusCode": 200, "message": "OK", "data": [ ... ] }
> ```
> Datasource cũ của Flutter parse raw list / `raw['items']` → **luôn nhận `[]`** và app
> **không bao giờ gửi SMS nào**.
>
> Xem chi tiết: §68 Phụ lục C trong `overall.md` (root project) hoặc
> `sms_fowarder/backend-sms-fowarder.md` mục **Phụ lục C**.

---

## 1. File cần patch

```
lib/features/sms_gateway/data/datasources/sms_gateway_remote_datasource.dart
```

## 2. Method cần patch — `fetchPendingMessages`

### Trước (broken):

```dart
Future<List<PendingSmsModel>> fetchPendingMessages({int limit = ApiConstants.defaultBatchSize}) async {
  final response = await apiClient.get(
    ApiConstants.pendingMessagesPath,
    queryParameters: {'limit': limit},
  );

  final raw = response.data;
  // ❌ Parse trực tiếp raw list — backend trả wrapper { isSuccess, data: [...] } → luôn `[]`
  if (raw is List<dynamic>) {
    return raw.map((item) => PendingSmsModel.fromJson(item)).toList();
  }
  return const [];
}
```

### Sau (correct — Phụ lục C.2):

```dart
Future<List<PendingSmsModel>> fetchPendingMessages({int limit = ApiConstants.defaultBatchSize}) async {
  final response = await apiClient.get(
    ApiConstants.pendingMessagesPath,
    queryParameters: {'limit': limit},
  );

  final raw = response.data;
  if (raw == null) return const [];

  final List<dynamic> data;
  if (raw is Map<String, dynamic> && raw['data'] is List) {
    data = raw['data'] as List<dynamic>;           // 🆕 backend CommonResponse wrapper
  } else if (raw is List<dynamic>) {
    data = raw;                                     // legacy fallback (server cũ)
  } else if (raw is Map<String, dynamic> && raw['items'] is List) {
    data = raw['items'] as List<dynamic>;           // legacy fallback (custom shape)
  } else {
    data = const [];
  }

  return data
      .map((item) => PendingSmsModel.fromJson(item as Map<String, dynamic>))
      .toList(growable: false);
}
```

## 3. (Optional, robust) — Phụ lục C.3: check `isSuccess`

```dart
// Throw NetworkException nếu BE trả isSuccess=false (vd device bị revoke → 403)
// thay vì silently nhận `[]` (FE nghĩ "no pending SMS", thật ra auth fail).
if (raw is Map<String, dynamic> && raw.containsKey('isSuccess')) {
  if (!(raw['isSuccess'] as bool? ?? false)) {
    throw NetworkException(
      raw['message']?.toString() ?? 'Backend rejected pending fetch',
      statusCode: raw['statusCode'] as int?,
    );
  }
}
```

## 4. Không cần patch

| Endpoint | Method | Lý do KHÔNG patch |
|----------|--------|-------------------|
| `POST /messages/report` | fire-and-forget | Datasource không parse body |
| `POST /heartbeat`       | fire-and-forget | Datasource không parse body |
| SignalR `NewPendingSms` / `BatchRevoked` | Hub event | Backend giữ đúng tên event + payload single object → `_asMap` parse OK |

## 5. Verify sau khi patch

1. Tạo gateway device + apiKey qua admin endpoint backend.
2. Cấu hình Flutter (Settings screen) với apiKey + deviceCode + backend URL.
3. Queue SMS qua backend (publish `SendSmsCommand` hoặc trigger AuthService OTP).
4. Kiểm tra Flutter app:
   - Realtime chip hiện `REALTIME` (SignalR connected).
   - Notification `NewPendingSms` xuất hiện < 1s.
   - `fetchPendingMessages` poll trả đúng list (KHÔNG còn `[]` nữa).
   - SIM gửi tin nhắn.
   - `POST /report` báo `Sent` thành công.
   - Backend `outbox_messages` có row → RabbitMQ nhận `SmsDeliveryReportEvent`.

---

**Task tracking:** GitHub issue #342 (Sprint SMS task `#SMS-41`).
**Owner:** FE/Mobile dev (không phải BE — code change ngoài scope backend repo).
**Estimate:** 30 phút.
