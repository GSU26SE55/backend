# OVERALL — Roadmap Backend GSU26SE55 (Full Detail Edition)

> **Document type:** Master backlog & technical specification + capstone project plan
> **Scope:** Toàn bộ backend còn lại để cover Core Business Flow (4 role · 6 phase · ticket state machine · SLA escalation · BR-01..BR-08 + 4 entity bổ sung) + Sprint 5B Alert–Ticket Saga execution + post-Sprint 8 defense prep.
> **Source of truth:** `core-business-flow.html` + `.claude/CLAUDE.md` + `.claude/rules/*` + `.claude/memory.md`.
> **Audience:** Leader + 3 BE Dev (Duy, Thắng, Thái) + 2 FE Dev (Trí, Minh) + GVHD (post-Sprint 8 dry-run review).
> **Cập nhật:** 2026-06-10 (v4.5) · Sprint 5B Saga + capacity planning + ops template + defense prep + wire value reconcile (xem §67 version log).

---

## Mục lục

- [Phần I — Bối cảnh](#phần-i--bối-cảnh)
  - [0. Trạng thái codebase hiện tại](#0-trạng-thái-codebase-hiện-tại)
  - [0bis. Stack & infra hiện hữu](#0bis-stack--infra-hiện-hữu)
- [Phần II — Microservices nghiệp vụ phải xây](#phần-ii--microservices-nghiệp-vụ-phải-xây)
  - [1. BatteryService](#1-batteryservice--p0)
  - [2. TicketService](#2-ticketservice--p0)
  - [3. NotificationService](#3-notificationservice--p1)
  - [4. KnowledgeBase module](#4-knowledgebase-module-trong-ticketservice--p2)
  - [5. Reporting endpoints](#5-reporting-endpoints--p2)
- [Phần III — Hạ tầng & cross-cutting](#phần-iii--hạ-tầng--cross-cutting)
  - [6. TimescaleDB integration](#6-timescaledb-integration--p1)
  - [6bis. FileStorage metadata foundation](#6bis-filestorage-metadata-foundation--p1)
  - [7. Mở rộng AuthService cho profile + skill](#7-mở-rộng-authservice-cho-profile--skill--p1)
  - [8. Cross-cutting concerns](#8-cross-cutting-concerns--p1)
  - [9. Observability](#9-observability--hoàn-thiện-p2)
  - [10. API Gateway hoàn thiện](#10-api-gateway-hoàn-thiện--p1)
- [Phần IV — Quality & operations](#phần-iv--quality--operations)
  - [11. Test strategy](#11-test-strategy-coverage--80-p1)
  - [12. Seed data & migration](#12-seed-data--migration-strategy-p1)
  - [13. Performance & caching](#13-performance--caching-strategy)
  - [14. Security checklist](#14-security-checklist)
  - [15. Email/Notification template catalog](#15-emailnotification-template-catalog)
- [Phần V — Lập kế hoạch](#phần-v--lập-kế-hoạch)
  - [16. Scaffold workflow](#16-scaffold-workflow-cho-từng-service)
  - [17. Sprint backlog 8 sprint](#17-sprint-backlog--8-sprint-chi-tiết)
  - [18. Definition of Done](#18-definition-of-done)
- [Phần VI — Phụ lục](#phần-vi--phụ-lục)
  - [20. Permission matrix](#20-permission-matrix-đầy-đủ)
  - [21. Error code catalog](#21-error-code-catalog)
  - [22. JWT claim structure](#22-jwt-claim-structure)
  - [23. Risk register](#23-risk-register)
  - [24. Checklist 6 phase business flow](#24-checklist-theo-6-phase-business-flow)
  - [25. Câu hỏi cần thống nhất](#25-câu-hỏi-cần-thống-nhất-trước-khi-bắt-đầu)
  - [26. Glossary & references](#26-glossary--references)
  - [27. Troubleshooting playbook](#27-troubleshooting-playbook)
  - [28. Tóm tắt files/paths tạo mới](#28-tóm-tắt-filespaths-cần-tạo)
- [Phần VII — Bổ sung sau review](#phần-vii--bổ-sung-sau-review-gap-analysis)
  - [30. AI Module integration](#30-ai-module-integration--p0)
  - [31. Site & BatteryGroup](#31-site--batterygroup-entities--p0)
  - [32. Ticket relationships](#32-ticket-relationships--parent-child-merge-watch--p0)
  - [33. SLA pause limits & advanced](#33-sla-pause-limits--advanced--p0)
  - [34. Real-time updates (SSE)](#34-real-time-updates-sse--push-channel--p0)
  - [35. Bulk operations + QR onboarding](#35-bulk-operations--qr-onboarding--p1)
  - [36. Comment / MaintenanceLog advanced](#36-comment--maintenancelog-advanced--p1)
  - [37. Alert silence/snooze + escalation](#37-alert-silence--snooze--ack-escalation--p1)
  - [38. Edge case business rules matrix](#38-edge-case-business-rules-matrix--p0)
  - [39. GDPR & compliance](#39-gdpr--compliance--p1)
  - [40. Operational documents](#40-operational-documents-adr--dr--runbook--p1)
  - [41. Maintenance schedule (preventive)](#41-preventive-maintenance-schedule--p2)
  - [42. Parts inventory](#42-parts-inventory--p2)
  - [43. Public KB + self-help](#43-public-knowledge-base--customer-self-help--p2)
  - [44. Mobile deep linking + Staff field](#44-mobile-deep-linking--staff-field-features--p1)
  - [45. Webhook outbound + public API](#45-webhook-outbound--public-api--p2)
  - [46. Advanced testing & chaos](#46-advanced-testing--chaos-engineering--p2)
  - [47. Security hardening additional](#47-security-hardening-additional--p1)
  - [48. AI feedback loop & analytics](#48-ai-feedback-loop--analytics--p1)
  - [49. Notification advanced](#49-notification-advanced-digest--batching--p1)
  - [50. Updated sprint backlog impact](#50-updated-sprint-backlog-impact)
- [Phần VIII — Bổ sung lần 2 (Final completeness)](#phần-viii--bổ-sung-lần-2-final-completeness)
  - [52. IoT Edge Device & Device Management](#52-iot-edge-device--device-management--p0)
  - [52bis. IoT implementation plan](#52bis-iot-implementation-plan)
  - [53. Battery scope reduction & Alert–Ticket Saga](#53-battery-scope-reduction--alertticket-saga--p0)
  - [54. Production Deployment (K8s + Helm)](#54-production-deployment-k8s--helm--p1)
  - [55. Mobile/Web App Management](#55-mobileweb-app-management--p1)
  - [56. Demo & Presentation Deliverables](#56-demo--presentation-deliverables--p0)
  - [57. AI advanced (deployment, retrain, batching)](#57-ai-advanced--deployment-retrain-batching--p1)
  - [58. Edge cases extension (EC-21..EC-34)](#58-edge-cases-extension-ec-21ec-34--p0)
  - [59. GDPR + security additional](#59-gdpr--security-additional--p1)
  - [60. Internal admin tools](#60-internal-admin-tools--p2)
  - [61. Search functionality](#61-search-functionality--p1)
  - [62. Media pipeline + accessibility](#62-media-pipeline--accessibility--p2)
  - [63. Customer success metrics](#63-customer-success-metrics--p2)
  - [64. Status page + maintenance broadcast](#64-status-page--maintenance-broadcast--p1)
  - [65. Documentation auto-generation](#65-documentation-auto-generation--p2)
  - [66. Final completeness checklist](#66-final-completeness-checklist)
  - [67. Tóm tắt final — file đầy đủ chưa?](#67-tóm-tắt-final--file-đầy-đủ-chưa)

---

# Phần I — Bối cảnh

## 0. Trạng thái codebase hiện tại

### 0.1. Đã có (DONE)

| Module | Trạng thái | Chi tiết |
|--------|-----------|----------|
| **`AuthService`** | ✅ Production-ready + profile extension | Account/Role/Permission/Session/RefreshToken/AuditLog/LoginAttempt/OTP/Outbox + Admin CRUD + Google OAuth helper + `AccountProfile`/`StaffProfile`/`StaffSkill` extension tables + uploaded/Google avatar flow |
| **`ApiGateway`** | ✅ Hoạt động | Route tới AuthService, port 4001 |
| **`EmailService`** | ✅ Consumer-only | Subscribe SendOtpRegisterEvent, SendAdminInviteEvent, SendPasswordResetOtpEvent, SendEmailChangeOtpEvent |
| **`SmsService`** | ✅ Consumer-only | Subscribe SendPhoneOtpEvent |
| **`FileStorageService`** | ✅ Metadata foundation ready | MinIO backend, signed URLs, `UploadedFile` metadata table, upload response trả `fileId`, metadata/presigned/download/delete theo `fileId` |
| **`SharedKernels`** | ✅ Done | `BaseEntity`, `AuditableEntity`, `IHardDeleteEntity`, `IGenericRepository`, `IUnitOfWork` |
| **`SharedInfrastructure`** | ⚠️ Foundation; hardening pending | Middleware (Global exception, CorrelationId, RequestLogging, SecurityHeaders, IdempotencyKey), Behaviors (Validation, Logging), Caching (Redis), Bus (MassTransit + correlation filters; retry/durable scheduler pending Sprint 5B `#235`), Redis Inbox hiện tại chưa transaction-safe cho DB consumer, Metrics, Swagger extensions, EnvFileLoader |
| **`SharedContracts`** | ✅ Foundation | Core response/event contracts done; Alert–Ticket Saga contracts pending Sprint 5B `#236` |
| **Docker compose** | ✅ Done | `timescale/timescaledb:latest-pg16`, postgres-init tạo logical DB riêng (`auth_db`, `file_storage_db`), redis:7, rabbitmq:3-management, minio, prometheus, grafana, loki, alertmanager |
| **CI/CD** | ✅ Done | GitHub Actions: detect-changes (matrix per service), build/unit-test/integration-test, dotnet format, Trivy filesystem scan, PR title validation (semantic), PR size warning, project rules check |
| **Pre-commit** | ✅ Done; Sprint 5B mở rộng | `.pre-commit-config.yaml` với dotnet format, secret-scan; Sprint 5B `#233` thêm hook `energy-co2-scope-guard` (xem §53.2ter) |
| **Hooks Claude** | ✅ Done | `.claude/hooks/be/*.sh`: block-dangerous, protect-sensitive, check-build, post-edit-feedback, validate-namespace, check-di-registration, check-dbcontext-update |

### 0.2. CHƯA có — Roadmap (phần chính document này)

| Service / Module | Status | Section | Details |
|------------------|----------|---------|-----------------|
| `BatteryService` | ✅ Core done | §1 | Core CRUD + Sensor batch ingest + Threshold detection + Alert deduplication; **không quản lý Energy/CO2 analytics** |
| `TicketService` | ✅ Core done | §2 | CQRS + Ticket State Machine + SLA/Activity/Reopen done; Alert–Ticket Saga pending Sprint 5B |
| IoT Edge Device backend + Device Management (ESP32 + hybrid HTTPS/MQTT) | 🔴 P0 | §52/§52bis + `newiot.md`/`overall.iot.md` | 1 sprint song song + hardware track |
| `NotificationService` (4 dự án, consumers + Expo push) | 🟠 P1 | §3 | 2 sprint |
| KnowledgeBase (module nội bộ TicketService) | 🟡 P2 | §4 | 0.5 sprint |
| Reporting endpoints (mỗi service expose) | 🟡 P2 | §5 | 1 sprint |
| TimescaleDB extension + hypertable | 🟠 P1 | §6 | 0.5 sprint |
| FileStorage metadata (`UploadedFile`) | ✅ Done | §6bis | Completed 13/5/2026 |
| AuthService profile expansion (avatar, phone, skill) | ✅ Done | §7 | Completed 13/5/2026 |
| BatteryService scope cleanup (bỏ Energy/CO2 + `Site.CapacityKw`) | 🔴 P0 | §53.1–§53.3 | Sprint 5B `#233`/`#234` |
| Outbox/Inbox hardening + Alert–Ticket Saga | 🔴 P0 | §8.1–§8.3, §53.4–§53.12 | Sprint 5B `#235`–`#239` |
| AuthService permission seed cho Saga (`ticket.saga.view/reprocess`) | 🔴 P0 | §7.5bis, §53.9 | Sprint 5B `#241` |
| Documentation sync (Swagger/Postman/SRS/CHANGELOG/runbook/Mermaid) | 🔴 P0 | §65, §40.3, §53.2bis | Sprint 5B `#240` |
| ADR-017 (Energy/CO2 removal) + ADR-018 (Saga orchestration) | 🔴 P0 | §40.1 | Sprint 5B `#233`/`#239` |
| AI Module integration (FastAPI + Polly + fallback) | 🟠 P1 | §30 | Sprint 3-4 (đã start) |
| Distributed tracing (OpenTelemetry → Tempo/Jaeger) | 🟡 P2 | §8.4 | 0.5 sprint |
| Gateway JWT validate + claim forwarding | 🟠 P1 | §10 | 0.5 sprint |
| Grafana business dashboards | 🟡 P2 | §9 | 0.5 sprint |
| Test coverage ≥ 80% | 🟠 P1 | §11 | Ongoing |
| Seed data scripts | 🟠 P1 | §12 | 0.5 sprint |

---

### 0.3. Đã hoàn tất trong lượt cập nhật 13/5/2026

- [x] Docker Compose dùng `timescale/timescaledb:latest-pg16`.
- [x] Docker Compose có `postgres-init` tạo database riêng cho từng service: `auth_db` và `file_storage_db`.
- [x] `AuthService` chỉ trỏ `ConnectionStrings__AuthDb` vào database Auth riêng.
- [x] `FileStorageService` chỉ trỏ `ConnectionStrings__FileStorageDb` vào database FileStorage riêng.
- [x] Bỏ fallback nguy hiểm `FileStorageService` → `AuthDb`; thiếu `FileStorageDb` thì fail rõ ràng.
- [x] `FileStorageService` metadata foundation: Domain project, `UploadedFile`, `FilePurposeEnum`, `FileStatusEnum`, EF configuration, migration `AddUploadedFileMetadata`.
- [x] `FileStorageService` upload flow tạo metadata sau khi upload object thành công và response có `fileId`.
- [x] `FileStorageService` có endpoint metadata/presigned/download/delete theo `fileId`.
- [x] `AuthService` profile extension: `AccountProfile`, `StaffProfile`, `StaffSkill`, migration `AddAccountProfileExtensionTables`.
- [x] `AuthService` avatar flow: uploaded avatar dùng `AvatarFileId`, Google avatar dùng `ExternalAvatarUrl`, FE dùng `displayAvatarUrl`.
- [x] Validate kỹ thuật đã chạy: `docker compose --env-file .env.Docker config --quiet`, `sh -n docker/postgres/create-service-databases.sh`, `dotnet build FileStorageService.Infrastructure`.

### 0.4. Đã hoàn tất trong lượt cập nhật 18/5/2026

**Auth (`docs/api-auth.md` — doc only):**
- [x] Làm rõ avatar route: `POST /api/auth/me/avatar` là route thật, entry trong Nhóm 2 (`/api/accounts`) là cross-reference có ghi chú.
- [x] Phân biệt Nhóm 2 (`/api/accounts/*`) vs Nhóm 3 (`/api/auth/*`): Nhóm 3 là canonical — FE/Mobile dùng Nhóm 3; Nhóm 2 tồn tại cho backward compat internal.
- [x] Bổ sung response schema cho `GET /api/staff/{id}/assignment-profile` → trả `StaffAssignmentProfileDto` (cùng shape với `GET /api/staff`).
- [x] Làm rõ `avatarUrl` trong `PUT /api/admin/accounts/{id}`: field legacy, không khuyến khích — dùng `avatarFileId` thay thế.
- [x] Bổ sung TTL token mời: `invitationToken` hết hạn sau **72 giờ**; trả `400 isSuccess=false` nếu expired hoặc đã dùng.
- [x] Quyết định 2FA behavior (Option B): giữ behavior hiện tại — `POST /api/accounts/me/2fa/enable` activate ngay, không có bước confirm riêng. Admin disable qua `DELETE /api/admin/accounts/{id}/2fa`.
- [x] Bổ sung lifecycle temporary role: tự expire qua background job, có audit log khi expire, không cần manual revoke.
- [x] Bổ sung error codes cho `GET /api/admin/accounts/{id}` (404), `DELETE /api/admin/accounts/{id}` (404, 409 nếu đang active), `POST /api/admin/accounts/{id}/unlock` (404, 409 nếu chưa bị khóa).

**FileStorage (`docs/api-filestorage.md` + code):**
- [x] `FilePurposeEnum.Other = 0` xác nhận là **legacy backward compat** có chủ ý — giữ nguyên, không migrate. Code `FileUploadPolicy` xử lý `0` như `Other`. Document rõ exception trong §6bis.3.
- [x] Sửa mô tả 409 trong bảng HTTP codes: áp dụng cho cả `GET /{id}/download` và `GET /{id}/presigned-url` (không chỉ presigned-url).
- [x] Cải thiện bảng so sánh objectKey vs fileId: không có endpoint metadata theo objectKey là quyết định thiết kế có chủ ý — ưu tiên `fileId` cho service mới.
- [x] Thêm note Sprint 1: `publicUrl` luôn `null` với MinIO local — FE phải handle null và fallback về `GET /{id}/download`.
- [x] Thêm note chuẩn hóa `objectKey`: trim whitespace, reject `..` (path traversal), không lowercase, client truyền đúng objectKey nhận từ upload response.
- [x] **Code fix (7 handlers):** Bổ sung `!IsDeleted` filter bị thiếu trong `GetFileMetadataQueryHandler`, `GetPresignedUrlQueryHandler`, `DownloadFileQueryHandler`, `DeleteFileCommandHandler` và 3 handler còn lại — tuân thủ rule "không có global query filter".

**Battery (`docs/api-battery.md` + code):**
- [x] **CRITICAL — Code fix:** 3 POST handlers (`CreateBatteryAsset`, `CreateBatteryGroup`, `CreateBatteryType`) đổi `StatusCode = 201` → `StatusCode = 200` trong body để nhất quán với `Ok()` controller pattern.
- [x] **CRITICAL — Code fix:** `BatchIngestSensorReadingsCommand.ValidateAsync()` bổ sung giới hạn `Items.Count > 1000` → trả `400 isSuccess=false`.
- [x] **CRITICAL — Doc:** Xác nhận `DedupWindowEndUtc` là `DateTime` non-nullable (không phải `DateTime?`) — alert **luôn** có dedup window khi tạo.
- [x] **IMPORTANT — Doc:** `DELETE /api/battery-groups/{id}` và `DELETE /api/battery-types/{id}` trả `409` nếu còn `BatteryAsset` liên kết (block, không cascade).
- [x] **IMPORTANT — Doc:** `GET /api/battery-assets` mặc định sort `createdAt` giảm dần, không hỗ trợ sort param động.
- [x] **IMPORTANT — Doc:** `GET /api/sensor-readings/{batteryAssetId}/aggregate` giữ "Planned Sprint 7" — FE/Mobile dùng raw `/history` cho chart tạm ở Sprint 4–5.
- [x] **MINOR — Doc:** `PUT /api/battery-assets/{id}` cho phép set `warrantyStatus = Void` không cần `voidReason` trong scope capstone.
- [x] **MINOR — Doc:** `POST /api/sensor-readings/batch` bổ sung lỗi thường gặp (`401` API Key, `400` batteryAssetId không tồn tại) và rate limit đề xuất: **60 requests/minute/device**.
- [x] **Code fix:** `UpdateBatteryAssetCommandHandler.UpdateGroupCountsAsync()` — bổ sung `&& !group.IsDeleted` cho cả `oldGroup` và `newGroup` lookup.
- [x] **Code fix:** `DeleteBatteryAssetCommandHandler` — bổ sung `&& !item.IsDeleted` cho group lookup khi decrement `BatteryCount`.
- [x] **Doc:** Sửa endpoint acknowledge alert: trả `409` (không phải `400`) cho state transition không hợp lệ; block cả `Resolved` lẫn `Merged`.
- [x] **Doc:** Sửa validation batch sensor readings: `voltage >= 0` (cho phép 0), temperature `-50..120°C`, timestamp cho phép +5 phút so với server time.
- [x] **Doc:** Bổ sung 4 trường hợp `409` còn thiếu cho `POST /api/battery-assets` (serial trùng, batteryType mismatch với group, group không thuộc site, site không thuộc customer).

---

## 0bis. Stack & infra hiện hữu

### 0bis.1. Phiên bản công nghệ
- .NET 8 LTS, C# 12
- EF Core 8, PostgreSQL 16 qua `timescale/timescaledb:latest-pg16` trong Docker Compose (xem §6)
- Redis 7 (cache + inbox + idempotency)
- RabbitMQ 3 (MassTransit)
- MinIO (S3-compatible) cho FileStorage
- Polly (đã wrap trong SharedInfrastructure cho retry/timeout)
- MediatR, FluentValidation thay thế bằng custom `IValidatable<T>` pattern
- Serilog → Loki
- Prometheus-net cho metrics
- OpenAPI/Swashbuckle
- **Sprint 5B bổ sung** (xem §53, §8.3):
  - `MassTransit.EntityFrameworkCoreIntegration` — EF Consumer Outbox/Inbox + Saga repository
  - `MassTransit.Quartz` + `Quartz.AspNetCore` + `Quartz.Serialization.Json` — durable scheduler cho Saga retry/timeout (RabbitMQ image hiện tại chưa có delayed-message plugin)

### 0bis.2. Patterns đã thiết lập
| Pattern | Vị trí | Áp dụng cho service mới |
|---------|--------|-------------------------|
| Clean Architecture 4 layer | Đã có ở AuthService | **Bắt buộc** copy cho Battery/Ticket/Notification |
| CQRS + MediatR | Đã có | **Bắt buộc** |
| Custom `IValidatable<T>` (không dùng FluentValidation) | `SharedContracts/Interfaces/IValidatable.cs` | **Bắt buộc** — pipeline đã chạy qua ValidationBehavior |
| `CommonResponse<T>` wrapper | `SharedContracts/Common/Responses` | **Bắt buộc** |
| Soft delete qua `AuditableEntityInterceptor` | SharedInfrastructure | **Bắt buộc** — KHÔNG dùng global query filter, luôn `.Where(x => !x.IsDeleted)` |
| Repository + UnitOfWork (`GetAllAsync` sync trả `IQueryable`) | `SharedKernels` | **Bắt buộc** — tên `GetAllAsync` legacy, **KHÔNG** await |
| Outbox pattern | AuthService có `OutboxMessage` entity (custom); Sprint 5B thêm `MassTransit EF Consumer Outbox` cho Saga endpoints | HTTP/background handler dùng custom; Saga participant consumer dùng EF Consumer Outbox (xem §8.3) |
| Inbox idempotency consumer | `SharedInfrastructure/Idempotency` (Redis); Sprint 5B Saga dùng MassTransit EF Consumer Inbox (durable) | Redis Inbox cho consumer KHÔNG thay đổi DB; EF Consumer Inbox cho consumer thay đổi DB |
| Orchestrated Saga (Sprint 5B) | `TicketService.Infrastructure/Sagas/` | Cross-service transaction qua Saga state machine + forward recovery (ADR-018, §53) |
| Correlation ID middleware + bus filter | SharedInfrastructure | Tự động — chỉ cần đăng ký DI |
| Redis caching wrapper | `SharedInfrastructure/Caching` | Inject `ICacheService` |
| Response wrapper `CommonResponse<T>` với `IsSuccess=true` mặc định | SharedContracts | Bắt buộc |
| JWT claims (`UserId`, `Role`, `FullName`, `Email`) | AuthService phát hành | Service downstream chỉ validate qua middleware từ gateway hoặc tự validate JWT |

### 0bis.3. Quy ước route tổng (cập nhật cho gateway aggregation)
```
/api/v1/auth/*               → AuthService          (port 5001)
/api/battery-assets/*     → BatteryService       (port 5002)
/api/battery-types/*      → BatteryService
/api/thresholds/*         → BatteryService
/api/sensor-readings/*    → BatteryService
/api/alerts/*             → BatteryService
/api/v1/iot-devices/*        → BatteryService       (device-side: provision/heartbeat/firmware/calibration — §52)
/api/v1/admin/iot-devices/*  → BatteryService       (admin CRUD device + firmware release — §52)
/api/v1/tickets/*            → TicketService        (port 5003)
/api/v1/comments/*           → TicketService
/api/v1/maintenance-logs/*   → TicketService
/api/v1/knowledge-base/*     → TicketService (module)
/api/v1/notifications/*      → NotificationService  (port 5004)
/api/v1/device-tokens/*      → NotificationService
/api/v1/notification-preferences/* → NotificationService
/api/v1/files/*              → FileStorageService   (port 5005)
/api/v1/reports/*            → Aggregated (Battery/Ticket reports)
/api/v1/admin/sagas/alert-ticket/* → TicketService    (Sprint 5B — xem §53.11)
```

> **Gateway port:** giữ nguyên `4001`. Downstream services chạy port `5001-5005`.

---

# Phần II — Microservices nghiệp vụ phải xây

## 1. BatteryService — P0

### 1.1. Trách nhiệm (Single Responsibility)
1. CRUD `BatteryType`, `ThresholdConfig`, `BatteryAsset`.
2. Ingest và lưu `SensorReading` (TimescaleDB hypertable).
3. Background detection: scan readings → so sánh threshold → generate `Alert`.
4. Dedup alert theo cửa sổ thời gian (BR-03).
5. Publish `BatteryAnomalyDetectedEvent` và tham gia Alert–Ticket Saga để link `Alert.TicketId`.
6. Expose realtime + history queries cho Mobile/Web.
7. Provide battery health summary/trend endpoints; không cung cấp Energy/CO2 analytics.

#### 1.1.1. Boundary chính thức sau scope review 10/6/2026

BatteryService chỉ quản lý **tài sản pin, telemetry điện học phục vụ chẩn đoán, sức khỏe pin và cảnh báo**.

**Giữ lại vì là dữ liệu kỹ thuật cốt lõi:**
- `Voltage`, `Current`, `Temperature`, `SocPercent`, `SohPercent`.
- `CycleCount`, `ChargingState`, internal resistance, cell voltage delta.
- `NominalCapacityAh`, `NominalVoltage`, ngưỡng dòng sạc/xả.
- Realtime/history/aggregate telemetry phục vụ chart sức khỏe, AI và anomaly detection.
- `SolarIrradiance` trong `AmbientReading` chỉ là ngữ cảnh môi trường/nhiệt cho battery health;
  không dùng để tính sản lượng, kWh, chi phí hoặc CO2.

**Loại khỏi sản phẩm và không được triển khai trong BatteryService:**
- Tính năng lượng sạc/xả theo kWh, energy session, energy throughput.
- Tổng hợp năng lượng theo ngày/tháng cho asset hoặc site.
- Hiệu suất round-trip dùng cho báo cáo kinh doanh.
- Biểu giá điện, tiền tiết kiệm, carbon emission factor, CO2 saved.
- API/dashboard/report/recommendation liên quan Energy, cost saving hoặc CO2.

Không tạo `EnergyService` thay thế. Đây là quyết định **bỏ scope**, không phải di chuyển domain sang service khác.
Chi tiết cleanup và acceptance criteria xem §53.1–§53.3.

### 1.2. Cấu trúc thư mục đầy đủ

```
services/BatteryService/
├── BatteryService.slnx
├── src/
│   ├── BatteryService.Api/
│   │   ├── BatteryService.Api.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── appsettings.Development.json
│   │   ├── appsettings.Docker.json
│   │   ├── Dockerfile
│   │   └── Controllers/
│   │       ├── BatteryAssetsController.cs
│   │       ├── BatteryTypesController.cs
│   │       ├── ThresholdConfigsController.cs
│   │       ├── SensorReadingsController.cs
│   │       ├── AlertsController.cs
│   │       ├── DashboardController.cs
│   │       └── HealthController.cs
│   ├── BatteryService.Application/
│   │   ├── BatteryService.Application.csproj
│   │   ├── CQRS/
│   │   │   ├── Command/
│   │   │   │   ├── BatteryAsset/
│   │   │   │   │   ├── BatteryAssetCreateCommand.cs
│   │   │   │   │   ├── BatteryAssetUpdateCommand.cs
│   │   │   │   │   ├── BatteryAssetDeleteCommand.cs
│   │   │   │   │   ├── BatteryAssetRestoreCommand.cs
│   │   │   │   │   └── BatteryAssetTransferOwnerCommand.cs
│   │   │   │   ├── BatteryType/...
│   │   │   │   ├── ThresholdConfig/
│   │   │   │   │   └── ThresholdConfigUpsertCommand.cs
│   │   │   │   ├── SensorReading/
│   │   │   │   │   └── SensorReadingBatchIngestCommand.cs
│   │   │   │   └── Alert/
│   │   │   │       ├── AlertCreateCommand.cs (internal — system)
│   │   │   │       ├── AlertAcknowledgeCommand.cs
│   │   │   │       └── AlertResolveCommand.cs
│   │   │   ├── Query/
│   │   │   │   ├── BatteryAsset/
│   │   │   │   │   ├── BatteryAssetGetListQuery.cs
│   │   │   │   │   ├── BatteryAssetGetByIdQuery.cs
│   │   │   │   │   ├── BatteryAssetRealtimeQuery.cs
│   │   │   │   │   └── MyBatteryAssetsQuery.cs (Customer)
│   │   │   │   ├── BatteryType/...
│   │   │   │   ├── ThresholdConfig/
│   │   │   │   │   └── ThresholdConfigGetByTypeQuery.cs
│   │   │   │   ├── SensorReading/
│   │   │   │   │   ├── SensorReadingGetHistoryQuery.cs
│   │   │   │   │   └── SensorReadingGetLatestQuery.cs
│   │   │   │   ├── Alert/
│   │   │   │   │   ├── AlertGetListQuery.cs
│   │   │   │   │   ├── AlertGetByIdQuery.cs
│   │   │   │   │   └── ActiveAlertsByAssetQuery.cs
│   │   │   │   └── Dashboard/
│   │   │   │       └── BatteryDashboardStatsQuery.cs
│   │   │   └── Handler/  (mirror command/query structure)
│   │   ├── DTOs/
│   │   │   └── Response/
│   │   │       ├── BatteryAsset/
│   │   │       │   ├── BatteryAssetDto.cs
│   │   │       │   ├── BatteryAssetResponse.cs
│   │   │       │   ├── BatteryAssetListResponse.cs
│   │   │       │   └── BatteryAssetRealtimeDto.cs
│   │   │       ├── BatteryType/...
│   │   │       ├── ThresholdConfig/...
│   │   │       ├── SensorReading/...
│   │   │       └── Alert/...
│   │   ├── Consumers/
│   │   │   ├── AccountActivatedConsumer.cs       (link Customer to asset)
│   │   │   ├── AccountDeletedConsumer.cs         (reassign or soft delete)
│   │   │   ├── AccountStatusChangedConsumer.cs
│   │   │   └── LinkAlertToTicketConsumer.cs       (Saga participant)
│   │   ├── Interfaces/
│   │   │   ├── Repositories/
│   │   │   │   └── IBatteryUnitOfWork.cs
│   │   │   └── Services/
│   │   │       ├── IAlertDeduplicationService.cs
│   │   │       └── IAnomalyDetector.cs
│   │   ├── Services/
│   │   │   ├── AlertDeduplicationService.cs
│   │   │   └── ThresholdAnomalyDetector.cs
│   │   └── Configuration/
│   │       └── BatteryServiceOptions.cs           (dedup window, scan interval)
│   ├── BatteryService.Domain/
│   │   ├── BatteryService.Domain.csproj
│   │   ├── Entities/
│   │   │   ├── BatteryAsset.cs
│   │   │   ├── BatteryType.cs
│   │   │   ├── ThresholdConfig.cs
│   │   │   ├── SensorReading.cs
│   │   │   ├── Alert.cs
│   │   │   ├── AlertHistory.cs
│   │   │   └── OutboxMessage.cs
│   │   └── Enums/
│   │       ├── BatteryStatusEnum.cs
│   │       ├── WarrantyStatusEnum.cs
│   │       ├── AnomalyTypeEnum.cs
│   │       ├── AlertSeverityEnum.cs
│   │       ├── AlertStatusEnum.cs
│   │       └── BatteryChemistryEnum.cs
│   └── BatteryService.Infrastructure/
│       ├── BatteryService.Infrastructure.csproj
│       ├── Persistence/
│       │   ├── ApplicationDbContext.cs
│       │   ├── Configurations/
│       │   │   ├── BatteryAssetConfiguration.cs
│       │   │   ├── BatteryTypeConfiguration.cs
│       │   │   ├── ThresholdConfigConfiguration.cs
│       │   │   ├── SensorReadingConfiguration.cs
│       │   │   ├── AlertConfiguration.cs
│       │   │   └── AlertHistoryConfiguration.cs
│       │   ├── Repositories/
│       │   │   ├── BatteryAssetRepository.cs (custom queries nếu cần)
│       │   │   ├── SensorReadingRepository.cs (batch insert raw SQL)
│       │   │   ├── AlertRepository.cs
│       │   │   └── BatteryUnitOfWork.cs
│       │   └── Migrations/
│       ├── BackgroundJobs/
│       │   ├── ThresholdCheckBackgroundService.cs
│       │   ├── AlertEscalationBackgroundService.cs
│       │   ├── AlertAutoResolveBackgroundService.cs
│       │   └── OutboxRelayBackgroundService.cs
│       ├── Mqtt/                                    ← MQTT v2 (P3 — xem §52.10, §52.14)
│       │   ├── MqttBridgeBackgroundService.cs        ← subscribe telemetry/heartbeat/status
│       │   ├── MqttTopicMap.cs                       ← solar/{site}/{dev}/...
│       │   ├── TelemetryMessageHandler.cs            ← validate → insert → anomaly (reuse ingest command)
│       │   └── LastWillHandler.cs                    ← status=offline → mark Offline + alert
│       ├── Security/
│       │   └── DeviceApiKeyService.cs                ← sinh/hash/rotate/revoke API key per-device (+ MQTT credential)
│       └── DependencyInjection/
│           └── ManageDependencyInjection.cs          ← đăng ký MQTT bridge + IoT background jobs
└── tests/
    ├── BatteryService.UnitTests/
    │   ├── BatteryService.UnitTests.csproj
    │   ├── Application/
    │   │   ├── CQRS/Commands/*HandlerTests.cs
    │   │   ├── CQRS/Queries/*HandlerTests.cs
    │   │   └── Services/
    │   │       ├── AlertDeduplicationServiceTests.cs
    │   │       └── ThresholdAnomalyDetectorTests.cs
    │   ├── Domain/
    │   │   └── EntityTests.cs
    │   └── Fixtures/
    │       └── MockUnitOfWorkFactory.cs
    └── BatteryService.IntegrationTests/
        ├── BatteryService.IntegrationTests.csproj
        ├── Controllers/
        │   ├── BatteryAssetsControllerTests.cs
        │   ├── SensorReadingsControllerTests.cs
        │   └── AlertsControllerTests.cs
        ├── BackgroundJobs/
        │   └── ThresholdCheckBackgroundServiceTests.cs
        ├── Consumers/
        │   ├── AccountActivatedConsumerTests.cs
        │   └── LinkAlertToTicketConsumerTests.cs
        └── Fixtures/
            ├── PostgresTimescaleFixture.cs  (TestContainers)
            └── WebApplicationFactoryFixture.cs
```

### 1.3. Entity detail — đầy đủ field & validation

#### 1.3.1. `BatteryAsset` (kế thừa `AuditableEntity`)

| Field | Type | Constraint | Index | Note |
|-------|------|-----------|-------|------|
| `Id` | `Guid` | PK | clustered | từ `AuditableEntity` |
| `SerialNumber` | `string(64)` | NOT NULL, UNIQUE | btree unique | Auto-generate hoặc nhập tay |
| `BatteryTypeId` | `Guid` | FK → BatteryType.Id, NOT NULL | btree | — |
| `CustomerId` | `Guid` | NOT NULL | btree | userId Customer (AuthService) |
| `InstallDate` | `DateTime` | NOT NULL | — | UTC |
| `WarrantyEndDate` | `DateTime?` | nullable | — | — |
| `WarrantyStatus` | `WarrantyStatusEnum` | NOT NULL default `Active` | — | 1=Active, 2=Expired, 3=Void |
| `Location` | `string(255)?` | nullable | — | Free text hoặc GPS |
| `Latitude` | `decimal(9,6)?` | nullable | — | optional |
| `Longitude` | `decimal(9,6)?` | nullable | — | optional |
| `Status` | `BatteryStatusEnum` | NOT NULL default `Active` | btree filter | 1=Active, 2=Inactive, 3=Decommissioned |
| `Notes` | `string(1000)?` | nullable | — | — |
| `LastSensorReadingAt` | `DateTime?` | nullable | btree | Cache để query nhanh "stale device" |
| `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted`, `DeletedAt` | — | từ `AuditableEntity` | — | — |

**Composite index:** `(CustomerId, IsDeleted, Status)` cho query "my batteries".

**Validation rules `BatteryAssetCreateCommand`:**
- `SerialNumber`: required, 5–64 chars, regex `^[A-Z0-9-]+$`, unique (check AnyAsync).
- `BatteryTypeId`: required, must exist + !IsDeleted.
- `CustomerId`: required, must exist (call AuthService API hoặc cache local).
- `InstallDate`: required, ≤ today, ≥ 5 năm trước.
- `WarrantyEndDate`: optional, > InstallDate.

#### 1.3.2. `BatteryType` (kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `Name` | `string(100)` | NOT NULL, UNIQUE | "Lithium-ion 12V 100Ah" |
| `Manufacturer` | `string(100)?` | — | — |
| `NominalCapacityAh` | `decimal(10,2)` | NOT NULL, > 0 | — |
| `NominalVoltage` | `decimal(6,2)` | NOT NULL, > 0 | — |
| `Chemistry` | `BatteryChemistryEnum` | NOT NULL | 1=LiFePO4, 2=NMC, 3=NCA, 4=LCO |
| `MaxCycleCount` | `int` | NOT NULL default 2000 | — |
| `Description` | `string(500)?` | — | — |

#### 1.3.3. `ThresholdConfig` (kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `BatteryTypeId` | `Guid` | FK, NOT NULL | One-active-config per type |
| `VoltageMin` | `decimal(6,2)` | NOT NULL | — |
| `VoltageMax` | `decimal(6,2)` | NOT NULL, > VoltageMin | — |
| `TemperatureMax` | `decimal(5,2)` | NOT NULL | °C |
| `TemperatureMin` | `decimal(5,2)` | NOT NULL | °C |
| `SocWarningThreshold` | `decimal(5,2)` | NOT NULL, 0–100 | % |
| `SocCriticalThreshold` | `decimal(5,2)` | NOT NULL, 0–100 | % |
| `CurrentMaxCharge` | `decimal(8,2)?` | nullable | A |
| `CurrentMaxDischarge` | `decimal(8,2)?` | nullable | A |
| `SohWarningThreshold` | `decimal(5,2)?` | nullable, 0–100 | %, vd 85 → cảnh báo pin xuống cấp |
| `SohCriticalThreshold` | `decimal(5,2)?` | nullable, 0–100 | %, vd 75 → EOL sắp tới |
| `InternalResistanceMaxMilliohm` | `decimal(8,2)?` | nullable, > 0 | mΩ — early aging indicator |
| `CellVoltageDeltaMaxMv` | `decimal(8,2)?` | nullable, ≥ 0 | mV, vd 100 — pack imbalance threshold |
| `NoiseSuppressionCount` | `int` | NOT NULL default 5 | **B1** — số lần breach tối thiểu trong window để escalate thành Alert (xem §1.6.5) |
| `NoiseSuppressionWindowHours` | `int` | NOT NULL default 24 | **B1** — cửa sổ thời gian đếm breach (giờ) |
| `NoiseSuppressionEnabled` | `bool` | NOT NULL default true | **B1** — tắt khi loại pin yêu cầu alert tức thì (vd chemistry nhạy nhiệt) |
| `EffectiveFromUtc` | `DateTime` | NOT NULL | — |
| `IsActive` | `bool` | NOT NULL default true | Chỉ 1 record active per type |

**Validation:**
- `SocCriticalThreshold < SocWarningThreshold`.
- `TemperatureMin < TemperatureMax`.
- Nếu cả 2 SOH threshold không null: `SohCriticalThreshold < SohWarningThreshold`.
- `NoiseSuppressionCount` ≥ 1 và ≤ 50, `NoiseSuppressionWindowHours` ≥ 1 và ≤ 168 (7 ngày).

#### 1.3.4. `SensorReading` (KHÔNG kế thừa `AuditableEntity` — time-series append-only)

| Field | Type | Constraint | Index |
|-------|------|-----------|-------|
| `Time` | `DateTime` | NOT NULL | TimescaleDB hypertable column |
| `BatteryAssetId` | `Guid` | NOT NULL | btree composite |
| `Voltage` | `decimal(6,2)` | NOT NULL | — |
| `Current` | `decimal(8,2)` | NOT NULL | — |
| `Temperature` | `decimal(5,2)` | NOT NULL | °C — đo trên thân/BMS pin |
| `SocPercent` | `decimal(5,2)` | NOT NULL, 0–100 | — |
| `CycleCount` | `int?` | nullable | — |
| `SohPercent` | `decimal(5,2)?` | nullable, 0–100 | **Target chính của AI module** |
| `ChargingState` | `ChargingStateEnum?` | nullable | 1=Idle, 2=Charging, 3=Discharging, 4=Float, 5=Bypass |
| `InternalResistanceMilliohm` | `decimal(8,2)?` | nullable, > 0 | mΩ — early aging indicator |
| `CellVoltageDeltaMv` | `decimal(8,2)?` | nullable, ≥ 0 | mV — chênh lệch Vmax-Vmin giữa các cell |
| `BmsErrorCode` | `string(64)?` | nullable | Mã lỗi BMS raw (vd `0x0A`, `OverCurrent,CellImbalance`) |
| `SourceDeviceId` | `string(64)?` | — | IoT edge device code (ESP32 `DeviceCode`) hoặc BMS module ID |
| `SourceType` | `SensorReadingSourceTypeEnum` | NOT NULL default `IotGateway` | **B9** — 1=Bms, 2=IotGateway, 3=External. Phân biệt nguồn đo |
| `SensorSourceCode` | `string(20)?` | nullable | **§52.9** — "primary" / "redundant" / "external-temp". Phân biệt nhiều sensor cùng đo 1 pin (vd BMS primary vs INA226 redundant vs DS18B20 external-temp). 1 pin có thể có nhiều reading cùng timestamp khác `SensorSourceCode` |

**Compound index:** `(BatteryAssetId, Time DESC)` cho realtime/history queries.
**Hypertable interval:** 1 day chunks.
**Retention policy:** 90 ngày raw, 1 năm 1h-aggregate, 5 năm 1d-aggregate.

**Lưu ý:** 5 field SOH/ChargingState/IR/CellDelta/BmsErrorCode đều **nullable** — backfill data cũ không cần. BMS có thì gửi, không có thì để null.

**Cross-source validation (B9 + B10):**
Khi cùng 1 `BatteryAssetId` có reading từ **cả BMS và IoT Gateway** trong cùng cửa sổ 60s, `ThresholdAnomalyDetector` phải so sánh:
- |Voltage_bms − Voltage_iot| > 0.5V → kích hoạt anomaly `SensorMismatch` (severity Warning).
- |Temperature_bms − Temperature_iot| > 5°C → `SensorMismatch` (Warning).
- Đây là tín hiệu BMS hoặc cảm biến IoT đang đo sai → cần Staff check.
Xem chi tiết logic ở §1.6.6 (Cross-source validation).

#### 1.3.5. `Alert` (kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `BatteryAssetId` | `Guid?` | FK, **nullable** | btree — alert per-pin |
| `SiteId` | `Guid?` | FK, **nullable** | btree — alert per-site (ambient/incident) |
| `EnvironmentalIncidentId` | `Guid?` | FK, **nullable** | Link tới incident nếu alert được tạo từ smoke/water |
| `AnomalyType` | `AnomalyTypeEnum` | NOT NULL | 1–15 (xem §1.3.6, mở rộng từ 7 → 15) — wire value cross-service đồng bộ §1.3.6, **không phải custom Saga numbering** (xem §53.7) |
| `Severity` | `AlertSeverityEnum` | NOT NULL | 1=Info, 2=Warning, 3=Critical |
| `ThresholdValue` | `decimal(10,4)?` | nullable | NULL cho incident-based alert (smoke/water không có threshold) |
| `ActualValue` | `decimal(10,4)?` | nullable | NULL như trên |
| `Unit` | `string(10)?` | nullable | V/A/°C/%/RH (nullable cho incident) |
| `DetectedAt` | `DateTime` | NOT NULL | UTC |
| `Status` | `AlertStatusEnum` | NOT NULL | 1=Open, 2=Acknowledged, 3=Merged, 4=Resolved |
| `MergedIntoAlertId` | `Guid?` | self-FK, nullable | BR-03 dedup |
| `TicketId` | `Guid?` | nullable, non-unique index `WHERE ticket_id IS NOT NULL` (Sprint 5B `AddAlertTicketLinkIndex`) | Link tới ticket — set bởi `LinkAlertToTicketConsumer` qua Saga; nhiều Alert có thể link cùng 1 Ticket khi reuse (xem §8.3, §53) |
| `AcknowledgedByUserId` | `Guid?` | — | — |
| `AcknowledgedAt` | `DateTime?` | — | — |
| `ResolvedAt` | `DateTime?` | — | — |
| `DedupWindowEndUtc` | `DateTime` | NOT NULL | `DetectedAt + DedupWindowMinutes` — **không nullable**, mọi alert đều có dedup window khi tạo |

**Check constraint:** `BatteryAssetId IS NOT NULL OR SiteId IS NOT NULL` — alert phải có ít nhất 1 chủ thể.

**Composite index:** `(BatteryAssetId, AnomalyType, Status, DedupWindowEndUtc) WHERE BatteryAssetId IS NOT NULL` cho dedup query per-pin.
**Composite index:** `(SiteId, AnomalyType, Status, DedupWindowEndUtc) WHERE SiteId IS NOT NULL` cho dedup query per-site.

#### 1.3.6. Enum values
```csharp
public enum BatteryStatusEnum { Active = 1, Inactive = 2, Decommissioned = 3 }
public enum WarrantyStatusEnum { Active = 1, Expired = 2, Void = 3 }
public enum BatteryChemistryEnum { LiFePO4 = 1, NMC = 2, NCA = 3, LCO = 4 }

// Sensor reading context
public enum ChargingStateEnum
{
    Idle = 1, Charging = 2, Discharging = 3, Float = 4, Bypass = 5
}

// Anomaly classification - mở rộng 7 → 15 giá trị
// Cập nhật Sprint 5B: rearrange wire values, site-level (9-11) trước pin-level extended (12-13) — match implementation hiện tại.
public enum AnomalyTypeEnum {
    // Pin-level baseline (1-7)
    Overheat = 1, Overvoltage = 2, Undervoltage = 3,
    LowSoc = 4, RapidDischarge = 5, AbnormalCharging = 6, DeviceOffline = 7,
    // Pin-level degradation (8)
    SohDegradation = 8,
    // Site-level ambient (9-11)
    HighAmbientTemp = 9,
    HighHumidity = 10,
    HighTempHumidityCombo = 11,
    // Pin-level Tier 2 (12-13)
    HighInternalResistance = 12,
    CellImbalance = 13,
    // Site-level incident (14)
    EnvironmentalIncident = 14,
    // Cross-source validation (15) - B10
    SensorMismatch = 15   // BMS reading vs IoT reading lệch quá ngưỡng
}

// Nguồn đo của SensorReading (B9) - phân biệt BMS vs IoT edge device
public enum SensorReadingSourceTypeEnum {
    Bms = 1,         // Từ BMS gắn trực tiếp trong pack (qua RS485/Modbus)
    IotGateway = 2,  // Từ IoT edge device (ESP32-S3 + sensor ngoài INA226/DS18B20); tên enum giữ legacy "Gateway"
    External = 3     // Manual import, third-party feed
}

public enum AlertSeverityEnum { Info = 1, Warning = 2, Critical = 3 }
public enum AlertStatusEnum { Open = 1, Acknowledged = 2, Merged = 3, Resolved = 4 }

// Ambient reading source - phân biệt từ IoT thật vs Weather API
public enum AmbientReadingSourceEnum { IotSensor = 1, WeatherApi = 2 }

// Environmental incident (smoke, fire, gas leak, flood, ...)
// Cập nhật Sprint 5B: tên enum + loại incident mở rộng.
public enum EnvironmentalIncidentTypeEnum
{
    Smoke = 1,
    FireDetected = 2,
    GasLeak = 3,
    Flood = 4,
    OverheatHazard = 5,
    Other = 99
}
// Severity tái dùng AlertSeverityEnum (Info/Warning/Critical) — không có IncidentSeverityEnum riêng.
public enum EnvironmentalIncidentStatusEnum
{
    Open = 1,             // mới phát hiện, chưa ack (tên thay 'Detected' để đồng bộ AlertStatusEnum)
    Acknowledged = 2,     // staff/manager đã thấy
    Resolved = 3,         // xử lý xong
    FalseAlarm = 4        // không phải sự cố thật
}
```

#### 1.3.7. `AmbientReading` (KHÔNG kế thừa `AuditableEntity` — time-series append-only)

Chuỗi đo định kỳ điều kiện môi trường tại Site. Có thể đến từ cảm biến IoT thật hoặc từ Weather API (OpenMeteo).

| Field | Type | Constraint | Index | Note |
|-------|------|-----------|-------|------|
| `Time` | `DateTime` | NOT NULL | TimescaleDB hypertable column | UTC |
| `SiteId` | `Guid` | NOT NULL, FK → Site.Id | btree composite | Bắt buộc |
| `AmbientTemperature` | `decimal(5,2)` | NOT NULL | — | °C — nhiệt độ MÔI TRƯỜNG (≠ Temperature của pin) |
| `Humidity` | `decimal(5,2)?` | nullable, 0–100 | — | % RH |
| `SolarIrradiance` | `decimal(8,2)?` | nullable, ≥ 0 | — | W/m² (`shortwave_radiation` từ OpenMeteo hoặc pyranometer) |
| `Source` | `AmbientReadingSourceEnum` | NOT NULL | btree | 1=IotSensor, 2=WeatherApi |
| `SourceDeviceId` | `string(64)?` | nullable | — | DeviceId IoT, hoặc "openmeteo" |

> `BatteryGroupId` đã bỏ — `BatteryGroup` entity deferred (xem §31). Ambient query chỉ scope theo Site.

**PK composite:** `(Time, SiteId)` (giống `sensor_readings`).
**Hypertable interval:** 7 day chunks.
**Index:** `(SiteId, Time DESC)`.
**Retention:** 90 ngày raw, 1 năm 1h-aggregate (Sprint sau).

**Query rule cho consumer (AnomalyDetector):**
Để lấy ambient cho 1 BatteryAsset → tra theo `SiteId` của asset → lấy latest reading của Site trong N phút.

#### 1.3.8. `AmbientThresholdConfig` (per Site, kế thừa `AuditableEntity`)

Tách riêng khỏi `ThresholdConfig` (vốn per BatteryType) vì threshold môi trường là đặc tính của địa điểm.

**Cập nhật Sprint 5B:** schema đổi sang Warning/Critical split (đồng bộ với pattern `ThresholdConfig`) — `AmbientTempMax/Min/HumidityMax` cũ replace bằng `HighAmbientTempWarning/Critical` + `HighHumidityWarning/Critical`. `IsActive` đổi tên thành `Enabled`. `EffectiveFromUtc` đã bỏ vì threshold không version (mọi update đè trực tiếp).

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `SiteId` | `Guid` | FK, NOT NULL | One-active-config per site |
| `HighAmbientTempWarning` | `decimal(5,2)?` | nullable | °C — vượt → HighAmbientTemp Warning |
| `HighAmbientTempCritical` | `decimal(5,2)?` | nullable | °C — vượt → HighAmbientTemp Critical |
| `HighHumidityWarning` | `decimal(5,2)?` | nullable, 0–100 | %RH — vượt → HighHumidity Warning |
| `HighHumidityCritical` | `decimal(5,2)?` | nullable, 0–100 | %RH — vượt → HighHumidity Critical |
| `ComboTempThreshold` | `decimal(5,2)?` | nullable | °C — trigger COMBO nếu cả 2 vượt |
| `ComboHumidityThreshold` | `decimal(5,2)?` | nullable, 0–100 | %RH — pair với `ComboTempThreshold` |
| `Enabled` | `bool` | NOT NULL default true | Tắt config cho site này nếu false |

**Unique:** `(SiteId) WHERE Enabled = true AND IsDeleted = false`.

**Validation:**
- Nếu cả `HighAmbientTempWarning` và `HighAmbientTempCritical` không null: `Warning < Critical`.
- Nếu cả `HighHumidityWarning` và `HighHumidityCritical` không null: `Warning < Critical`.
- Nếu set combo: cả `ComboTempThreshold` và `ComboHumidityThreshold` đều phải có giá trị.

#### 1.3.9. `EnvironmentalIncident` (event-driven, kế thừa `AuditableEntity`)

Sự kiện an toàn (smoke/fire/gas/flood/...) với lifecycle Open → Resolved. KHÔNG phải time-series (mỗi event = 1 record với start/end time).

**Cập nhật Sprint 5B:** đổi tên `IncidentTypeEnum` → `EnvironmentalIncidentTypeEnum` + mở rộng từ 2 → 6 loại (xem §1.3.6). `Status` initial state đổi tên `Detected` → `Open` để đồng bộ với `AlertStatusEnum`. Severity tái dùng `AlertSeverityEnum` (Info/Warning/Critical) thay vì có `IncidentSeverityEnum` riêng.

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `SiteId` | `Guid` | FK, NOT NULL | btree |
| `IncidentType` | `EnvironmentalIncidentTypeEnum` | NOT NULL | 1=Smoke, 2=FireDetected, 3=GasLeak, 4=Flood, 5=OverheatHazard, 99=Other |
| `Severity` | `AlertSeverityEnum` | NOT NULL default `Critical` | 1=Info, 2=Warning, 3=Critical (tái dùng) |
| `Status` | `EnvironmentalIncidentStatusEnum` | NOT NULL default `Open` | 1=Open, 2=Acknowledged, 3=Resolved, 4=FalseAlarm |
| `DetectedAt` | `DateTime` | NOT NULL | UTC |
| `ReportedBy` | `string?` | nullable | User report (text — có thể là username hoặc system tag) |
| `AcknowledgedAt` | `DateTime?` | — | — |
| `AcknowledgedBy` | `Guid?` | — | UserId từ AuthService |
| `ResolvedAt` | `DateTime?` | — | — |
| `ResolvedBy` | `Guid?` | — | UserId từ AuthService |
| `ResolutionNote` | `string?` | nullable | Note khi đóng incident |
| `FalseAlarmAt` | `DateTime?` | — | Thời điểm Manager đánh dấu false alarm |
| `FalseAlarmBy` | `Guid?` | — | UserId đánh dấu |
| `FalseAlarmReason` | `string?` | nullable | Lý do mark false alarm (audit) |
| `Notes` | `string?` | nullable | Note bổ sung (vị trí cảm biến, context) |

> `BatteryGroupId` và `SourceDeviceId` đã bỏ — `BatteryGroup` deferred (xem §31). Nếu cần track device source thì dùng `Notes`/`ReportedBy`.

**Index:** `(SiteId, Status, DetectedAt DESC)`, `(IncidentType, Status)`.

**Flow event-driven:**
1. IoT cảm biến phát hiện → POST `/api/environmental-incidents` (ApiKey).
2. Handler tạo record với `Status=Detected`.
3. Handler tạo Alert kèm theo (`SiteId`, `EnvironmentalIncidentId`, `AnomalyType=EnvironmentalIncident`).
4. Publish `EnvironmentalIncidentDetectedEvent` → NotificationService push notification Critical.
5. Staff/Manager gọi `PATCH /{id}/acknowledge` → `Status=Acknowledged`, `AcknowledgedAt`.
6. Khi xử lý xong: `PATCH /{id}/resolve` → `Status=Resolved`, đóng Alert liên kết.
7. Nếu false-positive: `PATCH /{id}/false-alarm` → `Status=FalseAlarm`, đóng Alert.

### 1.4. CQRS — Command catalog đầy đủ

#### Commands & response wrapper

| Command | Payload chính | Auth | Response |
|---------|---------------|------|----------|
| `BatteryAssetCreateCommand` | SerialNumber, BatteryTypeId, CustomerId, InstallDate, ... | Admin | `BatteryAssetCreateResponse : CommonResponse<BatteryAssetDto>` |
| `BatteryAssetUpdateCommand` | Id, ... | Admin | `BatteryAssetUpdateResponse : CommonResponse<BatteryAssetDto>` |
| `BatteryAssetDeleteCommand` | Id | Admin | `CommonResponse<object>` |
| `BatteryAssetRestoreCommand` | Id | Admin | — |
| `BatteryAssetTransferOwnerCommand` | Id, NewCustomerId, Reason | Admin | — |
| `BatteryTypeCreateCommand` | Name, Manufacturer, Capacity, Voltage, Chemistry, MaxCycle | Admin | — |
| `BatteryTypeUpdateCommand` | Id, ... | Admin | — |
| `BatteryTypeDeleteCommand` | Id | Admin | — *(409 nếu còn BatteryAsset liên kết — không cascade)* |
| `ThresholdConfigUpsertCommand` | BatteryTypeId, all threshold values, EffectiveFromUtc | Admin | — |
| `SensorReadingBatchIngestCommand` | List<SensorReadingItem> (BatteryAssetId, Time, V, I, T, SOC, SOH?, ChargingState?, IR?, CellDelta?, BmsErrorCode?) — **tối đa 1000 items/request** | ApiKey (`SensorIngest`) | `CommonResponse<BatchIngestResult>` |
| `AlertAcknowledgeCommand` | Id, Note? | Customer (own), Staff | — |
| `AlertResolveCommand` | Id, ResolutionNote | Staff, Manager | — |
| `AmbientReadingBatchIngestCommand` | List<AmbientReadingItem> (SiteId, Time, AmbientTemp, Humidity?, SolarIrradiance?, BatteryGroupId?) | ApiKey (`EnvironmentalIngest`) | `CommonResponse<BatchIngestResult>` |
| `UpsertAmbientThresholdConfigCommand` | SiteId, TempMax?, TempMin?, HumidityMax?, ComboTemp?, ComboHumidity?, EffectiveFromUtc | Admin | `CommonResponse<AmbientThresholdConfigDto>` |
| `ReportEnvironmentalIncidentCommand` | SiteId, BatteryGroupId?, IncidentType, Severity, DetectedAt, Description?, SourceDeviceId? | ApiKey (`EnvironmentalIngest`) | `CommonResponse<EnvironmentalIncidentDto>` |
| `AcknowledgeEnvironmentalIncidentCommand` | Id | Admin, Manager, Staff | — |
| `ResolveEnvironmentalIncidentCommand` | Id, ResolutionNote? | Admin, Manager, Staff | — |
| `MarkFalseAlarmEnvironmentalIncidentCommand` | Id, Reason | Admin, Manager | — |

#### Sample command class

```csharp
public class BatteryAssetCreateCommand
    : IRequest<BatteryAssetCreateResponse>,
      IValidatable<BatteryAssetCreateResponse>
{
    public string SerialNumber { get; set; } = string.Empty;
    public Guid BatteryTypeId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime InstallDate { get; set; }
    public DateTime? WarrantyEndDate { get; set; }
    public string? Location { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? Notes { get; set; }

    public Task<BatteryAssetCreateResponse> ValidateAsync()
    {
        var r = new BatteryAssetCreateResponse();

        if (string.IsNullOrWhiteSpace(SerialNumber))
            r.ListErrors.Add(new Errors { Field = nameof(SerialNumber), Detail = "Required" });
        else if (!Regex.IsMatch(SerialNumber, "^[A-Z0-9-]+$"))
            r.ListErrors.Add(new Errors { Field = nameof(SerialNumber), Detail = "Invalid format" });
        else if (SerialNumber.Length is < 5 or > 64)
            r.ListErrors.Add(new Errors { Field = nameof(SerialNumber), Detail = "Length 5-64" });

        if (BatteryTypeId == Guid.Empty)
            r.ListErrors.Add(new Errors { Field = nameof(BatteryTypeId), Detail = "Required" });

        if (CustomerId == Guid.Empty)
            r.ListErrors.Add(new Errors { Field = nameof(CustomerId), Detail = "Required" });

        if (InstallDate == default)
            r.ListErrors.Add(new Errors { Field = nameof(InstallDate), Detail = "Required" });
        else if (InstallDate > DateTime.UtcNow)
            r.ListErrors.Add(new Errors { Field = nameof(InstallDate), Detail = "Must be in past" });
        else if (InstallDate < DateTime.UtcNow.AddYears(-5))
            r.ListErrors.Add(new Errors { Field = nameof(InstallDate), Detail = "Too old (max 5 years)" });

        if (WarrantyEndDate.HasValue && WarrantyEndDate <= InstallDate)
            r.ListErrors.Add(new Errors { Field = nameof(WarrantyEndDate), Detail = "Must be after install date" });

        if (r.ListErrors.Count > 0) r.IsSuccess = false;
        return Task.FromResult(r);
    }
}
```

### 1.5. Query catalog

| Query | Params | Auth | Cache strategy |
|-------|--------|------|----------------|
| `BatteryAssetGetListQuery` | Pagination + status + customerId + batteryTypeId + search | Admin/Manager | None |
| `BatteryAssetGetByIdQuery` | Id | Admin/Manager (any) — Customer (own) | Redis 60s |
| `BatteryAssetRealtimeQuery` | Id | Customer (own) — Staff | No cache (realtime) |
| `MyBatteryAssetsQuery` | (CustomerId từ JWT) | Customer | Redis 30s |
| `SensorReadingGetHistoryQuery` | AssetId, From, To, Granularity (1m/1h/1d) | Customer (own) — Staff/Manager | Redis 60s for >1h granularity |
| `SensorReadingGetLatestQuery` | AssetId | — | No cache |
| `AlertGetListQuery` | Pagination + severity + status + assetId + dateRange | Customer (own assets) — Manager/Staff | None |
| `AlertGetByIdQuery` | Id | — | Redis 60s |
| `ActiveAlertsByAssetQuery` | AssetId | — | Redis 30s |
| `BatteryDashboardStatsQuery` | (none — admin/manager view) | Admin/Manager | Redis 60s |
| `ThresholdConfigGetByTypeQuery` | BatteryTypeId | Admin/Manager | Redis 600s |
| `AmbientReadingHistoryQuery` | SiteId, From, To, BatteryGroupId? | Admin/Manager/Staff/Customer (own site) | Redis 60s |
| `AmbientReadingLatestQuery` | SiteId, BatteryGroupId? | — same — | Redis 30s |
| `AmbientThresholdConfigBySiteQuery` | SiteId | Admin/Manager | Redis 600s |
| `AmbientThresholdConfigGetListQuery` | Pagination + SiteId? + IsActive? | Admin/Manager | None |
| `EnvironmentalIncidentGetListQuery` | Pagination + SiteId? + Type? + Status? + DateRange | Admin/Manager/Staff/Customer (own site) | None |
| `EnvironmentalIncidentGetByIdQuery` | Id | — same — | Redis 60s |
| `ActiveEnvironmentalIncidentsBySiteQuery` | SiteId | — same — | Redis 30s |

### 1.6. Background services — chi tiết

#### `ThresholdCheckBackgroundService`
```csharp
// Pseudo-code
while (!ct.IsCancellationRequested) {
    var since = DateTime.UtcNow.AddSeconds(-_options.ScanIntervalSeconds * 2);
    var readings = await _uow.SensorReadings
        .GetAllAsync()
        .Where(r => r.Time >= since)
        .Include(r => r.BatteryAsset)
        .ThenInclude(a => a.BatteryType)
        .ToListAsync(ct);

    foreach (var reading in readings) {
        var threshold = await _thresholdCache.GetForType(reading.BatteryAsset.BatteryTypeId);
        var anomalies = _detector.Detect(reading, threshold);
        foreach (var anomaly in anomalies) {
            await _mediator.Send(new AlertCreateCommand {
                BatteryAssetId = reading.BatteryAssetId,
                AnomalyType = anomaly.Type,
                Severity = anomaly.Severity,
                ThresholdValue = anomaly.Threshold,
                ActualValue = anomaly.Actual,
                Unit = anomaly.Unit,
                DetectedAt = reading.Time
            }, ct);
        }
    }
    await Task.Delay(TimeSpan.FromSeconds(_options.ScanIntervalSeconds), ct);
}
```

**Config:**
- `ScanIntervalSeconds`: default 30.
- `DedupWindowMinutes`: default 30.
- `CriticalAutoCreateTicket`: default true.

#### `AlertEscalationBackgroundService`
- Mỗi 1 phút: query Alert `Severity=Critical AND Status=Open AND DetectedAt < now - 5min`.
- Không publish lại `BatteryAnomalyDetectedEvent`, vì event này đã start Saga ngay khi Critical Alert
  được tạo. Nếu Alert vẫn chưa được ack sau 5 phút, publish contract riêng
  `BatteryAlertEscalationRequestedEvent` cho NotificationService/Manager escalation; TicketService Saga
  không subscribe contract này.

#### `AlertAutoResolveBackgroundService`
- Mỗi 5 phút: nếu Alert có `Status=Open` và `AnomalyType` không còn vượt ngưỡng trong N phút gần nhất → auto-resolve.

#### `OutboxRelayBackgroundService`
- Mỗi 5 giây: scan `OutboxMessage` `IsProcessed=false`, publish lên RabbitMQ, mark processed.

#### `WeatherSyncBackgroundService`

Pull dữ liệu thời tiết từ OpenMeteo cho mỗi Site (lat/lon đã set), insert vào `AmbientReading` với `Source=WeatherApi`.

```csharp
// Pseudo-code
public class WeatherSyncBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;   // KHÔNG inject UoW (Scoped) trực tiếp
    private readonly WeatherSyncOptions _options;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IBatteryUnitOfWork>();
            var weatherClient = scope.ServiceProvider.GetRequiredService<IOpenMeteoClient>();

            var sites = await uow.Sites.GetAllAsync()
                .Where(s => !s.IsDeleted && s.Status == SiteStatusEnum.Active
                    && s.Latitude != null && s.Longitude != null)
                .ToListAsync(ct);

            foreach (var site in sites)
            {
                // Dedup: skip nếu reading WeatherApi gần nhất < DedupMinutes
                var cutoff = DateTime.UtcNow.AddMinutes(-_options.DedupMinutes);
                var hasRecent = await uow.AmbientReadings.GetAllAsync()
                    .AnyAsync(r => r.SiteId == site.Id
                                && r.Source == AmbientReadingSourceEnum.WeatherApi
                                && r.Time >= cutoff, ct);
                if (hasRecent) continue;

                try
                {
                    var snapshot = await weatherClient.GetCurrentAsync(site.Latitude!.Value, site.Longitude!.Value, ct);
                    if (snapshot is null) continue;

                    await uow.AmbientReadings.AddAsync(new AmbientReading
                    {
                        Time = snapshot.ObservedAtUtc,
                        SiteId = site.Id,
                        AmbientTemperature = snapshot.Temperature,
                        Humidity = snapshot.Humidity,
                        SolarIrradiance = snapshot.ShortwaveRadiation,
                        Source = AmbientReadingSourceEnum.WeatherApi,
                        SourceDeviceId = "openmeteo"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Weather sync failed for site {SiteId}", site.Id);
                    // Không throw — 1 site fail không chặn site khác
                }
            }

            await uow.SaveChangesAsync(ct);
            await Task.Delay(TimeSpan.FromMinutes(_options.SyncIntervalMinutes), ct);
        }
    }
}
```

**Config (`Weather:` section trong appsettings):**
- `OpenMeteoBaseUrl`: `https://api.open-meteo.com/v1/forecast`.
- `SyncIntervalMinutes`: default 15.
- `DedupMinutes`: default 10 — site đã có reading WeatherApi trong N phút → skip.
- `TimeoutSeconds`: 10 — HTTP timeout.

**Rate limit:** OpenMeteo free tier 10,000 calls/day. 1 site/15min = 96 calls/day → 100 sites OK.

#### `ThresholdAnomalyDetector` (extend cho 15 anomaly types — xem §1.3.6)

Đã có trong Sprint 3 plan. Update logic:

| Anomaly | Input source | Threshold source | Severity quy ước |
|---------|-------------|------------------|------------------|
| `Overheat` | SensorReading.Temperature | ThresholdConfig.TemperatureMax | Critical nếu > +5°C ngưỡng, ngược lại Warning |
| `Overvoltage` / `Undervoltage` | Voltage | VoltageMax / VoltageMin | Critical |
| `LowSoc` | SocPercent | SocCritical / SocWarning | Critical / Warning |
| `RapidDischarge` / `AbnormalCharging` | Current | CurrentMaxDischarge / CurrentMaxCharge | Critical |
| `DeviceOffline` | LastSensorReadingAt | > 10 phút không có reading | Warning |
| `SohDegradation` | SensorReading.SohPercent | SohWarning / SohCritical | Critical / Warning |
| `HighInternalResistance` | SensorReading.InternalResistanceMilliohm | ThresholdConfig.InternalResistanceMaxMilliohm | Warning |
| `CellImbalance` | SensorReading.CellVoltageDeltaMv | ThresholdConfig.CellVoltageDeltaMaxMv | Warning |
| `HighAmbientTemp` | AmbientReading.AmbientTemperature | AmbientThresholdConfig.AmbientTempMax | Warning |
| `HighHumidity` | AmbientReading.Humidity | AmbientThresholdConfig.HumidityMax | Warning |
| `HighTempHumidityCombo` | Cả 2 cùng vượt ngưỡng combo | HumidityComboTempMax + HumidityComboHumidityMax | High (severity 2 = nguy hiểm hơn Warning) |
| `EnvironmentalIncident` | Trigger từ `EnvironmentalIncident.Detected` event | n/a | Critical (smoke/water đều Critical) |
| `SensorMismatch` | So sánh reading BMS vs IoT (xem §1.6.6) | Hard-coded delta (0.5V hoặc 5°C) | Warning |

#### 1.6.5. Noise Suppression Logic (B1) — phân biệt Noise vs Bất thường thật

**Bối cảnh:** Cảm biến IoT có thể có nhiễu (electrical spike, EMI, lỗi ADC tạm thời) → breach threshold lẻ tẻ nhưng KHÔNG phải bất thường thật. Nếu mỗi breach đều tạo Alert → spam false-positive.

**Quy tắc nghiệp vụ:**
- Một breach ĐƠN LẺ trong cửa sổ `NoiseSuppressionWindowHours` (default 24h) = **Noise** → KHÔNG tạo Alert, chỉ log internal.
- ≥ `NoiseSuppressionCount` (default 5) breach cùng `AnomalyType` cùng asset trong cửa sổ = **Bất thường thật** → tạo Alert thật + publish event.

**Logic flow trong `ThresholdAnomalyDetector`:**

```csharp
// Pseudo-code
foreach (var reading in batch)
{
    foreach (var rule in DetectAllAnomalies(reading))   // 15 rule check (xem §1.3.6)
    {
        // Lookup ThresholdConfig của BatteryType
        if (!config.NoiseSuppressionEnabled)
        {
            await CreateAlertImmediate(rule);  // bypass — alert tức thì
            continue;
        }

        // Đếm breach trong window
        var windowStart = DateTime.UtcNow.AddHours(-config.NoiseSuppressionWindowHours);
        var breachCount = await _unitOfWork.NoiseBreachEvents.GetAllAsync()
            .Where(x => !x.IsDeleted
                && x.BatteryAssetId == reading.BatteryAssetId
                && x.AnomalyType == rule.AnomalyType
                && x.OccurredAt >= windowStart)
            .CountAsync();

        // Log breach event mới (luôn luôn lưu để đếm)
        await _unitOfWork.NoiseBreachEvents.AddAsync(new NoiseBreachEvent
        {
            BatteryAssetId = reading.BatteryAssetId,
            AnomalyType = rule.AnomalyType,
            ActualValue = rule.ActualValue,
            ThresholdValue = rule.ThresholdValue,
            OccurredAt = DateTime.UtcNow
        });

        // Quyết định escalate
        if (breachCount + 1 >= config.NoiseSuppressionCount)
        {
            await CreateAlertImmediate(rule);  // Đạt ngưỡng → tạo Alert
            // Optional: mark các NoiseBreachEvent cũ là "promoted" để audit
        }
        // else: chỉ log breach, không tạo Alert
    }
}
```

**New entity `NoiseBreachEvent`** (KHÔNG kế thừa `AuditableEntity` — append-only time-series):

| Field | Type | Constraint | Index |
|-------|------|-----------|-------|
| `Id` | Guid | PK | clustered |
| `BatteryAssetId` | Guid | NOT NULL, FK | btree composite |
| `AnomalyType` | AnomalyTypeEnum | NOT NULL | btree composite |
| `ActualValue` | decimal(10,4) | NOT NULL | — |
| `ThresholdValue` | decimal(10,4) | NOT NULL | — |
| `OccurredAt` | DateTime | NOT NULL | btree DESC |
| `PromotedToAlertId` | Guid? | nullable | — |
| `SourceType` | SensorReadingSourceTypeEnum | NOT NULL | — |

**Composite index:** `(BatteryAssetId, AnomalyType, OccurredAt DESC)` cho query đếm breach trong window.
**Retention:** xóa sau 7 ngày (cron). Nếu được promoted → giữ vĩnh viễn (audit).

**Hai cấp lọc noise:**

| Cấp | Loại noise | Vị trí lọc | Ngưỡng |
|-----|-----------|------------|--------|
| **Cấp 1: Hardware noise** | Voltage = 9999V, Current âm bất thường, timestamp future | `SensorReadingBatchIngestCommandHandler` (trước khi lưu) | Hard-coded outlier bounds (đã có trong Sprint IoT-1 §52.4) |
| **Cấp 2: Frequency-based** | Threshold breach lẻ tẻ < 5 lần/24h | `ThresholdAnomalyDetector` (sau khi lưu sensor reading) | Configurable per BatteryType qua `ThresholdConfig` |

> Cấp 1 ngăn data rác vào DB. Cấp 2 ngăn alert spam khi data hợp lệ nhưng intermittent.

**Critical anomaly bypass noise filter:**
- `EnvironmentalIncident` (smoke, water) — `NoiseSuppressionEnabled = false` mặc định
- `Overheat` với `ActualValue > TemperatureMax + 10°C` — bypass (an toàn cao hơn noise tradeoff)

#### 1.6.6. Cross-source validation (B9 + B10)

Khi 1 `BatteryAsset` có cả BMS và IoT Gateway cùng đẩy reading:

```csharp
// Logic chạy trong ThresholdCheckBackgroundService mỗi 30s
foreach (var asset in assetsWithMultipleSources)
{
    var latestBms = await _unitOfWork.SensorReadings.GetAllAsync()
        .Where(x => x.BatteryAssetId == asset.Id
            && x.SourceType == SensorReadingSourceTypeEnum.Bms
            && x.Time >= DateTime.UtcNow.AddSeconds(-60))
        .OrderByDescending(x => x.Time)
        .FirstOrDefaultAsync();

    var latestIot = await _unitOfWork.SensorReadings.GetAllAsync()
        .Where(x => x.BatteryAssetId == asset.Id
            && x.SourceType == SensorReadingSourceTypeEnum.IotGateway
            && x.Time >= DateTime.UtcNow.AddSeconds(-60))
        .OrderByDescending(x => x.Time)
        .FirstOrDefaultAsync();

    if (latestBms == null || latestIot == null) continue;  // không đủ 2 nguồn

    if (Math.Abs(latestBms.Voltage - latestIot.Voltage) > 0.5m
        || Math.Abs(latestBms.Temperature - latestIot.Temperature) > 5m)
    {
        await _detector.RaiseAnomalyAsync(new AnomalyContext
        {
            BatteryAssetId = asset.Id,
            AnomalyType = AnomalyTypeEnum.SensorMismatch,
            Severity = AlertSeverityEnum.Warning,
            ActualValue = latestIot.Voltage,
            ThresholdValue = latestBms.Voltage,
            Unit = "V",
            DetectedAt = DateTime.UtcNow
        });
    }
}
```

> SensorMismatch CŨNG đi qua noise suppression (nếu lệch lẻ tẻ 1 lần do timing → không alert). 5 lần lệch trong 24h mới tạo Alert.

### 1.7. Integration events

#### Publish
```csharp
public record BatteryAssetCreatedEvent : IntegrationEvent {
    public Guid AssetId { get; init; }
    public Guid CustomerId { get; init; }
    public string SerialNumber { get; init; } = string.Empty;
    public Guid BatteryTypeId { get; init; }
}

public record BatteryAnomalyDetectedEvent(
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,  // denormalize for Ticket/Notification services
    int AnomalyType,           // wire value = integer của AnomalyTypeEnum §1.3.6; contract DTO không reference Domain enum type (assembly isolation), nhưng giá trị int phải khớp
    int Severity,              // wire value = integer của AlertSeverityEnum §1.3.6 (1=Info, 2=Warning, 3=Critical)
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt
) : IntegrationEvent;

// Chỉ dùng cho Alert Critical vẫn chưa ack sau escalation window.
// TicketService/Saga không subscribe event này.
public record BatteryAlertEscalationRequestedEvent(
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    int Severity,
    DateTime DetectedAt,
    DateTime EscalatedAtUtc
) : IntegrationEvent;

public record BatteryAssetTransferredEvent : IntegrationEvent {
    public Guid AssetId { get; init; }
    public Guid OldCustomerId { get; init; }
    public Guid NewCustomerId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

// Tách khỏi BatteryAnomalyDetectedEvent vì payload khác (site-level, không có assetSerial).
// NotificationService consume cả 2 nhưng template + routing khác.
public record EnvironmentalIncidentDetectedEvent : IntegrationEvent {
    public Guid IncidentId { get; init; }
    public Guid SiteId { get; init; }
    public Guid? BatteryGroupId { get; init; }
    public Guid CustomerId { get; init; }           // chủ Site, lookup khi publish
    public IncidentTypeEnum IncidentType { get; init; }
    public IncidentSeverityEnum Severity { get; init; }
    public DateTime DetectedAt { get; init; }
    public string SiteName { get; init; } = string.Empty;   // denormalize cho notification template
    public string? Description { get; init; }
}

public record EnvironmentalIncidentResolvedEvent : IntegrationEvent {
    public Guid IncidentId { get; init; }
    public Guid SiteId { get; init; }
    public DateTime ResolvedAt { get; init; }
    public Guid ResolvedByUserId { get; init; }
    public bool WasFalseAlarm { get; init; }
}

// IoT device chuyển Offline (publish bởi LWT handler hoặc IotDeviceOfflineDetectionBackgroundService — §52.6).
// NotificationService consume để báo Customer/Staff; payload denormalize battery/site cho template.
public record IotDeviceWentOfflineEvent : IntegrationEvent {
    public Guid DeviceId { get; init; }
    public string DeviceCode { get; init; } = string.Empty;
    public Guid? SiteId { get; init; }
    public string? SiteName { get; init; }                  // denormalize cho template
    public Guid[] AffectedBatteryAssetIds { get; init; } = Array.Empty<Guid>();
    public DateTime LastSeenAt { get; init; }
    public DateTime DetectedAt { get; init; }
}

// SharedContracts; BatteryService publish sau khi Alert.TicketId đã commit.
public record AlertLinkedToTicketEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId);

public record AlertLinkToTicketRejectedEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string ErrorCode,
    string Reason,
    bool IsRetryable);
```

#### Consume
- `AccountActivatedConsumer`: cache customer info nếu cần.
- `AccountDeletedConsumer`: soft delete asset hoặc transfer to "Inactive" placeholder.
- `AccountStatusChangedConsumer`: update local cache.
- `LinkAlertToTicketConsumer`: nhận Saga command, update `Alert.TicketId` idempotently,
  commit cùng `AlertLinkedToTicketEvent` qua Outbox; reject nếu Alert đã link Ticket khác.

### 1.8. REST API contract

#### Endpoint list đầy đủ
```
# BatteryAsset
POST   /api/battery-assets                            (Admin)
GET    /api/battery-assets?customerId=&status=&page=  (Admin/Manager)
GET    /api/battery-assets/{id}                       (Admin/Manager — Customer own)
PUT    /api/battery-assets/{id}                       (Admin)
DELETE /api/battery-assets/{id}                       (Admin)
PATCH  /api/battery-assets/{id}/restore               (Admin)
PUT    /api/battery-assets/{id}/transfer-owner        (Admin)
GET    /api/battery-assets/me                         (Customer — own list)
GET    /api/battery-assets/{id}/realtime              (Customer own — Staff/Manager)
GET    /api/battery-assets/{id}/history?from=&to=&granularity= (Customer own — Staff/Manager)
GET    /api/battery-assets/{id}/alerts                (— same auth as above —)

# BatteryType
POST   /api/battery-types                             (Admin)
GET    /api/battery-types                             (Admin/Manager)
GET    /api/battery-types/{id}                        (Admin/Manager)
PUT    /api/battery-types/{id}                        (Admin)
DELETE /api/battery-types/{id}                        (Admin)

# Threshold
GET    /api/thresholds                                (Admin/Manager)
GET    /api/thresholds/by-type/{batteryTypeId}        (Admin/Manager/internal)
PUT    /api/thresholds/by-type/{batteryTypeId}        (Admin) — upsert

# Sensor Reading
POST   /api/sensor-readings/batch                     (ApiKey `SensorIngest` — IoT gateway)
GET    /api/sensor-readings?assetId=&from=&to=        (Customer own — Staff/Manager)
GET    /api/sensor-readings/latest?assetId=           (— same —)

# Alert
GET    /api/alerts?severity=&status=&assetId=&siteId=&page=   (Customer own — Staff/Manager)
GET    /api/alerts/{id}                               (— same —)
PATCH  /api/alerts/{id}/acknowledge                   (Customer own — Staff)
PATCH  /api/alerts/{id}/resolve                       (Staff/Manager)

# Ambient Reading (NEW)
POST   /api/ambient-readings/batch                    (ApiKey `EnvironmentalIngest` — IoT)
GET    /api/ambient-readings?siteId=&from=&to=&batteryGroupId=  (— same auth as alerts —)
GET    /api/ambient-readings/latest?siteId=&batteryGroupId=     (— same —)

# Ambient Threshold (NEW)
GET    /api/ambient-thresholds                        (Admin/Manager)
GET    /api/ambient-thresholds/by-site/{siteId}       (Admin/Manager)
PUT    /api/ambient-thresholds/by-site/{siteId}       (Admin) — upsert

# Environmental Incident (NEW)
POST   /api/environmental-incidents                   (ApiKey `EnvironmentalIngest` — IoT cảm biến smoke/water)
GET    /api/environmental-incidents?siteId=&type=&status=&from=&to=&page=  (— same auth —)
GET    /api/environmental-incidents/{id}              (— same —)
POST   /api/environmental-incidents/{id}/acknowledge  (Admin/Manager/Staff)
POST   /api/environmental-incidents/{id}/resolve      (Admin/Manager/Staff)
POST   /api/environmental-incidents/{id}/false-alarm  (Admin/Manager)

# Dashboard
GET    /api/battery/dashboard/stats                   (Admin/Manager)

# Health
GET    /api/battery/health                            (Internal — for k8s probes)
```

**ApiKey policy update:**
- Tách thành 2 key trong `appsettings.json`:
  - `ApiKeys:SensorIngest` — chỉ cho `/api/sensor-readings/batch`
  - `ApiKeys:EnvironmentalIngest` — cho `/api/ambient-readings/batch` + `/api/environmental-incidents`
- Lý do: nếu IoT edge device smoke detector bị compromise, attacker không thể giả mạo sensor reading (và ngược lại). Mỗi key có scope giới hạn.

> **MVP global key vs production per-device key (§52):** 2 key global ở trên là **mức MVP (P0–P2)** để chạy nhanh với simulator. Từ Sprint IoT-1, production dùng **API key per-device** (hash, kèm `X-Device-Code`, scope `sensor.ingest`/`device.heartbeat`/`environmental.ingest` — §52.2) thay cho global key; backend chấp nhận **cả hai** trong giai đoạn chuyển tiếp (backward-compat, §52bis.3). MQTT (v2) dùng credential per-device riêng (§52.14). Global `SensorIngest`/`EnvironmentalIngest` chỉ nên giữ cho demo/simulator, không cấp cho device thật ngoài site.

**Batch limit & rate limit:**
- `POST /api/sensor-readings/batch`: tối đa **1000 readings/request** — vượt quá trả `400 isSuccess=false`. Rate limit đề xuất: **60 requests/minute/device**.
- Validation batch: `voltage >= 0`, temperature trong `-50..120°C`, `time` cho phép lệch tối đa **+5 phút** so với server UTC.

**POST endpoints response:**
- `POST /api/battery-assets`, `POST /api/battery-groups`, `POST /api/battery-types` dùng `Ok()` → HTTP **200**. Body `statusCode` cũng là **200**. FE/Mobile nên kiểm tra `isSuccess` thay vì HTTP status code.

**GET /api/battery-assets — default sort:**
- Mặc định sort `createdAt` **giảm dần**. Không hỗ trợ sort param động (không có `sortBy`/`isDescending` query param).

**DELETE /api/battery-groups/{id} và DELETE /api/battery-types/{id}:**
- Trả `409 isSuccess=false` nếu còn `BatteryAsset` liên kết. Không cascade soft-delete xuống assets.

#### Sample request/response

**POST /api/battery-assets**
```json
// Request
{
  "serialNumber": "BAT-2026-001",
  "batteryTypeId": "9c4d6f2e-...",
  "customerId": "7a2b1c8d-...",
  "installDate": "2026-01-15T00:00:00Z",
  "warrantyEndDate": "2031-01-15T00:00:00Z",
  "location": "Khu A, Solar Farm 1",
  "latitude": 10.776,
  "longitude": 106.701
}

// Response 200 OK
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Tạo tài sản pin thành công.",
  "listErrors": [],
  "data": {
    "id": "5e8f...",
    "serialNumber": "BAT-2026-001",
    "batteryType": { "id": "9c4d...", "name": "LiFePO4 12V 100Ah" },
    "customerId": "7a2b...",
    "status": 1,
    "warrantyStatus": 1,
    "installDate": "2026-01-15T00:00:00Z",
    "createdAt": "2026-05-12T08:30:00Z"
  }
}
```

**GET /api/battery-assets/{id}/realtime**
```json
{
  "isSuccess": true,
  "data": {
    "assetId": "5e8f...",
    "time": "2026-05-12T10:15:30Z",
    "voltage": 12.6,
    "current": -5.2,
    "temperature": 35.4,
    "socPercent": 78.5,
    "status": "Normal",
    "activeAlerts": 0
  }
}
```

### 1.9. Test catalog (BatteryService) — bắt buộc trước ship

#### Unit tests — core (pin)
- `BatteryAssetCreateCommandHandlerTests`: 6 cases (success, missing serial, duplicate serial, invalid type, customer not exist, install date future)
- `BatteryAssetCreateCommandValidationTests`: 8 cases (each field validation)
- `AlertCreateCommandHandlerTests`: 4 cases (new alert, dedup merge into existing, critical → publish event, info severity → no event)
- `AlertDeduplicationServiceTests`: 5 cases (within window same type → merge, outside window → new, different anomaly → new, status not Open → new, multiple recent → merge to most recent)
- `ThresholdAnomalyDetectorTests`: **15 cases** (1 per AnomalyTypeEnum value §1.3.6, gồm 7 baseline (Overheat/Overvoltage/Undervoltage/LowSoc/RapidDischarge/AbnormalCharging/DeviceOffline) + 8 extended (SohDegradation/HighInternalResistance/CellImbalance/HighAmbientTemp/HighHumidity/HighTempHumidityCombo/EnvironmentalIncident/SensorMismatch))
- `BatteryAssetGetListQueryHandlerTests`: filtering, paging, soft-delete exclusion

#### Unit tests — environmental + extended battery health
- `AmbientReadingBatchIngestCommandHandlerTests`: 4 cases (success, invalid site, dedup with WeatherApi source, mix IoT + API ok)
- `UpsertAmbientThresholdConfigCommandHandlerTests`: 5 cases (create new, update existing, invalid combo, min > max, missing site)
- `ReportEnvironmentalIncidentCommandHandlerTests`: 4 cases (success → alert created + event published, missing site, duplicate within 1 min same type → merge, critical severity → publish notification)
- `AcknowledgeEnvironmentalIncidentCommandHandlerTests`: 3 cases (success, already resolved, false alarm)
- `ResolveEnvironmentalIncidentCommandHandlerTests`: 3 cases (success closes linked alert, already false-alarm 409, missing user 401)
- `OpenMeteoClientTests`: 4 cases (success parse, 4xx error returns null, timeout returns null, malformed JSON returns null) — dùng `HttpMessageHandler` stub
- `WeatherSyncBackgroundServiceTests`: 4 cases (site with lat/lon → insert reading, site missing lat/lon → skip, dedup window → skip, OpenMeteo fail → continue next site)
- `SensorReadingNewFieldsValidationTests`: 5 cases (SOH out of range, IR ≤ 0, CellDelta < 0, BmsErrorCode too long, ChargingState invalid enum)

#### Integration tests (TestContainers postgres + timescaledb image)
- POST asset → query list returns it
- POST sensor batch → background scan detects anomaly → alert created → event published (assert via MassTransit TestHarness)
- DELETE asset → soft delete (IsDeleted=true), list excludes
- Auth: Customer A cannot GET asset of Customer B
- **NEW:** POST ambient batch → query latest returns insert
- **NEW:** POST sensor batch với SOH < threshold → detector tạo `SohDegradation` alert
- **NEW:** Ambient reading vượt cả temp + humidity combo → tạo alert `HighTempHumidityCombo` Severity=High
- **NEW:** POST `/api/environmental-incidents` (smoke) → record incident + alert Critical + publish `EnvironmentalIncidentDetectedEvent`
- **NEW:** PATCH `/false-alarm` đóng cả incident và alert liên kết
- **NEW:** Migration rollback bao gồm ambient_readings + ambient_threshold_configs + environmental_incidents

### 1.10. External integrations

#### OpenMeteo (weather data)

| Item | Value |
|------|-------|
| Base URL | `https://api.open-meteo.com/v1/forecast` |
| Auth | None (free tier) |
| Rate limit | 10,000 calls/day |
| Cost | Free |
| Variables used | `temperature_2m`, `relative_humidity_2m`, `shortwave_radiation` |

**Client interface (trong Application layer):**
```csharp
public interface IOpenMeteoClient
{
    Task<WeatherSnapshot?> GetCurrentAsync(decimal latitude, decimal longitude, CancellationToken ct);
}

public record WeatherSnapshot(
    DateTime ObservedAtUtc,
    decimal Temperature,                // °C
    decimal? Humidity,                  // % RH
    decimal? ShortwaveRadiation);       // W/m² ~ solar irradiance proxy
```

**Implementation:** `OpenMeteoClient` dùng `HttpClient` injected qua `IHttpClientFactory`. Polly retry policy 3 lần exponential backoff. Timeout 10s. Mọi error → log warning + return null (không throw để không chặn WeatherSync).

**Sample call:**
```
GET https://api.open-meteo.com/v1/forecast?latitude=10.776&longitude=106.701&current=temperature_2m,relative_humidity_2m,shortwave_radiation&timezone=UTC
```

**DI registration** (`ManageDependencyInjection.cs` BatteryService.Infrastructure):
```csharp
services.AddHttpClient<IOpenMeteoClient, OpenMeteoClient>(client =>
{
    client.BaseAddress = new Uri(configuration["Weather:OpenMeteoBaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddPolicyHandler(GetRetryPolicy());

services.AddHostedService<WeatherSyncBackgroundService>();
services.Configure<WeatherSyncOptions>(configuration.GetSection("Weather"));
```

---

## 2. TicketService — P0

### 2.1. Trách nhiệm
1. CRUD ticket với state machine 12+ trạng thái.
2. Quản lý SLA timer (start/pause/resume/breach) — BR-04.
3. Quản lý Activity timeline (BR-08).
4. Orchestrate `Alert–Ticket Saga`: nhận `BatteryAnomalyDetectedEvent`, tạo hoặc reuse Ticket theo BR-02, rồi yêu cầu BatteryService liên kết `Alert.TicketId`.
5. Manager approval workflow (BR-05).
6. Reopen policy 7 ngày (BR-06) + escalate khi ≥ 2 reopen (BR-07).
7. KnowledgeBase module (xem §4).
8. Maintenance log + attachment.
9. Comment với Internal/External visibility.

### 2.2. Cấu trúc thư mục
(tương tự BatteryService, tham khảo §1.2)
```
services/TicketService/
├── src/
│   ├── TicketService.Api/Controllers/
│   │   ├── TicketsController.cs
│   │   ├── TicketCommentsController.cs
│   │   ├── MaintenanceLogsController.cs
│   │   ├── KnowledgeBaseController.cs        ← §4
│   │   ├── ManagerWorkflowController.cs      ← queue, workload
│   │   ├── StaffWorkflowController.cs        ← my tickets
│   │   ├── ReportsController.cs              ← §5
│   │   └── HealthController.cs
│   ├── TicketService.Application/
│   │   ├── CQRS/Command/Ticket/...
│   │   ├── CQRS/Command/Comment/...
│   │   ├── CQRS/Command/MaintenanceLog/...
│   │   ├── CQRS/Command/KnowledgeBase/...
│   │   ├── CQRS/Query/...
│   │   ├── StateMachine/
│   │   │   ├── ITicketStateMachine.cs
│   │   │   ├── TicketStateMachine.cs
│   │   │   ├── TransitionRequest.cs
│   │   │   └── TransitionResult.cs
│   │   ├── Services/
│   │   │   ├── ISlaCalculator.cs
│   │   │   ├── SlaCalculator.cs
│   │   │   ├── IPriorityAdvisor.cs           ← gợi ý priority cho Manager
│   │   │   ├── PriorityAdvisor.cs
│   │   │   ├── IStaffAssignmentService.cs    ← workload + skill match
│   │   │   └── StaffAssignmentService.cs
│   │   └── Consumers/
│   │       ├── CreateTicketFromAlertConsumer.cs
│   │       ├── AccountActivatedConsumer.cs            ← upsert CustomerAccount/StaffAccount khi tài khoản kích hoạt
│   │       ├── AccountStatusChangedConsumer.cs        ← cập nhật Status (Active/Disabled/Locked) → suspend ticket nếu Customer
│   │       ├── AccountProfileUpdatedConsumer.cs       ← đồng bộ FullName/Email/Avatar
│   │       ├── StaffProfileUpdatedConsumer.cs         ← đồng bộ IsAvailable, MaxConcurrentTickets, EmployeeCode
│   │       └── StaffSkillsUpdatedConsumer.cs          ← đồng bộ SkillCodes (skill match khi assign)
│   ├── TicketService.Domain/Entities/
│   │   ├── Ticket.cs
│   │   ├── TicketActivity.cs
│   │   ├── TicketComment.cs
│   │   ├── MaintenanceLog.cs
│   │   ├── SlaTimer.cs
│   │   ├── SlaPauseEvent.cs
│   │   ├── TicketAttachment.cs
│   │   ├── KnowledgeBaseArticle.cs           ← §4
│   │   ├── CustomerAccount.cs                ← read-model cache từ AuthService (xem §2.7 Read-model)
│   │   ├── StaffAccount.cs                   ← read-model cache từ AuthService (xem §2.7 Read-model)
│   │   └── OutboxMessage.cs
│   ├── TicketService.Domain/Enums/
│   │   ├── TicketStatusEnum.cs
│   │   ├── TicketPriorityEnum.cs
│   │   ├── TicketCategoryEnum.cs
│   │   ├── TicketOriginEnum.cs
│   │   ├── EscalationReasonEnum.cs
│   │   ├── PauseReasonEnum.cs
│   │   ├── ActivityActionEnum.cs
│   │   ├── ActorRoleEnum.cs
│   │   ├── MaintenanceLogTypeEnum.cs
│   │   ├── SlaTimerStatusEnum.cs
│   │   └── KbArticleStatusEnum.cs
│   └── TicketService.Infrastructure/
│       ├── Persistence/...
│       ├── Sagas/
│       │   ├── AlertTicketSagaState.cs         ← persistence model, CorrelationId = AlertId
│       │   ├── AlertTicketSagaStateMachine.cs
│       │   └── AlertTicketSagaDefinition.cs
│       ├── BackgroundJobs/
│       │   ├── SlaTimerBackgroundService.cs
│       │   ├── AutoCloseBackgroundService.cs
│       │   ├── EscalationBackgroundService.cs
│       │   └── OutboxRelayBackgroundService.cs
│       └── Consumers/...
└── tests/...
```

### 2.3. Entity detail

#### 2.3.1. `Ticket` (kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `Id` | `Guid` | PK | — |
| `Code` | `string(20)` | NOT NULL, UNIQUE | "TKT-2605-0001" (auto-gen YYMM-NNNN reset hàng tháng) |
| `BatteryAssetId` | `Guid` | NOT NULL | BR-01 mandatory |
| `CustomerId` | `Guid` | NOT NULL | Owner |
| `AssignedStaffId` | `Guid?` | nullable | Set khi Manager assign |
| `Title` | `string(200)` | NOT NULL | — |
| `Description` | `string(4000)` | NOT NULL | — |
| `Category` | `TicketCategoryEnum` | NOT NULL | 1=Charging, 2=Overheat, 3=NoPower, 4=Performance, 5=Other |
| `Priority` | `TicketPriorityEnum?` | nullable until ASSIGNED | 1=P1Critical, 2=P2High, 3=P3Normal — **derived từ ImpactScope × UrgencyLevel** (xem §2.10) |
| `ImpactScope` | `ImpactScopeEnum?` | nullable until ASSIGNED | **B3** — 1=SingleAsset, 2=BatteryGroup, 3=Site, 4=MultiSite. Manager gán lúc triage |
| `UrgencyLevel` | `UrgencyLevelEnum?` | nullable until ASSIGNED | **B3** — 1=Low, 2=Medium, 3=High. Manager gán lúc triage |
| `Status` | `TicketStatusEnum` | NOT NULL default `NEW` | xem §2.4 |
| `Origin` | `TicketOriginEnum` | NOT NULL | 1=ManualByCustomer, 2=AutoFromAlert, 3=CreatedByStaff |
| `OriginAlertId` | `Guid?` | nullable, **unique filtered index** `WHERE origin_alert_id IS NOT NULL AND is_deleted = false` (Sprint 5B `AddAlertTicketSagaFoundation`) | Lưu Alert **đầu tiên** tạo Ticket. Reuse cho Alert mới KHÔNG ghi đè — quan hệ many-alerts-to-one-ticket nằm ở `Alert.TicketId` (xem §53.6, §53.8) |
| `ReopenCount` | `int` | NOT NULL default 0 | BR-07 escalate khi ≥ 2 |
| `ResolutionSummary` | `string(2000)?` | nullable | Staff điền khi mark RESOLVED |
| `ResolvedAt` | `DateTime?` | nullable | — |
| `ResolvedByStaffId` | `Guid?` | nullable | — |
| `ApprovedAt` | `DateTime?` | nullable | Manager approve |
| `ApprovedByManagerId` | `Guid?` | — | — |
| `RejectionReason` | `string(1000)?` | — | — |
| `ClosedAt` | `DateTime?` | — | — |
| `Rating` | `int?` (1–5) | — | Customer rate |
| `RatingComment` | `string(1000)?` | — | — |
| `RatedAt` | `DateTime?` | — | — |
| `EscalatedAt` | `DateTime?` | — | — |
| `EscalationReason` | `EscalationReasonEnum?` | — | 1=SkillGap, 2=PartsRequired, 3=SafetyConcern, 4=SlaBreach, 5=CustomerComplaint |
| `IsIncident` | `bool` | NOT NULL default false | Critical flag |

**Indexes:**
- `(CustomerId, Status, IsDeleted)` — Customer "my tickets"
- `(AssignedStaffId, Status)` — Staff "my queue"
- `(Status, Priority, CreatedAt)` — Manager queue
- `(BatteryAssetId, Status)` — dedup auto-create

#### 2.3.2. `SlaTimer` (one-to-one với Ticket)

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | PK |
| `TicketId` | `Guid` | FK, UNIQUE |
| `Priority` | `TicketPriorityEnum` | Snapshot lúc start, cố định |
| `StartedAt` | `DateTime` | Khi Status sang ASSIGNED |
| `DueAt` | `DateTime` | StartedAt + SLA hours, sẽ recalc khi resume từ pause |
| `OriginalDueAt` | `DateTime` | Snapshot lúc start, immutable |
| `TotalPausedMinutes` | `int` | Tổng pause |
| `CurrentPauseStartedAt` | `DateTime?` | Đang pause |
| `WarningSentAt` | `DateTime?` | Khi 80% — chỉ gửi 1 lần |
| `BreachAt` | `DateTime?` | Khi vượt DueAt |
| `Status` | `SlaTimerStatusEnum` | 1=Running, 2=Paused, 3=Met, 4=Breached |

**SLA hours mapping:**
- P1 Critical = 4
- P2 High = 24
- P3 Normal = 72

#### 2.3.3. `SlaPauseEvent` (audit pause/resume)

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | PK |
| `SlaTimerId` | `Guid` | FK |
| `Reason` | `PauseReasonEnum` | 1=WaitingCustomer, 2=WaitingParts, 3=WaitingOnsiteSchedule |
| `Note` | `string(500)` | free text |
| `PausedAt` | `DateTime` | — |
| `PausedByUserId` | `Guid` | — |
| `ResumedAt` | `DateTime?` | — |
| `ResumedByUserId` | `Guid?` | — |
| `DurationMinutes` | `int?` | computed when resumed |

#### 2.3.4. `TicketActivity` (BR-08)

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | — |
| `TicketId` | `Guid` | FK, indexed |
| `ActorUserId` | `Guid` | — |
| `ActorRole` | `ActorRoleEnum` | 1=Admin, 2=Manager, 3=Staff, 4=Customer, 5=System |
| `ActorDisplayName` | `string(200)` | denormalize |
| `Action` | `ActivityActionEnum` | xem enum bên dưới |
| `OldValue` | `string(2000)?` | JSON |
| `NewValue` | `string(2000)?` | JSON |
| `Reason` | `string(1000)?` | — |
| `CreatedAt` | `DateTime` | indexed DESC |

```csharp
public enum ActivityActionEnum {
    Created = 1, StatusChanged = 2, PriorityAssigned = 3,
    StaffAssigned = 4, StaffReassigned = 5,
    Commented = 6, MaintenanceLogged = 7, AttachmentAdded = 8,
    SlaPaused = 9, SlaResumed = 10, SlaWarning = 11, SlaBreached = 12,
    EscalationRequested = 13, Escalated = 14, IncidentDeclared = 15,
    Resolved = 16, Approved = 17, Rejected = 18,
    Rated = 19, Reopened = 20, Closed = 21, AutoClosed = 22
}
```

#### 2.3.5. `TicketComment`

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | — |
| `TicketId` | `Guid` | FK |
| `AuthorUserId` | `Guid` | — |
| `AuthorRole` | `ActorRoleEnum` | — |
| `AuthorDisplayName` | `string(200)` | denormalize |
| `Body` | `string(4000)` | Markdown — sanitize XSS |
| `IsInternal` | `bool` | default false; Internal=true thì Customer không thấy |
| `AttachmentFileIds` | `string` | JSON array of Guid |

#### 2.3.6. `MaintenanceLog`

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | — |
| `TicketId` | `Guid` | FK |
| `StaffId` | `Guid` | — |
| `LogType` | `MaintenanceLogTypeEnum` | 1=RemoteSupport, 2=OnSite, 3=PartReplacement, 4=Inspection |
| `Summary` | `string(2000)` | required |
| `DiagnosisDetails` | `string(4000)?` | — |
| `ActionsTaken` | `string(4000)?` | — |
| `StartedAt` | `DateTime` | — |
| `CompletedAt` | `DateTime?` | — |
| `PartsUsed` | `string(2000)?` | JSON |
| `AttachmentFileIds` | `string` | JSON |
| `BeforePhotosFileIds` | `string` | JSON |
| `AfterPhotosFileIds` | `string` | JSON |

#### 2.3.7. `TicketAttachment`

| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | — |
| `TicketId` | `Guid` | FK |
| `UploadedByUserId` | `Guid` | — |
| `FileId` | `Guid` | reference FileStorageService |
| `FileName` | `string(255)` | — |
| `ContentType` | `string(100)` | — |
| `SizeBytes` | `long` | — |
| `Source` | `enum` | 1=CustomerSubmission, 2=StaffWork, 3=MaintenanceLog |

### 2.4. State Machine — đầy đủ matrix

#### 2.4.1. States
```csharp
public enum TicketStatusEnum {
    New = 1,
    Open = 2,
    Assigned = 3,
    InProgress = 4,
    WaitingCustomer = 5,
    WaitingParts = 6,
    WaitingOnsiteSchedule = 7,
    Resolved = 8,
    Escalated = 9,
    ClosedPendingRate = 10,
    Closed = 11,
    ClosedRejected = 12,
    Incident = 13,
    Approved = 14
}
```

#### 2.4.2. Transition matrix

| From → To | Actor allowed | Required fields | Side effects |
|-----------|---------------|-----------------|--------------|
| `*` → `New` | System (initial) | — | Activity Created |
| `New` → `Open` | Manager / System | — | Activity StatusChanged |
| `Open` → `Approved` | Manager | `Priority`, `Impact`, `Urgency` | **Triage Success**: Phê duyệt tính hợp lệ, chưa gán Staff |
| `Open` → `ClosedRejected` | Manager | `RejectionReason` | **Triage Fail**: Từ chối trực tiếp ticket không hợp lệ |
| `Approved` → `Assigned` | Manager | `AssignedStaffId` | **Assignment**: Gán Staff, Start SlaTimer, publish TicketAssignedEvent |
| `Assigned` → `InProgress` | AssignedStaff | — | Activity StatusChanged |
| `InProgress` → `WaitingCustomer` | Staff | `Reason`, `Note` | Pause SlaTimer, create SlaPauseEvent |
| `InProgress` → `WaitingParts` | Staff | `Reason`, `Note` | Same as above |
| `InProgress` → `WaitingOnsiteSchedule` | Staff | `Reason`, `Note` | Same |
| `Waiting*` → `InProgress` | Staff / System (customer reply) | — | Resume SlaTimer, update SlaPauseEvent.ResumedAt |
| `InProgress` → `Resolved` | Staff | `ResolutionSummary` | Publish TicketResolvedEvent, notify Manager |
| `InProgress` → `Escalated` | Staff | `EscalationReason` | Activity EscalationRequested, notify Manager |
| `Assigned` → `Escalated` | System (SLA breach P1/P2) | (auto) | Activity Escalated by System |
| `Escalated` → `Assigned` | Manager | `AssignedStaffId` (new senior) | Activity StaffReassigned |
| `Escalated` → `Incident` | Manager | `Reason` | Set IsIncident=true, broadcast IncidentDeclaredEvent |
| `Escalated` → `ClosedRejected` | Manager | `RejectionReason` | Activity Rejected, publish TicketClosedEvent |
| `Incident` → `Assigned` | Manager | `AssignedStaffId` | Activity |
| `Resolved` → `ClosedPendingRate` | Manager | — | Approve, set ApprovedAt, publish TicketApprovedEvent |
| `Resolved` → `InProgress` | Manager | `RejectionReason` | Reject, Activity Rejected |
| `ClosedPendingRate` → `Closed` | Customer | `Rating` (1-5), `RatingComment?` | Set ClosedAt, publish TicketClosedEvent |
| `ClosedPendingRate` → `Closed` | System (auto, 7 days) | — | AutoClosed activity |
| `ClosedPendingRate` → `Open` | Customer (within 7d) | `ReopenReason` | ReopenCount++, BR-07 check, Activity Reopened |
| `Closed` → `Open` | ❌ NOT ALLOWED | — | Must create new ticket |

#### 2.4.2.bis. Escalation closure rule (B7)

**Quy tắc nghiệp vụ:** Khi ticket đã `Escalated` → chỉ Staff được escalate-tới (`Ticket.AssignedStaffId` sau khi Manager reassign sang `Tier2/Tier3`) mới được transition sang `Resolved`. Staff tầng dưới (Tier 1) KHÔNG được resolve thay.

**Enforcement trong `TicketResolveCommandHandler`:**

```csharp
public async Task<CommonResponse<TicketResolveResponse>> Handle(
    TicketResolveCommand request, CancellationToken ct)
{
    var ticket = await _unitOfWork.Tickets.GetByIdAsync(request.TicketId);
    if (ticket == null) return Fail("Ticket not found");

    // B7: nếu ticket đã từng escalated → bắt buộc actor là current AssignedStaff
    if (ticket.EscalatedAt.HasValue && ticket.AssignedStaffId != request.ActorUserId)
    {
        return Fail("Ticket đã escalated — chỉ Staff được assign sau escalation mới có thể resolve");
    }

    // B7: enforce staff tier ≥ tier yêu cầu của escalation reason
    if (ticket.EscalationReason == EscalationReasonEnum.SkillGap)
    {
        var staff = await _unitOfWork.StaffAccounts.GetByIdAsync(request.ActorUserId);
        if (staff?.SkillTier < StaffSkillTierEnum.ModuleSpecialist)
        {
            return Fail("Escalation lý do SkillGap → cần Staff Tier 2 (ModuleSpecialist) trở lên");
        }
    }

    // ... rest of resolve logic
}
```

**Activity log bổ sung:** `ActivityActionEnum.ResolvedByEscalatedStaff = 23` để audit trail rõ "ai mới được resolve sau escalation".

#### 2.4.3. State machine class skeleton
```csharp
public interface ITicketStateMachine {
    TransitionResult CanTransition(Ticket ticket, TicketStatusEnum target, ActorRoleEnum actorRole, Guid actorUserId);
    Task<TransitionResult> ExecuteAsync(Ticket ticket, TicketStatusEnum target, TransitionContext ctx, CancellationToken ct);
}

public sealed class TransitionContext {
    public ActorRoleEnum ActorRole { get; init; }
    public Guid ActorUserId { get; init; }
    public string ActorDisplayName { get; init; } = string.Empty;
    public Dictionary<string, object?> Payload { get; init; } = new();
}

public sealed class TransitionResult {
    public bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public List<DomainEvent> RaisedEvents { get; init; } = new();
}
```

### 2.4bis. Priority Calculation Matrix (B3) — Impact × Urgency

**Bối cảnh:** Việc Manager gán Priority `P1/P2/P3` cần dựa trên **framework có cơ sở**, không tùy tiện. Áp dụng mô hình **ITIL 4 Service Value System — Incident Prioritization** (xem `docs/adr/0005-b2b-itil-stance.md` cho stance B2B).

**Công thức:**

```
Priority = f(ImpactScope, UrgencyLevel)
```

**Impact Scope (B3) — phạm vi ảnh hưởng kỹ thuật:**

| Giá trị | Tên | Mô tả | Ví dụ |
|---------|-----|------|-------|
| 1 | `SingleAsset` | 1 BatteryAsset đơn lẻ | 1 pin overheat |
| 2 | `BatteryGroup` | Cả 1 group/cluster trong site | 1 string A bị low SOC |
| 3 | `Site` | Cả 1 site (≥ 50% asset bị ảnh hưởng) | Site An Giang mất điện |
| 4 | `MultiSite` | Nhiều site cùng nhà cung cấp/khu vực | Lô pin LFP batch X gặp lỗi hàng loạt |

**Urgency Level (B3) — mức độ khẩn cấp nghiệp vụ:**

| Giá trị | Tên | Mô tả | Trigger |
|---------|-----|------|---------|
| 1 | `Low` | Có thể đợi lịch bảo trì định kỳ | SOH giảm chậm, tỉ lệ < 5%/tháng |
| 2 | `Medium` | Cần xử lý trong vài ngày, chưa ảnh hưởng dịch vụ | Cell imbalance không lan rộng |
| 3 | `High` | Đe doạ dịch vụ ngay hoặc nguy cơ an toàn | Overheat, smoke detected, mất điện |

**Priority Matrix (gán Priority từ 2 chiều):**

| ↓ Impact / Urgency → | Low (1) | Medium (2) | High (3) |
|---|---|---|---|
| **SingleAsset (1)** | P3 | P3 | P2 |
| **BatteryGroup (2)** | P3 | P2 | P2 |
| **Site (3)** | P2 | P2 | **P1** |
| **MultiSite (4)** | P2 | **P1** | **P1** |

**Service implementation:**

```csharp
public interface IPriorityCalculator
{
    TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency);
}

public class PriorityCalculator : IPriorityCalculator
{
    public TicketPriorityEnum Calculate(ImpactScopeEnum impact, UrgencyLevelEnum urgency)
    {
        // Matrix lookup — không có if-else lằng nhằng
        if (impact == ImpactScopeEnum.MultiSite && urgency >= UrgencyLevelEnum.Medium)
            return TicketPriorityEnum.P1Critical;
        if (impact == ImpactScopeEnum.Site && urgency == UrgencyLevelEnum.High)
            return TicketPriorityEnum.P1Critical;
        if (impact >= ImpactScopeEnum.Site
            || (impact == ImpactScopeEnum.BatteryGroup && urgency >= UrgencyLevelEnum.Medium)
            || (impact == ImpactScopeEnum.SingleAsset && urgency == UrgencyLevelEnum.High))
            return TicketPriorityEnum.P2High;
        return TicketPriorityEnum.P3Normal;
    }
}
```

**Áp dụng trong `TicketAssignCommand`:**
- Manager gán `ImpactScope` + `UrgencyLevel` → `PriorityCalculator` tự tính `Priority`.
- Manager KHÔNG gán `Priority` trực tiếp nữa (sai framework).
- Override: nếu Manager muốn override (rare, vd safety override) → require justification field `PriorityOverrideReason` ghi vào `TicketActivity`.

**Auto-derivation cho AUTO-CREATE ticket (BR-02 từ Saga `CreateTicketFromAlertCommand`):**

`CreateTicketFromAlertConsumer` nhận wire-value `AnomalyType` (int) từ command rồi map về `AnomalyTypeEnum` nội bộ của TicketService bằng cùng helper deterministic ở §53.7 trước khi đưa qua `PriorityCalculator`. Hai bảng dưới đây phải đồng bộ — sửa một bảng thì sửa cả hai và update unit test mapping.

| Anomaly (internal enum §1.3.6) | Wire value | Default ImpactScope | Default UrgencyLevel | → Priority |
|--------------------------------|------------|---------------------|----------------------|-----------|
| `Overheat` (Critical severity) | 1 | SingleAsset | High | P2 |
| `Overvoltage` | 2 | SingleAsset | Medium | P3 |
| `Undervoltage` | 3 | SingleAsset | Medium | P3 |
| `LowSoc` | 4 | SingleAsset | Low | P3 |
| `RapidDischarge` | 5 | SingleAsset | Medium | P3 |
| `AbnormalCharging` | 6 | SingleAsset | Medium | P3 |
| `DeviceOffline` | 7 | SingleAsset | Low | P3 |
| `SohDegradation` | 8 | SingleAsset | Low | P3 |
| `HighAmbientTemp` | 9 | Site | Medium | P2 |
| `HighHumidity` | 10 | Site | Low | P3 |
| `HighTempHumidityCombo` | 11 | Site | High | P2 |
| `HighInternalResistance` | 12 | SingleAsset | Low | P3 |
| `CellImbalance` | 13 | SingleAsset | Medium | P3 |
| `EnvironmentalIncident` (smoke/fire/gas/flood) | 14 | Site | High | P1 |
| `SensorMismatch` | 15 | SingleAsset | Medium | P3 |
| Unknown wire value | — | SingleAsset | Medium | P3 (+ warning metric) |

> Manager có thể re-triage sau khi auto-create, nhưng Priority phải tính lại qua matrix — không nhập thẳng.
> Saga contract không tham chiếu enum nội bộ — wire value là source of truth cross-service.

**Enum bổ sung:**

```csharp
public enum ImpactScopeEnum {
    SingleAsset = 1,
    BatteryGroup = 2,
    Site = 3,
    MultiSite = 4
}

public enum UrgencyLevelEnum {
    Low = 1,
    Medium = 2,
    High = 3
}
```

**References:**
- ITIL 4 Foundation, Service Value System — Incident Management Practice
- Xem `.claude/docs/ai-research-references.md` mục "SLA & Priority frameworks" cho cite paper đầy đủ.

### 2.5. CQRS — đầy đủ command + query

#### Commands (16 commands)
1. `TicketCreateCommand` (Customer)
2. `TicketAutoCreateFromAlertCommand` (System — gọi nội bộ từ consumer)
3. `TicketAssignCommand` (Manager): StaffId, Priority
4. `TicketReassignCommand` (Manager): NewStaffId, Reason
5. `TicketStartCommand` (Staff): → InProgress
6. `TicketHoldCommand` (Staff): Reason, Note → Waiting*
7. `TicketResumeCommand` (Staff)
8. `TicketResolveCommand` (Staff): ResolutionSummary
9. `TicketApproveCommand` (Manager)
10. `TicketRejectCommand` (Manager): RejectionReason
11. `TicketEscalateRequestCommand` (Staff): EscalationReason
12. `TicketEscalateForceCommand` (Manager): for manual escalation
13. `TicketDeclareIncidentCommand` (Manager)
14. `TicketRateCommand` (Customer): Rating, Comment
15. `TicketReopenCommand` (Customer): Reason
16. `TicketCloseCommand` (System: auto-close 7d)

Plus comment/log/attachment commands:
- `CommentAddCommand` (Customer/Staff/Manager)
- `MaintenanceLogAddCommand` (Staff)
- `MaintenanceLogUpdateCommand` (Staff)
- `AttachmentUploadCommand` (Customer/Staff)

#### Queries (15 queries)
1. `TicketGetListQuery` — Admin/Manager: full filter
2. `TicketGetByIdQuery` — with includes (activities, comments, sla, logs)
3. `MyTicketsAsCustomerQuery`
4. `MyTicketsAsStaffQuery`
5. `ManagerQueueQuery` (status=Open, priority sort)
6. `TicketActivityTimelineQuery`
7. `SlaStatusQuery` — countdown + pauseHistory
8. `StaffWorkloadQuery` (Manager: list Staff with active count)
9. `TicketDashboardStatsQuery` (Admin/Manager: open, overdue, breach rate, avg resolve time)
10. `TicketCommentsQuery` (filter internal/external by role)
11. `MaintenanceLogsByTicketQuery`
12. `TicketSearchQuery` (full-text Title + Description + Code)
13. `OverdueTicketsQuery` (Manager view)
14. `EscalatedTicketsQuery`
15. `IncidentsQuery`

### 2.6. Background services

#### `SlaTimerBackgroundService` (frequency: 60s)
```csharp
foreach (var timer in await _uow.SlaTimers.GetAllAsync()
    .Where(t => t.Status == SlaTimerStatusEnum.Running)
    .ToListAsync()) {

    var now = DateTime.UtcNow;
    var remaining = timer.DueAt - now;
    var totalMinutes = (timer.DueAt - timer.StartedAt).TotalMinutes - timer.TotalPausedMinutes;
    var remainingPercent = remaining.TotalMinutes / totalMinutes;

    // 80% threshold warning
    if (remainingPercent <= 0.2 && timer.WarningSentAt == null) {
        timer.WarningSentAt = now;
        await _outbox.AddEvent(new SlaWarningEvent { TicketId = timer.TicketId, Priority = timer.Priority });
        await _activity.LogAsync(timer.TicketId, ActivityActionEnum.SlaWarning);
    }

    // breach
    if (remaining <= TimeSpan.Zero && timer.Status == SlaTimerStatusEnum.Running) {
        timer.BreachAt = now;
        timer.Status = SlaTimerStatusEnum.Breached;
        await _outbox.AddEvent(new SlaBreachedEvent { TicketId = timer.TicketId, Priority = timer.Priority });
        await _activity.LogAsync(timer.TicketId, ActivityActionEnum.SlaBreached);

        // Per design.md: P1/P2 breach → auto escalate (state change), P3 → only log
        if (timer.Priority is TicketPriorityEnum.P1Critical or TicketPriorityEnum.P2High) {
            var ticket = await _uow.Tickets.GetByIdAsync(timer.TicketId);
            ticket.Status = TicketStatusEnum.Escalated;
            ticket.EscalatedAt = now;
            ticket.EscalationReason = EscalationReasonEnum.SlaBreach;
            _uow.Tickets.UpdateAsync(ticket);
        }
    }
}
await _uow.CommitTransactionAsync();
```

#### `AutoCloseBackgroundService` (frequency: hourly)
- Scan tickets `Status=ClosedPendingRate AND ApprovedAt < now - 7d` → set Closed, log AutoClosed.

#### `EscalationBackgroundService` (event-driven, không scheduled)
- Subscribe `SlaBreachedEvent` internal → trigger escalation flow.

#### `OutboxRelayBackgroundService` (5s)
- Standard outbox relay.

### 2.7. Integration events

#### Publish (12 domain events + Saga response events)
1. `TicketCreatedEvent`
2. `TicketAssignedEvent`
3. `TicketStatusChangedEvent` (generic)
4. `TicketResolvedEvent`
5. `TicketApprovedEvent`
6. `TicketRejectedEvent`
7. `TicketReopenedEvent`
8. `TicketClosedEvent`
9. `TicketEscalatedEvent`
10. `IncidentDeclaredEvent`
11. `SlaWarningEvent`
12. `SlaBreachedEvent`
13. `TicketProvisionedForAlertEvent` — Saga response: trả Ticket đã tạo/reuse, không liên quan trạng thái `Resolved`.
14. `TicketProvisionForAlertRejectedEvent` — business rejection có error code/retryability.
15. `AlertTicketSagaFailedEvent` — terminal failure để observability/ops xử lý.

Sample:
```csharp
public record TicketAssignedEvent : IntegrationEvent {
    public Guid TicketId { get; init; }
    public string TicketCode { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public Guid AssignedStaffId { get; init; }
    public TicketPriorityEnum Priority { get; init; }
    public DateTime SlaDueAt { get; init; }
}
```

#### Read-model: `CustomerAccount` và `StaffAccount`

**Lý do tồn tại.** TicketService cần biết Customer/Staff là ai để (a) validate `CustomerId` lúc tạo ticket — chủ pin có còn active không, (b) validate `AssignedStaffId` lúc Manager assign — staff active + có skill phù hợp + chưa vượt `MaxConcurrentTickets`, (c) hiển thị tên/email trên queue cho Manager mà không phải call sang AuthService mỗi lần load list. Nếu sync-call HTTP sang AuthService → tạo **circular dependency** (AuthService publish event xuống TicketService consume; TicketService lại sync-call ngược lại) và phá nguyên tắc *database per service*.

→ Pattern dùng: giữ một bản **read-model cục bộ** trong DB của TicketService, đồng bộ qua integration events (giống `CustomerAccount` đã áp dụng ở BatteryService — xem checklist Sprint 2).

**Phạm vi.** Read-model này CHỈ dùng cho business validate + hiển thị. KHÔNG dùng để authorize request (token vẫn validate qua JWT signature + Auth introspect như cũ) và KHÔNG dùng cho audit cần real-time chính xác.

##### `CustomerAccount` (read-model, kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `AccountId` | `Guid` | PK | = `Account.Id` bên AuthService |
| `Email` | `string(256)` | NOT NULL, INDEX | Hiển thị trong queue + tìm kiếm |
| `FullName` | `string(200)` | NOT NULL | — |
| `PhoneNumber` | `string(20)?` | nullable | Liên hệ khi cần escalate |
| `Status` | `AccountStatusEnum` | NOT NULL | 1=Active, 2=Disabled, 3=Locked |
| `LastSyncedAt` | `DateTime` | NOT NULL | UTC — debug/audit consistency lag |

**Index:** `(Status, IsDeleted)` — query "list customer active".

##### `StaffAccount` (read-model, kế thừa `AuditableEntity`)

| Field | Type | Constraint | Note |
|-------|------|-----------|------|
| `AccountId` | `Guid` | PK | = `Account.Id` bên AuthService |
| `Email` | `string(256)` | NOT NULL, INDEX | — |
| `FullName` | `string(200)` | NOT NULL | — |
| `EmployeeCode` | `string(50)?` | nullable | Hiển thị trên queue |
| `Status` | `AccountStatusEnum` | NOT NULL | — |
| `IsAvailable` | `bool` | NOT NULL default true | Staff bật/tắt nhận ticket |
| `MaxConcurrentTickets` | `int` | NOT NULL default 10 | Cap workload |
| `SkillCodes` | `string[]` (jsonb) | NOT NULL default `[]` | E.g. `["BMS","HV","TH"]` — match `Ticket.Category` |
| `LastSyncedAt` | `DateTime` | NOT NULL | — |

**Index:** `(Status, IsAvailable, IsDeleted)` — query staff khả dụng để assign.

##### Event sync (5 consumer ở §2.7 Consume)

| Event nguồn (AuthService) | Consumer trong TicketService | Hành động |
|--------------------------|------------------------------|-----------|
| `AccountActivatedEvent` | `AccountActivatedConsumer` | Upsert `CustomerAccount` (nếu role=Customer) hoặc `StaffAccount` (nếu role=Staff). Bỏ qua role khác (Admin/Manager) |
| `AccountStatusChangedEvent` | `AccountStatusChangedConsumer` | Update `Status`. Nếu Customer chuyển Disabled → publish `CustomerSuspendedDomainEvent` nội bộ → suspend ticket đang mở |
| `AccountProfileUpdatedEvent` | `AccountProfileUpdatedConsumer` | Update `Email`, `FullName`, `PhoneNumber` |
| `StaffProfileUpdatedEvent` | `StaffProfileUpdatedConsumer` | Update `EmployeeCode`, `IsAvailable`, `MaxConcurrentTickets` |
| `StaffSkillsUpdatedEvent` | `StaffSkillsUpdatedConsumer` | Replace `SkillCodes[]` |

Mọi consumer đều: (a) qua `IInboxStore` để dedup theo `MessageId` (§8.2), (b) set `LastSyncedAt = UtcNow`, (c) dùng upsert (insert nếu chưa có — bảo vệ trường hợp event ra trước khi consumer kịp xử lý event activate).

##### Rule validate

```csharp
// TicketCreateCommandHandler — validate CustomerId
var customer = await _uow.CustomerAccounts.GetAllAsync()
    .Where(x => x.AccountId == request.CustomerId && !x.IsDeleted)
    .FirstOrDefaultAsync();
if (customer == null)
    return Fail("Customer không tồn tại trong hệ thống");
if (customer.Status != AccountStatusEnum.Active)
    return Fail("Customer đang bị disabled/locked, không thể tạo ticket");

// TicketAssignCommandHandler — validate AssignedStaffId
var staff = await _uow.StaffAccounts.GetAllAsync()
    .Where(x => x.AccountId == request.AssignedStaffId && !x.IsDeleted)
    .FirstOrDefaultAsync();
if (staff == null || staff.Status != AccountStatusEnum.Active)
    return Fail("Staff không khả dụng");
if (!staff.IsAvailable)
    return Fail("Staff đang tắt nhận ticket");

// Check skill match (cảnh báo, không block — Manager có quyền override)
var requiredSkill = MapCategoryToSkill(ticket.Category);
if (requiredSkill != null && !staff.SkillCodes.Contains(requiredSkill))
    response.Warnings.Add($"Staff thiếu skill {requiredSkill} cho ticket category {ticket.Category}");

// Check workload (đếm trên DB cục bộ TicketService — xem §7.5)
var activeCount = await _uow.Tickets.GetAllAsync()
    .CountAsync(t => t.AssignedStaffId == staff.AccountId
                  && t.Status >= TicketStatusEnum.Assigned
                  && t.Status <= TicketStatusEnum.WaitingOnsiteSchedule
                  && !t.IsDeleted);
if (activeCount >= staff.MaxConcurrentTickets)
    return Fail($"Staff đã đạt cap {staff.MaxConcurrentTickets} ticket active");
```

##### Eventual consistency note

Read-model có thể **trễ vài giây** so với state thật bên AuthService (RabbitMQ delivery + consumer lag). Hệ quả & cách xử lý:

- **Edge case 1 — Customer mới activate, Manager assign ticket ngay:** event `AccountActivatedEvent` chưa tới TicketService → validate fail "không tồn tại". Mitigate: ApiGateway nên publish `AccountActivatedEvent` *trước khi* return 200 cho client activate request, và FE chờ 1–2s trước khi mở màn tạo ticket. Nếu vẫn miss: error rõ ràng "đồng bộ chưa hoàn tất, thử lại sau 5s" — KHÔNG fallback gọi HTTP sang Auth.
- **Edge case 2 — Account vừa bị disabled, ticket vẫn được tạo:** event `AccountStatusChangedEvent` chưa tới → validate cho qua. Sau vài giây consumer xử lý → ticket vẫn tồn tại nhưng customer không truy cập được. Acceptable cho capstone scope (background job sẽ flag).
- **KHÔNG dùng read-model cho:** (a) authorization (JWT vẫn là source of truth), (b) compliance/audit log cần state real-time, (c) report tài chính.
- Health check cần thiết: endpoint `/health/sync-lag` trả `MAX(NOW() - LastSyncedAt)` per bảng — alert nếu > 60s liên tiếp 5 phút (consumer chết).

#### Consume (5 account-sync events + Saga commands)

1. `CreateTicketFromAlertConsumer` nhận `CreateTicketFromAlertCommand` từ Saga:
   - Tìm Ticket chưa xóa có `OriginAlertId == AlertId` trước để bảo đảm retry cùng Alert trả đúng Ticket cũ.
   - Nếu chưa có, map anomaly → category và tìm Ticket active cùng `(BatteryAssetId, Category)` theo BR-02.
   - Nếu có Ticket active cùng asset/category, trả `CreatedNew=false`; không đổi `Origin`/`OriginAlertId`
     của Ticket được reuse. `Alert.TicketId` mới là link cho Alert hiện tại.
   - Nếu chưa có, tạo Ticket + Activity `Created` với actor System.
   - Nếu tạo mới, publish cả `TicketCreatedEvent` và `TicketProvisionedForAlertEvent`; nếu reuse chỉ
     publish provision response, không giả lập một TicketCreated lần hai.
   - Commit Ticket/Activity và outgoing event trong cùng local transaction + EF Consumer Outbox.
   - Unique filtered index trên `tickets.origin_alert_id` cho row `is_deleted=false` là lớp bảo vệ
     cuối trước concurrent delivery.
2. `AlertTicketSagaStateMachine` consume `BatteryAnomalyDetectedEvent`, success/rejection response của
   hai participant, `Fault<T>`, timeout và `ReconcileAlertTicketSagaCommand`; chi tiết ở §8.3 và §53.
3. `AccountActivatedConsumer` — upsert `CustomerAccount` / `StaffAccount` read-model theo role.
4. `AccountStatusChangedConsumer` — update `Status`; Customer disabled → suspend ticket đang mở.
5. `AccountProfileUpdatedConsumer` — sync `Email`, `FullName`, `PhoneNumber`.
6. `StaffProfileUpdatedConsumer` — sync `EmployeeCode`, `IsAvailable`, `MaxConcurrentTickets`.
7. `StaffSkillsUpdatedConsumer` — replace `SkillCodes[]`.

### 2.8. REST API contract

#### Endpoints
```
# Customer-facing
POST   /api/v1/tickets                                   (Customer)
GET    /api/v1/tickets/me                                (Customer)
PUT    /api/v1/tickets/{id}/rate                         (Customer — own)
PUT    /api/v1/tickets/{id}/reopen                       (Customer — own, within 7d)

# Common read
GET    /api/v1/tickets/{id}                              (Admin/Manager any; Customer own; Staff assigned)
GET    /api/v1/tickets/{id}/activities                   (— same —)
GET    /api/v1/tickets/{id}/comments                     (filter IsInternal by role)
GET    /api/v1/tickets/{id}/maintenance-logs             (— same —)
GET    /api/v1/tickets/{id}/sla                          (— same —)

# Manager
GET    /api/v1/manager/queue                             (Manager)
PUT    /api/v1/tickets/{id}/assign                       (Manager)
PUT    /api/v1/tickets/{id}/reassign                     (Manager)
PUT    /api/v1/tickets/{id}/approve                      (Manager)
PUT    /api/v1/tickets/{id}/reject                       (Manager)
PUT    /api/v1/tickets/{id}/escalate                     (Manager)
PUT    /api/v1/tickets/{id}/declare-incident             (Manager)
GET    /api/v1/manager/staff-workload                    (Manager)
GET    /api/v1/manager/tickets?filter=overdue            (Manager)
GET    /api/v1/manager/tickets?filter=escalated          (Manager)
GET    /api/v1/manager/tickets?filter=incidents          (Manager)

# Staff
GET    /api/v1/staff/my-tickets                          (Staff)
PUT    /api/v1/tickets/{id}/start                        (Staff — assigned)
PUT    /api/v1/tickets/{id}/hold                         (Staff — assigned)
PUT    /api/v1/tickets/{id}/resume                       (Staff — assigned)
PUT    /api/v1/tickets/{id}/resolve                      (Staff — assigned)
PUT    /api/v1/tickets/{id}/request-escalation           (Staff — assigned)

# Comments
POST   /api/v1/tickets/{id}/comments                     (Customer/Staff/Manager)
GET    /api/v1/tickets/{id}/comments                     (filter)

# Maintenance log
POST   /api/v1/tickets/{id}/maintenance-logs             (Staff)
PUT    /api/v1/maintenance-logs/{id}                     (Staff — own)
GET    /api/v1/maintenance-logs/{id}                     (Customer own ticket; Staff/Manager)

# Attachment
POST   /api/v1/tickets/{id}/attachments                  (Customer/Staff)
DELETE /api/v1/attachments/{id}                          (uploader / Admin)

# Dashboard
GET    /api/v1/ticket/dashboard/stats                    (Admin/Manager)
GET    /api/v1/ticket/dashboard/sla-trend                (Admin/Manager)

# Health
GET    /api/v1/ticket/health

# Alert–Ticket Saga operations
GET    /api/v1/admin/sagas/alert-ticket?state=&olderThan=&page=    (TicketSagaView — Admin + Manager read-only)
GET    /api/v1/admin/sagas/alert-ticket/{alertId}                   (TicketSagaView — Admin + Manager read-only)
POST   /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess         (TicketSagaReprocess — Admin only, audit log + idempotency key)
```

#### Sample request — assign
```json
PUT /api/v1/tickets/{id}/assign
{
  "assignedStaffId": "6f4b...",
  "priority": 2,                  // P2 High
  "note": "Forwarded to Staff Long for routine check."
}
```
Response includes computed `slaDueAt`.

### 2.9. Test catalog

#### Unit tests (must-have)
- `TicketStateMachineTests`: full matrix 30+ transitions (every cell of §2.4.2)
- `TicketCreateCommandHandlerTests`: 8 cases
- `TicketAssignCommandHandlerTests`: 6 cases (valid, missing priority, staff inactive, ticket not in Open, double assign)
- `TicketResolveCommandHandlerTests`: 4 cases
- `TicketReopenCommandHandlerTests`: 5 cases (within 7d ok, >7d rejected, reopen count++, escalate on 2nd reopen, escalate on 3rd+)
- `SlaCalculatorTests`: 8 cases (compute due, pause/resume, total paused, breach detection)
- `CreateTicketFromAlertConsumerTests`: create mới, retry cùng Alert trả Ticket cũ, reuse ticket active
  cùng category, khác category tạo mới, ticket terminal không được reuse, soft-deleted ticket bị bỏ qua,
  concurrent duplicate cùng Alert, concurrent Alerts cùng asset/category, PostgreSQL `23505` known/unknown
  constraint, và mapping đủ **15 anomaly** + unknown fallback (đồng bộ §1.3.6).
- `AlertTicketSagaStateMachineTests`: **≥ 21 cases** đồng bộ với test matrix §53.10 (happy path, reuse path, duplicate start trước/sau Completed, duplicate sau Saga Completed, escalation chưa-ack không start lần hai, dispatch flag off, concurrent duplicate command, concurrent Alerts cùng asset/category, Ticket DB transient failure, Battery unavailable, timeout, late response, conflict TicketId, business rejection, retryable rejection lặp lại, `Fault<T>` sau retry, manual reprocess, existing direct-consumer Ticket reconciliation, service restart khi timeout, broker restart, consumer crash trước/sau commit, feature-flag mis-config, reconciliation 2 lần)
- `CustomerAccountSyncConsumerTests`: 4 cases (upsert mới khi chưa có, update khi đã có, bỏ qua role Admin/Manager, idempotent qua Inbox)
- `StaffAccountSyncConsumerTests`: 5 cases (upsert StaffAccount, update IsAvailable + MaxConcurrentTickets, replace SkillCodes, status Disabled cập nhật, idempotent qua Inbox)
- `TicketAssignCommandHandler__SkillWorkloadTests`: 4 cases (skill match warning, skill miss vẫn cho assign, workload cap block, staff không Active block)

#### Integration tests
- POST create → GET list returns
- Assign → SLA timer starts → 80% warning event → breach event (use time mocking)
- Alert anomaly → Saga → create/reuse Ticket → link `Alert.TicketId` qua MassTransit TestHarness
- Redelivery cùng `BatteryAnomalyDetectedEvent` không tạo thêm Ticket/Saga
- RabbitMQ/BatteryService tạm unavailable → retry/timeout quan sát được, reprocess hoàn tất Saga
- Direct consumer cũ không còn endpoint registration; chỉ Saga path xử lý anomaly event
- Contract tests cho toàn bộ command/success/rejection/failure message của Saga
- Reopen flow end-to-end

---

## 3. NotificationService — P1

### 3.1. Trách nhiệm
1. Centralize notification orchestration.
2. Consume tất cả integration events từ Battery/Ticket/Auth → quyết định ai nhận, channel nào.
3. Push qua Expo (Mobile), email qua EmailService bus, SMS qua SmsService bus, in-app stored.
4. Customer preference + quiet hours + severity filter.
5. Device token management.
6. Notification history endpoint cho Mobile/Web.

### 3.2. Cấu trúc
```
services/NotificationService/
├── src/
│   ├── NotificationService.Api/Controllers/
│   │   ├── NotificationsController.cs        (list, mark read)
│   │   ├── PreferencesController.cs
│   │   ├── DeviceTokensController.cs
│   │   └── HealthController.cs
│   ├── NotificationService.Application/
│   │   ├── Consumers/
│   │   │   ├── TicketCreatedConsumer.cs
│   │   │   ├── TicketAssignedConsumer.cs
│   │   │   ├── TicketStatusChangedConsumer.cs
│   │   │   ├── TicketResolvedConsumer.cs
│   │   │   ├── TicketApprovedConsumer.cs
│   │   │   ├── TicketClosedConsumer.cs
│   │   │   ├── TicketEscalatedConsumer.cs
│   │   │   ├── IncidentDeclaredConsumer.cs
│   │   │   ├── SlaWarningConsumer.cs
│   │   │   ├── SlaBreachedConsumer.cs
│   │   │   ├── BatteryAnomalyDetectedConsumer.cs
│   │   │   ├── BatteryAlertEscalationRequestedConsumer.cs   ← Sprint 5B: push Manager khi Critical Alert chưa-ack > 5 phút (xem §1, §8.3)
│   │   │   ├── AlertTicketSagaFailedConsumer.cs             ← Sprint 5B: notify Admin/Manager khi Saga Failed (xem §53.11)
│   │   │   ├── AccountActivatedConsumer.cs
│   │   │   └── AccountInvitedConsumer.cs
│   │   ├── Templates/
│   │   │   ├── ITemplateRenderer.cs
│   │   │   ├── HandlebarsTemplateRenderer.cs
│   │   │   └── Templates/                     (embedded .hbs files)
│   │   ├── Services/
│   │   │   ├── INotificationDispatcher.cs
│   │   │   ├── NotificationDispatcher.cs
│   │   │   ├── IUserResolver.cs               (resolve roleId → userIds — call AuthService)
│   │   │   └── UserResolver.cs
│   │   └── CQRS/...
│   ├── NotificationService.Domain/Entities/
│   │   ├── Notification.cs
│   │   ├── DeviceToken.cs
│   │   ├── NotificationPreference.cs
│   │   └── NotificationTemplate.cs
│   └── NotificationService.Infrastructure/
│       ├── Channels/
│       │   ├── INotificationChannel.cs
│       │   ├── ExpoPushChannel.cs              (HTTP + Polly retry)
│       │   ├── EmailBusChannel.cs              (publish to EmailService)
│       │   ├── SmsBusChannel.cs                (publish to SmsService)
│       │   └── InAppChannel.cs                 (store in DB)
│       └── Persistence/...
└── tests/...
```

### 3.3. Entities

#### `Notification`
| Field | Type | Note |
|-------|------|------|
| `Id` | `Guid` | — |
| `UserId` | `Guid` | recipient |
| `Type` | `NotificationTypeEnum` | xem enum |
| `Title` | `string(200)` | localized |
| `Body` | `string(1000)` | — |
| `Data` | `jsonb` | deep-link payload `{ ticketId, alertId, ... }` |
| `Channel` | `NotificationChannelEnum` | 1=Push, 2=Email, 3=Sms, 4=InApp |
| `Status` | `NotificationStatusEnum` | 1=Pending, 2=Sent, 3=Failed, 4=Read |
| `ReadAt` | `DateTime?` | — |
| `SentAt` | `DateTime?` | — |
| `FailureReason` | `string?` | — |
| `CreatedAt` | `DateTime` | indexed DESC |

```csharp
public enum NotificationTypeEnum {
    TicketCreated = 1, TicketAssigned = 2, TicketStatusChanged = 3,
    TicketResolved = 4, TicketApproved = 5, TicketClosed = 6,
    TicketEscalated = 7, IncidentDeclared = 8,
    SlaWarning = 9, SlaBreached = 10,
    BatteryAlertInfo = 11, BatteryAlertWarning = 12, BatteryAlertCritical = 13,
    AccountActivated = 14, AccountInvited = 15,
    BatteryAlertEscalationPending = 16,   // Sprint 5B: Critical Alert chưa ack > 5 phút (BatteryAlertEscalationRequestedEvent)
    AlertTicketSagaFailed = 17            // Sprint 5B: Saga Failed cần operator reprocess (AlertTicketSagaFailedEvent)
}
```

#### `DeviceToken`
| Field | Type |
|-------|------|
| `Id` | Guid |
| `UserId` | Guid (indexed) |
| `ExpoPushToken` | string(255) UNIQUE |
| `Platform` | enum (iOS=1, Android=2) |
| `AppVersion` | string |
| `LastSeenAt` | DateTime |

#### `NotificationPreference`
| Field | Type | Default |
|-------|------|---------|
| `UserId` | Guid (PK) | — |
| `PushEnabled` | bool | true |
| `EmailDigestEnabled` | bool | true |
| `SmsCriticalEnabled` | bool | true (P1 only) |
| `MinSeverityForPush` | enum | Warning |
| `QuietHoursStart` | TimeOnly? | null |
| `QuietHoursEnd` | TimeOnly? | null |
| `TimeZone` | string | "Asia/Ho_Chi_Minh" |

### 3.4. Notification routing logic (`NotificationDispatcher`)

```
INPUT: NotificationTypeEnum + targetUserIds + payload
1. For each targetUserId:
   a. Load preference (cache 5min).
   b. Check quiet hours → if yes, defer push to in-app only.
   c. For each channel candidate by type mapping (e.g., Critical → Push+Email+Sms):
      - If channel disabled in preference → skip.
      - Render template.
      - Create Notification record (Status=Pending).
      - Invoke channel.SendAsync().
      - On success → update Status=Sent, SentAt.
      - On failure (3 retries via Polly) → Status=Failed, log.
```

**Type → Channel matrix:**
| NotificationType | InApp | Push | Email | SMS |
|-----------------|-------|------|-------|-----|
| TicketCreated (to Manager) | ✅ | ✅ | digest | — |
| TicketAssigned (to Staff) | ✅ | ✅ | ✅ | — |
| TicketAssigned (to Customer) | ✅ | ✅ | ✅ | — |
| SlaWarning (Staff + Manager) | ✅ | ✅ | — | — |
| SlaBreached P1 (Manager + Admin) | ✅ | ✅ | ✅ | ✅ |
| SlaBreached P2 (Manager) | ✅ | ✅ | ✅ | — |
| SlaBreached P3 (Manager) | ✅ | — | digest | — |
| BatteryAlertCritical (Customer) | ✅ | ✅ | ✅ | ✅ (if enabled) |
| BatteryAlertWarning (Customer) | ✅ | ✅ | — | — |
| BatteryAlertInfo | ✅ | — (chỉ in-app) | — | — |
| IncidentDeclared (broadcast Manager/Admin/LeadStaff) | ✅ | ✅ | ✅ | ✅ |
| BatteryAlertEscalationPending (Manager + Admin) | ✅ | ✅ | ✅ | — | Critical Alert chưa-ack > 5 phút (xem §1, §8.3) |
| AlertTicketSagaFailed (Admin) | ✅ | ✅ | ✅ | — | Saga Failed cần operator reprocess (xem §53.11) |
| DeviceOffline (Customer) | ✅ | ✅ | — | — | Từ `DeviceOffline` Alert (Warning) — Customer biết pin mất giám sát (§52.6) |
| DeviceOffline (Staff/ops) | ✅ | ✅ | — | — | Từ `IotDeviceWentOfflineEvent` — Staff đi kiểm tra device tại site (§52.6) |

### 3.5. Endpoints
```
GET    /api/v1/notifications?status=&type=&page=         (mine)
GET    /api/v1/notifications/unread-count                (mine)
PUT    /api/v1/notifications/{id}/read                   (mine)
PUT    /api/v1/notifications/read-all                    (mine)
GET    /api/v1/notification-preferences                  (mine)
PUT    /api/v1/notification-preferences                  (mine)
POST   /api/v1/device-tokens                             (Mobile register)
DELETE /api/v1/device-tokens/{token}
```

### 3.6. Expo Push integration

```csharp
public class ExpoPushChannel : INotificationChannel {
    private readonly HttpClient _http;  // Polly retry via SharedInfrastructure
    private const string ExpoUrl = "https://exp.host/--/api/v2/push/send";

    public async Task<ChannelResult> SendAsync(SendRequest req, CancellationToken ct) {
        var payload = new {
            to = req.ExpoToken,
            title = req.Title,
            body = req.Body,
            data = req.Data,
            sound = "default",
            priority = req.IsCritical ? "high" : "normal",
            channelId = req.IsCritical ? "alerts-critical" : "alerts-default"
        };
        var resp = await _http.PostAsJsonAsync(ExpoUrl, payload, ct);
        // Parse Expo receipt; if DeviceNotRegistered → mark token invalid
        ...
    }
}
```

**Polly policy:** retry 3 lần exponential backoff (đã có sẵn pattern trong SharedInfrastructure).

---

## 4. KnowledgeBase module trong TicketService — P2

### 4.1. Mục đích
- Staff tìm hướng xử lý nhanh cho lỗi lặp lại.
- Manager soạn solution template.

### 4.2. Entity `KnowledgeBaseArticle`

| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Code` | `string(20)` | **B8** NOT NULL UNIQUE — format `KB-YYYY-NNNN` (auto-gen, reset hàng năm) |
| `Category` | `TicketCategoryEnum` | Match với ticket category để suggest |
| `Title` | string(200) | — |
| `Symptoms` | string(2000) | Markdown |
| `DiagnosisSteps` | string(4000) | Markdown checklist |
| `SolutionSteps` | string(4000) | Markdown steps |
| `RecommendedParts` | string? | JSON |
| `Tags` | string[] | Postgres array |
| `Status` | enum | 1=Draft, 2=Published, 3=Archived |
| `Version` | int | Bump khi update |
| `ViewCount` | int | Analytics |
| `HelpfulCount` | int | Staff vote helpful |
| `CreatedByUserId` | Guid | — |

### 4.2bis. Entity `TicketKbReference` (B8) — link KB ↔ Ticket

Many-to-many: 1 ticket có thể tham chiếu nhiều KB article, 1 KB có thể được dùng cho nhiều ticket.

| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `TicketId` | Guid | FK → Ticket, indexed |
| `KbArticleId` | Guid | FK → KnowledgeBaseArticle, indexed |
| `KbArticleCode` | string(20) | denormalize cho query không join |
| `ReferencedByUserId` | Guid | Staff đã dùng KB này |
| `ReferenceType` | enum | 1=ConsultedDuringResolve, 2=ProvidedToCustomer, 3=GeneratedAfterResolve |
| `Note` | string(500)? | Optional — Staff ghi chú "đã làm theo step 3-5" |
| `CreatedAt` | DateTime | indexed DESC |

**Composite unique constraint:** `(TicketId, KbArticleId, ReferenceType)` — tránh duplicate.

**Logic:**
- Khi Staff resolve ticket bằng KB → frontend gọi `POST /api/v1/tickets/{id}/kb-references` với `KbArticleId` + `ReferenceType=ConsultedDuringResolve`.
- Khi Manager đóng ticket → có thể tạo KB mới và link với `ReferenceType=GeneratedAfterResolve` để audit "ticket này sinh ra KB mới".
- Analytics: `GET /api/v1/knowledge-base/{id}/usage-stats` đếm số lần được tham chiếu.

**MaintenanceLog cũng có** field `RelatedKbArticleIds` (JSON array Guid) — Staff log "đã dùng KB nào trong quá trình bảo trì".

**Endpoints bổ sung:**
```
POST   /api/v1/tickets/{id}/kb-references                (Staff)
DELETE /api/v1/tickets/{id}/kb-references/{refId}        (Staff)
GET    /api/v1/tickets/{id}/kb-references                (mọi role internal)
GET    /api/v1/knowledge-base/{id}/usage-stats           (Manager/Admin — count + recent tickets)
```

### 4.3. Endpoints
```
GET    /api/v1/knowledge-base?category=&q=&tag=&page=    (Staff/Manager/Admin)
GET    /api/v1/knowledge-base/{id}                       (mọi role internal)
POST   /api/v1/knowledge-base                            (Manager/Admin) — Status=Draft
PUT    /api/v1/knowledge-base/{id}
PUT    /api/v1/knowledge-base/{id}/publish               (Manager/Admin)
PUT    /api/v1/knowledge-base/{id}/archive
DELETE /api/v1/knowledge-base/{id}                       (Admin)
POST   /api/v1/knowledge-base/{id}/helpful               (Staff vote)
GET    /api/v1/knowledge-base/suggest?ticketId={id}      (Staff — gợi ý theo ticket category + symptom match)
```

### 4.4. Suggest logic
```sql
SELECT id, title, helpful_count
FROM kb_articles
WHERE status = 2  -- Published
  AND category = :ticketCategory
ORDER BY helpful_count DESC, view_count DESC
LIMIT 5;
```
Future: ElasticSearch full-text trên Symptoms (out of scope capstone).

---

## 5. Reporting endpoints — P2

### 5.1. Quyết định kiến trúc
- KHÔNG tạo ReportingService riêng. Mỗi service tự expose `/api/v1/reports/*` endpoint.
- ApiGateway aggregate dashboard.

### 5.2. Reports

#### TicketService reports

| Report | Endpoint | Output |
|--------|----------|--------|
| SLA Compliance by Staff | `GET /api/v1/reports/sla-by-staff?from=&to=` | Array<{staffId, name, totalAssigned, met, breached, complianceRate}> |
| SLA Compliance by Priority | `GET /api/v1/reports/sla-by-priority?from=&to=` | {P1: ..., P2: ..., P3: ...} |
| Ticket Volume Trend | `GET /api/v1/reports/ticket-volume?granularity=day&from=&to=` | TimeSeries |
| Top Reopen Issues | `GET /api/v1/reports/top-reopen-issues?limit=10` | Array<{category, count, avgReopenCount}> |
| Staff Performance | `GET /api/v1/reports/staff-performance?from=&to=` | Array<{staffId, ticketsResolved, avgResolveHours, avgRating, slaCompliance}> |
| CSAT | `GET /api/v1/reports/csat?from=&to=` | {avgRating, ratingDistribution, totalRated} |
| Resolution Time Distribution | `GET /api/v1/reports/resolution-time-histogram` | Buckets |
| Category Breakdown | `GET /api/v1/reports/category-breakdown?from=&to=` | Array<{category, count}> |
| **Saga Failed Rate** (Sprint 5B) | `GET /api/v1/reports/saga-failed-rate?from=&to=&granularity=day` | TimeSeries<{date, started, completed, failed, failedRate, p95DurationSec}> — chỉ Admin (`TicketSagaView`); cho hội đồng KLTN demo SRE practice (xem §40.5 SLO) |

#### BatteryService reports

| Report | Endpoint | Output |
|--------|----------|--------|
| Battery Health by Type | `GET /api/v1/reports/battery-health-by-type` | Array<{typeId, name, totalAssets, withActiveAlerts, healthScore}> |
| Alert Volume | `GET /api/v1/reports/alert-volume?granularity=day` | TimeSeries |
| Top Anomaly Types | `GET /api/v1/reports/top-anomalies?from=&to=&limit=10` | Array<{anomalyType, count, criticalCount}> |
| Asset Lifecycle | `GET /api/v1/reports/asset-lifecycle` | Array<{assetId, ageDays, cycleCount, alertsTotal}> — `cycleCount` là metric **battery health** (số chu kỳ sạc/xả từ BMS), **không** phải energy throughput; xem scope §53.1 |
| Warranty Expiry | `GET /api/v1/reports/warranty-expiring?within=90d` | Array<BatteryAsset> |
| **Environmental Incident Report** (Sprint 7) | `GET /api/v1/reports/environmental-incidents?from=&to=&siteId=&type=` | Array<{siteId, incidentType, severity, detectedAt, resolvedAt, durationHours, wasFalseAlarm}> — Manager/Admin |
| **Ambient Temperature Trend** (Sprint 7) | `GET /api/v1/reports/ambient-trend?siteId=&from=&to=&granularity=day` | TimeSeries<{date, avgTemp, maxTemp, minTemp, humidityAvg, irradianceAvg}> — Customer (own site) / Manager / Admin |

### 5.3. Export
- Mỗi report có optional `?format=csv` hoặc `?format=xlsx` → return file download.
- Sử dụng `ClosedXML` cho xlsx (lightweight, no Excel install required).

---

# Phần III — Hạ tầng & cross-cutting

## 6. TimescaleDB integration — P1

### 6.1. Đổi Postgres image
TimescaleDB vẫn là PostgreSQL 16 có thêm extension. Việc đổi image không biến toàn bộ database thành time-series database:

- AuthService, TicketService, NotificationService vẫn dùng table PostgreSQL thường.
- Chỉ các bảng time-series như `sensor_readings`, `iot_device_heartbeats`, `analytics_events` mới gọi `create_hypertable(...)`.
- Test đổi image phải chạy trên branch riêng và verify AuthService migrations/build không bị ảnh hưởng trước khi merge.

```yaml
# docker-compose.yml
postgres:
  image: timescale/timescaledb:latest-pg16
  # giữ nguyên rest of config (port 5433, volume, env vars)
```

### 6.2. Migration đầu tiên BatteryService
```csharp
public partial class InitialBatterySchema : Migration {
    protected override void Up(MigrationBuilder mb) {
        // Standard tables
        mb.CreateTable("battery_types", ...);
        mb.CreateTable("threshold_configs", ...);
        mb.CreateTable("battery_assets", ...);
        mb.CreateTable("alerts", ...);

        // SensorReading hypertable
        mb.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");
        mb.CreateTable("sensor_readings", t => new {
            Time = t.Column<DateTime>("time", nullable: false),
            BatteryAssetId = t.Column<Guid>("battery_asset_id", nullable: false),
            Voltage = t.Column<decimal>("voltage", precision: 6, scale: 2, nullable: false),
            Current = t.Column<decimal>("current", precision: 8, scale: 2, nullable: false),
            Temperature = t.Column<decimal>("temperature", precision: 5, scale: 2, nullable: false),
            SocPercent = t.Column<decimal>("soc_percent", precision: 5, scale: 2, nullable: false),
            CycleCount = t.Column<int>("cycle_count", nullable: true),
            SourceDeviceId = t.Column<string>("source_device_id", maxLength: 64, nullable: true)
        });
        mb.Sql("SELECT create_hypertable('sensor_readings', 'time', if_not_exists => TRUE);");
        mb.Sql("CREATE INDEX idx_sr_asset_time ON sensor_readings (battery_asset_id, time DESC);");

        // Retention policy (90 days raw)
        mb.Sql("SELECT add_retention_policy('sensor_readings', INTERVAL '90 days');");
    }

    protected override void Down(MigrationBuilder mb) {
        mb.Sql("SELECT remove_retention_policy('sensor_readings', if_exists => TRUE);");
        mb.DropTable("sensor_readings");
        mb.DropTable("alerts");
        mb.DropTable("battery_assets");
        mb.DropTable("threshold_configs");
        mb.DropTable("battery_types");
    }
}
```

### 6.3. Continuous aggregates (Sprint sau)
```sql
CREATE MATERIALIZED VIEW sensor_readings_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time) AS bucket,
    battery_asset_id,
    AVG(voltage) AS avg_voltage, MIN(voltage) AS min_voltage, MAX(voltage) AS max_voltage,
    AVG(current) AS avg_current,
    MAX(temperature) AS max_temperature, AVG(temperature) AS avg_temperature,
    AVG(soc_percent) AS avg_soc, MIN(soc_percent) AS min_soc
FROM sensor_readings
GROUP BY bucket, battery_asset_id;

SELECT add_continuous_aggregate_policy('sensor_readings_hourly',
    start_offset => INTERVAL '2 hours',
    end_offset => INTERVAL '1 hour',
    schedule_interval => INTERVAL '30 minutes');
```

### 6.4. Query strategy
- `granularity=1m` → query raw `sensor_readings`
- `granularity=1h` → query `sensor_readings_hourly`
- `granularity=1d` → manual aggregate hoặc continuous aggregate `_daily`

---

## 6bis. FileStorage metadata foundation — P1

### 6bis.1. Lý do

FileStorageService hiện tại upload trực tiếp lên MinIO/S3 và trả `objectKey`. Cách này đủ cho demo upload/download đơn giản nhưng chưa đủ cho business flow:

- `AccountProfile.AvatarFileId`
- `TicketAttachment.FileId`
- `MaintenanceLog.BeforePhotosFileIds`
- `IotFirmwareRelease.FileId`

Các service trên cần tham chiếu file bằng `fileId` ổn định, không nên lưu raw `objectKey` của object storage.

### 6bis.2. Quyết định

Bổ sung metadata DB cho FileStorageService:

- Thêm `FileStorageService.Domain` nếu service hiện tại chưa có Domain project.
- Thêm entity `UploadedFile` kế thừa `AuditableEntity`.
- Thêm enum `FilePurposeEnum` và `FileStatusEnum`.
- Thêm `ApplicationDbContext`, EF configuration, migration `AddUploadedFileMetadata`.
- Upload flow: upload binary lên object storage trước, tạo `UploadedFile` metadata sau khi upload thành công, response trả `fileId`.

### 6bis.3. Entity `UploadedFile`

| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | `fileId` trả về cho Auth/Ticket/MaintenanceLog |
| `BucketName` | string(100) | MinIO/S3 bucket |
| `ObjectKey` | string(500) | đường dẫn object storage, internal detail |
| `OriginalFileName` | string(255) | tên file client upload |
| `ContentType` | string(100) | whitelist theo purpose |
| `SizeBytes` | long | validate max size |
| `Purpose` | FilePurposeEnum | Avatar, TicketAttachment, MaintenancePhoto, KbImage, Firmware, Other |
| `UploadedByUserId` | Guid? | null nếu system/internal |
| `Status` | FileStatusEnum | Uploaded, Processing, Ready, Quarantined, Deleted |
| `ChecksumSha256` | string(64)? | integrity/dedup sau này |
| `DeletedAt` | DateTime? | soft delete/cleanup |
| `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `IsDeleted` | — | từ `AuditableEntity` |

```csharp
public enum FilePurposeEnum
{
    Other = 0,        // ⚠️ Exception to "enum starts at 1" rule — legacy backward compat có chủ ý.
                      // Code FileUploadPolicy xử lý 0 như Other. Không migrate sang 1 để tránh data corruption.
    Avatar = 1,
    TicketAttachment = 2,
    MaintenancePhoto = 3,
    KbImage = 4,
    Firmware = 5
}

public enum FileStatusEnum
{
    Uploaded = 1,
    Processing = 2,
    Ready = 3,
    Quarantined = 4,
    Deleted = 5
}
```

### 6bis.4. Migration

```csharp
public partial class AddUploadedFileMetadata : Migration
{
    protected override void Up(MigrationBuilder mb)
    {
        mb.CreateTable("uploaded_files", t => new
        {
            Id = t.Column<Guid>("id", nullable: false),
            BucketName = t.Column<string>("bucket_name", maxLength: 100, nullable: false),
            ObjectKey = t.Column<string>("object_key", maxLength: 500, nullable: false),
            OriginalFileName = t.Column<string>("original_file_name", maxLength: 255, nullable: false),
            ContentType = t.Column<string>("content_type", maxLength: 100, nullable: false),
            SizeBytes = t.Column<long>("size_bytes", nullable: false),
            Purpose = t.Column<int>("purpose", nullable: false),
            UploadedByUserId = t.Column<Guid>("uploaded_by_user_id", nullable: true),
            Status = t.Column<int>("status", nullable: false, defaultValue: 3),
            ChecksumSha256 = t.Column<string>("checksum_sha256", maxLength: 64, nullable: true),
            DeletedAt = t.Column<DateTime>("deleted_at", nullable: true),
            CreatedAt = t.Column<DateTime>("created_at", nullable: false),
            CreatedBy = t.Column<Guid>("created_by", nullable: true),
            UpdatedAt = t.Column<DateTime>("updated_at", nullable: true),
            UpdatedBy = t.Column<Guid>("updated_by", nullable: true),
            IsDeleted = t.Column<bool>("is_deleted", nullable: false, defaultValue: false)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_uploaded_files", x => x.Id);
        });

        mb.CreateIndex("ix_uploaded_files_object_key", "uploaded_files", "object_key", unique: true);
        mb.CreateIndex("ix_uploaded_files_uploaded_by_purpose", "uploaded_files", new[] { "uploaded_by_user_id", "purpose", "is_deleted" });
        mb.CreateIndex("ix_uploaded_files_status_created", "uploaded_files", new[] { "status", "created_at" });
    }

    protected override void Down(MigrationBuilder mb)
    {
        mb.DropTable("uploaded_files");
    }
}
```

### 6bis.5. API contract update

```
POST   /api/v1/files/upload                             (multipart)
GET    /api/v1/files/{id}/metadata
GET    /api/v1/files/{id}/presigned-url?variant=original
GET    /api/v1/files/{id}/download
DELETE /api/v1/files/{id}                               (soft delete metadata + delete object or schedule cleanup)
```

Upload request:
```http
POST /api/v1/files/upload
Content-Type: multipart/form-data

file=<avatar.png>
purpose=Avatar
```

Upload response:
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Upload file thành công.",
  "data": {
    "fileId": "6c9f6e5d-bf26-49e0-a2f4-7e1d2e3a5c90",
    "objectKey": "avatars/6c9f6e5dbf2649e0a2f47e1d2e3a5c90.png",
    "fileName": "avatar.png",
    "contentType": "image/png",
    "sizeBytes": 123456,
    "purpose": "Avatar",
    "status": "Ready",
    "publicUrl": null
  },
  "listErrors": []
}
```

`objectKey` có thể trả cho debug/backward compatibility, nhưng service khác không được lưu `objectKey` làm foreign reference. Chỉ lưu `fileId`.

> **Sprint 1 note:** `publicUrl` luôn `null` khi dùng MinIO local. FE phải handle `null` và fallback về `GET /{id}/download`. Khi deploy production với public bucket, `publicUrl` sẽ có giá trị — test lại path này trước khi go-live.

**HTTP error codes cho file endpoints:**
| Code | Trường hợp |
|------|-----------|
| `404` | `fileId` không tồn tại hoặc đã bị xóa |
| `403` | File thuộc user khác (không phải Admin) |
| `409` | File đang ở trạng thái `Processing` hoặc `Quarantined` — áp dụng cho **cả `GET /{id}/download` lẫn `GET /{id}/presigned-url`** |

**Chuẩn hóa `objectKey` (legacy endpoints):** trim whitespace, reject nếu chứa `..` (path traversal). Không lowercase. Client phải truyền đúng `objectKey` nhận được từ upload response.

### 6bis.6. Validation theo purpose

| Purpose | Max size | Content type |
|---------|----------|--------------|
| Avatar | 5MB | image/png, image/jpeg, image/webp |
| TicketAttachment | 10MB | image/png, image/jpeg, application/pdf |
| MaintenancePhoto | 10MB | image/png, image/jpeg |
| KbImage | 5MB | image/png, image/jpeg, image/webp |
| Firmware | configurable | application/octet-stream, application/x-binary |

### 6bis.7. Sprint 1 scope

Sprint 1 chỉ cần metadata foundation:

- `UploadedFile` entity + enum + migration.
- Upload trả `fileId`.
- Get metadata by `fileId`.
- Get presigned URL by `fileId`.
- Delete by `fileId`.
- Chưa cần resize, EXIF strip, virus scan, variants. Các phần đó nằm ở §62.

---

## 7. Mở rộng AuthService cho profile + skill — P1

### 7.1. Quyết định
KHÔNG tách UserService trong scope capstone, nhưng cũng KHÔNG nhét staff-specific fields trực tiếp vào bảng `Account`.

AuthService vẫn là owner của identity/profile metadata. `Account` giữ vai trò bảng identity chung cho toàn hệ thống; các thông tin mở rộng được tách thành extension tables:

- `AccountProfile`: thông tin hồ sơ chung cho mọi role.
- `StaffProfile`: thông tin phục vụ phân công công việc cho role Staff.
- `StaffSkill`: skill matrix dạng normalized để query/filter Staff theo kỹ năng.

### 7.2. Entity bổ sung

#### `AccountProfile` (1-1 với `Account`)
| Field | Type | Note |
|-------|------|------|
| `AccountId` | Guid | PK/FK → `accounts.id` |
| `AvatarFileId` | Guid? | file nội bộ user upload, reference FileStorageService `UploadedFile.Id` |
| `ExternalAvatarUrl` | string(1000)? | avatar từ provider ngoài như Google `picture` |
| `AvatarSource` | enum | 0=None, 1=Uploaded, 2=Google *(None=0 là sentinel exception — không có avatar; Uploaded/Google bắt đầu từ 1 theo rule)* |
| `Address` | string(500)? | profile chung |
| `BirthDate` | DateOnly? | phục vụ compliance/minor policy sau này |
| `TimeZone` | string(64) | default `Asia/Ho_Chi_Minh` |
| `CreatedAt`, `UpdatedAt` | DateTime | audit nhẹ |

#### `StaffProfile` (1-1 với `Account`, chỉ role Staff)
| Field | Type | Note |
|-------|------|------|
| `AccountId` | Guid | PK/FK → `accounts.id` |
| `EmployeeCode` | string(20) | UNIQUE, mã nhân viên |
| `Department` | string(100)? | bộ phận |
| `MaxConcurrentTickets` | int | default 10, TicketService dùng để validate assign |
| `IsAvailable` | bool | Manager có thể tạm ẩn Staff khỏi assignment queue |
| `SkillTier` | `StaffSkillTierEnum` | **B6** NOT NULL default `Generalist` — phân tầng cho SLA escalation routing |
| `Notes` | string(500)? | ghi chú nội bộ |
| `CreatedAt`, `UpdatedAt` | DateTime | audit nhẹ |

```csharp
// B6 — Staff skill tier theo SLA escalation tier
public enum StaffSkillTierEnum
{
    Generalist = 1,        // Tier 1 — xử lý ticket toàn diện scope SingleAsset, P3/P2
    ModuleSpecialist = 2,  // Tier 2 — chuyên 1 module (BMS, charging, thermal) — P2/P1 module
    SeniorSpecialist = 3   // Tier 3 — chuyên sâu lĩnh vực (LiFePO4 chemistry, NMC failure analysis) — P1 site/multi-site
}
```

**Routing logic (TicketService assign):**
- Auto-create ticket có Priority được tính từ Matrix §2.4bis:
  - `P3` → ưu tiên gán `Tier1 (Generalist)`
  - `P2` → `Tier2 (ModuleSpecialist)`, fallback Tier 1 nếu Tier 2 không có ai available
  - `P1` → `Tier3 (SeniorSpecialist)`, fallback Tier 2
- Khi `Ticket.EscalatedAt` set + `EscalationReason = SkillGap` → Manager BẮT BUỘC gán Staff Tier ≥ Tier 2.
- Sau escalation, chỉ Staff Tier match mới được resolve (xem §2.4.2.bis B7).

#### `StaffSkill` (nhiều skill cho 1 Staff)
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `StaffAccountId` | Guid | FK → `staff_profiles.account_id` |
| `SkillCode` | string(50) | ví dụ `LiFePO4`, `NMC`, `OnSite`, `BMS` |
| `SkillLevel` | int | 1=Basic, 2=Intermediate, 3=Advanced |
| `CertifiedUntil` | DateTime? | optional |

**Constraint:** unique `(StaffAccountId, SkillCode)`.

> **Phân biệt `SkillTier` (StaffProfile) vs `SkillLevel` (StaffSkill):**
> - `SkillTier` = **tầng năng lực tổng quát** (Generalist/Specialist/Senior) — quyết định routing SLA.
> - `SkillLevel` = **độ thành thạo từng skill cụ thể** (LiFePO4 Basic vs Advanced) — để Manager match đúng người cho ticket cụ thể.

#### Avatar source rule
```csharp
public enum AvatarSourceEnum
{
    None = 0,
    Uploaded = 1,
    Google = 2
}
```

`AvatarFileId` chỉ dùng cho file do user upload vào FileStorageService. Không lưu Google avatar URL vào `AvatarFileId`.

Rule resolve avatar cho FE:
1. Nếu `AvatarFileId != null` và file còn usable → `displayAvatarUrl` = presigned/public URL từ FileStorageService.
2. Nếu không có uploaded avatar nhưng `ExternalAvatarUrl != null` → `displayAvatarUrl` = Google/external URL.
3. Nếu cả hai null → `displayAvatarUrl = null`, FE hiển thị initials/default avatar.

Google login chỉ cập nhật `ExternalAvatarUrl` khi user chưa upload avatar nội bộ. Không được ghi đè avatar do user tự upload.

### 7.3. Migration
```csharp
public partial class AddAccountProfileExtensionTables : Migration {
    protected override void Up(MigrationBuilder mb) {
        mb.CreateTable("account_profiles", t => new {
            AccountId = t.Column<Guid>("account_id", nullable: false),
            AvatarFileId = t.Column<Guid>("avatar_file_id", nullable: true),
            ExternalAvatarUrl = t.Column<string>("external_avatar_url", maxLength: 1000, nullable: true),
            AvatarSource = t.Column<int>("avatar_source", nullable: false, defaultValue: 0),
            Address = t.Column<string>("address", maxLength: 500, nullable: true),
            BirthDate = t.Column<DateOnly>("birth_date", nullable: true),
            TimeZone = t.Column<string>("time_zone", maxLength: 64, nullable: false, defaultValue: "Asia/Ho_Chi_Minh"),
            CreatedAt = t.Column<DateTime>("created_at", nullable: false),
            UpdatedAt = t.Column<DateTime>("updated_at", nullable: true)
        }, constraints: table => {
            table.PrimaryKey("pk_account_profiles", x => x.AccountId);
            table.ForeignKey("fk_account_profiles_accounts", x => x.AccountId, "accounts", "id", onDelete: ReferentialAction.Cascade);
        });

        mb.CreateTable("staff_profiles", t => new {
            AccountId = t.Column<Guid>("account_id", nullable: false),
            EmployeeCode = t.Column<string>("employee_code", maxLength: 20, nullable: false),
            Department = t.Column<string>("department", maxLength: 100, nullable: true),
            MaxConcurrentTickets = t.Column<int>("max_concurrent_tickets", nullable: false, defaultValue: 10),
            IsAvailable = t.Column<bool>("is_available", nullable: false, defaultValue: true),
            Notes = t.Column<string>("notes", maxLength: 500, nullable: true),
            CreatedAt = t.Column<DateTime>("created_at", nullable: false),
            UpdatedAt = t.Column<DateTime>("updated_at", nullable: true)
        }, constraints: table => {
            table.PrimaryKey("pk_staff_profiles", x => x.AccountId);
            table.ForeignKey("fk_staff_profiles_accounts", x => x.AccountId, "accounts", "id", onDelete: ReferentialAction.Cascade);
        });
        mb.CreateIndex("ix_staff_profiles_employee_code", "staff_profiles", "employee_code", unique: true);

        mb.CreateTable("staff_skills", t => new {
            Id = t.Column<Guid>("id", nullable: false),
            StaffAccountId = t.Column<Guid>("staff_account_id", nullable: false),
            SkillCode = t.Column<string>("skill_code", maxLength: 50, nullable: false),
            SkillLevel = t.Column<int>("skill_level", nullable: false),
            CertifiedUntil = t.Column<DateTime>("certified_until", nullable: true)
        }, constraints: table => {
            table.PrimaryKey("pk_staff_skills", x => x.Id);
            table.ForeignKey("fk_staff_skills_staff_profiles", x => x.StaffAccountId, "staff_profiles", "account_id", onDelete: ReferentialAction.Cascade);
        });
        mb.CreateIndex("ix_staff_skills_staff_skill", "staff_skills", new[] { "staff_account_id", "skill_code" }, unique: true);
    }
    protected override void Down(MigrationBuilder mb) {
        mb.DropTable("staff_skills");
        mb.DropTable("staff_profiles");
        mb.DropTable("account_profiles");
    }
}
```

### 7.4. New endpoints
```
GET    /api/v1/auth/staff?skill=LiFePO4                  (Manager — for assignment)
GET    /api/v1/auth/staff/{id}/assignment-profile        (internal — TicketService validate staff active/skill/capacity)
PUT    /api/v1/auth/admin/staff/{id}/profile             (Admin/Manager — update StaffProfile)
POST   /api/v1/auth/admin/staff/{id}/skills              (Admin/Manager — add/update skill)
DELETE /api/v1/auth/admin/staff/{id}/skills/{skillCode}  (Admin/Manager)
GET    /api/v1/auth/me                                   (mọi role — profile response cho FE)
PUT    /api/v1/auth/me/profile                           (mọi role update profile)
POST   /api/v1/auth/me/avatar                            (mọi role — body `{ "avatarFileId": "..." }`)
```

**Lưu ý luồng assign:** AuthService chỉ trả staff metadata (`IsAvailable`, `MaxConcurrentTickets`, skills). TicketService vẫn là nơi đếm workload thực tế vì ticket active thuộc DB của TicketService.

### 7.5. New integration event
- `AccountProfileUpdatedEvent`
- `StaffProfileUpdatedEvent`
- `StaffSkillsUpdatedEvent` → TicketService có thể invalidate/cache lại skill matrix.
- `PermissionsChangedEvent` (Sprint 5B `#241`) → publish khi seed/cập nhật role-permission mapping; các service downstream (TicketService cache permission, ApiGateway) invalidate cache. Cần cho task `#241` AuthService seed `ticket.saga.view`/`ticket.saga.reprocess`.

### 7.5bis. Sprint 5B — Permission seed update (`#241`)
- Data migration `SeedSagaPermissions` thêm 2 row vào `permissions` table: `ticket.saga.view`, `ticket.saga.reprocess`.
- Data migration `BindSagaPermissionsToRoles`: bind `Admin` cả 2; bind `Manager` chỉ `ticket.saga.view`.
- Publish `PermissionsChangedEvent` qua Outbox sau khi commit.
- Integration test: login Admin/Manager → decode JWT → assert claim chứa permission đúng.
- Migration phải có Down() để rollback cleanly nếu cần.

### 7.6. Avatar upload & Google avatar flow

#### User upload avatar nội bộ
```
FE/Mobile
  └─ POST /api/v1/files/upload (purpose=avatar, multipart file)
        └─ FileStorageService upload MinIO/S3 + tạo UploadedFile metadata
              └─ response { fileId, objectKey, contentType, sizeBytes, status }
  └─ POST /api/v1/auth/me/avatar { "avatarFileId": fileId }
        └─ AuthService update AccountProfile.AvatarFileId, AvatarSource=Uploaded
```

AuthService không xử lý multipart stream. FileStorageService là service duy nhất quản lý binary file, metadata, signed URL, cleanup, resize/scan sau này.

Không xóa avatar cũ ngay khi user đổi avatar. Chỉ đổi `AvatarFileId`; file cũ để cleanup job xử lý sau để tránh lỗi khi client/cache còn dùng URL cũ.

#### Google login avatar
Google ID token/profile trả về `picture` URL. AuthService lưu URL này vào `AccountProfile.ExternalAvatarUrl`, không upload vào FileStorageService ở Sprint 1.

Pseudo-flow:
```csharp
var googleUser = await _googleOAuthHelper.ValidateAsync(idToken, ct);
// googleUser.Picture = "https://lh3.googleusercontent.com/..."

if (newAccount)
{
    await _unitOfWork.Accounts.AddAsync(account);
    await _unitOfWork.AccountProfiles.AddAsync(new AccountProfile
    {
        AccountId = account.Id,
        ExternalAvatarUrl = googleUser.Picture,
        AvatarSource = string.IsNullOrWhiteSpace(googleUser.Picture)
            ? AvatarSourceEnum.None
            : AvatarSourceEnum.Google,
        TimeZone = "Asia/Ho_Chi_Minh"
    });
}
else if (profile.AvatarFileId == null && !string.IsNullOrWhiteSpace(googleUser.Picture))
{
    profile.ExternalAvatarUrl = googleUser.Picture;
    profile.AvatarSource = AvatarSourceEnum.Google;
}
```

Nếu sau này muốn hệ thống tự quản lý cả avatar Google, có thể thêm background/command tải ảnh từ Google về FileStorageService. Việc đó phải có SSRF guard, content-type validation, file-size limit, và không nằm trong Sprint 1.

#### Profile response cho FE

FE chỉ cần dùng `data.profile.displayAvatarUrl` để render avatar. Backend resolve theo priority Uploaded → Google → null.

Customer login Google, chưa upload avatar:
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Lấy thông tin profile thành công.",
  "data": {
    "id": "9f0c1b4e-2b2a-4f43-83de-89e12c9b6f2a",
    "email": "nguyenvana@gmail.com",
    "phoneNumber": null,
    "fullName": "Nguyen Van A",
    "status": 1,
    "emailConfirmed": true,
    "phoneConfirmed": false,
    "twoFactorEnabled": false,
    "roles": ["Customer"],
    "profile": {
      "avatarFileId": null,
      "externalAvatarUrl": "https://lh3.googleusercontent.com/a/ACg8ocK...",
      "avatarSource": "Google",
      "displayAvatarUrl": "https://lh3.googleusercontent.com/a/ACg8ocK...",
      "address": null,
      "birthDate": null,
      "timeZone": "Asia/Ho_Chi_Minh"
    },
    "staffProfile": null,
    "lastLoginAt": "2026-05-13T09:30:00Z",
    "createdAt": "2026-05-13T09:30:00Z",
    "updatedAt": null
  },
  "listErrors": []
}
```

User đã upload avatar nội bộ:
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Lấy thông tin profile thành công.",
  "data": {
    "id": "9f0c1b4e-2b2a-4f43-83de-89e12c9b6f2a",
    "email": "nguyenvana@gmail.com",
    "phoneNumber": "0901234567",
    "fullName": "Nguyen Van A",
    "status": 1,
    "emailConfirmed": true,
    "phoneConfirmed": true,
    "twoFactorEnabled": false,
    "roles": ["Customer"],
    "profile": {
      "avatarFileId": "6c9f6e5d-bf26-49e0-a2f4-7e1d2e3a5c90",
      "externalAvatarUrl": "https://lh3.googleusercontent.com/a/ACg8ocK...",
      "avatarSource": "Uploaded",
      "displayAvatarUrl": "https://minio-or-cdn.example.com/avatars/6c9f6e5d.png?signature=...",
      "address": "Quận 7, TP.HCM",
      "birthDate": "2002-04-20",
      "timeZone": "Asia/Ho_Chi_Minh"
    },
    "staffProfile": null,
    "lastLoginAt": "2026-05-13T09:30:00Z",
    "createdAt": "2026-05-01T08:00:00Z",
    "updatedAt": "2026-05-13T09:40:00Z"
  },
  "listErrors": []
}
```

Staff profile:
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "Lấy thông tin profile thành công.",
  "data": {
    "id": "2b6e47c2-f3c0-4de7-9e1d-3d90f1b5c12a",
    "email": "staff1@gsu26se55.com",
    "phoneNumber": "0912345678",
    "fullName": "Pham Huu Long",
    "status": 1,
    "emailConfirmed": true,
    "phoneConfirmed": true,
    "twoFactorEnabled": false,
    "roles": ["Staff"],
    "profile": {
      "avatarFileId": null,
      "externalAvatarUrl": null,
      "avatarSource": "None",
      "displayAvatarUrl": null,
      "address": "Thu Duc, TP.HCM",
      "birthDate": "1998-10-12",
      "timeZone": "Asia/Ho_Chi_Minh"
    },
    "staffProfile": {
      "employeeCode": "STF001",
      "department": "Maintenance",
      "maxConcurrentTickets": 10,
      "isAvailable": true,
      "skills": [
        {
          "skillCode": "LiFePO4",
          "skillLevel": 3,
          "skillLevelName": "Advanced",
          "certifiedUntil": "2027-01-01T00:00:00Z"
        },
        {
          "skillCode": "OnSite",
          "skillLevel": 2,
          "skillLevelName": "Intermediate",
          "certifiedUntil": null
        }
      ]
    },
    "lastLoginAt": "2026-05-13T09:30:00Z",
    "createdAt": "2026-05-01T08:00:00Z",
    "updatedAt": "2026-05-12T10:00:00Z"
  },
  "listErrors": []
}
```

---

## 8. Cross-cutting concerns — P1

### 8.1. Outbox pattern cho mọi service publish event

#### Pattern (đã có trong AuthService — copy structure)
1. Entity `OutboxMessage`:
   ```csharp
   public class OutboxMessage {
       public Guid Id { get; set; }
       public string EventType { get; set; } = string.Empty;  // typeof(T).FullName
       public string Payload { get; set; } = string.Empty;     // JSON
       public DateTime OccurredOnUtc { get; set; }
       public DateTime? ProcessedOnUtc { get; set; }
       public int RetryCount { get; set; }
       public string? Error { get; set; }
       public string CorrelationId { get; set; } = string.Empty;
   }
   ```
2. Trong handler — thay vì publish trực tiếp:
   ```csharp
   await _uow.OutboxMessages.AddAsync(new OutboxMessage {
       EventType = typeof(BatteryAnomalyDetectedEvent).FullName!,
       Payload = JsonSerializer.Serialize(evt),
       OccurredOnUtc = DateTime.UtcNow,
       CorrelationId = _correlation.Get()
   });
   await _uow.CommitTransactionAsync();  // atomic với business changes
   ```
3. `OutboxRelayBackgroundService` (5s tick):
   - Lấy 100 message chưa processed.
   - Deserialize → publish qua MassTransit.
   - Mark `ProcessedOnUtc = now`.
   - Exception → tăng `RetryCount`, ghi `Error`, retry exponential backoff đến max 5 lần → dead letter queue.

### 8.2. Inbox idempotency cho consumer

`SharedInfrastructure/Idempotency` đã có `RedisInboxStore`, nhưng Inbox phải tuân thủ nguyên tắc:

1. Không đánh dấu message là processed trước khi business action commit thành công.
2. Nếu consumer throw exception, message phải được phép retry.
3. Với consumer thay đổi database, ưu tiên durable Inbox cùng database/transaction hoặc MassTransit EF Inbox.
4. Redis Inbox chỉ dùng khi có cơ chế `processing lease → completed`; key `completed` chỉ set sau action thành công.
5. Mọi command consumer của Saga vẫn phải idempotent ở database bằng unique constraint/business key, không chỉ dựa vào Redis.

**Quyết định cho Sprint 5B:** mọi Saga/participant consumer thay đổi DB dùng MassTransit EF Consumer Outbox
trên chính service DbContext, với bảng durable `mt_inbox_state`, `mt_outbox_state`, `mt_outbox_message`
(đặt tên/schema riêng để không đụng custom `outbox_messages`). Redis Inbox hiện tại không được dùng
cho các endpoint này vì `TryMarkProcessedAsync` đang ghi key trước business commit.

Pseudo-code cho consumer còn dùng custom Inbox (không áp dụng thủ công cho Saga endpoint đã dùng EF Consumer Outbox):
```csharp
public class AccountActivatedConsumer : IConsumer<AccountActivatedEvent> {
    private readonly IInboxStore _inbox;

    public async Task Consume(ConsumeContext<AccountActivatedEvent> ctx) {
        var messageId = ctx.Message.Id;
        if (await _inbox.IsCompletedAsync(messageId, nameof(AccountActivatedConsumer)))
            return;

        await ProcessAndCommitAsync(ctx.Message, ctx.CancellationToken);
        await _inbox.MarkCompletedAsync(
            messageId,
            nameof(AccountActivatedConsumer),
            ttl: TimeSpan.FromDays(7));
    }
}
```

### 8.3. Alert–Ticket Saga (MassTransit State Machine)

#### 8.3.1. Vì sao cần Saga

Luồng `Critical Alert → auto-create/reuse Ticket → cập nhật Alert.TicketId` đi qua hai database:

- BatteryService sở hữu `Alert`.
- TicketService sở hữu `Ticket`.

Transaction local hoặc Outbox một chiều không thể đảm bảo cả hai phía cùng hoàn tất. Hiện trạng có thể tạo Ticket thành công nhưng `Alert.TicketId` vẫn null, hoặc redelivery tạo Ticket trùng. Sprint 5B triển khai Saga orchestration để theo dõi toàn bộ workflow và hỗ trợ retry/timeout/reprocess.

#### 8.3.2. Saga ownership và correlation

- Saga host: **TicketService**.
- Persistence: EF Core Saga repository trong `ticket_db`.
- Table: `alert_ticket_saga_states`.
- `CorrelationId`: dùng chính `AlertId`.
- Initial event phải cấu hình `CorrelateById(x => x.Message.AlertId)` + `InsertOnInitial`;
  mọi success/rejection/timeout response correlate theo `CorrelationId`.
- Với `Fault<CreateTicketFromAlertCommand>` và `Fault<LinkAlertToTicketCommand>`, correlation lấy từ
  command lồng bên trong: `x.Message.Message.CorrelationId`; không lấy nhầm `FaultId`.
- `ReconcileAlertTicketSagaCommand` cũng được phép `InsertOnInitial`, nhưng phải mang đủ
  `BatteryAssetId`/`CustomerId`, khởi tạo thẳng `TicketProvisioned` với Ticket hiện hữu và tuyệt đối
  không chạy bước create.
- Một Alert chỉ có tối đa một Saga instance.
- State `Completed` được giữ trong DB, không xóa ngay bằng `SetCompletedWhenFinalized`.
  Completed row là durable tombstone chống event cũ tạo lại Saga. Trong capstone không chạy cleanup
  Saga tombstone; nếu bổ sung retention sau này thì thời gian giữ phải ít nhất bằng retention của Alert
  và Inbox dedup tương ứng.
- `Completed` và `Failed` là persisted operational states. State machine phải cấu hình explicit
  ignore/no-op cho duplicate start và late message hợp lệ; không để message đi `_skipped` chỉ vì Saga
  đã terminal. Late message bất thường vẫn ghi metric/audit trước khi ignore.
- Saga không thay thế `TicketStateMachine`; Saga quản lý consistency liên service, còn `TicketStateMachine` quản lý lifecycle nghiệp vụ trong TicketService.

#### 8.3.3. States

```text
Initial
  → TicketRequested
  → TicketProvisioned     # đã tạo mới hoặc reuse ticket active
  → AlertLinkRequested
  → Completed

Any non-terminal state
  → Failed                # hết retry / timeout / business rejection
```

Tên `TicketProvisioned` cố ý tránh dùng từ `Resolved`, vì `TicketStatusEnum.Resolved`
là trạng thái lifecycle hoàn toàn khác.

#### 8.3.4. Message contracts trong SharedContracts

```csharp
public record CreateTicketFromAlertCommand(
    Guid CorrelationId,       // = AlertId
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt);

public record TicketProvisionedForAlertEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string TicketCode,
    bool CreatedNew);

public record TicketProvisionForAlertRejectedEvent(
    Guid CorrelationId,
    Guid AlertId,
    string ErrorCode,
    string Reason,
    bool IsRetryable);

public record LinkAlertToTicketCommand(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string TicketCode);

public record ReconcileAlertTicketSagaCommand(
    Guid CorrelationId,       // = AlertId
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    Guid TicketId,
    string TicketCode);

public record AlertLinkedToTicketEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId);

public record AlertLinkToTicketRejectedEvent(
    Guid CorrelationId,
    Guid AlertId,
    Guid TicketId,
    string ErrorCode,
    string Reason,
    bool IsRetryable);

public record AlertTicketSagaFailedEvent(
    Guid CorrelationId,
    Guid AlertId,
    string FailedStep,
    string Reason,
    DateTime FailedAtUtc);
```

Tất cả contract phải version-safe, không reference enum assembly nội bộ của BatteryService/TicketService.

#### 8.3.5. Workflow

```text
BatteryService
  Alert + BatteryAnomalyDetectedEvent commit cùng Outbox
        ↓
AlertTicketSagaStateMachine (TicketService)
  consume BatteryAnomalyDetectedEvent, correlate AlertId
        ↓ send
CreateTicketFromAlertCommand
        ↓
TicketService consumer
  - tìm ticket theo OriginAlertId trước
  - nếu chưa có, tìm ticket active cùng BatteryAssetId + Category
  - tạo mới hoặc reuse ticket
  - commit Ticket/Activity + TicketProvisionedForAlertEvent cùng Outbox
        ↓
Saga receive TicketProvisionedForAlertEvent
        ↓ send
LinkAlertToTicketCommand
        ↓
BatteryService consumer
  - update Alert.TicketId idempotently
  - commit Alert + AlertLinkedToTicketEvent cùng Outbox
        ↓
Saga receive AlertLinkedToTicketEvent
        ↓
Completed / persist terminal state (không delete row)
```

Nhánh lỗi:

- Business validation không được “return im lặng”; participant publish rejection event có `ErrorCode`,
  `Reason`, `IsRetryable`.
- Exception sau khi retry/redelivery hết được Saga consume qua `Fault<CreateTicketFromAlertCommand>`
  hoặc `Fault<LinkAlertToTicketCommand>`.
- Permanent rejection như `ALERT_NOT_FOUND`, `ASSET_NOT_FOUND`, `CUSTOMER_INVALID`,
  `ALERT_TICKET_CONFLICT` chuyển `Failed` ngay.
- Transient rejection/fault giữ nguyên step và để Saga schedule lần gửi command kế tiếp; không để
  participant tự delayed-redelivery vô hạn.

#### 8.3.6. Dedup và database constraints

- `Ticket.OriginAlertId` có filtered unique index khi không null và `is_deleted=false`.
- Business dedup `(BatteryAssetId + Category + active status)` vẫn giữ để reuse ticket đang xử lý.
- Partial unique guard cho auto-ticket active cùng `(BatteryAssetId, Category)` ngăn hai Alert đồng thời
  cùng tạo Ticket; consumer bắt unique violation và reload Ticket thắng race.
- Nếu reuse ticket, Saga vẫn gửi link command để Alert mới có `TicketId`.
- `Alert.TicketId` là source of truth cho quan hệ many-alerts-to-one-ticket.
  `Ticket.OriginAlertId` chỉ lưu Alert đầu tiên tạo Ticket, không được ghi đè khi reuse cho Alert khác.
- `alerts.ticket_id` có non-unique index để tra toàn bộ Alert đã link tới một Ticket.
- `Alert.TicketId` update phải idempotent:
  - null → set TicketId;
  - cùng TicketId → success/no-op;
  - khác TicketId → conflict, không overwrite âm thầm.
- Saga table có PK/unique `CorrelationId`.
- Consumer không dựa duy nhất vào query `AnyAsync`; unique constraint là lớp bảo vệ cuối trước race condition.

#### 8.3.7. Retry, timeout và failure

- Participant endpoint immediate retry tối đa 3 lần cho lỗi transient ngắn hạn.
- Sau khi participant publish rejection/fault, Saga schedule tối đa 3 lần gửi lại command với delay
  5s, 30s, 2m; tính cả lần đầu là tối đa 4 attempt cho mỗi step.
- Retry schedule và Saga timeout phải dùng **durable scheduler**. RabbitMQ image hiện tại chỉ enable
  management/prometheus, không có delayed-message plugin; TicketService host một persistent Quartz
  scheduler endpoint dùng `ticket_db`. Không cấu hình `UseDelayedRedelivery` ở BatteryService nếu chưa
  trỏ endpoint đó tới scheduler durable.
- Saga schedule timeout:
  - tạo/reuse Ticket: 10 phút;
  - link Alert: 10 phút.
- Saga state lưu riêng `StepTimeoutTokenId` và `RetryTokenId`. Trước khi schedule token mới hoặc khi
  nhận success phải unschedule token không còn hợp lệ để tránh timeout/retry cũ tác động lên Saga đã tiến bước.
- Hết retry/timeout:
  - chuyển Saga sang `Failed`;
  - lưu `FailedStep`, `FailureCode`, `LastError`, attempt count tương ứng và `LastAttemptAtUtc`;
  - publish `AlertTicketSagaFailedEvent`;
  - expose admin endpoint reprocess.

#### 8.3.8. Compensation policy

Luồng này dùng **forward recovery**, không hard-delete Ticket đã tạo:

- Ticket tạo thành công nhưng link Alert thất bại → retry link cho đến khi thành công.
- Ticket creation thất bại → Alert vẫn Open; Saga Failed và Admin có thể reprocess.
- Nếu business rejection vĩnh viễn (asset/customer invalid) → Saga Failed, ghi audit và notify Manager.
- Không rollback/xóa Alert vì Alert là bằng chứng telemetry.
- Không rollback/xóa Ticket tự động vì Ticket có activity/audit. Nếu operator xác nhận duplicate, phải
  re-link Alert về Ticket canonical rồi mới mark duplicate `IsDeleted=true` kèm activity/audit theo runbook.

#### 8.3.9. Outbox và DI bắt buộc sửa trước Saga

- Audit hiện trạng: TicketService đăng ký `OutboxMessagePublisher` trước rồi `AddMessageBus()` đăng ký
  `MassTransitProducer` sau, nên business handler có thể resolve nhầm direct publisher.
- HTTP/background business handler phải inject `IIntegrationEventOutboxWriter`, không dùng chung
  interface với transport.
- Saga và participant consumer không ghi thêm custom Outbox row. Chúng publish command/response/domain
  event qua `ConsumeContext`/MassTransit publish-send endpoint trong consume scope để EF Consumer Outbox
  capture cùng transaction.
- Application operation được participant gọi phải trả outcome và dùng cùng scoped `DbContext`; không
  tự `SaveChanges`/commit hoặc direct-publish trước consumer transaction, tránh double-outbox.
- `OutboxRelayService` phải inject transport publisher riêng, ví dụ `IIntegrationEventTransport`, để tránh relay ghi ngược lại chính Outbox.
- DI test phải assert mỗi interface chỉ có đúng implementation/lifetime dự kiến và relay không resolve Outbox writer.
- Battery/Ticket business change và Outbox row phải commit cùng `DbContext`.
- Relay cần max retry, backoff, lock/claim batch và metric pending/failed.
- `AlertTicketDispatchEnabled=false` chỉ hold row `BatteryAnomalyDetectedEvent` ở trạng thái pending;
  relay phải tiếp tục xử lý event type khác và không mark held row processed. Query/batch selection phải
  loại held type để 100 row đầu không làm starve toàn Outbox.

#### 8.3.10. Admin operations

```text
GET  /api/v1/admin/sagas/alert-ticket?state=Failed
GET  /api/v1/admin/sagas/alert-ticket/{alertId}
POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess
```

Controller phải thin và gọi MediatR Query/Command. Reprocess chỉ hợp lệ với Saga `Failed`,
phải có permission, idempotency key, audit log và không được tạo Ticket trùng.
MediatR handler không sửa trực tiếp Saga row; nó gửi internal
`ReprocessAlertTicketSagaCommand(CorrelationId, RequestedBy, Reason)` vào Saga endpoint. State machine
đọc `FailedStep`, reset attempt budget của đúng step, giữ nguyên `TicketId` nếu đã provisioned và tiếp tục
forward recovery.

#### 8.3.11. Endpoint topology

Đặt endpoint name cố định, không phụ thuộc auto-generated kebab-case để cutover và dashboard không đổi:

```text
ticket-alert-ticket-saga
ticket-create-ticket-from-alert
battery-link-alert-to-ticket
ticket-alert-ticket-quartz
```

- `BatteryAnomalyDetectedEvent` và success/rejection/failure là event: dùng `Publish`.
- `CreateTicketFromAlertCommand`, `LinkAlertToTicketCommand`, reconciliation và reprocess là command:
  dùng `Send` tới đúng endpoint; không `Publish` command.
- Mỗi endpoint cấu hình EF Consumer Outbox, retry policy và dead-letter/error queue riêng.
- Queue direct consumer cũ phải có tên riêng trong cutover; không đổi binding queue cũ thành Saga queue.

#### 8.3.11bis. Endpoint runtime config (Sprint 5B)

Cấu hình MassTransit cho mỗi Saga endpoint trong `Program.cs`/DI registration:

| Endpoint | PrefetchCount | ConcurrentMessageLimit | Lý do |
|----------|--------------|------------------------|-------|
| `ticket-alert-ticket-saga` | 4 | 4 | Saga state DB write per message; cao hơn sẽ gây lock contention trên `alert_ticket_saga_states` row khi cùng Saga có nhiều message dồn dập |
| `ticket-create-ticket-from-alert` | 8 | 8 | Participant write Ticket + Outbox; cap để không drain RabbitMQ queue và bypass back-pressure |
| `battery-link-alert-to-ticket` | 8 | 8 | Tương tự participant |
| `ticket-alert-ticket-quartz` | 16 | 16 | In-memory schedule, không DB-bound |

```csharp
cfg.ReceiveEndpoint("ticket-alert-ticket-saga", e => {
    e.PrefetchCount = 4;
    e.ConcurrentMessageLimit = 4;
    e.UseInMemoryOutbox(); // optional cho test; production dùng EF Consumer Outbox
    e.ConfigureSaga<AlertTicketSagaState>(provider);
    e.UseMessageRetry(r => r.Immediate(3));  // immediate retry 3 lần cho transient
});
```

**Dead-letter queue convention:** mỗi endpoint có `<endpoint>_error` queue (MassTransit auto-create). Alert rule `RabbitMqDeadLetterDepthHigh` (xem §9) trigger nếu depth > 10 sustained 5 phút.

**Quartz cluster checkin:** `quartz.scheduler.instanceId=AUTO`, `quartz.scheduler.makeSchedulerThreadDaemon=true`, `quartz.threadPool.threadCount=10`, `quartz.jobStore.clusterCheckinInterval=10000` (10s). Hai TicketService instance dùng cùng schema sẽ tự coordinate, không double-fire trigger.

### 8.4. Distributed tracing (OpenTelemetry)

```csharp
// Program.cs từng service
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddSource("MassTransit")
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://tempo:4317")));
```

Add Tempo to docker-compose:
```yaml
tempo:
  image: grafana/tempo:latest
  command: ["-config.file=/etc/tempo.yaml"]
  volumes: ["./monitoring/tempo.yaml:/etc/tempo.yaml"]
  ports: ["3200:3200"]
```

### 8.5. Correlation ID propagation
- Đã có middleware + bus filter trong SharedInfrastructure.
- Mọi log thông qua Serilog enricher tự động append `CorrelationId`.

### 8.6. Idempotency-Key cho POST mutating
Middleware `IdempotencyKeyMiddleware` đã có. Apply cho:
- `POST /api/v1/tickets` (Customer mobile có thể retry)
- `POST /api/v1/tickets/{id}/comments`
- `POST /api/sensor-readings/batch`
- `POST /api/v1/notifications/mark-read-bulk`
- `POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess` (Sprint 5B — chống admin click lặp) — header **bắt buộc**, không có header trả 400

Header: `Idempotency-Key: <uuid>` → server lưu response 24h trong Redis.

**Sprint 5B — Saga reprocess idempotency convention:**
- FE generate UUID v4 client-side, gửi cùng request.
- Server flow: lookup Redis key `idem:saga-reprocess:{alertId}:{key}`; nếu hit → return cached response (không re-trigger Saga).
- Saga state cũng track `ManualReprocessCount` để audit; mỗi `Idempotency-Key` chỉ tăng counter 1 lần.
- TTL 24h sau khi Saga chuyển Completed/Failed; xóa sớm nếu admin force-cleanup.

---

## 9. Observability — hoàn thiện P2

### 9.1. Đã có
- Prometheus scrape `/metrics` (chưa wire cho service mới).
- Grafana có default dashboard.
- Loki nhận log Serilog.
- AlertManager + RabbitMQ Prometheus plugin.

### 9.2. Cần thêm

#### Metrics business
Mỗi service đăng ký custom counter:
```csharp
// SharedInfrastructure/Metrics/AppMetrics.cs đã có. Thêm:
public static readonly Counter TicketsCreated = Metrics
    .CreateCounter("tickets_created_total", "Total tickets created", "priority", "category", "origin");

public static readonly Counter SlaBreaches = Metrics
    .CreateCounter("sla_breaches_total", "Total SLA breaches", "priority");

public static readonly Histogram TicketResolutionMinutes = Metrics
    .CreateHistogram("ticket_resolution_minutes", "Time to resolve ticket",
        new HistogramConfiguration {
            LabelNames = new[] { "priority", "category" },
            Buckets = new[] { 30.0, 60, 120, 240, 480, 1440, 2880, 4320 }
        });

public static readonly Counter AlertsDetected = Metrics
    .CreateCounter("battery_alerts_detected_total", "Total alerts", "severity", "anomaly_type");

public static readonly Counter NotificationsSent = Metrics
    .CreateCounter("notifications_sent_total", "Total notifications", "channel", "status");

// Sprint 5B — Alert–Ticket Saga (xem §53.11). Phải register cùng `AppMetrics.cs` để Prometheus scrape.
public static readonly Counter SagaStarted = Metrics
    .CreateCounter("alert_ticket_saga_started_total", "Saga started");
public static readonly Counter SagaCompleted = Metrics
    .CreateCounter("alert_ticket_saga_completed_total", "Saga completed");
public static readonly Counter SagaFailed = Metrics
    .CreateCounter("alert_ticket_saga_failed_total", "Saga failed", "step", "reason");
public static readonly Histogram SagaDuration = Metrics
    .CreateHistogram("alert_ticket_saga_duration_seconds", "Saga duration",
        new HistogramConfiguration { Buckets = new[] { 1.0, 5, 15, 30, 60, 300, 600 } });
public static readonly Gauge SagaStuck = Metrics
    .CreateGauge("alert_ticket_saga_stuck_count", "Stuck Saga (non-progressing > 10min)", "state");
public static readonly Counter TicketReused = Metrics
    .CreateCounter("alert_ticket_ticket_reused_total", "Tickets reused by Saga");
public static readonly Gauge OutboxUnprocessed = Metrics
    .CreateGauge("outbox_unprocessed_count", "Outbox pending rows", "service");
public static readonly Counter InboxProcessingFailed = Metrics
    .CreateCounter("inbox_processing_failed_total", "Inbox processing failures", "consumer");
```

#### Dashboards Grafana
1. **SLA Operations** (panel list):
   - Queue size (open tickets) — gauge per priority
   - Breach rate last 1h/24h
   - Avg resolution time per priority
   - Reopen rate trend
   - Staff workload heatmap

2. **Battery Health**:
   - Active alerts count by severity
   - Top anomaly types pie
   - Asset count by status
   - Sensor ingest rate (msgs/s)

3. **System Health**:
   - Request rate per service
   - Error rate per service
   - P95/P99 latency
   - RabbitMQ queue depth + DLQ count
   - DB connection pool usage
   - Outbox lag (unprocessed count)

4. **Alert–Ticket Saga** (Sprint 5B, xem §53.11):
   - Saga started/completed/failed rate
   - Saga duration P50/P95/P99
   - Stuck Saga gauge by state
   - Ticket reuse vs new ratio
   - Inbox processing failures by consumer

5. **IoT Device Monitoring** (Sprint IoT-1/7, xem §52.12):
   - Devices online/offline count — gauge (`iot_devices_online_count` / `iot_devices_offline_total`)
   - Heartbeat rate by device + last-seen age
   - Sensor readings ingested vs rejected (`iot_sensor_readings_ingested_total` / `iot_sensor_readings_rejected_total{reason}`)
   - Local queue depth per device (phát hiện mất mạng kéo dài)
   - Reject reason breakdown (clock_drift / sensor_outlier)
   - Firmware update status (`iot_firmware_updates_total{status}`)
   - (MQTT P3) broker connected clients + LWT offline events

#### Alert rules (`alertmanager.yaml`)
```yaml
groups:
  - name: business
    rules:
      - alert: SlaBreachRateHigh
        expr: rate(sla_breaches_total[15m]) > 0.1
        for: 5m
        annotations:
          summary: "SLA breach rate > 10% trong 15 phút"

      - alert: OutboxLagging
        expr: outbox_unprocessed_count > 100
        for: 5m

      - alert: ServiceDown
        expr: up{job=~"battery|ticket|notification"} == 0
        for: 1m

      # Sprint 5B — Saga ops (xem §53.11)
      - alert: AlertTicketSagaStuck
        expr: alert_ticket_saga_stuck_count > 0
        for: 10m
        annotations:
          summary: "Saga non-terminal không update > 10 phút — check runbook 09-saga-stuck.md"

      - alert: AlertTicketSagaFailedSpike
        expr: increase(alert_ticket_saga_failed_total[5m]) > 0
        for: 5m
        annotations:
          summary: "Saga Failed phát sinh trong 5 phút — admin reprocess theo runbook 08-saga-failed.md"

      # Sprint IoT-1 — IoT device ops (xem §52.12)
      - alert: IotDevicesOfflineSpike
        expr: increase(iot_devices_offline_total[10m]) > 2
        for: 5m
        annotations:
          summary: "Nhiều IoT device chuyển Offline trong 10 phút — kiểm tra mạng site / nguồn / broker"

      - alert: IotIngestRejectHigh
        expr: rate(iot_sensor_readings_rejected_total[15m]) > 0.2
        for: 5m
        annotations:
          summary: "Tỉ lệ reject reading IoT cao (clock_drift/outlier) > 15 phút — kiểm tra NTP/calibration device"

  # SLO error budget burn rate (xem §40.5)
  - name: slo
    rules:
      # Fast burn — consume 2% budget trong 1h → page on-call
      - alert: SloFastBurn
        expr: |
          (
            1 - (rate(http_requests_success_total[1h]) / rate(http_requests_total[1h]))
          ) > on(service) (1 - slo_target) * 14.4
        for: 5m
        labels:
          severity: page
        annotations:
          summary: "{{ $labels.service }} burning error budget tốc độ 14.4× — sẽ hết budget trong 2 ngày"

      # Slow burn — consume 5% budget trong 6h → notify only
      - alert: SloSlowBurn
        expr: |
          (
            1 - (rate(http_requests_success_total[6h]) / rate(http_requests_total[6h]))
          ) > on(service) (1 - slo_target) * 6
        for: 30m
        labels:
          severity: notify
        annotations:
          summary: "{{ $labels.service }} burning error budget tốc độ 6× — sẽ hết budget trong 5 ngày"

      # Saga-specific burn (Sprint 5B)
      - alert: SagaErrorBudgetFastBurn
        expr: |
          (rate(alert_ticket_saga_failed_total[1h]) / rate(alert_ticket_saga_started_total[1h])) > 0.144
        for: 5m
        labels:
          severity: page
        annotations:
          summary: "Saga Failed rate vượt 14.4% — error budget 99% sẽ hết trong 2 ngày"
```

**Recording rule** cho `slo_target` (Prometheus `prometheus.yml` rules):
```yaml
groups:
  - name: slo_targets
    rules:
      - record: slo_target
        expr: 0.999
        labels: { service: "auth" }
      - record: slo_target
        expr: 0.995
        labels: { service: "battery" }
      - record: slo_target
        expr: 0.999
        labels: { service: "ticket" }
      - record: slo_target
        expr: 0.99
        labels: { service: "notification" }
      - record: slo_target
        expr: 0.99
        labels: { service: "saga" }
```

### 9.3. Structured logging convention
```csharp
_logger.LogInformation("Ticket {TicketId} assigned to Staff {StaffId} with priority {Priority} by Manager {ManagerId}",
    ticket.Id, staffId, priority, currentUserId);
```
- Luôn dùng structured (named placeholders), không string interpolation.
- Loki query: `{service="ticket"} | json | TicketId="..."`.

**Sprint 5B — Saga structured field requirements (xem §53.11):**
- Mọi log thuộc luồng Saga BẮT BUỘC có: `CorrelationId` (= AlertId), `AlertId`, `TicketId` (nếu đã provisioned), `CurrentState`, `MessageId`.
- Thêm khi áp dụng: `TicketAttemptCount`, `AlertLinkAttemptCount`, `FailedStep`, `FailureCode`.
- **KHÔNG** log: PII email/phone từ payload, full JWT, password, OTP, hoặc Saga payload snapshot raw.
- Loki query mẫu cho ops:
  - Saga theo Alert: `{service="ticket"} | json | CorrelationId="<alert-id>"`
  - Saga Failed gần đây: `{service="ticket"} | json | CurrentState="Failed" | line_format "{{.AlertId}} {{.FailedStep}} {{.FailureCode}}"`

---

## 10. API Gateway hoàn thiện — P1

### 10.1. JWT validation tại gateway
- Validate signature + expiry tại gateway.
- Inject claims vào header cho downstream:
  - `X-User-Id: {userId}`
  - `X-User-Role: {role}`
  - `X-User-Email: {email}`
- Downstream service dùng `CurrentUserService` (đã có trong SharedInfrastructure) đọc header thay vì decode JWT lại.

### 10.2. Rate limiting
Tận dụng built-in .NET 8:
```csharp
services.AddRateLimiter(opts => {
    opts.AddFixedWindowLimiter("auth", o => { o.PermitLimit = 10; o.Window = TimeSpan.FromMinutes(1); });
    opts.AddSlidingWindowLimiter("api", o => { o.PermitLimit = 100; o.Window = TimeSpan.FromMinutes(1); o.SegmentsPerWindow = 6; });
});
```
- `/api/v1/auth/login`: 5 req/min per IP
- `/api/v1/auth/forgot-password`: 3 req/hour per IP
- `/api/v1/tickets` POST: 30 req/min per user
- `/api/sensor-readings/batch`: 1000 req/min per ApiKey
- `/api/v1/admin/sagas/alert-ticket/{id}/reprocess` POST: 10 req/min per Admin (chống loop reprocess vô tình)

### 10.3. CORS
```csharp
services.AddCors(opts => opts.AddPolicy("frontend", p => p
    .WithOrigins("http://localhost:5173", "https://app.gsu26se55.com")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
```

### 10.4. Aggregate Swagger
Gateway expose `/swagger` aggregate từ N service:
- `/swagger/auth/v1/swagger.json`
- `/swagger/battery/v1/swagger.json`
- ...

### 10.5. Health & readiness
```
GET /health/live              → 200 if process alive
GET /health/ready             → 200 if DB + Redis + RabbitMQ reachable
GET /health/startup           → 200 after migrations done
GET /health/sync-lag          → MAX(NOW() - LastSyncedAt) per read-model table (xem §2.7)
```
Sprint 5B bổ sung cho TicketService:
```
GET /health/saga              → 200 if alert_ticket_saga_states reachable + Quartz scheduler started + qrtz_triggers table exists; 503 nếu Saga endpoint chưa register hoặc Quartz schema chưa apply (xem §53.8, R-21)
```
Map vào k8s probes; `/health/saga` add vào `readinessProbe` cho TicketService Sprint 5B.

---

# Phần IV — Quality & operations

## 11. Test strategy (coverage ≥ 80%) — P1

### 11.1. Pyramid
```
        E2E (15%)
       ──────────
      Integration (35%)
     ─────────────────
    Unit (50%)
   ───────────────────
```

### 11.2. Unit test stack
- xUnit + Moq + FluentAssertions
- Bộ test mỗi handler:
  - `success` case
  - `validation failure` (mỗi field)
  - `business rule violation` (entity not found, soft deleted, status conflict)
  - `concurrent edit conflict` nếu có

### 11.3. Integration test stack
- `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`
- TestContainers: postgres + redis + rabbitmq
- TimescaleDB cần image `timescale/timescaledb:latest-pg16` trong fixture
- MassTransit `TestHarness` cho event/consumer
- Sprint 5B: thêm fixture cho **EF Consumer Outbox/Inbox** và **Quartz scheduler** — `qrtz_*` schema được initialize trong test container; Saga test phải verify cả persistent timeout/redelivery recovery sau restart (xem §53.10).

### 11.4. Sample test
```csharp
public class TicketAssignCommandHandlerTests {
    [Fact]
    public async Task Should_Start_Sla_Timer_With_P1_4hours() {
        // arrange
        var uow = MockUowFactory.WithTicket(status: TicketStatusEnum.Open);
        var handler = new TicketAssignCommandHandler(uow.Object, ...);
        var cmd = new TicketAssignCommand { TicketId = TicketId, AssignedStaffId = StaffId, Priority = TicketPriorityEnum.P1Critical };

        // act
        var resp = await handler.Handle(cmd, default);

        // assert
        resp.IsSuccess.Should().BeTrue();
        uow.Verify(x => x.SlaTimers.AddAsync(It.Is<SlaTimer>(t =>
            t.Priority == TicketPriorityEnum.P1Critical &&
            (t.DueAt - t.StartedAt).TotalHours == 4)));
    }

    [Fact]
    public async Task Should_Reject_Assign_When_Ticket_Not_Open() {
        var uow = MockUowFactory.WithTicket(status: TicketStatusEnum.Resolved);
        var handler = new TicketAssignCommandHandler(uow.Object, ...);
        var resp = await handler.Handle(new TicketAssignCommand { ... }, default);
        resp.IsSuccess.Should().BeFalse();
        resp.Message.Should().Contain("Open");
    }
}
```

### 11.5. State machine test pattern
```csharp
[Theory]
[InlineData(TicketStatusEnum.Open, TicketStatusEnum.Assigned, ActorRoleEnum.Manager, true)]
[InlineData(TicketStatusEnum.Open, TicketStatusEnum.Assigned, ActorRoleEnum.Staff, false)]   // wrong actor
[InlineData(TicketStatusEnum.Closed, TicketStatusEnum.Open, ActorRoleEnum.Customer, false)]  // can't reopen closed
[InlineData(TicketStatusEnum.ClosedPendingRate, TicketStatusEnum.Open, ActorRoleEnum.Customer, true)]
// ... all 30+ transitions
public void CanTransition_Matrix(TicketStatusEnum from, TicketStatusEnum to, ActorRoleEnum actor, bool expected) {
    var ticket = new Ticket { Status = from };
    var result = _stateMachine.CanTransition(ticket, to, actor, Guid.NewGuid());
    result.IsAllowed.Should().Be(expected);
}
```

### 11.6. CI coverage gate
GitHub Actions step:
```yaml
- name: Coverage gate
  run: |
    dotnet tool install -g dotnet-reportgenerator-globaltool
    reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:"coverage" -reporttypes:"Cobertura"
    THRESHOLD=80
    PCT=$(grep -oP 'line-rate="\K[0-9.]+' coverage/Cobertura.xml | head -1 | awk '{print $1*100}')
    [ "$(echo "$PCT < $THRESHOLD" | bc)" = 1 ] && exit 1 || exit 0
```

### 11.7. CI execution time budget

| Service | Unit test (s) | Integration test (s) | Notes |
|---------|---------------|----------------------|-------|
| AuthService | 30s | 60s | Mature, không thay đổi nhiều |
| BatteryService | 60s | 120s | TimescaleDB fixture nặng |
| TicketService (current) | 60s | 90s | CQRS handlers |
| **TicketService + Saga (Sprint 5B)** | **+90s** | **+300s** (5 phút) | **21+ Saga case** + restart-recovery (kill container × 3 case) + Quartz schema apply |
| NotificationService (Sprint 6+) | 30s | 60s | Channel mocks |
| **Total (Sprint 5B end)** | **4 phút** | **~12 phút** | **PR CI time ~16 phút** |

**Mitigation cho CI time blow up:**
- **detect-changes matrix** (đã có §0.1): Service không changes skip test → save 50-70% time.
- **Parallel test execution**: `dotnet test --parallel` cho mỗi service.
- **Saga test category split**: `[Category=Saga]` tách riêng `[Category=Critical]` (5 case quan trọng nhất, ~60s) cho PR fast feedback; `[Category=Saga-Full]` (21 case, ~5 phút) chạy ở `dev` branch merge.
- **Test container reuse**: MassTransit TestHarness reuse fixture giữa các test case (giảm Quartz schema apply overhead).
- **Quartz schema cache**: pre-built TimescaleDB+Quartz image ở GitHub Actions cache → save 30s per CI run.

Mục tiêu: PR CI time ≤ 10 phút.

---

## 12. Seed data & migration strategy — P1

### 12.1. Seed data scope (script `tools/seed.sh`)

**Accounts (AuthService):**
- 1 Admin: `admin@gsu26se55.com` / `Admin@123`
- 2 Manager: `manager1@`, `manager2@`
- 3 Staff: `staff1@`, `staff2@`, `staff3@` với skills khác nhau
- 5 Customer: `customer1@`...`customer5@`

**Battery (BatteryService):**
- 3 BatteryType: LiFePO4 12V 100Ah / LiFePO4 24V 200Ah / NMC 48V 50Ah
- 3 ThresholdConfig (1 per type)
- 10 BatteryAsset gắn với 5 Customer (mỗi customer 2 asset)
- 10000 SensorReading (last 7 days, 1 reading/10min, có chèn ~5 anomaly events)
- **1 IotDevice** (`DeviceCode=GW-DEMO-001`, `Model=ESP32-S3-N16R8`, `Status=Active`, `ConfigJson.batteryMappings` map 2–3 BatteryAsset qua `unitId` 1–3) + API key hash seed (key plaintext in ra console khi seed cho demo) + vài `IotDeviceHeartbeat` gần nhất + 1 `IotDeviceCalibration` (Voltage offset mẫu). Đủ để demo provision/heartbeat/offline mà không cần phần cứng.

**Ticket (TicketService):**
- 5 KnowledgeBaseArticle (1 per category)
- 12 Ticket trong các state khác nhau:
  - 2 NEW, 2 OPEN, 2 ASSIGNED, 2 IN_PROGRESS, 1 RESOLVED, 1 CLOSED_PENDING_RATE, 1 CLOSED (rated), 1 ESCALATED

**Notification (NotificationService):**
- 50 Notification history mẫu

### 12.2. Migration ordering
1. FileStorageService AddUploadedFileMetadata (nếu Sprint 1 dùng avatar `fileId`)
2. AuthService (đã done) + AddAccountProfileExtensionTables
3. BatteryService InitialBatterySchema (+ TimescaleDB extension)
4. TicketService InitialTicketSchema
5. NotificationService InitialNotificationSchema

**Sprint 5B (#234 → #238) migration order — bắt buộc tuần tự, không xen kẽ:**
1. BatteryService `RemoveSiteCapacityKw` (`#234`) — drop column + rollback test.
2. BatteryService + TicketService `AddDurableMessagingFoundation` (`#235`) — MassTransit EF Consumer Outbox/Inbox tables (`mt_inbox_state`, `mt_outbox_state`, `mt_outbox_message`).
3. TicketService `AddQuartzPersistenceSchema` (`#235`) — `qrtz_*` 11 tables qua official SQL script.
4. **Preflight data cleanup** (`#236`, runbook `10-saga-duplicate-canonical.md`) — query duplicate `OriginAlertId` + duplicate active `(BatteryAssetId, Category)`, chọn Ticket canonical, mark duplicate `IsDeleted=true` kèm audit log. **Migration step 5 sẽ fail nếu skip step này.**
5. TicketService `AddAlertTicketSagaFoundation` (`#236`) — `alert_ticket_saga_states` + unique filtered index `tickets.origin_alert_id` + partial unique guard auto-ticket active.
6. BatteryService `AddAlertTicketLinkIndex` (`#236`) — non-unique filtered index `alerts(ticket_id)` (column đã tồn tại).
7. AuthService `SeedSagaPermissions` + `BindSagaPermissionsToRoles` (`#241`) — data migration (KHÔNG schema change); publish `PermissionsChangedEvent` để invalidate cache cross-service. Step này có thể chạy song song với #235–#238 vì khác DB; nhưng PHẢI hoàn tất trước khi enable Saga admin endpoints (#238 cutover).

Mọi migration step phải pass rollback test trước khi apply step kế tiếp.

**Sprint IoT-1 migration (`AddIotDeviceManagement`, sau Sprint 5B):**
- BatteryService `AddIotDeviceManagement` — 5 entity (`IotDevice`, `IotDeviceHeartbeat`, `IotDeviceCalibration`, `IotFirmwareRelease`, `IotFirmwareUpdateLog`) + `create_hypertable('iot_device_heartbeats','time')` (retention 30 ngày) + thêm `SensorReading.SourceType` (NOT NULL default `IotGateway` → seed data cũ = IotGateway) **và** `SensorReading.SensorSourceCode` (nullable) — **B9**, xem §1.3.4/§52.2.
- Chạy độc lập DB BatteryService, không phụ thuộc thứ tự Saga ở trên; vẫn phải pass rollback test (`Down()` drop 5 bảng + 2 column).

### 12.3. Migration checklist (theo `be.md §14`)
- [ ] Tên migration mô tả rõ
- [ ] Có `Down()` method
- [ ] NOT NULL columns: có `defaultValue` hoặc seed trước
- [ ] Test rollback: `database update <prev> && database update`
- [ ] Không có DROP TABLE/TRUNCATE raw

### 12.4. Production migration deploy strategy
- Run migrations as init container trong k8s (separate from app pod).
- App pod chỉ start sau khi migration thành công.
- Rollback plan: keep N-1 migration scripts handy.

---

## 13. Performance & caching strategy

### 13.1. Cache TTL chuẩn

| Data | TTL | Invalidation |
|------|-----|--------------|
| BatteryAsset detail | 60s | On update event |
| BatteryAsset list (per customer) | 30s | On asset CUD |
| Battery realtime | 0 (no cache) | — |
| Sensor history granularity≥1h | 60s | — |
| Active alerts per asset | 30s | On alert CUD |
| Ticket detail | 30s | On status change |
| Manager queue | 15s | — |
| KB article | 5min | On publish/update |
| Saga state (admin views) | **0 (no cache)** | State chuyển nhanh; cache sẽ gây lệch giữa ops view và actual state |
| Saga list (admin queries) | 10s (chỉ cho list filter `state=Failed`) | Acceptable cho dashboard refresh |
| Threshold config | 10min | On update |
| User profile | 5min | On update |
| Notification preference | 5min | On update |
| Permission claims | 10min | On role change |

### 13.2. Database
- Connection pool: `Maximum Pool Size=100` per service (default 100, sufficient).
- Pgbouncer optional cho production scale.
- Index strategy: theo §1.3, §2.3 (mỗi entity rõ index).

### 13.3. Pagination defaults
- `PageSize` default 20, max 100.
- `OrderBy` default `CreatedAt DESC`.
- Pagination response wrapper:
  ```csharp
  public class PaginationResponse<T> {
      public int Page { get; set; }
      public int PageSize { get; set; }
      public int TotalCount { get; set; }
      public int TotalPages { get; set; }
      public IEnumerable<T> Items { get; set; } = [];
  }
  ```

### 13.4. Performance SLA per endpoint

| Endpoint | P50 | P95 | P99 |
|----------|-----|-----|-----|
| GET realtime | 50ms | 150ms | 300ms |
| POST ticket | 100ms | 300ms | 500ms |
| GET ticket detail (with includes) | 80ms | 200ms | 400ms |
| Manager queue list | 100ms | 300ms | 500ms |
| Sensor batch ingest (100 readings) | 200ms | 500ms | 1000ms |
| Saga `POST /reprocess` (Sprint 5B) | 100ms | 300ms | 500ms |
| Saga `GET /alert-ticket?state=` (Sprint 5B, page=50) | 80ms | 200ms | 400ms |
| Alert→Saga Completed end-to-end (happy path, Sprint 5B) | 1.5s | 4s | 8s (mục tiêu, không phải HTTP SLA) |

---

## 14. Security checklist

### 14.1. AuthN/AuthZ
- [x] JWT signed HS256 (config secret ≥ 32 bytes)
- [x] RefreshToken rotation trên mỗi refresh
- [x] Permission-based authorization (đã có `HasPermissionAttribute`)
- [ ] Gateway validate JWT (xem §10.1)
- [ ] Permission claim cache 10min (xem §13.1)

### 14.2. Input validation
- [x] `IValidatable<T>` pipeline cho mọi command
- [ ] HTML sanitize cho TicketComment.Body (dùng `HtmlSanitizer` package)
- [ ] File upload size limit 10MB, content-type whitelist (image/png, image/jpeg, application/pdf)

### 14.3. Secrets
- [x] `.env` không commit
- [x] Pre-commit hook secret-scan
- [ ] Production: dùng Azure Key Vault / AWS Secrets Manager (out of scope capstone, dùng env)

### 14.4. CORS
- Whitelist origins (xem §10.3), no `*` for prod.

### 14.5. Rate limiting
- Xem §10.2.

### 14.6. Audit
- AuditLog đã có trong AuthService cho login/role change.
- TicketActivity đã có cho ticket changes.
- Battery asset CUD → cũng cần audit (CreatedBy/UpdatedBy đã có qua AuditableEntity).
- Sprint 5B: **Saga reprocess** (`POST /api/v1/admin/sagas/alert-ticket/{id}/reprocess`) phải ghi `AuditLog` với `Actor`, `Action="SagaReprocess"`, `Target=AlertId`, `Reason` (sanitized) + `IdempotencyKey`. Saga state cũng track `ManualReprocessCount`, `LastReprocessedBy`, `LastReprocessReason` (§53.6).
- Saga rejection/failure event không log payload chứa PII (`AssetSerialNumber` được phép, `CustomerId` GUID OK; KHÔNG log email/phone).

### 14.7. OWASP top 10 quick check
- **A01 Broken access:** permission attribute mọi endpoint + ownership check
- **A02 Crypto:** Argon2id password hash (đã có)
- **A03 Injection:** EF Core parameterized, không string-concat SQL
- **A04 Insecure design:** state machine validate transition, không tin client
- **A05 Misconfig:** SecurityHeadersMiddleware đã có (X-Frame-Options, CSP)
- **A07 AuthN failures:** rate limit login, login attempt tracking đã có
- **A08 Software integrity:** dependabot đã có (PR #45 ví dụ); **OTA firmware verify SHA-256 trước khi flash** (§52.7)
- **A09 Logging:** Serilog + CorrelationId

### 14.8. IoT device security (§52)
- [ ] **API key per-device** chỉ lưu **hash** (không plaintext), key hiện 1 lần khi tạo/provision, hỗ trợ **rotate/revoke**; scope giới hạn (`sensor.ingest`/`device.heartbeat`/`environmental.ingest`).
- [ ] Mọi ingest/heartbeat phải kèm `X-Device-Code` + device `Status=Active`; reject nếu device `Offline/Decommissioned`.
- [ ] **Anti-spoofing:** reject clock skew > 5 phút, reject outlier, auto-disable device sau N outlier (EC-24/EC-25); device chỉ gửi được reading cho battery trong mapping của nó (§52.2).
- [ ] **TLS:** HTTPS cho ingest/provision/firmware; MQTT-over-TLS 8883 (production) — dev mới `setInsecure()`.
- [ ] **(MQTT)** credential MQTT per-device + **ACL phân quyền topic** — device chỉ pub/sub topic của chính nó (`infra/mqtt/acl.conf`, §52.14).
- [ ] **Rate limit per device:** 60 requests/minute/device cho ingest (§1.8); broker connection limit.
- [ ] OTA `downloadUrl` là **signed URL** hết hạn ngắn; firmware verify SHA-256 trước khi ghi partition (§52.7).
- [ ] Audit: tạo/rotate/revoke device key + decommission device → `AuditLog` (`Action="IotDeviceKeyRotate"`/`"IotDeviceDecommission"`).

---

## 15. Email/Notification template catalog

### 15.1. Email templates

| Template | When | Recipient | Subject |
|----------|------|-----------|---------|
| `welcome-customer.hbs` | AccountActivatedEvent | Customer | "Chào mừng đến với Solar Battery Monitor" |
| `admin-invite.hbs` | SendAdminInviteEvent (đã có) | Staff/Manager invited | "Lời mời tham gia hệ thống" |
| `password-reset.hbs` | SendPasswordResetOtpEvent | Mọi role | "Đặt lại mật khẩu" |
| `battery-alert-critical.hbs` | BatteryAnomalyDetectedEvent (Critical) | Customer | "🔴 Cảnh báo nghiêm trọng: {AnomalyType}" |
| `ticket-created.hbs` | TicketCreatedEvent | Manager | "[TKT-{Code}] Ticket mới: {Title}" |
| `ticket-assigned-staff.hbs` | TicketAssignedEvent | Staff | "[TKT-{Code}] Bạn được giao ticket" |
| `ticket-assigned-customer.hbs` | TicketAssignedEvent | Customer | "[TKT-{Code}] Ticket của bạn đang được xử lý" |
| `ticket-resolved.hbs` | TicketResolvedEvent | Manager | "[TKT-{Code}] Staff đã đánh dấu RESOLVED" |
| `ticket-approved.hbs` | TicketApprovedEvent | Customer | "[TKT-{Code}] Ticket đã được giải quyết" |
| `sla-warning.hbs` | SlaWarningEvent | Staff + Manager | "[TKT-{Code}] SLA 80% — còn {Hours}h" |
| `sla-breach.hbs` | SlaBreachedEvent | Manager (+ Admin nếu P1) | "[TKT-{Code}] SLA BREACH" |
| `incident-declared.hbs` | IncidentDeclaredEvent | Admin/Manager/LeadStaff | "🚨 INCIDENT: {Title}" |
| `battery-alert-escalation-pending.hbs` | BatteryAlertEscalationRequestedEvent | Manager + Admin | "⚠️ Critical Alert chưa-ack > 5 phút: {AssetSerialNumber}" |
| `alert-ticket-saga-failed.hbs` | AlertTicketSagaFailedEvent | Admin | "❌ Saga Failed (AlertId={AlertId}, step={FailedStep}) — cần reprocess" |

### 15.2. Push notification templates (Expo)

| Type | Title | Body | Data |
|------|-------|------|------|
| BatteryAlertCritical | 🔴 Pin {Serial} cảnh báo nghiêm trọng | {AnomalyType} — {Actual}{Unit} (ngưỡng {Threshold}) | `{ "screen": "AlertDetail", "alertId": "..." }` |
| TicketAssigned (Staff) | Ticket mới: {Code} | {Title} — Priority {Priority} | `{ "screen": "TicketDetail", "ticketId": "..." }` |
| SlaWarning | ⚠️ SLA {Code} còn {Hours}h | {Title} | — |
| BatteryAlertEscalationPending | ⚠️ Alert chưa ack | {AssetSerialNumber} — Critical > 5 phút | `{ "screen": "AlertDetail", "alertId": "..." }` |
| AlertTicketSagaFailed | ❌ Saga Failed | AlertId {AlertId} — {FailedStep} | `{ "screen": "SagaDetail", "alertId": "..." }` |

### 15.3. Localization
- Bộ template Tiếng Việt làm chính cho capstone.
- Future: thêm English bằng cách parallel folder `Templates/en/`.

---

# Phần V — Lập kế hoạch

## 16. Scaffold workflow cho từng service

### 16.1. BatteryService (sprint 2)
```bash
# Tay: tạo solution structure (4 csproj + .slnx) — tham khảo AuthService

# Domain + scaffold đầu
/scaffold-crud BatteryService BatteryType
/scaffold-crud BatteryService ThresholdConfig
/scaffold-crud BatteryService BatteryAsset
/scaffold-entity BatteryService SensorReading        # custom hypertable migration
/scaffold-crud BatteryService Alert

# Events
/scaffold-integration-event BatteryAssetCreatedEvent
/scaffold-integration-event BatteryAnomalyDetectedEvent
/scaffold-integration-event BatteryAssetTransferredEvent

# Consumers
/scaffold-consumer BatteryService AccountActivatedEvent
/scaffold-consumer BatteryService AccountDeletedEvent
/scaffold-consumer BatteryService AccountStatusChangedEvent

# Sprint 5B additions (xem §1.7 + §53)
/scaffold-integration-event BatteryAlertEscalationRequestedEvent
/scaffold-integration-event BatteryAnomalyDetectedV2Event
/scaffold-consumer BatteryService LinkAlertToTicketCommand          # Saga participant
# Migration thêm tay: AddAlertTicketLinkIndex (chỉ thêm filtered index), RemoveSiteCapacityKw, AddDurableMessagingFoundation

# Custom CQRS (không có scaffold sẵn)
/scaffold-cqrs-command BatteryService Alert Acknowledge
/scaffold-cqrs-command BatteryService Alert Resolve
/scaffold-cqrs-command BatteryService SensorReading BatchIngest
/scaffold-cqrs-command BatteryService BatteryAsset TransferOwner

/scaffold-cqrs-query BatteryService BatteryAsset Realtime
/scaffold-cqrs-query BatteryService BatteryAsset MyBatteries
/scaffold-cqrs-query BatteryService SensorReading GetHistory
/scaffold-cqrs-query BatteryService Dashboard Stats

# Tests
/scaffold-unit-tests BatteryService BatteryAsset
/scaffold-unit-tests BatteryService Alert
/scaffold-unit-tests BatteryService SensorReading

# Migration
/run-migration BatteryService InitialBatterySchema

# Background services: làm tay
# - ThresholdCheckBackgroundService
# - AlertEscalationBackgroundService (Sprint 5B đổi sang publish BatteryAlertEscalationRequestedEvent, KHÔNG republish BatteryAnomalyDetectedEvent — xem §1, §53.4)
# - AlertAutoResolveBackgroundService
# - OutboxRelayBackgroundService

# Sprint IoT-1 additions (ESP32 edge device — xem §52/§52bis)
/scaffold-entity BatteryService IotDevice
/scaffold-entity BatteryService IotDeviceHeartbeat          # custom hypertable migration, retention 30d
/scaffold-crud   BatteryService IotDeviceCalibration
/scaffold-crud   BatteryService IotFirmwareRelease
/scaffold-entity BatteryService IotFirmwareUpdateLog
# Migration thêm tay: AddIotDeviceManagement (5 entity + heartbeat hypertable + SensorReading.SourceType/SensorSourceCode — B9)
/scaffold-cqrs-command BatteryService IotDevice Create        # admin tạo device + sinh API key (hash) + (MQTT) credential
/scaffold-cqrs-command BatteryService IotDevice Provision
/scaffold-cqrs-command BatteryService IotDevice Heartbeat
/scaffold-cqrs-command BatteryService IotDevice UpdateConfig
/scaffold-cqrs-command BatteryService IotDevice MarkOffline   # dùng cho MQTT LWT (§52.6)
/scaffold-cqrs-query   BatteryService IotDevice GetList
/scaffold-cqrs-query   BatteryService IotDevice HeartbeatHistory
# Background services làm tay:
# - IotDeviceOfflineDetectionBackgroundService (2 phút — backup cho LWT)
# - CalibrationExpiryNotificationService
# MQTT (P3, optional — §52.14): Infrastructure/Mqtt/{MqttBridgeBackgroundService,MqttTopicMap,TelemetryMessageHandler,LastWillHandler} + Security/DeviceApiKeyService — làm tay (không scaffold)
# Broker hạ tầng: infra/mqtt/ (EMQX/Mosquitto + TLS 8883 + ACL per-device)
```

### 16.2. TicketService (sprint 3-4)
```bash
/scaffold-crud TicketService Ticket
/scaffold-crud TicketService TicketComment
/scaffold-crud TicketService MaintenanceLog
/scaffold-crud TicketService KnowledgeBaseArticle
/scaffold-entity TicketService TicketActivity
/scaffold-entity TicketService SlaTimer
/scaffold-entity TicketService SlaPauseEvent
/scaffold-entity TicketService TicketAttachment
/scaffold-entity TicketService CustomerAccount         # read-model — xem §2.7
/scaffold-entity TicketService StaffAccount            # read-model — xem §2.7

# 12+ commands cho state machine
/scaffold-cqrs-command TicketService Ticket Assign
/scaffold-cqrs-command TicketService Ticket Reassign
/scaffold-cqrs-command TicketService Ticket Start
/scaffold-cqrs-command TicketService Ticket Hold
/scaffold-cqrs-command TicketService Ticket Resume
/scaffold-cqrs-command TicketService Ticket Resolve
/scaffold-cqrs-command TicketService Ticket Approve
/scaffold-cqrs-command TicketService Ticket Reject
/scaffold-cqrs-command TicketService Ticket RequestEscalation
/scaffold-cqrs-command TicketService Ticket Escalate
/scaffold-cqrs-command TicketService Ticket DeclareIncident
/scaffold-cqrs-command TicketService Ticket Rate
/scaffold-cqrs-command TicketService Ticket Reopen

# Queries
/scaffold-cqrs-query TicketService Ticket GetList
/scaffold-cqrs-query TicketService Ticket GetById
/scaffold-cqrs-query TicketService Ticket MyAsCustomer
/scaffold-cqrs-query TicketService Ticket MyAsStaff
/scaffold-cqrs-query TicketService Ticket ManagerQueue
/scaffold-cqrs-query TicketService Ticket ActivityTimeline
/scaffold-cqrs-query TicketService Sla GetStatus
/scaffold-cqrs-query TicketService Staff Workload

# Saga participant + read-model sync (xem §2.7, §8.3, §53)
/scaffold-consumer TicketService CreateTicketFromAlertCommand
/scaffold-consumer BatteryService LinkAlertToTicketCommand
/scaffold-consumer TicketService AccountActivatedEvent
/scaffold-consumer TicketService AccountStatusChangedEvent
/scaffold-consumer TicketService AccountProfileUpdatedEvent
/scaffold-consumer TicketService StaffProfileUpdatedEvent
/scaffold-consumer TicketService StaffSkillsUpdatedEvent

# Events publish
/scaffold-integration-event TicketCreatedEvent
/scaffold-integration-event TicketAssignedEvent
/scaffold-integration-event TicketStatusChangedEvent
/scaffold-integration-event TicketResolvedEvent
/scaffold-integration-event TicketApprovedEvent
/scaffold-integration-event TicketClosedEvent
/scaffold-integration-event TicketEscalatedEvent
/scaffold-integration-event IncidentDeclaredEvent
/scaffold-integration-event SlaWarningEvent
/scaffold-integration-event SlaBreachedEvent

# Sprint 5B — Alert–Ticket Saga contracts vào SharedContracts (làm tay, không scaffold vì shared lib)
# - SharedContracts/Saga/AlertTicket/CreateTicketFromAlertCommand.cs
# - SharedContracts/Saga/AlertTicket/TicketProvisionedForAlertEvent.cs
# - SharedContracts/Saga/AlertTicket/TicketProvisionForAlertRejectedEvent.cs
# - SharedContracts/Saga/AlertTicket/LinkAlertToTicketCommand.cs
# - SharedContracts/Saga/AlertTicket/AlertLinkedToTicketEvent.cs
# - SharedContracts/Saga/AlertTicket/AlertLinkToTicketRejectedEvent.cs
# - SharedContracts/Saga/AlertTicket/ReconcileAlertTicketSagaCommand.cs
# - SharedContracts/Saga/AlertTicket/AlertTicketSagaFailedEvent.cs
# Saga state machine + repository làm tay (không có /scaffold-saga):
# - TicketService.Infrastructure/Sagas/AlertTicketSagaState.cs
# - TicketService.Infrastructure/Sagas/AlertTicketSagaStateMachine.cs
# - TicketService.Infrastructure/Sagas/AlertTicketSagaDefinition.cs (endpoint name + retry/timeout)

# Tests
/scaffold-unit-tests TicketService Ticket
/scaffold-unit-tests TicketService SlaTimer
/scaffold-unit-tests TicketService TicketActivity
# Manual: TicketStateMachineTests (matrix test)
# Manual: AlertTicketSagaStateMachineTests + MassTransit TestHarness E2E

# Migrations
/run-migration TicketService InitialTicketSchema

# Background services: làm tay
# - SlaTimerBackgroundService
# - AutoCloseBackgroundService
# - EscalationBackgroundService
# - OutboxRelayBackgroundService
# - Sprint 5B: Quartz persistent scheduler endpoint (in-process, không phải BackgroundService riêng — host qua MassTransit) cho Saga retry/timeout (xem §53.8)
```

### 16.3. NotificationService (sprint 5)
```bash
/scaffold-crud NotificationService Notification
/scaffold-crud NotificationService DeviceToken
/scaffold-crud NotificationService NotificationPreference
/scaffold-crud NotificationService NotificationTemplate

# Consumers cho tất cả events
/scaffold-consumer NotificationService TicketCreatedEvent
/scaffold-consumer NotificationService TicketAssignedEvent
/scaffold-consumer NotificationService TicketStatusChangedEvent
/scaffold-consumer NotificationService TicketResolvedEvent
/scaffold-consumer NotificationService TicketApprovedEvent
/scaffold-consumer NotificationService TicketClosedEvent
/scaffold-consumer NotificationService TicketEscalatedEvent
/scaffold-consumer NotificationService IncidentDeclaredEvent
/scaffold-consumer NotificationService SlaWarningEvent
/scaffold-consumer NotificationService SlaBreachedEvent
/scaffold-consumer NotificationService BatteryAnomalyDetectedEvent
/scaffold-consumer NotificationService AccountActivatedEvent
/scaffold-consumer NotificationService SendAdminInviteEvent

# Sprint 5B additions (xem §3.2, §53)
/scaffold-consumer NotificationService BatteryAlertEscalationRequestedEvent
/scaffold-consumer NotificationService AlertTicketSagaFailedEvent

# Sprint 6 additions (environmental — xem §3.4)
/scaffold-consumer NotificationService EnvironmentalIncidentDetectedEvent
/scaffold-consumer NotificationService EnvironmentalIncidentResolvedEvent

# Sprint IoT-1 addition (device offline — xem §52.6, §3.4)
/scaffold-consumer NotificationService IotDeviceWentOfflineEvent

# Tests
/scaffold-unit-tests NotificationService Notification

# Migration
/run-migration NotificationService InitialNotificationSchema
```

---

## 17. Sprint backlog — 8 sprint chi tiết + Sprint 5B + Sprint IoT-1

### Sprint 1 (Hiện tại: 11/5–24/5/2026)
**Goal:** Stabilize foundations + close AuditLog/Permission.
**Tasks:**
- [x] AuthService AuditLog + Permission + LoginAttempt (DONE — merged #46)
- [x] Apply Polly retry/timeout cross-service (PR #47 chờ merge)
- [x] **Decision:** đổi postgres image sang `timescale/timescaledb:latest-pg16` — compose config validated
- [x] Docker Compose tách logical database theo service:
  - [x] `AuthService` dùng `auth_db` qua `ConnectionStrings__AuthDb`.
  - [x] `FileStorageService` dùng `file_storage_db` qua `ConnectionStrings__FileStorageDb`.
  - [x] `postgres-init` tạo DB idempotent, chạy được cả khi volume Postgres đã tồn tại.
- [x] FileStorageService metadata foundation (§6bis):
  - [x] Thêm `FileStorageService.Domain` nếu service hiện tại chưa có Domain project.
  - [x] Thêm entity `UploadedFile : AuditableEntity`.
  - [x] Thêm enum `FilePurposeEnum`, `FileStatusEnum`.
  - [x] Thêm `ApplicationDbContext` + EF configuration cho `uploaded_files`.
  - [x] Tạo migration `AddUploadedFileMetadata`.
  - [x] Update upload flow: upload object thành công → tạo `UploadedFile` metadata → response trả `fileId`.
  - [x] Update endpoint metadata/presigned/download/delete để dùng `fileId`.
- [x] **Decision:** giữ `Account` sạch, thêm extension tables `AccountProfile`, `StaffProfile`, `StaffSkill` trong AuthService → migration `AddAccountProfileExtensionTables`
- [x] AuthService: hỗ trợ avatar 2 nguồn (`AvatarFileId` nội bộ, `ExternalAvatarUrl` từ Google) và trả `displayAvatarUrl` cho FE
- [ ] Update CLAUDE.md memory + tài liệu API contract initial cho FE team (controller XML docs đã cập nhật, file doc riêng còn pending) — #64
- [ ] Migration rollback test cho `AddUploadedFileMetadata` và `AddAccountProfileExtensionTables` — #64
- [ ] **B5** — Tạo `docs/adr/0005-b2b-itil-stance.md` chốt B2B/B2C scope + ITIL 4 SVS stance — #146
- [ ] **B2-draft** — Tạo skeleton `.claude/docs/ai-research-references.md` (paper citation cho 15 anomaly types + IsolationForest hyperparameters + B2B SLA frameworks) — #147
- [ ] **B11** — Cập nhật §26 References (clarify ITIL 4 SVS B2B) — đã hoàn thành trong overall.md commit — #148

### Sprint 2 (25/5–7/6/2026)
**Goal:** BatteryService MVP (no anomaly detection yet).
**Tasks:**
- [x] Tạo solution skeleton `services/BatteryService/`
- [x] Migration: `InitialBatterySchema` (BatteryType, ThresholdConfig, BatteryAsset, Alert)
- [x] Migration: SensorReading table + TimescaleDB hypertable SQL
- [x] Migration: `CustomerAccount` read-model cache cho Auth account sync
- [x] CQRS BatteryType CRUD (4 commands + 2 queries)
- [x] CQRS BatteryAsset CRUD + TransferOwner (5 commands + 4 queries)
- [x] CQRS ThresholdConfig Upsert + Get
- [x] Consumer `AccountActivatedConsumer` + `AccountDeletedConsumer` + `AccountStatusChangedConsumer`
- [x] Validate `CustomerId` qua local `CustomerAccount` read-model khi tạo Site/BatteryAsset và TransferOwner
- [x] Unit tests + focused integration tests cho BatteryService critical paths
- [x] Coverage ≥ 80% report/enforcement (đạt 95.8% line coverage trên Application + Infrastructure, exclude Migrations/Factory/Seeders/DTO/Mapping; `services/BatteryService/scripts/check-coverage.sh` enforce threshold)
- [x] Migration rollback test trên TimescaleDB (script `services/BatteryService/scripts/test-migration-rollback.sh` — apply/rollback/re-apply cycle PASS, hypertable metadata auto-cleaned)
- [x] Update docker-compose + ApiGateway route
- [x] Seed BatteryType + 3 sample asset + sample customer/site/group
- [x] Site + BatteryGroup entities/CRUD + asset link/filter/dashboard MVP

### Sprint 3 (8/6–21/6/2026)
**Goal:** BatteryService anomaly engine + alert pipeline + Tier 1 extended battery health (SOH).
**Tasks:**
- [x] `SensorReadingBatchIngestCommand` + endpoint với ApiKey auth (done early in Sprint 2)
- [x] **Migration** `ExtendSensorReadingTierOne`: thêm `SohPercent`, `ChargingState` vào `sensor_readings` (nullable, không backfill) — #75
- [x] **Migration** `ExtendThresholdConfigSoh`: thêm `SohWarningThreshold`, `SohCriticalThreshold` vào `threshold_configs` — #75
- [x] Update `SensorReadingItem` + validation (SOH 0-100, ChargingState enum) — #80
- [x] Update `UpsertThresholdConfigCommand` validation (SOH critical < warning) — #80
- [x] `ThresholdAnomalyDetector` service + unit tests (**8 anomaly types**: 7 cũ + `SohDegradation`) — #76
- [x] `AlertDeduplicationService` + unit tests (BR-03) — #76
- [x] `ThresholdCheckBackgroundService` (30s tick) — #77
- [x] `AlertEscalationBackgroundService` (publish event) — #77
- [x] `OutboxRelayBackgroundService` + Outbox entity — #78
- [x] Publish `BatteryAnomalyDetectedEvent` — #78
- [x] Realtime + History query endpoint — #79
- [x] Extend `BatteryAssetRealtimeDto` thêm `SohPercent` + `ChargingState` — #79
- [x] Seed sensor data với pre-built anomaly scenarios (gồm SOH degradation scenario) — #81
- [x] Integration test end-to-end: ingest → detect → publish event (TestHarness) — #82

### Sprint 4 (22/6–5/7/2026)
**Goal:** TicketService foundation only — service skeleton, schema, state machine, basic lifecycle commands/queries. Không phát triển song song BatteryService advanced monitoring trong sprint này.
**Tasks:**
- [x] Tạo solution skeleton `services/TicketService/` — #83
- [x] Entities + migration `InitialTicketSchema` (Ticket, SlaTimer, SlaPauseEvent, TicketActivity, TicketComment, MaintenanceLog, TicketAttachment, OutboxMessage, **CustomerAccount, StaffAccount** — read-model cache từ AuthService, xem §2.7 Read-model) — #83
- [x] `TicketStateMachine` class + 30+ transition unit tests — #84
- [x] Commands: Create, Assign, Start, Hold, Resume, Resolve, Approve, Reject (8 commands) — #85
- [x] Queries: GetById, GetList, MyAsCustomer, MyAsStaff, ManagerQueue, ActivityTimeline (6) — #86
- [x] Code generation utility (TKT-YYMM-NNNN) — #87
- [x] Outbox + relay service — #88
- [x] Coverage ≥ 80% — #88
- [x] **B3** — Priority Calculation Matrix: thêm `ImpactScopeEnum`, `UrgencyLevelEnum`, field `Ticket.ImpactScope` + `Ticket.UrgencyLevel`, `IPriorityCalculator` + impl, áp dụng trong `TicketAssignCommand`. Schema PHẢI vào migration `InitialTicketSchema` ngay từ Sprint này (xem §2.4bis) — #149

### Sprint 5 (6/7–19/7/2026)
**Goal:** TicketService workflow integration — SLA, pause/resume, auto-create from Battery anomaly, maintenance log/comment/attachment.
**Tasks:**
- [x] `SlaCalculator` service + unit tests — #94
- [x] `SlaTimerBackgroundService` (60s tick — warning + breach) — #94
- [x] Pause/Resume commands (3 commands cho 3 Waiting* states) — #95
- [x] `EscalationBackgroundService` event-driven — #96
- [x] Reopen + Rate commands (Customer flow) — #97
- [x] `AutoCloseBackgroundService` (7d auto-close) — #98
- [x] Incident commands — #98
- [x] All events publish (SlaWarning, SlaBreached, Escalated, Incident, etc.) — #99
- [x] Coverage ≥ 80% + integration test SLA breach end-to-end with time mocking — #99
- [x] Consumer `BatteryAnomalyDetectedConsumer` → baseline Sprint 5 auto-create Ticket. Audit code hiện tại cho thấy dedup mới theo BatteryAsset active, chưa khóa Category/concurrency; consumer này sẽ bị thay và decommission bởi Saga flow Sprint 5B — #142
- [x] **5 read-model sync consumers** cho TicketService (`AccountActivatedConsumer`, `AccountStatusChangedConsumer`, `AccountProfileUpdatedConsumer`, `StaffProfileUpdatedConsumer`, `StaffSkillsUpdatedConsumer`) → upsert `CustomerAccount`/`StaffAccount` qua Inbox idempotency (xem §2.7 Read-model) — #142
- [x] Validate `CustomerId` (active) + `AssignedStaffId` (active, IsAvailable, skill warning, workload cap) qua read-model trong `TicketCreateCommandHandler` và `TicketAssignCommandHandler` — #142
- [x] Health endpoint `/health/sync-lag` trả `MAX(NOW() - LastSyncedAt)` cho `CustomerAccount` + `StaffAccount` — alert nếu > 60s — #142
- [x] MaintenanceLog + comments + attachments workflow trong TicketService — #143
- [x] **B6** — `StaffSkillTierEnum` + migration AuthService `AddStaffSkillTier` + sync `StaffAccount.SkillTier` qua `StaffProfileUpdatedEvent` + routing logic theo tier trong `TicketAssignCommandHandler` (xem §7) — #150
- [x] **B7** — Escalation closure rule: enforcement trong `TicketResolveCommandHandler` (chỉ assigned-after-escalation staff được resolve) + thêm `ActivityActionEnum.ResolvedByEscalatedStaff = 23` + 4 unit test edge cases (xem §2.4.2.bis) — #151

### Sprint 5B (20/7–26/7/2026)
**Goal:** Chốt lại BatteryService đúng phạm vi battery health, loại bỏ Energy/CO2 khỏi model/API/roadmap, đồng thời triển khai Alert–Ticket Saga bền vững giữa BatteryService và TicketService. Ambient/environmental/tier-2 chỉ thực hiện sau các task P0 này.

**⚠️ Sprint length: 7 calendar days = 5 working days (Mon-Fri 20/7-24/7) + 2 weekend.** Đây là sprint **NỬA** so với sprint thường (14 days). Sprint 5B tải 9 task `#233-#241` (P0 release gate) trong 5 working days là **rủi ro cao**. Mitigation tổng hợp:
- **Working weekend option**: Nếu team đồng ý làm thứ 7-CN, ~7 working days. Leader phải confirm trước Sprint 5B start.
- **Kéo sang Sprint 6 đầu**: Nếu không kịp Sun 26/7, `#239` (test + observability) hoặc `#240` (doc sync) có thể lấn 1-2 ngày Sprint 6 đầu — nhưng NGUY HIỂM vì block Sprint 6 NotificationService owner.
- **Defer scope phụ**: Ambient/B2-finalize đã được defer (xem §17 overload mitigation). Nếu cuối tuần thấy critical path slip, defer thêm: AI module retry config (§30.11), Alert silence/snooze (§37).
- **Combine với Sprint 5 buffer**: Sprint 5 kết thúc 19/7 (Sunday). Nếu Sprint 5 finish sớm Fri 17/7, team có 2-3 ngày weekend prep cho Sprint 5B (đọc spec, setup local environment, đăng ký external services theo §56.15).

**Release gate:** `#233–#241` là P0 và phải hoàn thành theo thứ tự phụ thuộc. Nếu Sprint 5B thiếu capacity, defer `B2-finalize`, OpenMeteo/ambient và false-alarm workflow; không cắt giảm Outbox/Inbox, unique constraint hoặc Saga test.

**⚠️ Capacity warning — Solo owner risk:** Thắng là sole owner cho toàn bộ 9 task `#233`–`#241`. Estimate ~8.5 dev-day trong 7-day sprint là **rủi ro cao**. Mitigation:
- **Working weekend**: tận dụng 2 ngày T7-CN để đạt ~7 working days.
- **Defer scope phụ**: Ambient/B2-finalize đã defer. Nếu cuối tuần slip, defer thêm AI module retry config (§30.11), Alert silence/snooze (§37).
- **KHÔNG defer `#237`** (Saga orchestrator) — task critical nhất.
- **Documentation immediate**: update `docs/onboarding/be-newcomer.md` Saga section (§40.6) NGAY sau khi `#237` merge — không đợi `#240`.

**⚠️ Bus factor = 1 cho Saga code:** Nếu Thắng không available giữa Sprint 5B → block toàn bộ critical path. Mitigation tối thiểu: code walkthrough video sau khi `#237` merge (record + upload `docs/knowledge-transfer/saga-walkthrough.md`) để team đọc kế thừa nếu cần.

**FE work song song Sprint 5B (Trí + Minh):**

Tài liệu này là BE-focused, nhưng Sprint 5B 7 ngày FE cũng cần work plan để không idle:
- **Sprint 5 carryover** (UI bug fix, polish) — primary task.
- **Mock Saga admin UI** dựa trên §60.4bis spec (BE endpoint chưa ready, FE dùng MSW/json-server mock). Khi `#236` SharedContracts merge, swap mock sang real type definitions từ shared contracts package.
- **Postman collection update** — FE thường xuyên dùng Postman, có thể giúp `#240` documentation sync với example payload.
- **Mobile work**: Customer mobile app polish, push notification integration test với real Expo token.

FE start Saga admin UI **production-ready** ở Sprint 7 (sau `#239` endpoint stable). Đến demo Sprint 8, Saga admin UI phải:
- Hiển thị danh sách Saga Failed.
- Cho Admin reprocess được (đầy đủ Idempotency-Key + confirmation modal).
- Cho Manager xem read-only.

**FE owner explicit**: Trí (Web Admin portal — Saga admin UI primary) + Minh (Mobile app + Customer flow).

**Owner mapping (P0 release gate):**

| Task | Owner | Reviewer | Đầu ra chính |
|------|-------|----------|--------------|
| `#233` Battery scope cleanup + ADR-017 | **Thắng** | — | Backlog cleaned, ADR-017 merged, scope-guard CI rule |
| `#234` Remove `Site.CapacityKw` | **Thắng** | — | Migration `RemoveSiteCapacityKw` + Up/Down test |
| `#235` Messaging reliability hardening | **Thắng** | — | DI split, Outbox relay v2, Quartz schema, EF Consumer Outbox tables |
| `#236` Saga contracts + DB foundation | **Thắng** | — | `SharedContracts/Saga/AlertTicket/*`, migration `AddAlertTicketSagaFoundation`, `AddAlertTicketLinkIndex` |
| `#237` Saga orchestrator | **Thắng** | — | `AlertTicketSagaState` + state machine + EF repo + persistent timeout |
| `#238` Saga participants + cutover | **Thắng** | — | `CreateTicketFromAlertConsumer`, `LinkAlertToTicketConsumer`, NotificationService 2 consumer + enum 16/17, escalation event split, cutover flags |
| `#239` Saga verification + operations | **Thắng** | — | Unit/integration/E2E tests, admin endpoints, metrics/alert rules, runbook, ADR-018 |
| `#240` Documentation sync | **Thắng** | — | Swagger/Postman/SRS/CHANGELOG/3 runbook |
| `#241` AuthService permission seed | **Thắng** | — | Data migration seed 2 permission + role bind + cache invalidate event |

**Tasks:**
- [x] **#233 — Battery scope cleanup:** xóa toàn bộ Energy/CO2 analytics khỏi backlog, API contract, report/dashboard/demo; không tạo `EnergySession`, `BatteryCycleLog`, `EnergyDailySummary`, `SiteEnergySummary`, `ElectricityRate`, `CarbonEmissionFactor`; giữ Voltage/Current/SOC/SOH/CycleCount/NominalCapacity vì phục vụ battery health; merge **ADR-017** vào `docs/adrs/`; thêm CI scope-guard rule grep `Energy|CO2|kWh|CapacityKw` trên active source — xem §53.1–§53.3. **Owner: Thắng.**
- [x] **#234 — Remove `Site.CapacityKw`:** bỏ field khỏi Domain entity, command/query DTO, mapping, validation, seed, API docs và test; migration BatteryService `RemoveSiteCapacityKw` có Up/Down + rollback test; rà soát không còn JSON property `capacityKw`. **Owner: Thắng.**
- [x] **#235 — Messaging reliability hardening:** tách `IIntegrationEventOutboxWriter` khỏi `IIntegrationEventTransport`, sửa DI overwrite hiện tại, thêm DI tests; Outbox relay có claim/lock, retry/backoff, error/metrics; thêm MassTransit EF Consumer Outbox/Inbox tables riêng cho Battery/Ticket consumers; cài NuGet `MassTransit.Quartz` + `Quartz.AspNetCore` + `Quartz.Serialization.Json`; migration `AddQuartzPersistenceSchema` (11 bảng `qrtz_*` qua official Quartz.NET PostgreSQL SQL script); cấu hình participant immediate retry và persistent Quartz scheduler endpoint cho Saga retry/timeout; **cấu hình endpoint runtime** (PrefetchCount/ConcurrentMessageLimit per endpoint — xem §8.3.11bis); cluster mode bật cho hai TicketService instance không double-fire (`quartz.scheduler.instanceId=AUTO`, `clusterCheckinInterval=10000`) — xem §8.1–§8.3 và §8.3.11bis. **Owner: Thắng.**
- [x] **#236 — Saga contracts + DB foundation:** đưa contract Alert/Ticket dùng chung vào `SharedContracts/Saga/AlertTicket/`; cập nhật XML docs/subscriber của `BatteryAnomalyDetectedEvent` sang Saga; thêm command, success/rejection response cho cả create/reuse Ticket và link Alert, reconciliation command đủ asset/customer context, cùng terminal failure contract; migration TicketService `AddAlertTicketSagaFoundation` tạo `alert_ticket_saga_states`, unique filtered index `tickets.origin_alert_id` và guard chống hai auto-ticket active cùng asset/category sau khi preflight/resolve duplicate data; migration BatteryService `AddAlertTicketLinkIndex` thêm non-unique index `alerts(ticket_id) WHERE ticket_id IS NOT NULL` (cột đã tồn tại, không add column). **Owner: Thắng.**
- [x] **#237 — Saga orchestrator:** implement `AlertTicketSagaState`, state machine, EF repository/configuration, explicit correlation `AlertId`, lưu đủ anomaly payload snapshot để resend sau restart, PostgreSQL `xmin` optimistic concurrency, persistent timeout, bounded retry/rejection/fault transition và transactional Outbox; cancel schedule cũ khi tiến bước, giữ row `Completed` làm tombstone, không trộn với `TicketStateMachine`; subscribe cả `BatteryAnomalyDetectedEvent` V1 và `BatteryAnomalyDetectedV2Event` (xem §30.6); **PR review tick 9 mục Saga state machine** trong §18.2bis trước khi approve. **Owner: Thắng.**
- [x] **#238 — Saga participants + cutover:** thay direct `BatteryAnomalyDetectedConsumer` bằng `CreateTicketFromAlertConsumer`; refactor application operation để dùng cùng consumer transaction, không tự commit/direct-publish; chuẩn hóa mapping toàn bộ anomaly→category (wire value 1–15 + unknown fallback (đồng bộ `AnomalyTypeEnum` §1.3.6), xem §53.7), create/reuse Ticket theo asset+category idempotently và publish success/rejection response qua EF Consumer Outbox; BatteryService thêm `LinkAlertToTicketConsumer`, update `Alert.TicketId` idempotently và publish success/rejection response; đổi `AlertEscalationService` sang `BatteryAlertEscalationRequestedEvent` riêng cho NotificationService, không republish Saga-start event; NotificationService thêm `BatteryAlertEscalationRequestedConsumer` + `AlertTicketSagaFailedConsumer` và 2 `NotificationTypeEnum` value mới (16, 17) + 2 email template + push template (xem §3.4 routing + §15 template catalog); **Notification debounce policy 5min** (xem §49.2); thêm `AlertTicketDispatchEnabled`/`AlertTicketSagaEnabled` cho maintenance cutover, disable/decommission queue consumer cũ và không để direct flow với Saga chạy song song; **PR review tick 12 mục participant + cutover** trong §18.2bis. **Owner: Thắng.**
- [x] **#239 — Saga verification + operations:** state-machine unit tests **≥ 21 case** đồng bộ test matrix §53.10, MassTransit TestHarness E2E, duplicate/concurrency/redelivery/RabbitMQ-down/timeout/late-message/feature-flag-misconfig tests; **restart-recovery integration test** (kill TicketService giữa transaction, restore, verify Saga continue); reconciliation các auto-ticket cũ có `OriginAlertId` nhưng Alert chưa có `TicketId`; admin query/reprocess endpoint qua MediatR (`TicketSagaView` cho Admin + Manager read-only, `TicketSagaReprocess` chỉ Admin); **Idempotency-Key required cho reprocess** (§8.6); `/health/saga` endpoint (§10.5); 8 Prometheus metric + 2 alert rule deploy (§9.2); structured logging fields đầy đủ + KHÔNG PII (§9.3); tracing CorrelationId xuyên service; Manager notification debounce (§49.2); runbook `08-saga-failed.md` + `09-saga-stuck.md` + `10-saga-duplicate-canonical.md` viết đầy đủ (§40.3); merge **ADR-018** vào `docs/adrs/`; **PR review tick 9 mục test + observability** trong §18.2bis — xem §53.9–§53.12. **Owner: Thắng.**
- [x] **#240 — Documentation sync:** cập nhật Swagger/Postman collection bỏ contract Energy/CO2 + `capacityKw`, thêm Saga admin endpoints; cập nhật SRS Phần 4 & 5; tạo `docs/runbooks/saga-failed.md`, `docs/runbooks/saga-stuck.md`, `docs/runbooks/saga-duplicate-canonical.md`; update `CHANGELOG.md`. **Owner: Thắng.**
- [x] **#241 — AuthService permission seed update:** AuthService data migration seed 2 permission mới (`ticket.saga.view`, `ticket.saga.reprocess`) vào `permissions` table + bind cho Admin (cả 2) và Manager (`ticket.saga.view` only); publish `PermissionsChangedEvent` để invalidate cache cross-service; integration test verify JWT mới có claim đúng. **Owner: Thắng.**
- [x] Entity `AmbientReading` (hypertable) + `AmbientThresholdConfig` (per Site, regular table) — #89
- [x] Migration `AddAmbientMonitoring`: tạo bảng + hypertable + index — #89
- [x] `IOpenMeteoClient` interface + `OpenMeteoClient` HTTP impl (Polly retry, 10s timeout) — #90
- [x] `WeatherSyncBackgroundService` (15min interval, dedup 10min, per-site lat/lon) — #90
- [x] `BatchIngestAmbientReadingsCommand` + endpoint (ApiKey `EnvironmentalIngest`) — #91
- [x] `GetAmbientReadingHistoryQuery` + `GetLatestAmbientReadingQuery` + endpoints — #91
- [x] `UpsertAmbientThresholdConfigCommand` + 2 query (by-site, list) + endpoints — #92
- [x] Extend `ThresholdAnomalyDetector` thêm 3 type: `HighAmbientTemp`, `HighHumidity`, `HighTempHumidityCombo` — #93
- [x] Update `appsettings.json`: tách `ApiKeys:SensorIngest` và `ApiKeys:EnvironmentalIngest`, thêm `Weather:*` config — #92
- [x] Unit tests OpenMeteoClient (HttpMessageHandler stub) + WeatherSyncBackgroundService (mock client) + 3 anomaly types mới — #93
- [x] Integration test: ingest ambient → query latest, combo threshold → alert — #93
- [x] Entity `EnvironmentalIncident` (regular table với lifecycle) — #100
- [x] Migration `AddEnvironmentalIncidentAndAlertSiteLevel`: tạo bảng `environmental_incidents` + relax `alerts.battery_asset_id` thành nullable + thêm `alerts.site_id` + `alerts.environmental_incident_id` + check constraint + index — #100
- [x] Migration `ExtendSensorReadingTierTwo`: thêm `InternalResistanceMilliohm`, `CellVoltageDeltaMv` vào `sensor_readings` — #101
- [x] Migration `ExtendThresholdConfigTierTwo`: thêm `InternalResistanceMaxMilliohm`, `CellVoltageDeltaMaxMv` vào `threshold_configs` — #101
- [x] `ReportEnvironmentalIncidentCommand` (ApiKey `EnvironmentalIngest`) — tạo incident + alert + publish event — #102
- [x] `AcknowledgeEnvironmentalIncidentCommand` + `ResolveEnvironmentalIncidentCommand` + `MarkFalseAlarmEnvironmentalIncidentCommand` — #102
- [x] `GetEnvironmentalIncidentsQuery` (list + filter) + `GetEnvironmentalIncidentByIdQuery` + `ActiveEnvironmentalIncidentsBySiteQuery` — #103
- [x] Endpoints `/api/environmental-incidents` (6 endpoint) — #103
- [x] Integration event `EnvironmentalIncidentDetectedEvent` + `EnvironmentalIncidentResolvedEvent` — #104
- [x] Extend Alert table — handler tạo alert cho cả site-level (chỉnh `AlertCreateCommandHandler` + dedup logic) — #104
- [x] Extend `ThresholdAnomalyDetector` thêm 3 type: `HighInternalResistance`, `CellImbalance`, `EnvironmentalIncident` — #105
- [x] Update `SensorReadingItem` + validation (IR > 0, CellDelta ≥ 0) — #105
- [x] Unit tests cho mọi command/handler + Tier 2 anomaly types — #105
- [x] Integration test: report smoke incident → alert critical tạo → event publish → false-alarm flow đóng cả 2 — #105
- [x] Coverage ≥ 80% maintain — #105
- [x] **B1** — Noise Suppression Logic: entity `NoiseBreachEvent` (hypertable) + migration nhét vào `ExtendThresholdConfigTierTwo` (thêm `NoiseSuppressionCount`/`WindowHours`/`Enabled`) + frequency-based logic trong `ThresholdAnomalyDetector` + bypass cho EnvironmentalIncident và Critical Overheat + retention job 7 ngày (xem §1.6.5) — #152
- [ ] **B2-finalize** — Hoàn thiện `.claude/docs/ai-research-references.md` với paper cite đầy đủ cho 15 anomaly types — #153

### Sprint IoT-1 (song song Sprint 6: 27/7–9/8/2026)
**Goal:** Biến kênh ingest sensor hiện có thành backend IoT production-ready, đồng thời chuẩn bị **ESP32** simulator/hardware path cho demo.

**Owner:** **Thắng** (sole owner). Sprint 5B (#233–#241) hoàn tất 26/7, ngay liền IoT-1 27/7 — Thắng tiếp tục BatteryService domain seamlessly.

**Scope note:** Nếu thiếu nhân lực, giữ `IotFirmware*` + **MQTT (P3, §52.14)** ở backlog và vẫn phải hoàn thành provision + heartbeat + ingest + offline (HTTPS đủ cho MVP/demo). **Hardware pilot** (ESP32-S3 + MAX485 + RS485 multi-drop) cần đối tác hỗ trợ phần cứng — Thắng liên hệ trước Sprint 5B kết thúc để không bị block giữa IoT-1.
**Tasks:**
- [x] Tạo bộ tài liệu triển khai IoT v2: `newiot.md` (thiết kế ESP32+MQTT), `overall.iot.md` (BOM + luồng), `wiring-diagram.md` (đấu dây), `hardware-bom.csv` (mua sắm). Bản `iot.md` (RPi v1) deprecated.
- [ ] Entity + migration `AddIotDeviceManagement`: `IotDevice`, `IotDeviceHeartbeat` hypertable, `IotDeviceCalibration`, `IotFirmwareRelease`, `IotFirmwareUpdateLog`. — #242
- [ ] Thiết kế API key per-device: sinh key khi admin tạo device, chỉ lưu hash, hỗ trợ rotate/revoke, scope `sensor.ingest` + `device.heartbeat`. — #243
- [ ] Admin endpoints: `POST/GET/PUT/DELETE /api/v1/admin/iot-devices`, `POST/GET /api/v1/admin/iot-firmware-releases`. — #244
- [ ] Device endpoints: `POST /api/v1/iot-devices/provision`, `POST /api/v1/iot-devices/heartbeat`, `GET /api/v1/iot-devices/firmware-check`, `PUT /api/v1/iot-devices/firmware-update-log/{id}`. — #245
- [ ] Update `POST /api/sensor-readings/batch`: nhận thêm `X-Device-Code`, `Idempotency-Key`, `deviceTimestamp`, hỗ trợ mapping `batteryAssetSerial` nhưng vẫn giữ legacy `batteryAssetId` cho simulator/MVP. — #246
- [ ] Validate IoT-specific: clock skew <= 5 phút, reject sensor outlier, apply calibration offset/scale, update `IotDevice.LastSeenAt`. — #247
- [ ] `IotDeviceOfflineDetectionBackgroundService`: device Active mất heartbeat > 5 phút => mark Offline, tạo `DeviceOffline` alert cho battery liên quan, publish `IotDeviceWentOfflineEvent`. — #248
- [ ] Khai báo `IotDeviceWentOfflineEvent` trong SharedContracts (§1.7) + **NotificationService**: `IotDeviceWentOfflineConsumer` + template device-offline (push/in-app, routing §3.4) — **+1 consumer / +1 template ngoài baseline Sprint 6 `#107`/`#111`**. — #249
- [ ] ESP32/simulator script: gửi heartbeat + sensor batch định kỳ, queue local (NVS/LittleFS) khi backend down, retry với `Idempotency-Key`. MVP có thể dùng `mock_bms` (data giả) trước khi có BMS thật. — #250
- [ ] ESP32 hardware pilot guide: ESP32-S3 + MAX485 + RS485/Modbus multi-drop (mỗi BMS 1 `unitId`), mapping BMS register sang payload backend (tham chiếu `newiot.md` §5/§8, `wiring-diagram.md`). — #251
- [ ] IoT route trong ApiGateway cho `/api/v1/iot-devices/*` và `/api/v1/admin/iot-devices/*` (xem §0bis.3). — #252
- [ ] **(P3 — MQTT realtime, optional/giãn sang Sprint 7 nếu thiếu nhân lực — §52.14)** Xây hạ tầng MQTT, gồm: — #253
  - [ ] Dựng broker `infra/mqtt/` (EMQX/Mosquitto, Docker) + TLS 8883 + `mosquitto.conf` + `certs/`.
  - [ ] Cấp **credential MQTT per-device** (gắn `IotDevice`) + **ACL phân quyền topic per-device** (`infra/mqtt/acl.conf`).
  - [ ] `MqttBridgeBackgroundService` (`Infrastructure/Mqtt/`) subscribe `telemetry`/`heartbeat`/`status`, đăng ký DI `AddHostedService`.
  - [ ] `TelemetryMessageHandler` → reuse `SensorReadingBatchIngestCommand` (không viết lại validate/insert/anomaly).
  - [ ] `LastWillHandler`: `status=offline` → `IotDeviceMarkOfflineCommand` (mark Offline tức thì) + alert `DeviceOffline`.
  - [ ] `MqttTopicMap` + `IMqttBridgePublisher` (publish downlink `cmd`: đổi config / trigger OTA).
  - [ ] Thêm broker vào docker-compose (xem §51) — chỉ bật khi triển khai MQTT.
  - [ ] Tests: telemetry qua broker đi đúng ingest command; LWT → Offline + alert; ACL chặn device lạ.
- [ ] Unit/integration tests: provision, heartbeat, offline detection, ingest dedup, clock skew, outlier, calibration, firmware check happy path. — #254
- [ ] **B9** — Thêm `SensorReadingSourceTypeEnum` + field `SensorReading.SourceType` (NOT NULL default IotGateway) vào migration `AddIotDeviceManagement` + update ingest endpoint accept `sourceType` per item (BMS/IotGateway/External) (xem §1.3.4 + §1.3.6) — #154

### Sprint IoT-2 (sau IoT-1: 10/8–6/9/2026, song song Sprint 7+8 — backend IoT task-level)

**Goal:** Hoàn thiện đầy đủ contract production, MQTT bridge, cross-source validation, calibration, OTA, observability backend cho IoT track. Đây là **single source of truth** cho mọi task backend IoT — nhóm IoT/firmware (`iot/tasksprint.md`) chỉ tham chiếu, không tự ý thêm BE task.

**Owner:** **Thắng** (BatteryService domain, tiếp tục từ Sprint IoT-1).

**Scope note:**
- Sprint IoT-1 đã set foundation (entity, provision, heartbeat baseline, ingest contract, offline job, optional MQTT skeleton). Sprint IoT-2 **hoàn thiện task-level** chi tiết hơn dựa trên rà soát `iot/tasksprint.md` (NI §1–§13, OV §A–§D, WD §1–§8).
- Sprint IoT-2 **đè lên** thời gian Sprint 7+8 — chấp nhận vì Thắng đã xong Sprint 5B Saga vào 26/7 + IoT-1 vào 9/8 và muốn close out backend IoT trước demo Sprint 8.
- Nếu thiếu thời gian: priority cứng = Phase B → C → E → F → D (MQTT). MQTT có thể trượt sang sau capstone vì HTTPS đủ cho demo.

**Map sprint↔Phase với IoT plan (`iot/tasksprint.md`):**

| Phase IoT-2 | tasksprint.md sprint | Topic |
|-------------|---------------------|-------|
| A | S0–S1 | BatteryService bootstrap + seed + legacy shim |
| B | S2 | Device management + offline detection + notification consumer |
| C | S3 | Production contract + resilience |
| D | S4 | MQTT broker + bridge |
| E | S6 | Anomaly + cross-source + environmental |
| F | S7 | Calibration + OTA + observability |

#### Phase A — Bootstrap & MVP shim (S0–S1 trong IoT plan)

- [ ] **S0-BE-01** — Pull BatteryService, `dotnet build` xanh, chạy migration cũ trên DB dev mới (`battery_service_dev` + TimescaleDB extension). Swagger UI mở được — #IoT2-01 → #255
- [ ] **S1-BE-01** — Seed script `tools/seed-iot-mvp.sh`: 1× Site + 4× BatteryAsset (serial `BAT-2026-001..004`) + threshold config mặc định (voltage 11–14V, temp -10..60°C, SOC 20–100%). `dotnet run --seed` → DB có dữ liệu — #IoT2-02 → #256
- [ ] **S1-BE-02** — Endpoint legacy `POST /api/sensor-readings/batch` nhận payload simulator MVP: shim `serial → BatteryAssetId` nội bộ nếu cần. POST từ ESP32 mock → row mới trong `sensor_readings` — #IoT2-03 → #257

#### Phase B — Device Management (S2 trong IoT plan)

- [ ] **S2-BE-01** — Migration `AddIotDeviceManagement`: 5 entity (`IotDevice`, `IotDeviceHeartbeat`, `IotDeviceCalibration`, `IotFirmwareRelease`, `IotFirmwareUpdateLog`) + 3 enum (`IotDeviceTypeEnum`, `IotDeviceStatusEnum`, `SensorReadingSourceTypeEnum`) + cột `SourceType` (NOT NULL default `IotGateway`) + `SensorSourceCode` (string(20)?) vào `SensorReading`. `dotnet ef database update` pass; bảng + index tạo đầy đủ — xem §52.2, §1.3.4 — #IoT2-04 → #258
- [ ] **S2-BE-02** — Hypertable hóa `iot_device_heartbeats` (`time` column, chunk 1 ngày, retention 30 ngày). `SELECT * FROM timescaledb_information.hypertables` thấy bảng — xem §52.2 retention — #IoT2-05 → #259
- [ ] **S2-BE-03** — `DeviceApiKeyService`: sinh key 32 byte random, hash SHA-256 lưu DB; xác thực header `X-Api-Key` + `X-Device-Code`; rotate/revoke. **Scope key (§52.2):** `sensor.ingest` + `device.heartbeat` mặc định; nếu device có sensor môi trường (SHT31/MQ-2/water) thêm `environmental.ingest` để gọi cùng key vào `/api/ambient-readings/batch` + `/api/environmental-incidents`. Unit test: key đúng → pass, sai → 401, revoked → 403, scope thiếu → 403 — #IoT2-06 → #260
- [ ] **S2-BE-04** — `POST /api/v1/admin/iot-devices` (Admin) tạo device + trả `apiKey` plaintext **đúng 1 lần** trong response. Lần GET sau không trả lại key (chỉ lastFour). Response: `{deviceCode, apiKey, provisioningQrCode}` — xem §52.3 — #IoT2-07 → #261
- [ ] **S2-BE-05** — Admin endpoints còn lại: `GET /api/v1/admin/iot-devices?status=&siteId=`, `GET /api/v1/admin/iot-devices/{id}`, `PUT /api/v1/admin/iot-devices/{id}/config` (push config), `DELETE /api/v1/admin/iot-devices/{id}` (soft decommission). Swagger test pass — xem §52.11 — #IoT2-08 → #262
- [ ] **S2-BE-06** — `POST /api/v1/iot-devices/provision`: set `Status=Active`, trả `configJson` chứa `pollingInterval`, `heartbeatInterval=60s`, `batteryMappings[]` (serial+unitId+sensorSourceCode), `ntpServer`, `supportedSensors`. Provisioning → Active trên DB — xem §52.3, §52.4 — #IoT2-09 → #263
- [ ] **S2-BE-07** — `POST /api/v1/iot-devices/heartbeat`: ghi 1 row hypertable + update `IotDevice.LastSeenAt`. Frequency expected 60s. Field mapping ESP32: `Cpu`/`DiskFreeMb` cho phép null; map `Temperature`/`MemoryUsageMb`/`SignalStrengthDbm`/`LocalQueueDepth`. Sau 5 phút có 5 row — xem §52.4, §52.2 ESP32 mapping — #IoT2-10 → #264
- [ ] **S2-BE-08** — `IotDeviceOfflineDetectionBackgroundService` chạy 2 phút/lần: device `Active` + `LastSeenAt < now-5min` → `Status=Offline` + publish `IotDeviceWentOfflineEvent` (outbox) + tạo `Alert(DeviceOffline)` (AnomalyType=7, Warning) cho mọi battery liên kết. **Phân vai dedup (§52.6):** Customer được báo qua `DeviceOffline` Alert đi đường BatteryAlert (§3.4); Staff/ops được báo qua `IotDeviceWentOfflineEvent` đến NotificationService (xem #IoT2-13). Dedup theo `DeviceId` cửa sổ offline. Tắt ESP32 6 phút → đổi Offline + Customer push 1 lần + Staff in-app 1 lần — #IoT2-11 → #265
- [ ] **S2-BE-09** — ApiGateway route `/api/v1/iot-devices/*` + `/api/v1/admin/iot-devices/*` (xem §0bis.3). Gọi qua gateway hoạt động — #IoT2-12 → #266
- [ ] **S2-BE-10** — Khai báo `IotDeviceWentOfflineEvent` trong `SharedContracts/IntegrationEvents/`. **NotificationService:** viết `IotDeviceWentOfflineConsumer` + template `device-offline.hbs` (push/in-app cho Staff/ops, routing §3.4 — KHÔNG gửi Customer ở đây vì đã đi qua Alert). +1 consumer / +1 template ngoài baseline. Event publish từ #IoT2-11 → Staff nhận in-app/push, Customer KHÔNG nhận từ kênh này — #IoT2-13 → #267

#### Phase C — Production Contract & Resilience (S3 trong IoT plan)

- [ ] **S3-BE-01** — Update `SensorReadingBatchIngestCommand`: nhận header `X-Device-Code`, `Idempotency-Key`, body `deviceTimestamp` + readings (`batteryAssetSerial`, `sensorSourceCode`, `sourceType`, optional `bmsErrorCode ≤ 64`). **GIỮ song song backward compat (§52.5):** vẫn chấp nhận legacy `items[].batteryAssetId` để simulator MVP (Phase A) chạy không gãy — detect format qua presence của `deviceTimestamp`/`readings[]`. Regression test cho cả 2 schema — #IoT2-14 → #268
- [ ] **S3-BE-02** — Validate clock skew: `|deviceTimestamp - serverNow| > 5 phút` → 400 + tăng `iot_sensor_readings_rejected_total{reason=clock_drift}`. Test: timestamp -10 phút → 400 — xem §52.5 — #IoT2-15 → #269
- [ ] **S3-BE-03** — Idempotency: lưu `(deviceCode, idempotencyKey)` vào table riêng (TTL 24h) + index unique. Trùng → 200 trả lại result cũ (không insert). Test: POST 2 lần cùng key → 200, DB chỉ 1 batch — xem §8.6 — #IoT2-16 → #270
- [ ] **S3-BE-04** — Outlier reject: voltage `>1000V` hoặc `<0`, temp ngoài `[-50..150]`, soc ngoài `[0..100]`, soh ngoài `[0..100]`, `bmsErrorCode > 64 chars`. Tăng `iot_sensor_readings_rejected_total{reason=sensor_outlier}` + counter `IotDevice.OutlierIncidentCount`. **Auto-disable (§52.15 / EC-25):** vượt N=50 outlier trong 1h → `Status=Decommissioned` tự động + alert Admin. Test: voltage=-5 → 400; 51 outlier liên tiếp → device tự decommission — #IoT2-17 → #271
- [ ] **S3-BE-05** — Map `batteryAssetSerial` → `BatteryAssetId` + kiểm tra `device.BatteryAssetIds` / `batteryMappings` có chứa battery này (nếu không → 403 + log + metric `reason=mapping_invalid`). Test: device không quyền pin X → 403 — xem §52.5 — #IoT2-18 → #272
- [ ] **S3-BE-06** — Apply calibration trước khi insert: lấy active calibration của `(deviceId, sensorMetric)` từ cache (Redis TTL 5 phút). `calibrated_value = raw_value * ScaleFactor + OffsetValue`. Test: tạo calibration offset=0.5 → voltage insert lệch +0.5 — xem §52.8 — #IoT2-19 → #273
- [ ] **S3-BE-07** — Update `IotDevice.LastSeenAt` mỗi lần ingest thành công. LastSeenAt nhảy mỗi 5s khi simulator chạy — xem §52.5 — #IoT2-20 → #274

#### Phase D — MQTT Bridge (S4 trong IoT plan, P3 — optional/giãn)

> Nếu thiếu thời gian, phase này trượt sang sau capstone. HTTPS đủ cho MVP/demo. Topic 5 cái — bắt buộc đủ `cmd/ack` để trace downlink (§52.14).

- [ ] **S4-BE-01** — Add NuGet `MQTTnet` + `MQTTnet.Extensions.ManagedClient` vào `BatteryService.Infrastructure`. Build pass — #IoT2-21 → #275
- [ ] **S4-BE-02** — `MqttBridgeBackgroundService` subscribe **4 topic** (publish-side 5 nếu tính `cmd`): `solar/+/+/telemetry`, `solar/+/heartbeat`, `solar/+/status`, `solar/+/cmd/ack`. Đăng ký qua `AddHostedService`. Service start log "connected to broker, 4 subscriptions"; ack từ ESP32 sau khi exec cmd được log để admin trace — xem §52.14 — #IoT2-22 → #276
- [ ] **S4-BE-03** — `TelemetryMessageHandler`: parse JSON payload từ `solar/+/+/telemetry` → gọi `SensorReadingBatchIngestCommand` (reuse logic validate/calibrate/insert từ Phase C, KHÔNG viết lại). Publish 1 batch qua MQTT → DB ghi đúng — xem §52.14 — #IoT2-23 → #277
- [ ] **S4-BE-04** — `LastWillHandler`: nhận `solar/{dev}/status` payload `offline` → `IotDeviceMarkOfflineCommand` (mark Offline tức thì) + alert `DeviceOffline` + publish `IotDeviceWentOfflineEvent`. Rút điện ESP32 → ≤ 90s sau (keep-alive 60s + xử lý) DB `Status=Offline` — xem §52.6 cơ chế 1 — #IoT2-24 → #278
- [ ] **S4-BE-05** — `IMqttBridgePublisher` để service khác publish downlink `solar/{dev}/cmd`. Endpoint `POST /api/v1/admin/iot-devices/{id}/command` body `{cmdId, type, params}`. API test: POST cmd → ESP32 nhận → bridge log ack `{cmdId, status:"ok"}` từ topic `cmd/ack` — #IoT2-25 → #279
- [ ] **S4-BE-06** — Cấp credential MQTT per-device khi tạo `IotDevice` (lưu hash hoặc bcrypt; sync lên EMQX qua HTTP auth hook hoặc bảng built-in). ACL phân quyền topic per-device (`infra/mqtt/acl.conf`). Tạo device mới → ESP32 connect được; xóa device → connect bị từ chối — xem §52.14 — #IoT2-26 → #280

#### Phase E — Anomaly · Cross-source · Environmental (S6 trong IoT plan)

- [ ] **S6-BE-01** — Thêm `AnomalyTypeEnum.SensorMismatch = 15` + entry vào catalog (§1.3.6). Migration thêm enum value — B10 (Sprint 7 #157, có thể carry-over) — #IoT2-27 → #281
- [ ] **S6-BE-02** — `CrossSourceValidationService`: khi insert reading mới, query reading cùng `BatteryAssetId` trong cửa sổ 60s ở `sourceType` khác (Bms vs IotGateway); lệch V > 0.5V hoặc temp > 5°C → tạo `Alert(SensorMismatch, Warning)`. Test: bơm 2 reading lệch → alert xuất hiện — xem §1.6.6 — #IoT2-28 → #282
- [ ] **S6-BE-03** — Verify `ThresholdCheckBackgroundService` cũ vẫn quét reading mới, trigger `Alert(Overheat/LowSoc/...)`; nếu chưa có thì viết. Bơm voltage = 15V → alert Critical xuất hiện — xem §1.6 — #IoT2-29 → #283
- [ ] **S6-BE-04** — Publish outbox event `BatteryAnomalyDetectedEvent` **V2** từ BatteryService (schema đầy đủ `AlertId/AnomalyType/Severity/Source/BatteryAssetId/Site` theo §53.7). **KHÔNG để TicketService consume trực tiếp** — Sprint 5B đã chuyển sang **Alert–Ticket Saga** (§53, MassTransit State Machine): event → Saga orchestrate qua 8 message (`CreateTicketFromAlertCommand` → `TicketProvisionedForAlertEvent` → `LinkAlertToTicketCommand` → `AlertLinkedToTicketEvent` → `Completed`). Direct consumer sẽ tạo Ticket trùng. IoT track CHỈ chịu trách nhiệm emit event đúng schema; Saga state machine + retry/compensation thuộc Sprint 5B `#237`. NotificationService consume `BatteryAnomalyDetectedEvent` riêng cho push/email (§3.4) — không qua Saga. Test: Ticket được Saga tạo (state `TicketProvisioned` → `Completed`); `Alert.TicketId` được link; không Ticket trùng nếu Saga retry — xem §53, §52.12bis — #IoT2-30 → #284
- [ ] **S6-BE-05** — Endpoint `POST /api/ambient-readings/batch` (AmbientReading — `Source=IotSensor`, `SourceDeviceId=<DeviceCode>`) + `POST /api/environmental-incidents` (EnvironmentalIncident: `SmokeDetected`/`WaterLeak`). Khi tạo incident → publish `EnvironmentalIncidentDetectedEvent` (§1.7) → NotificationService route lên **Critical channel** (push + email + SMS), **bypass quiet hours** (§3.4 + §49.3) — page Manager + Admin. Cùng device API key (scope `environmental.ingest` — xem #IoT2-06). Test: smoke incident → Manager nhận push **trong quiet hours** + email + SMS — xem §52.9bis — #IoT2-31 → #285

#### Phase F — Calibration · OTA · Observability (S7 trong IoT plan)

- [ ] **S7-BE-01** — Endpoint `POST /api/v1/iot-devices/{id}/calibrations` (Staff/Admin) + `GET` list + `GET /api/v1/iot-devices/calibrations-expiring?within=30d` (Manager). Tạo, list, filter expiring trong 30d hoạt động — xem §52.8, §52.11 — #IoT2-32 → #286
- [ ] **S7-BE-02** — `CalibrationExpiryNotificationService` (background, hằng ngày): scan calibration `ValidUntil < now+30d` → notify Manager. Tạo calibration ValidUntil = hôm nay → service báo — xem §52.8 — #IoT2-33 → #287
- [ ] **S7-BE-03** — Invalidate Redis cache calibration khi tạo mới (xem #IoT2-19). Tạo mới → reading kế tiếp dùng calibration mới — xem §52.8 — #IoT2-34 → #288
- [ ] **S7-BE-04** — `POST /api/v1/admin/iot-firmware-releases` (multipart): upload `.bin` + sha256 + isRequired + channel (Stable/Beta) + releaseNotes + deviceModel + version. Upload thành công, file lưu `FileStorageService` — xem §52.7 — #IoT2-35 → #289
- [ ] **S7-BE-05** — `GET /api/v1/iot-devices/firmware-check` → trả `{hasUpdate, version, downloadUrl signed, sha256, isRequired, releaseNotes}`. ESP32 GET thấy update khi có firmware version cao hơn — xem §52.7 — #IoT2-36 → #290
- [ ] **S7-BE-06** — `PUT /api/v1/iot-devices/firmware-update-log/{id}` cho ESP32 update status (`Pending → Downloading → Installing → Success | Failed | RolledBack`). Trạng thái hiển thị web — xem §52.7 — #IoT2-37 → #291
- [ ] **S7-BE-07** — Expose Prometheus metrics đầy đủ label theo §52.12: `iot_device_heartbeats_total{device_id, status}` (label `status`), `iot_devices_online_count` (gauge), `iot_devices_offline_total` (counter), `iot_sensor_readings_ingested_total{device_id}`, `iot_sensor_readings_rejected_total{reason=clock_drift|sensor_outlier|mapping_invalid|...}`, `iot_firmware_updates_total{from_version, to_version, status}`. `/metrics` endpoint trả đúng schema — #IoT2-38 → #292

#### Acceptance Sprint IoT-2

- [ ] 38 task #IoT2-01..38 đều close + có log review/test trong `logs/IoT2-{NN}/`.
- [ ] Regression test cuối sprint: ingest legacy payload + ingest production payload cùng đi qua endpoint mới, không gãy simulator MVP.
- [ ] Saga path verify: trigger 1 anomaly Critical → Saga `TicketProvisioned → Completed`; bơm cùng anomaly 2 lần (idempotent) → 1 Ticket duy nhất.
- [ ] Cross-source mismatch verify: bơm cặp reading BMS vs INA226 lệch > 0.5V → `Alert(SensorMismatch)` xuất hiện trong < 30s.
- [ ] Environmental Critical bypass verify: trigger MQ-2 incident trong giờ 23:00 (quiet hours) → Manager vẫn nhận push.
- [ ] Auto-disable verify: bơm 51 outlier voltage → device `Decommissioned` + alert Admin.
- [ ] Metric `/metrics` đầy đủ label `status`, `reason`, `from_version`, `to_version` — Grafana panel vẽ được.

### Sprint 6 (27/7–9/8/2026)
**Goal:** NotificationService + KnowledgeBase + Environmental notification routing.

**Owner:** **Duy** (BE Lead, NotificationService primary) + **Thắng** (KnowledgeBase module + Saga carryover verify + Sprint IoT-1 song song).

**Dependency note:** Sprint 5B `#238` đã thêm 2 consumer + 2 enum value (16/17) + 2 template Saga vào NotificationService skeleton. Sprint 6 **không** được refactor xoá phần này — phải build trên nền đó. Owner Sprint 6 đọc §3 + xem commit Sprint 5B trước khi start.

**Tasks:**
- [ ] Tạo solution `services/NotificationService/` — #106
- [ ] **17 consumers** cho mọi events (13 cũ + 2 Saga từ Sprint 5B `#238` + `EnvironmentalIncidentDetectedConsumer` + `EnvironmentalIncidentResolvedConsumer`) — #107
- [ ] `ExpoPushChannel` + integration test (sandbox token) — #108
- [ ] `EmailBusChannel`, `SmsBusChannel`, `InAppChannel` — #108
- [ ] `NotificationDispatcher` + preference + quiet hours + **Sprint 5B debounce policy** (Redis key `notif_debounce:escalation/saga-failed:{alertId}` TTL 5min — xem §49.2) — #109
- [ ] DeviceToken endpoints — #110
- [ ] KnowledgeBase module trong TicketService (CRUD + suggest endpoint) — #112
- [ ] Email templates **16 file `.hbs`** (12 cũ + 2 Saga từ Sprint 5B + `environmental-incident-detected.hbs` + `environmental-incident-resolved.hbs`) — #111
- [ ] Push template: `EnvironmentalIncidentCritical` (smoke/water → page Manager + Admin) — #111
- [ ] Routing rule: incident Critical → Critical channel (push + email + SMS), bypass quiet hours — #109
- [ ] Routing rule Sprint 5B verify: `BatteryAlertEscalationPending` (Manager+Admin: InApp+Push+Email), `AlertTicketSagaFailed` (Admin only) — xem §3.4 matrix — #109
- [ ] Seed 5 KB articles — #112
- [ ] Coverage ≥ 80% — #112
- [ ] **B8** — Thêm `KnowledgeBaseArticle.Code` (format `KB-YYYY-NNNN` auto-gen) + entity `TicketKbReference` (many-to-many ticket↔KB) + 4 endpoints + analytics `usage-stats` (xem §4.2 + §4.2bis) — #155

### Sprint 7 (10/8–23/8/2026)
**Goal:** Reports + Gateway hardening + Observability + Tier 3 sensor finalize.

**Owner:** **Thắng** (Reports + Gateway primary) + **Duy** (Observability + Tracing) + **Thái** (Tier 3 sensor + B4 Cascade Risk). FE Trí + Minh có thể start Saga admin UI (§60.4bis) song song.

**Dependency note (Sprint 5B carryover):** Sprint 5B `#235`/`#239` đã thêm 8 Saga metric + 2 Grafana panel + 2 AlertManager rule + 2 Saga seed row + Saga admin endpoints. Sprint 7 PHẢI integrate (không refactor xoá):
- Grafana panel "Alert–Ticket Saga" (§9.2 panel #4) → vào dashboard final.
- AlertManager rule `AlertTicketSagaStuck` + `AlertTicketSagaFailedSpike` → vào ruleset production.
- ApiGateway aggregated swagger → include `/api/v1/admin/sagas/alert-ticket/*` schema từ TicketService.
- OpenTelemetry tracing → propagate qua Saga endpoint, `CorrelationId/AlertId` cross-service.
- `tools/seed.sh` → giữ 2 Saga seed row.

**Tasks:**
- [ ] **Migration** `ExtendSensorReadingTierThree`: thêm `BmsErrorCode` vào `sensor_readings` (nullable, 64 chars) — #113
- [ ] Update `SensorReadingItem` + validation (`BmsErrorCode` ≤ 64 chars) — #113
- [ ] Reports endpoints (Ticket: **9 endpoints** — 8 cũ + Saga Failed rate report cho Admin, **Battery: 7 endpoints** — 5 cũ + Environmental Incident report + Ambient temperature trend) — #114
- [ ] CSV/XLSX export — #114
- [ ] ApiGateway: JWT validate + claim forwarding + rate limiting + aggregated swagger (**bao gồm Saga admin endpoints từ Sprint 5B**) — #115
- [ ] OpenTelemetry tracing setup → Tempo (include WeatherSync + EnvironmentalIncident + **Alert–Ticket Saga flow** với CorrelationId=AlertId xuyên BatteryService↔TicketService) — #116
- [ ] Grafana dashboards: SLA Ops, **Battery Health (gồm SOH/DCIR/Imbalance)**, **Environmental Monitoring (ambient + incidents)**, **IoT Device Monitoring (online/offline, ingest/reject, queue depth — §9.2 #5)**, **Alert–Ticket Saga (verify panel từ Sprint 5B đã hiển thị metric đúng)**, System Health — #117
- [ ] AlertManager rules — bao gồm rule cho environmental incident detection latency + **verify Saga rules từ Sprint 5B đã active** — #118
- [ ] Full seed data script (`tools/seed.sh`) — bao gồm ambient readings + 1 incident historical example + **2 Saga seed row từ Sprint 5B giữ nguyên** — #119
- [ ] End-to-end test scenarios (golden path + SLA breach + reopen + smoke incident lifecycle + **Saga happy path + failure recovery**) — #119
- [ ] IoT hardware pilot E2E: ESP32-S3 (RS485 multi-drop) / simulator gửi heartbeat + readings qua API mới, dashboard thấy realtime, dừng device tạo `DeviceOffline` alert (job 5 phút, hoặc LWT tức thì nếu đã bật MQTT P3) — #127
- [ ] **[Optional P1] Deploy staging K8s** — viết Helm chart per service (umbrella + 6 service chart theo §54.2) + deploy lên k3s/minikube + smoke test. Nếu **không kịp đến 17/8/2026 (giữa sprint)** → fallback `docker compose -f docker-compose.staging.yml` trên 1 VM cho demo Sprint 8. **Helm chart vẫn phải viết** dù không deploy — để có artifact production-ready cho hồ sơ. Không ảnh hưởng điểm chức năng capstone (xem §54.1 Sprint risk) — #126
- [ ] **B4** — Cascade Risk Assessment rule-based: field `BatteryAsset.CascadeRiskScore`/`CascadeRiskUpdatedAt`/`ElectricalTopology` + migration `AddCascadeRiskFields` + `CascadeRiskCalculator` + `CascadeRiskBackgroundService` (5min) + 3 endpoint + integration với Priority Matrix (xem §31.7) — #156
- [ ] **B10** — `AnomalyTypeEnum.SensorMismatch = 15` + cross-source validation logic trong `ThresholdCheckBackgroundService` (BMS vs IoT delta 0.5V/5°C) + migration value bổ sung enum + 3 unit test (xem §1.6.6) — #157

### Sprint 8 (24/8–6/9/2026)
**Goal:** Demo prep + polish.

**Owner:** **Leader** (overall coordination, demo script, slide deck, Q&A prep) + **toàn team standby** (bug bash, dry-run, rehearsal). Mỗi dev primary owner cho service domain của mình khi bug fix.

**Dependency note (Sprint 5B carryover):** Sprint 5B Saga infrastructure phải đã stable trước Sprint 8. Bug bash + performance test PHẢI cover Saga endpoints + flow. Documentation final phải include:
- Saga contracts (8 messages) + admin endpoints trong Postman/Swagger
- ADR-017 + ADR-018 publish vào `docs/adrs/` (đã merge từ `#233`/`#239` nhưng verify available)
- 3 Saga runbook publish vào `docs/operations/runbook/`
- Saga state machine diagram (Mermaid) vào architecture poster

**Tasks:**
- [ ] Performance testing + tuning per §13.4 SLAs (**bao gồm 3 Saga endpoint SLA mới + end-to-end Saga 4s P95**) — #120
- [ ] Security audit (OWASP checklist §14.7) (**bao gồm Saga admin endpoints: TicketSagaReprocess audit log + Idempotency-Key required + AdminIpWhitelist apply**) — #121
- [ ] Documentation: API contracts final (**Saga 8 contracts + V1/V2 BatteryAnomalyDetected**), README per service (**TicketService README bao gồm Saga ops section**), postman collection (**+ Saga admin folder**) — #122
- [ ] Documentation: IoT runbook final (`newiot.md`/`overall.iot.md`/`wiring-diagram.md`/`hardware-bom.csv`), ESP32 setup checklist, Postman/curl collection cho provision/heartbeat/ingest — #122
- [ ] Final seed data với scenarios realistic (**giữ 2 Saga seed row từ Sprint 5B, không refactor**) — #123
- [ ] Demo script: walkthrough end-to-end flow trên Mobile + Web — #123
- [ ] Demo script IoT: simulator/ESP32 path + hardware path (RS485 multi-drop), normal reading, overheat/low SOC alert, stop ESP32 => `DeviceOffline` (LWT tức thì nếu có MQTT, hoặc job 5 phút) — #123
- [ ] Demo script Saga: happy path (Alert → Ticket → link → `Completed`) + failure scenario (BatteryService down → Saga `Failed` → admin reprocess → recovery) không tạo Ticket trùng — #123
- [ ] Architecture poster A1: thêm Saga state machine diagram (Initial → TicketRequested → TicketProvisioned → AlertLinkRequested → Completed/Failed) — #123
- [ ] Bug bash + bug fix (**ưu tiên Saga edge case: timeout, late response, reconciliation, conflict TicketId**) — #124
- [ ] Final coverage push — #125
- [ ] **Architecture publish:** verify ADR-016/017/018 + 10 runbook (7 baseline + 3 Saga) đã available trong `docs/`; render Mermaid Saga state diagram vào poster. — #125

---

## 18. Definition of Done

### 18.1. Per ticket (theo `workflow.md`)
- [ ] `/kltn-task KAN-XX` đã viết `logs/KAN-XX/plan.md`
- [ ] User approve plan
- [ ] Code implement
- [ ] `/kltn-reviewcode` → PASS, log `logs/KAN-XX/review.md`
- [ ] `/kltn-test` → PASS với coverage report, log `logs/KAN-XX/test.md`
- [ ] `/kltn-ship KAN-XX` — push branch + commit logs folder + tạo PR
- [ ] Reviewer chạy `/kltn-reviewpr KAN-XX` → APPROVE
- [ ] Author chạy `/kltn-complete` → merge

### 18.2. Per service (production-ready demo)
- [ ] All CQRS handlers có unit test
- [ ] All endpoints có integration test
- [ ] Coverage ≥ 80% line
- [ ] Migration tested rollback
- [ ] Outbox relay running với claim/lock + retry/backoff + error metrics (Sprint 5B `#235`)
- [ ] Consumer thay đổi DB dùng durable Inbox/idempotency và chỉ mark Completed sau commit
- [ ] DI test assert mỗi interface có đúng 1 implementation/lifetime (không có direct producer override outbox writer)
- [ ] Saga + participant endpoints dùng EF Consumer Outbox/Inbox (Sprint 5B `#237/#238`)
- [ ] Health endpoints work (`/health/live`, `/health/ready`)
- [ ] Swagger documented
- [ ] Docker container build < 200MB
- [ ] Startup < 10s in container
- [ ] README per service với run local + run test instructions

### 18.2bis. Sprint 5B — Saga-specific code review checklist (PR `#237`/`#238`/`#239`)

Reviewer phải tick từng item này trong `logs/GH-NNN/review.md` cho PR thuộc Sprint 5B `#237`–`#239`. Các trap dưới đây là **non-obvious** và sẽ bị miss bởi generic review:

**Saga state machine (`#237`):**
- [ ] `Initially()` có `InsertOnInitial = true`?
- [ ] `Fault<CreateTicketFromAlertCommand>.Message.Message.CorrelationId` (không phải `FaultId`)?
- [ ] Cancel `StepTimeoutTokenId` trước khi schedule retry token mới?
- [ ] Cancel both token khi nhận success response (tránh late timeout/retry trigger)?
- [ ] `Completed` row có Explicit `.Ignore()` cho duplicate start event (KHÔNG `_skipped`)?
- [ ] State `Failed` có explicit `.Ignore()` cho late response message?
- [ ] `SetCompletedWhenFinalized` = false (giữ tombstone)?
- [ ] PostgreSQL `xmin` optimistic concurrency configured?
- [ ] Saga repository sử dụng EF Consumer Outbox cho transactional publish?

**Participant consumer (`#238`):**
- [ ] `CreateTicketFromAlertConsumer` lookup `OriginAlertId` trước khi lookup `(BatteryAssetId, Category)`?
- [ ] Reuse path KHÔNG overwrite `Ticket.OriginAlertId` của Ticket cũ?
- [ ] Reuse path trả `CreatedNew=false` (không publish `TicketCreatedEvent` lần hai)?
- [ ] Wire value `AnomalyType` int → internal enum: handle unknown với fallback (`Other` + warning metric)?
- [ ] `LinkAlertToTicketConsumer` handle 3 case: null/match/conflict (không overwrite âm thầm)?
- [ ] PostgreSQL `23505` chỉ catch khi constraint name khớp known guard (không nuốt mọi unique violation)?
- [ ] Sau bắt `23505`: rollback transaction + clear DbContext + reload bằng scope mới?
- [ ] Application operation dùng cùng scoped DbContext với consumer (không tự `SaveChanges`/direct-publish)?

**Cutover + flags (`#238`):**
- [ ] Direct `BatteryAnomalyDetectedConsumer` (TicketService) đã decommission khỏi DI registration?
- [ ] `AlertEscalationService` publish `BatteryAlertEscalationRequestedEvent` (KHÔNG republish `BatteryAnomalyDetectedEvent`)?
- [ ] Feature flag default trong appsettings.json đúng (xem §53.9)?
- [ ] Endpoint name cố định (không phụ thuộc auto-generated kebab-case)?

**Test (`#239`):**
- [ ] ≥ 21 test case khớp test matrix §53.10?
- [ ] Restart-recovery integration test pass (kill TicketService giữa transaction, restore, verify Saga continue)?
- [ ] Quartz schema apply trong test fixture (`qrtz_*` tables)?
- [ ] Contract test cho 8 Saga message + V1/V2 BatteryAnomaly?

**Observability (`#239`):**
- [ ] 8 metric đã register (xem §9.2 AppMetrics.cs)?
- [ ] 2 alert rule deploy vào alertmanager.yaml?
- [ ] Log structured fields đầy đủ (CorrelationId/AlertId/TicketId/CurrentState/MessageId)?
- [ ] KHÔNG log PII (email/phone/JWT)?

**Documentation (`#240`):**
- [ ] ADR-017 + ADR-018 merged vào `docs/adrs/`?
- [ ] 3 runbook tạo trong `docs/operations/runbook/`?
- [ ] 3 Mermaid diagram trong `docs/architecture/`?
- [ ] Swagger include Saga admin endpoints?
- [ ] Postman collection có Saga folder + Idempotency-Key header example?

### 18.3. Per system (end-to-end demo)
1. `docker compose --env-file .env.Docker up -d --build` chạy tất cả service xanh trong < 60s.
2. `tools/seed.sh` populate đầy đủ data.
3. End-to-end scenario chạy được:
   - Customer login Mobile → xem battery realtime → nhận push critical alert
   - Alert–Ticket Saga create/reuse ticket → xác nhận `Alert.TicketId` → Saga `Completed`
   - Manager assign Staff trên Web
   - SLA timer chạy → Staff resolve trên Web → Manager approve
   - Customer rate trên Mobile → ticket CLOSED
4. SLA breach scenario demo được (chỉnh sensor data hoặc time mock):
   - Ticket P1 SLA tới 80% → push warning
   - Ticket P1 SLA breach → auto ESCALATED → notify Admin
5. Reports endpoint trả số liệu khớp với data thực tế.
6. Grafana dashboards realtime updating.
7. Swagger UI ApiGateway có đủ schema mọi service.
8. Coverage report ≥ 80% per service tại CI.
9. Failure scenario Saga chạy được: ngắt BatteryService/RabbitMQ, khôi phục, reprocess và không tạo Ticket trùng.

---

# Phần VI — Phụ lục

## 20. Permission matrix đầy đủ

### Convention
Permission code: `{service}.{resource}.{action}`. Đã có `PermissionCodes` trong AuthService. Thêm mới:

```csharp
public static class PermissionCodes {
    // Battery
    public const string BatteryAssetView = "battery.asset.view";
    public const string BatteryAssetViewOwn = "battery.asset.view-own";
    public const string BatteryAssetManage = "battery.asset.manage";
    public const string BatteryAssetTransfer = "battery.asset.transfer";
    public const string BatteryTypeManage = "battery.type.manage";
    public const string BatteryThresholdManage = "battery.threshold.manage";
    public const string BatterySensorIngest = "battery.sensor.ingest";
    public const string BatterySensorView = "battery.sensor.view";
    public const string BatterySensorViewOwn = "battery.sensor.view-own";
    public const string AlertView = "battery.alert.view";
    public const string AlertViewOwn = "battery.alert.view-own";
    public const string AlertAcknowledge = "battery.alert.acknowledge";
    public const string AlertResolve = "battery.alert.resolve";
    public const string BatteryDashboardView = "battery.dashboard.view";

    // IoT device management (§52)
    public const string IotDeviceView = "iot.device.view";
    public const string IotDeviceManage = "iot.device.manage";          // create/update config/decommission + sinh/rotate API key
    public const string IotFirmwareManage = "iot.firmware.manage";      // upload/list firmware release
    public const string IotCalibrationManage = "iot.calibration.manage"; // tạo/xem calibration (Staff/Admin)

    // Ticket
    public const string TicketCreate = "ticket.create";
    public const string TicketViewOwn = "ticket.view-own";
    public const string TicketViewAll = "ticket.view-all";
    public const string TicketAssign = "ticket.assign";
    public const string TicketStart = "ticket.start";
    public const string TicketHold = "ticket.hold";
    public const string TicketResolve = "ticket.resolve";
    public const string TicketApprove = "ticket.approve";
    public const string TicketEscalate = "ticket.escalate";
    public const string TicketIncidentDeclare = "ticket.incident.declare";
    public const string TicketRate = "ticket.rate";
    public const string TicketReopen = "ticket.reopen";
    public const string TicketCommentAdd = "ticket.comment.add";
    public const string TicketCommentInternalView = "ticket.comment.internal.view";
    public const string MaintenanceLogManage = "maintenance-log.manage";
    public const string KbView = "kb.view";
    public const string KbManage = "kb.manage";
    public const string TicketReportsView = "ticket.reports.view";
    public const string TicketSagaView = "ticket.saga.view";
    public const string TicketSagaReprocess = "ticket.saga.reprocess";

    // Notification
    public const string NotificationViewOwn = "notification.view-own";
    public const string DeviceTokenManage = "notification.device.manage";
    public const string PreferenceManage = "notification.preference.manage";
}
```

### Default role → permission mapping (seed)

| Permission | Admin | Manager | Staff | Customer |
|-----------|:-----:|:-------:|:-----:|:--------:|
| BatteryAssetView | ✅ | ✅ | ✅ | — |
| BatteryAssetViewOwn | — | — | — | ✅ |
| BatteryAssetManage | ✅ | — | — | — |
| BatteryAssetTransfer | ✅ | — | — | — |
| BatteryTypeManage | ✅ | — | — | — |
| BatteryThresholdManage | ✅ | — | — | — |
| BatterySensorIngest | ✅ | — | — | — (use ApiKey) |
| BatterySensorView | ✅ | ✅ | ✅ | — |
| BatterySensorViewOwn | — | — | — | ✅ |
| AlertView | ✅ | ✅ | ✅ | — |
| AlertViewOwn | — | — | — | ✅ |
| AlertAcknowledge | ✅ | ✅ | ✅ | ✅ |
| AlertResolve | ✅ | ✅ | ✅ | — |
| BatteryDashboardView | ✅ | ✅ | — | — |
| IotDeviceView | ✅ | ✅ | ✅ | — |
| IotDeviceManage | ✅ | — | — | — |
| IotFirmwareManage | ✅ | — | — | — |
| IotCalibrationManage | ✅ | — | ✅ | — |
| TicketCreate | ✅ | ✅ | ✅ | ✅ |
| TicketViewOwn | — | — | ✅ (assigned) | ✅ (owned) |
| TicketViewAll | ✅ | ✅ | — | — |
| TicketAssign | — | ✅ | — | — |
| TicketStart | — | — | ✅ | — |
| TicketHold | — | — | ✅ | — |
| TicketResolve | — | — | ✅ | — |
| TicketApprove | — | ✅ | — | — |
| TicketEscalate | ✅ | ✅ | ✅ (request) | — |
| TicketIncidentDeclare | ✅ | ✅ | — | — |
| TicketRate | — | — | — | ✅ |
| TicketReopen | — | — | — | ✅ |
| TicketCommentAdd | ✅ | ✅ | ✅ | ✅ |
| TicketCommentInternalView | ✅ | ✅ | ✅ | — |
| MaintenanceLogManage | — | — | ✅ | — |
| KbView | ✅ | ✅ | ✅ | — |
| KbManage | ✅ | ✅ | — | — |
| TicketReportsView | ✅ | ✅ | — | — |
| TicketSagaView | ✅ | ✅ (read-only) | — | — |
| TicketSagaReprocess | ✅ | — | — | — |
| NotificationViewOwn | ✅ | ✅ | ✅ | ✅ |
| DeviceTokenManage | ✅ | ✅ | ✅ | ✅ |
| PreferenceManage | ✅ | ✅ | ✅ | ✅ |

### Ownership check (cross-cutting)
- `ViewOwn` permission → handler bắt buộc check `entity.CustomerId == currentUserId`.
- `Staff` xem ticket: bắt buộc check `ticket.AssignedStaffId == currentUserId` (trừ khi Admin/Manager).

---

## 21. Error code catalog

Chuẩn hóa cho FE handle dễ hơn. Trả về trong `CommonResponse.Message` hoặc field code riêng nếu cần.

### Format
`{SERVICE}_{CATEGORY}_{N}` — ví dụ `TICKET_STATE_001`.

| Code | Meaning | HTTP | Note |
|------|---------|------|------|
| `AUTH_LOGIN_001` | Sai email/password | 200 (isSuccess=false) | — |
| `AUTH_LOGIN_002` | Account bị khóa do nhiều lần sai | 200 | — |
| `AUTH_TOKEN_001` | Token expired | 401 | — |
| `AUTH_TOKEN_002` | Refresh token revoked | 401 | — |
| `AUTH_PERM_001` | Forbidden — thiếu permission | 403 | — |
| `BATTERY_ASSET_001` | Serial number trùng | 200 | — |
| `BATTERY_ASSET_002` | Customer not found | 200 | — |
| `BATTERY_ASSET_003` | BatteryType not found | 200 | — |
| `BATTERY_SENSOR_001` | Asset không tồn tại | 200 | — |
| `BATTERY_ALERT_001` | Alert đã được resolved | 200 | — |
| `TICKET_VAL_001` | BatteryAssetId required | 200 | — |
| `TICKET_VAL_002` | Customer không sở hữu asset | 200 | — |
| `TICKET_STATE_001` | Invalid transition | 200 | "Cannot transition from {From} to {To} by {Actor}" |
| `TICKET_STATE_002` | Ticket đã closed | 200 | — |
| `TICKET_ASSIGN_001` | Staff inactive | 200 | — |
| `TICKET_ASSIGN_002` | Staff vượt quá MaxConcurrentTickets | 200 | — |
| `TICKET_REOPEN_001` | Quá 7 ngày kể từ resolved | 200 | BR-06 |
| `TICKET_REOPEN_002` | Reopen lần thứ 2 → auto escalate | 200 (warning) | BR-07 |
| `TICKET_SLA_001` | SLA timer not running, không thể pause | 200 | — |
| `TICKET_RATE_001` | Đã rate rồi | 200 | — |
| `TICKET_SAGA_001` | Alert–Ticket Saga không tồn tại | 404 | — |
| `TICKET_SAGA_002` | Saga không ở trạng thái cho phép reprocess | 409 | Chỉ `Failed` |
| `TICKET_SAGA_003` | Alert đã link với Ticket khác | 409 | Không overwrite |
| `NOTIF_DEVICE_001` | Token Expo không hợp lệ | 200 | — |
| `FILE_UPLOAD_001` | File quá lớn (>10MB) | 400 | — |
| `FILE_UPLOAD_002` | Content-type không hỗ trợ | 400 | — |
| `GEN_RATE_001` | Rate limit exceeded | 429 | — |
| `GEN_VAL_001` | Validation error (generic) | 200 | `listErrors` populated |
| `GEN_NOTFOUND_001` | Resource not found | 200 (isSuccess=false) | — |

---

## 22. JWT claim structure

```json
{
  "sub": "{accountId}",
  "nameid": "{accountId}",
  "UserId": "{accountId}",
  "FullName": "Nguyễn Văn A",
  "Email": "a@example.com",
  "Role": "3",                                    // 1=Admin, 2=Manager, 3=Staff, 4=Customer
  "Permissions": ["ticket.view-own", "ticket.start", "ticket.resolve", ...],
  "session_id": "{sessionId}",
  "iat": 1715500000,
  "exp": 1715503600,                              // 1 hour
  "iss": "GSU26SE55-AuthService",
  "aud": "GSU26SE55-Clients"
}
```

- AccessToken: 1h
- RefreshToken: 7d (Redis key `RT_{userId}`)
- Permissions cache 10min — nếu role/permission đổi → AuthService publish event `PermissionsChangedEvent` → các service invalidate cache.

---

## 23. Risk register

> **29 risk items** chia 5 nhóm chính:
> - **R-01..R-13**: Technical baseline (state machine, SLA, dedup, migration, performance, security)
> - **R-14..R-18**: Sprint 5B Saga design (forward recovery, duplicate, scope creep, cutover, restart)
> - **R-19..R-22**: Sprint 5B operational (preflight cleanup, mapping, Quartz schema, notification spam)
> - **R-23..R-27**: Capacity + planning + external (Thắng solo owner Sprint 5B + IoT-1, bus factor, ext quota, mentor schedule)
> - **R-28..R-29**: IoT v2 pivot (ESP32 firmware codebase mới, BMS procurement / register map — xem §52, `overall.iot.md` §D)
>
> Mỗi risk có owner cụ thể. Leader review weekly trong daily standup, escalate Sev-High risk khi likelihood tăng.

| # | Risk | Likelihood | Impact | Mitigation | Owner |
|---|------|-----------|--------|------------|-------|
| R-01 | State machine TicketService bug | High | High | Test matrix 30+ transitions, code review focus | BE Lead |
| R-02 | SLA pause/resume tính sai → KPI bị skew | Med | High | `SlaCalculator` unit test 8 case, audit trail SlaPauseEvent | Thắng |
| R-03 | Alert dedup window không đúng → spam ticket | Med | Med | Configurable window, default 30min, test với scenario burst | Thái |
| R-04 | TimescaleDB migration phá DB hiện tại | High | High | Test trên branch riêng, rollback migration verified | Thái |
| R-05 | Outbox lag → event không publish kịp demo | Med | Med | Monitor unprocessed count, 5s tick frequency, retry với backoff | Duy |
| R-06 | Reopen infinite loop | Low | Med | BR-06 enforce 7d, BR-07 escalate sau 2 lần | Duy |
| R-07 | Test coverage không đạt 80% | High | Med | Scaffold-unit-tests luôn chạy cùng scaffold-crud, weekly coverage report | Leader |
| R-08 | Docker compose chậm/fail trên demo machine | Med | High | Health check thorough, restart policy, image pre-pull | Leader |
| R-09 | Expo push token rate limit / sandbox quirks | Med | Low | Polly retry, fallback in-app, document Expo setup | Thắng |
| R-10 | OWASP vulnerability lúc demo | Low | High | Trivy scan đã có, manual review §14 | Leader |
| R-11 | Performance: realtime endpoint < 100ms khó | Med | Med | Caching strategy §13, index `(asset, time DESC)`, benchmark | Thái |
| R-12 | Microservice event chain race condition | Med | High | Outbox + Inbox idempotency, integration test với TestHarness | Duy |
| R-13 | Demo gặp bug khi live | High | High | Final sprint dành cho bug bash + rehearsal | Cả team |
| R-14 | Saga tạo Ticket nhưng không link được Alert | Med | High | Persisted state, timeout, forward recovery, admin reprocess, stuck alert | Thắng |
| R-15 | Duplicate/redelivery tạo nhiều Ticket cho một Alert | Med | High | Unique `OriginAlertId`, durable Inbox, idempotent lookup, concurrency test | Thắng |
| R-16 | Scope creep đưa Energy/CO2 quay lại BatteryService | Med | Med | ADR scope guard, contract search trong CI, backlog review theo §53.1 | Thắng |
| R-17 | Direct consumer cũ và Saga cùng chạy khi cutover | Med | High | Feature flag, drain queue cũ, remove registration, smoke test trước enable | Thắng |
| R-18 | Timeout/redelivery bị mất khi service restart | Med | High | Persistent Quartz scheduler, restart-recovery integration test | Thắng |
| R-19 | Preflight cleanup bỏ sót duplicate → migration `AddAlertTicketSagaFoundation` fail giữa rollout | Low | High | Runbook `10-saga-duplicate-canonical.md`, dry-run trên staging trước, transaction wrap migration | Thắng |
| R-20 | `OriginAlertId` reuse logic bị nhầm → reuse Ticket sai category | Med | High | Mapping table §53.7 có 15 wire value + unknown, unit test mapping đầy đủ, integration test reuse vs new | Thắng |
| R-21 | Quartz schema chưa apply → Saga timeout không trigger sau restart | Low | High | Migration `AddQuartzPersistenceSchema` chạy ở init container; health check assert `qrtz_triggers` exists khi startup | Thắng |
| R-22 | Manager notification AlertTicketSagaFailed spam | Low | Med | Rate-limit notification dispatcher (debounce 5 phút per AlertId), §49 batching | Thắng |
| R-23 | Thắng solo owner Sprint 5B (9 task ~8.5 dev-day) → bottleneck `#237`/`#239` slip | High | High | Working weekend, defer scope phụ (Ambient/B2-finalize), KHÔNG defer `#237` (xem §17 capacity warning) | Thắng |
| R-24 | Sprint IoT-1 chồng Sprint 6 window → solo owner Thắng overload nếu cả 2 không kịp tách thời gian | Med | Med | Thắng owns IoT-1 (đồng bộ tiếp BatteryService work từ Sprint 5B); hardware partner liên hệ trước Sprint 5B kết thúc; defer MQTT P3 sang Sprint 7 nếu thiếu giờ | Thắng |
| R-25 | Bus factor=1 cho Saga code → Thắng unavailable block toàn bộ Sprint 5B | Low | Critical | Code walkthrough video sau khi `#237` merge (upload `docs/knowledge-transfer/saga-walkthrough.md`); documentation immediate sau mỗi merge để team đọc kế thừa nếu cần | Thắng |
| R-26 | External service quota hết giữa demo Sprint 8 (email/SMS/Expo) | Med | Med | Đăng ký nhiều provider + fallback in-app + monitor quota hàng tuần (xem §56.15 external dependency register) | Leader |
| R-27 | Mentor (GVHD) không available cho dry-run review post-Sprint 8 | Low | High | Leader confirm GVHD lịch trước Sprint 8 kết thúc, book 2 slot dự phòng (xem §56.14 timeline) | Leader |
| R-28 | Pivot ESP32 → firmware C++/Arduino là **codebase mới**, team BE thiếu kinh nghiệm embedded → IoT-1 slip | Med | Med | MVP `mock_bms` (data giả) chứng minh flow backend trước, không phụ thuộc firmware; MQTT là P3 optional (HTTPS đủ demo); reuse logic từ `iot.md` v1; pair với đối tác phần cứng (xem §52.10, R-24) | Thắng |
| R-29 | Mua nhầm BMS không có register map / không đổi được `unitId` → không đọc được data dù pin chạy tốt (multi-drop fail) | Med | High | Checklist mua BMS bắt buộc (RS485/Modbus + register map + đổi unitId + CRC — `overall.iot.md` §A4); test 1 BMS bằng USB-RS485 + Modbus Poll trước khi mua số lượng; ESP32 `mock_bms` fallback cho demo | Thắng |

---

## 24. Checklist theo 6 phase business flow

### Phase 1 — Setup & Configuration (ADMIN)
- [x] User CRUD — AuthService DONE
- [x] Account profile expansion (avatar, phone, skill) — §7
- [ ] BatteryType CRUD — BatteryService §1
- [ ] ThresholdConfig CRUD — §1
- [ ] BatteryAsset CRUD + TransferOwner — §1
- [x] SLA rules hardcoded P1/P2/P3 = 4/24/72h — không cần CRUD
- [x] Audit log endpoint — AuthService DONE

### Phase 2 — Monitoring & Detection (CUSTOMER + SYSTEM)
- [ ] SensorReading batch ingest — §1.8
- [ ] Realtime query — §1.8
- [ ] History query với granularity — §1.8
- [ ] ThresholdCheckBackgroundService — §1.6
- [ ] AlertCreate + dedup BR-03 — §1.6
- [ ] Publish `BatteryAnomalyDetectedEvent` (+ V2 enriched) qua Outbox — §1.7
- [ ] Push notify Customer khi critical — §3.4
- [ ] `AlertEscalationBackgroundService` publish `BatteryAlertEscalationRequestedEvent` khi Critical Alert chưa-ack > 5 phút — §1.6, §53.4

### Phase 3 — Ticket Creation (CUSTOMER / SYSTEM)
- [ ] TicketCreateCommand (Customer mobile) BR-01 mandatory asset — §2.5
- [ ] Alert–Ticket Saga nhận anomaly và gửi `CreateTicketFromAlertCommand` — §8.3
- [ ] Ticket create/reuse BR-02 idempotent — §2.7
- [ ] BatteryService link `Alert.TicketId`, Saga `Completed` — §53
- [ ] Activity Created BR-08 — §2.3.4

### Phase 4 — Triage & Assignment (MANAGER)
- [ ] Manager queue query — §2.5
- [ ] StaffWorkloadQuery + skill match — §2.5
- [ ] TicketAssignCommand (priority cố định) — §2.5
- [ ] Start SlaTimer on ASSIGNED — §2.4
- [ ] Notify Staff via NotificationService — §3.4

### Phase 5 — Resolution (STAFF)
- [ ] TicketStartCommand → IN_PROGRESS — §2.5
- [ ] TicketHoldCommand → WAITING_* BR-04 — §2.5
- [ ] TicketResumeCommand — §2.5
- [ ] CommentAddCommand — §2.5
- [ ] MaintenanceLogAddCommand — §2.5
- [ ] TicketResolveCommand — §2.5
- [ ] TicketEscalateRequestCommand — §2.5
- [ ] KB suggest endpoint — §4
- [ ] SlaTimerBackgroundService warning 80% — §2.6

### Phase 6 — Verification & Closure (MANAGER + CUSTOMER)
- [ ] TicketApproveCommand BR-05 — §2.5
- [ ] TicketRejectCommand → IN_PROGRESS — §2.5
- [ ] TicketRateCommand (Customer) — §2.5
- [ ] TicketReopenCommand 7d BR-06 — §2.5
- [ ] Escalate on reopen ≥ 2 BR-07 — §2.5
- [ ] AutoCloseBackgroundService 7d — §2.6
- [ ] CSAT report — §5.2

### Cross-cutting
- [x] FileStorageService `UploadedFile` metadata + `fileId` reference — §6bis
- [x] Docker Compose per-service logical database setup (`auth_db`, `file_storage_db`)
- [ ] Outbox cho BatteryService + TicketService — §8.1
- [ ] Inbox idempotency consumer — §8.2
- [ ] Alert–Ticket Saga + timeout/reprocess/observability — §8.3, §53
- [ ] OpenTelemetry tracing — §8.4
- [ ] Gateway JWT validate + claim forward — §10.1
- [ ] OpenAPI aggregate at gateway — §10.4
- [ ] Grafana business dashboards — §9.2
- [ ] Coverage ≥ 80% per service — §11
- [ ] Seed data script — §12.1

---

## 25. Câu hỏi cần thống nhất trước khi bắt đầu

| # | Câu hỏi | Đề xuất |
|---|---------|---------|
| Q-01 | Đổi postgres image sang `timescaledb` ngay hay tách DB riêng cho BatteryService? | **Đổi image** — đơn giản, postgres 16 vẫn full feature |
| Q-02 | Outbox cho BatteryService/TicketService áp dụng từ đầu hay sau? | **Từ đầu** — AuthService đã có template |
| Q-03 | Expo Push thật hay mock cho demo? | **Thật** — capstone có Mobile demo |
| Q-04 | KnowledgeBase module hay service riêng? | **Module trong TicketService** — scope hợp lý |
| Q-05 | Account profile/staff fields: nhét vào `Account`, tách bảng extension, hay tách UserService? | **Tách bảng extension trong AuthService** — giữ `Account` sạch, chưa cần UserService riêng |
| Q-06 | API versioning `/api/v1/` từ đầu hay sau? | **Từ đầu** |
| Q-07 | Gateway JWT validate khi nào? | **Sau khi 2 service đầu (Battery, Ticket) có endpoint chạy** |
| Q-08 | TestContainers hay shared dev Postgres? | **TestContainers** |
| Q-09 | Có cần WebSocket cho realtime dashboard? | **Tạm dùng polling 30s** (TanStack Query refetchInterval), WebSocket Sprint 8+ nếu kịp |
| Q-10 | IoT data source thật hay simulator? | **Simulator script** cho capstone (real IoT out of scope) |
| Q-11 | Notification có cần "do not disturb" (quiet hours)? | **Có** — nằm trong NotificationPreference §3.3 |
| Q-12 | Customer có thể cancel ticket không? | **KHÔNG** — chỉ rate hoặc reopen, vì cần audit trail |
| Q-13 | Manager có thể đổi priority sau khi gán không? | **KHÔNG** — theo design.md priority policy |
| Q-14 | Có cần SMS OTP cho login Customer Mobile? | **Có** (đã có SmsService) — optional flag |
| Q-15 | File attachment limit size? | **10MB/file, 5 files/ticket** |
| Q-16 | Cache strategy: Redis hay InMemory? | **Redis** (đã có sẵn) |
| Q-17 | Có pre-staging environment? | **Không** — chỉ local + final demo |
| Q-18 | Các service lưu file bằng `objectKey` hay `fileId`? | **Lưu `fileId`** — FileStorageService phải có `UploadedFile` metadata table, `objectKey` chỉ là internal detail |
| Q-19 | Alert→Ticket dùng choreography (direct consumer) hay orchestrated Saga? | **Orchestrated Saga** (ADR-018) — xem §53.4–§53.5 |
| Q-20 | Saga retry/timeout dùng RabbitMQ delayed-message hay durable scheduler riêng? | **Persistent Quartz scheduler** trong TicketService — RabbitMQ image hiện tại không có delayed-message plugin (§53.8) |
| Q-21 | Saga `Completed` row giữ hay cleanup? | **Giữ làm tombstone** — chống event cũ tạo lại Saga; không bật `SetCompletedWhenFinalized` (§53.5) |
| Q-22 | BR-02 dedup theo `OriginAlertId` hay `(BatteryAssetId, Category)`? | **Cả hai** — lookup `OriginAlertId` trước (retry cùng Alert), rồi mới fallback `(asset, category)` (§53.8) |
| Q-23 | Khi reuse Ticket cho Alert mới, có overwrite `Ticket.OriginAlertId` không? | **KHÔNG** — `OriginAlertId` chỉ lưu Alert đầu tiên; quan hệ many-alerts-to-one-ticket nằm ở `Alert.TicketId` (§53.6) |
| Q-24 | Energy/CO2 analytics có làm trong capstone không? | **KHÔNG** (ADR-017) — out of scope; chỉ giữ battery health metric (Voltage/Current/SOC/SOH/CycleCount) — §53.1 |
| Q-25 | `BatteryAnomalyDetectedEvent` V1 và V2 có dual-publish không? | **Có trong cutover** — Saga subscribe cả hai; deprecate V1 sau khi V2 stable (§30.6) |

---

## 26. Glossary & references

### Glossary
| Term | Định nghĩa |
|------|-----------|
| **Ticket** | Yêu cầu hỗ trợ từ Customer hoặc tự động sinh từ alert critical |
| **Asset** | Một bộ pin cụ thể (BatteryAsset entity) gắn với Customer |
| **Alert** | Cảnh báo sinh ra khi sensor reading vượt ngưỡng |
| **Anomaly** | Bất thường được phát hiện (overheat, overvoltage, ...) |
| **SLA** | Service Level Agreement — deadline xử lý ticket theo priority |
| **Priority** | Mức độ ưu tiên ticket (P1/P2/P3), Manager gán 1 lần |
| **Escalation** | Đẩy ticket lên level cao hơn khi không xử lý được |
| **Incident** | Critical event ảnh hưởng nhiều ticket/asset hoặc rủi ro an toàn |
| **Activity** | Log mỗi hành động trên ticket (BR-08) |
| **Reopen** | Customer mở lại ticket trong 7 ngày sau khi resolved (BR-06) |
| **Maintenance log** | Ghi nhận công việc Staff đã làm khi xử lý |
| **CSAT** | Customer Satisfaction (rating 1-5) |
| **Outbox** | Pattern lưu event vào DB trước khi publish, đảm bảo atomic |
| **Inbox** | Pattern dedup message ở consumer để idempotent |
| **Saga** | State machine persisted điều phối transaction nghiệp vụ qua nhiều service/database |
| **Forward recovery** | Khôi phục bằng retry/reprocess bước chưa hoàn tất thay vì xóa ngược dữ liệu đã commit |
| **Tombstone** | Row terminal-state (Completed/Failed) được giữ lại trong DB để chống event/message cũ tạo lại entity mới — không hard-delete |
| **EF Consumer Outbox** | MassTransit feature commit consumed message + outgoing message cùng `DbContext` transaction của business action |
| **Wire value** | Số nguyên ổn định cross-service trong contract. Trong dự án này, wire value của `AnomalyType` **bằng** integer của `AnomalyTypeEnum` ở §1.3.6 (sau v4.5 reconcile) — single source of truth. Upgrade không breaking khi chỉ thêm enum value mới (existing values KHÔNG ĐƯỢC thay đổi); subscriber luôn handle unknown wire value an toàn cho forward-compatible rolling deploy. |
| **IoT edge device** | Thiết bị tại site đọc sensor/BMS rồi gửi backend. Chuẩn v2 = **ESP32-S3** (`DeviceType=StandaloneSensor`); v1 legacy = Raspberry Pi. Là điểm kiểm soát bảo mật duy nhất (1 device = 1 key + TLS). Xem §52, ADR-016 |
| **BMS** | Battery Management System — mạch quản lý tích hợp trong pack pin, expose voltage/current/temp/SOC/SOH/error qua RS485-Modbus hoặc CAN |
| **RS485 / Modbus RTU** | Chuẩn truyền nối tiếp công nghiệp (request/response) ESP32 dùng để đọc BMS. Bus đa điểm (multi-drop) |
| **Multi-drop** | Nhiều BMS nối chung 1 cặp dây RS485 A/B; mỗi BMS có **`unitId`** (slave address) khác nhau để ESP32 poll lần lượt → 1 ESP32 quản nhiều pin |
| **`unitId`** | Địa chỉ slave Modbus của 1 BMS trên bus multi-drop (1,2,3…). Map sang `BatteryAsset` qua `IotDevice.ConfigJson.batteryMappings` (§52.2) |
| **MQTT** | Giao thức pub/sub realtime (kết nối thường trực, <100ms) cho telemetry/downlink (v2/P3 — §52.14). Khác HTTPS REST (request/response, v1) |
| **Broker** | Phần mềm trung chuyển MQTT (EMQX/Mosquitto) — như "database của message", chỉ deploy + config (`infra/mqtt/`), không viết code |
| **LWT (Last Will & Testament)** | Cơ chế MQTT: broker tự publish `offline` khi device rớt kết nối (keep-alive ~60s) → backend phát hiện offline tức thì thay vì chờ job 5 phút (§52.6) |
| **Calibration** | Hiệu chuẩn sensor: `calibrated = raw × scaleFactor + offsetValue`, lấy chuẩn từ thiết bị đo (Fluke 87V). Lưu `IotDeviceCalibration`, có `ValidUntil` (§52.8) |
| **Cross-source validation** | So reading cùng pin từ 2 nguồn độc lập (BMS-relayed `SourceType=Bms` vs INA226 `SourceType=IotGateway`) trong cửa sổ 60s → `SensorMismatch` nếu lệch quá ngưỡng (§1.6.6, §52.9) |
| **OTA** | Over-The-Air firmware update — backend đẩy `.bin` (signed URL + SHA-256), ESP32 verify rồi flash, rollback nếu fail (§52.7) |

### References

**Service management & SLA framework (B5 + B11):**
- **ITIL 4 Service Value System (SVS) — Incident Management Practice** — cho B2B customer-facing service.
  > **Lưu ý stance (B5):** Hệ thống GSU26SE55 phục vụ **B2B** (doanh nghiệp vận hành solar farm + B2C end-user). KHÔNG áp dụng ITIL 4 phiên bản internal-IT — sử dụng ITIL 4 SVS với góc nhìn Service Provider → External Customer. Xem `docs/adr/0005-b2b-itil-stance.md` cho quyết định đầy đủ.
- **ITIL 4 Foundation — Incident Prioritization (Impact × Urgency matrix)** — cơ sở của Priority Matrix §2.4bis.
- **ITIL 4 Problem Management** — cơ sở của Incident flag.
- **ISO/IEC 20000-1:2018** — service management requirements (cho B2B).
- **B2B SaaS SLA frameworks** — Atlassian/Jira Service Management SLA best practices (B2B field service).

**Security & compliance:**
- OWASP Top 10 2021 — security checklist §14.7
- GDPR Articles 15–22 — data subject rights §39

**Architecture & patterns:**
- Clean Architecture — Robert C. Martin, layered structure
- Microsoft Microservices Patterns — Outbox, Saga
- MassTransit docs — consumer + retry/circuit breaker
- TimescaleDB docs — hypertable, continuous aggregate
- Expo Push docs — https://docs.expo.dev/push-notifications/sending-notifications/

**AI & battery research (B2):**
- Xem `.claude/docs/ai-research-references.md` cho danh sách paper đầy đủ cite cho 15 anomaly types, IsolationForest hyperparameter justification, NASA dataset spec.

**IoT (v2 — ESP32 + MQTT):**
- `newiot.md` — thiết kế tổng thể ESP32-S3 + MQTT (topic, broker, bridge, firmware, roadmap P0–P5).
- `overall.iot.md` — BOM phần cứng đầy đủ + luồng vận hành (provision/data/anomaly/offline/calibration/OTA).
- `wiring-diagram.md` — sơ đồ đấu dây + bảng GPIO ESP32-S3.
- `hardware-bom.csv` — bảng mua sắm theo cấp ngân sách (Cấp 0→4).
- `iot.md` — **deprecated** (Raspberry Pi v1, Python) — chỉ tham khảo logic queue/calibration/validation.
- MQTT/MQTTnet docs, Eclipse Mosquitto / EMQX docs, Modbus RTU spec — cho bridge + broker + firmware.

**ADRs:**
- `docs/adr/0005-b2b-itil-stance.md` — B2B/B2C scope + ITIL stance (B5)
- (các ADR khác bổ sung khi triển khai)

---

## 27. Troubleshooting playbook

### 27.1. "Migration báo lỗi 'relation does not exist'"
- Check DbContext có `DbSet<T>` chưa.
- Check entity Configuration có `ToTable("...")` chưa.
- Run `dotnet ef migrations remove` rồi add lại.

### 27.2. "Consumer không nhận event"
- Check RabbitMQ Management UI: exchange + queue binding đúng?
- Check `appsettings.json`: `RabbitMq:Host` đúng?
- Check log MassTransit: có error consume không?
- Check DI: consumer đã register chưa (`AddMessageBus(... typeof(MyConsumer).Assembly)`)?

### 27.3. "Test integration timeout chờ DB"
- TestContainers cần Docker chạy.
- TimescaleDB image lớn — pre-pull: `docker pull timescale/timescaledb:latest-pg16`.

### 27.4. "JWT 401 ngay sau login"
- Check `JwtSettings:SecretKey` đồng nhất giữa AuthService và gateway.
- Check clock skew giữa containers (NTP).

### 27.5. "Outbox messages không được publish"
- Check `OutboxRelayBackgroundService` đã `AddHostedService` chưa.
- Check log: có exception khi serialize event không.
- Check RabbitMQ queue depth có tăng không.

### 27.6. "SLA timer không trigger warning"
- Check `WarningSentAt` đã null chưa (chỉ gửi 1 lần).
- Check background service đang chạy (`/health/ready`).
- Check `Status = Running` chưa (có thể đang Paused).

### 27.7. "Customer không nhận push"
- Check `NotificationPreference.PushEnabled = true`.
- Check `DeviceToken` còn `LastSeenAt` gần đây.
- Check quiet hours.
- Check Expo response: nếu `DeviceNotRegistered` → invalidate token.

### 27.8. "Performance chậm GET ticket list"
- Check index `(CustomerId, Status, IsDeleted)` tồn tại.
- Check N+1 query trong handler: dùng `.Include()` đầy đủ.
- Check pagination có applied (`Skip/Take`).

### 27.9. "Saga stuck ở `TicketRequested` không tiến" (Sprint 5B)
- Query `SELECT current_state, updated_at_utc, ticket_attempt_count, last_error FROM alert_ticket_saga_states WHERE correlation_id = '<alert-id>'`.
- Check `mt_inbox_state` của participant: message `CreateTicketFromAlertCommand` đã arrive chưa?
- Check Quartz `qrtz_triggers` xem `StepTimeoutTokenId`/`RetryTokenId` còn active không.
- Nếu TicketService crash sau commit Ticket nhưng trước response: chờ restart-recovery hoặc admin reprocess theo runbook `08-saga-failed.md`.
- Nếu bị `Fault<CreateTicketFromAlertCommand>` repeat: kiểm tra mapping `AnomalyType` (xem §53.7) và `category` enum đầy đủ chưa.

### 27.10. "Duplicate Ticket sau khi enable Saga" (Sprint 5B)
- Check feature flag: `AlertTicketDispatchEnabled` + direct consumer cùng on → xem alert rule §9 và runbook cutover.
- Verify unique filtered index `tickets.origin_alert_id` đã apply (preflight cleanup làm chưa?).
- Verify queue direct cũ đã decommission, không còn binding `BatteryAnomalyDetectedConsumer`.

### 27.11. "`Alert.TicketId` vẫn null sau Saga Completed" (Sprint 5B)
- Saga `Completed` chỉ chuyển sau khi nhận `AlertLinkedToTicketEvent`. Nếu Saga đã Completed mà Alert.TicketId vẫn null → có race condition giữa Saga state machine và Battery DB commit; kiểm tra log `LinkAlertToTicketConsumer` và `mt_outbox_message` của BatteryService.
- Chạy reconciliation: `POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess` với reason `link-recovery`.

---

## 28. Tóm tắt files/paths cần tạo

### Service skeleton (cho 3 service mới)
```
services/BatteryService/
├── BatteryService.slnx
├── src/
│   ├── BatteryService.Api/                     (~10 files: Program + Controllers + appsettings + Dockerfile)
│   ├── BatteryService.Application/             (~50 files: CQRS + DTOs + Consumers + Services)
│   ├── BatteryService.Domain/                  (~14 files: Entities + Enums)
│   └── BatteryService.Infrastructure/          (~20 files: Persistence + Migrations + Background jobs + DI)
└── tests/
    ├── BatteryService.UnitTests/               (~25 test files)
    └── BatteryService.IntegrationTests/        (~10 test files)

services/TicketService/                         (Tương tự, ~140 files total)
services/NotificationService/                   (Tương tự, ~80 files total)
```

### FileStorageService updates (Sprint 1)
```
services/FileStorageService/src/
├── FileStorageService.Domain/                  ← nếu chưa có
│   ├── FileStorageService.Domain.csproj
│   ├── Entities/
│   │   └── UploadedFile.cs
│   └── Enums/
│       ├── FilePurposeEnum.cs
│       └── FileStatusEnum.cs
├── FileStorageService.Application/
│   ├── DTOs/
│   │   ├── FileMetadataDto.cs
│   │   └── FileUploadResponse.cs              ← thêm FileId/Purpose/Status/SizeBytes
│   ├── CQRS/
│   │   ├── Query/GetFileMetadataQuery.cs
│   │   ├── Query/GetPresignedUrlByFileIdQuery.cs
│   │   └── Command/DeleteFileByIdCommand.cs
│   └── Interfaces/Repositories/
│       └── IFileStorageUnitOfWork.cs
└── FileStorageService.Infrastructure/
    ├── Persistence/
    │   ├── ApplicationDbContext.cs
    │   ├── Configurations/UploadedFileConfiguration.cs
    │   └── Migrations/*AddUploadedFileMetadata*
    └── Persistence/Repositories/FileStorageUnitOfWork.cs
```

### Shared updates
```
shared/src/SharedContracts/Events/
├── Battery/
│   ├── BatteryAssetCreatedEvent.cs
│   ├── BatteryAnomalyDetectedEvent.cs                ← V1 baseline (giữ trong cutover, deprecate sau khi V2 stable)
│   ├── BatteryAnomalyDetectedV2Event.cs              ← Sprint 5B: enrich Classification/SOH/AnomalyScore từ AI (xem §30.6)
│   ├── BatteryAlertEscalationRequestedEvent.cs       ← Sprint 5B: alert chưa-ack > 5 phút (xem §1.5)
│   └── BatteryAssetTransferredEvent.cs
├── Ticket/
│   ├── TicketCreatedEvent.cs
│   ├── TicketAssignedEvent.cs
│   ├── TicketStatusChangedEvent.cs
│   ├── TicketResolvedEvent.cs
│   ├── TicketApprovedEvent.cs
│   ├── TicketRejectedEvent.cs
│   ├── TicketReopenedEvent.cs
│   ├── TicketClosedEvent.cs
│   ├── TicketEscalatedEvent.cs
│   ├── IncidentDeclaredEvent.cs
│   ├── SlaWarningEvent.cs
│   └── SlaBreachedEvent.cs
├── Saga/AlertTicket/
│   ├── CreateTicketFromAlertCommand.cs
│   ├── TicketProvisionedForAlertEvent.cs
│   ├── TicketProvisionForAlertRejectedEvent.cs
│   ├── LinkAlertToTicketCommand.cs
│   ├── ReconcileAlertTicketSagaCommand.cs
│   ├── AlertLinkedToTicketEvent.cs
│   ├── AlertLinkToTicketRejectedEvent.cs
│   └── AlertTicketSagaFailedEvent.cs
├── Account/
│   ├── AccountProfileUpdatedEvent.cs          (update: AvatarFileId, ExternalAvatarUrl, AvatarSource)
│   ├── StaffProfileUpdatedEvent.cs            (new)
│   └── StaffSkillsUpdatedEvent.cs             (new)
└── Notification/
    └── PermissionsChangedEvent.cs             (new — invalidate cache cross-service)
```

### Infra/config updates
```
docker-compose.yml                              ← postgres image, thêm tempo container
.env / .env.Docker                              ← Battery/Ticket/Notification DB conn, Expo token
.env.example                                    ← cập nhật
ci/                                             ← (giữ nguyên)
deploy/                                         ← helm chart cho 3 service mới
.github/workflows/ci.yml                        ← thêm matrix cho 3 service mới
monitoring/grafana/dashboards/                  ← thêm 3 dashboard JSON
monitoring/prometheus/prometheus.yml            ← thêm scrape config cho 3 service
monitoring/alertmanager/alertmanager.yml        ← thêm 3 alert rules
monitoring/tempo.yaml                           ← config Tempo
```

### Gateway updates
```
services/ApiGateway/src/                        ← route config + JWT validate middleware + rate limit + swagger aggregate
                                                  + Sprint 5B: /api/v1/admin/sagas/alert-ticket/* route + reprocess rate limit (10/min/Admin)
```

### Sprint 5B — Saga + Quartz infra (files mới)
```
shared/src/SharedContracts/Saga/AlertTicket/        ← 8 contract files (xem §28 shared updates)

services/TicketService/src/TicketService.Infrastructure/
├── Sagas/
│   ├── AlertTicketSagaState.cs
│   ├── AlertTicketSagaStateMachine.cs
│   └── AlertTicketSagaDefinition.cs               ← endpoint name + retry/timeout policy
├── Persistence/
│   ├── Configurations/AlertTicketSagaStateConfiguration.cs
│   └── Migrations/*AddAlertTicketSagaFoundation*  ← + unique filtered index tickets.origin_alert_id + partial unique guard
└── Persistence/Migrations/*AddQuartzPersistenceSchema*  ← qrtz_* 11 tables (official SQL script)

services/BatteryService/src/BatteryService.Application/Consumers/
└── LinkAlertToTicketConsumer.cs                    ← saga participant
services/BatteryService/src/BatteryService.Infrastructure/
└── Persistence/Migrations/*AddAlertTicketLinkIndex*       ← non-unique filtered index alerts.ticket_id

services/NotificationService/src/NotificationService.Application/Consumers/
├── BatteryAlertEscalationRequestedConsumer.cs      ← push Manager + Admin
└── AlertTicketSagaFailedConsumer.cs                ← push Admin

services/NotificationService/src/NotificationService.Application/Templates/
├── battery-alert-escalation-pending.hbs            ← email template
└── alert-ticket-saga-failed.hbs                    ← email template

docs/adrs/
├── ADR-017-remove-energy-co2-analytics.md
└── ADR-018-orchestrated-alert-ticket-saga.md

docs/operations/runbook/
├── 08-saga-failed.md
├── 09-saga-stuck.md
└── 10-saga-duplicate-canonical.md

docs/architecture/                                  ← 3 Mermaid diagram mới (xem §65.3, link từ ADR-018)
├── state-machine-alert-ticket-saga.mmd
├── sequence-alert-ticket-saga-happy.mmd
└── sequence-alert-ticket-saga-failure.mmd

docs/onboarding/be-newcomer.md                      ← cập nhật 3 section: Saga local setup, Debug Saga, Common mistakes (xem §40.6)
.claude/CLAUDE.md / .claude/rules/tech/be.md        ← cập nhật pattern "Orchestrated Saga" + EF Consumer Outbox/Inbox (xem §0bis.2)
.github/workflows/ci.yml                            ← thêm step "Energy/CO2 scope guard (ADR-017)" (xem §53.2bis)
```

### Scripts
```
tools/
├── seed.sh                                     ← seed accounts + battery + ticket + KB
├── generate-sensor-data.py                     ← Python simulator IoT data
├── load-test.k6.js                             ← k6 perf test (optional)
├── reset-demo.sh                               ← reset demo data (xem §56.2)
├── inject-anomaly.sh                           ← inject sensor anomaly (xem §56.4)
├── fast-forward-sla.sh                         ← time mock SLA breach (xem §56.4)
├── trigger-incident.sh                         ← declare incident (xem §56.4)
├── seed-demo-scenarios.sh                      ← demo scenario seed bao gồm Saga states (xem §56.3)
└── Sprint 5B additions (xem §56.4 + §56.12):
    ├── simulate-saga-failure.sh                ← demo Saga Failed → reprocess recovery
    ├── inspect-saga.sh                         ← query Saga state cho debug
    ├── smoke-test.sh                           ← pre-demo health check verify all service + Saga endpoint
    ├── restart-stack.sh                        ← mid-demo recovery, restart 1 service < 30s
    ├── dev-cleanup.sh                          ← local dev disk cleanup (Quartz + Saga old states) (xem §40.6)
    └── release-notes.sh                        ← parse commits → CHANGELOG.md (xem §65.5)
```

### Docs (project-level)
```
docs/
├── core-business-flow.html                     ← Đã có (source of truth)
├── api/
│   ├── auth.swagger.json                       ← export per service
│   ├── battery.swagger.json
│   ├── ticket.swagger.json
│   └── notification.swagger.json
├── architecture/
│   ├── microservices-overview.md
│   ├── event-flow.md                           ← biểu đồ event giữa services
│   └── state-machine-ticket.md                 ← chi tiết visual state machine
└── onboarding/
    └── be-newcomer.md                          ← onboarding doc BE dev mới
```

---

## 29. Tóm tắt nhanh — "tôi sẽ làm gì tuần này?"

Cập nhật 2026-05-13:

1. **Đã xong Sprint 1 foundation:**
   - [x] Đổi postgres image sang `timescale/timescaledb:latest-pg16`.
   - [x] Docker Compose tách logical database theo service (`auth_db`, `file_storage_db`) và có `postgres-init` idempotent.
   - [x] Bổ sung `UploadedFile` metadata cho FileStorageService và chuẩn hóa tham chiếu bằng `fileId`.
   - [x] Tạo migration `AddAccountProfileExtensionTables` cho AuthService (`AccountProfile`, `StaffProfile`, `StaffSkill`).
   - [x] Chuẩn hóa avatar flow: uploaded avatar dùng `AvatarFileId`, Google avatar dùng `ExternalAvatarUrl`, FE dùng `displayAvatarUrl`.
2. **Còn lại trước khi chuyển hẳn sang Sprint 2:**
   - [ ] Migration rollback test cho AuthService/FileStorageService.
   - [ ] Viết API contract doc draft riêng cho FE team start Sprint 2.
   - [ ] Update CLAUDE.md memory nếu workflow team yêu cầu.
3. **Tuần sau:** Bắt đầu Sprint 2 — BatteryService MVP:
   - Tạo solution skeleton.
   - Chạy `/scaffold-crud BatteryService BatteryType` đầu tiên (theo §16.1).
4. **Hằng ngày:** Cập nhật memory bằng `/kltn-task KAN-XX` cho mỗi ticket Jira nhận được.

---

# Phần VII — Bổ sung sau review (Gap Analysis)

> Phần này bổ sung sau khi review lần 2 phát hiện gap. Mỗi section đánh dấu **P0/P1/P2** và link tới section gốc trong Phần II–VI để biết cần update ở đâu.

---

## 30. AI Module integration — P0

> **Đây là gap lớn nhất.** Capstone có 3 trụ cột (Mobile/Web/AI) — overall ban đầu gần như bỏ qua AI integration. Hội đồng sẽ hỏi đầu tiên.

### 30.1. Bối cảnh
- AI Module = FastAPI + PyTorch (rules/tech/ai.md), output:
  - **SOH prediction** (LSTM/CNN-LSTM): regression % SOH với MAE < 2%
  - **Anomaly classification** (Isolation Forest): Normal / Degrading / Failed
- Backend (BatteryService) phải gọi AI để:
  1. Predict SOH định kỳ → lưu trend.
  2. Classify anomaly sau khi threshold detector kích hoạt (hybrid pipeline).
  3. Cung cấp SOH/classification cho FE/Mobile dashboard.
  4. Gửi feedback từ Staff về AI để retrain.

### 30.2. Architecture pattern — Hybrid threshold + AI

```
SensorReading ingest
    │
    ▼
ThresholdAnomalyDetector (fast, rule-based)
    │
    ├──[Normal]──→ skip
    │
    └──[Threshold breached]──→
            │
            ▼
    AiInferenceClient.ClassifyAnomaly(last 30 readings)
            │
            ├── Normal     → log false-positive candidate (Staff review)
            ├── Degrading  → Alert severity = Warning
            └── Failed     → Alert severity = Critical → publish event
            │
            └──→ AiInferenceClient.PredictSoh(window)
                     │
                     └──→ enrich Alert with SOH%, attach to event
```

### 30.3. New entities (BatteryService)

#### `SohPrediction`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `BatteryAssetId` | Guid (FK) | indexed |
| `PredictedSohPercent` | decimal(5,2) | 0–100 |
| `Confidence` | decimal(4,3) | 0–1 |
| `ModelVersion` | string(20) | "1.0", "1.1" |
| `InputWindowStartUtc` | DateTime | — |
| `InputWindowEndUtc` | DateTime | — |
| `PredictedAt` | DateTime | indexed DESC |
| `LatencyMs` | int | Cho monitoring |
| `RawResponse` | jsonb? | Debug |

#### `AnomalyClassification`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `AlertId` | Guid? (FK) | Link tới Alert nếu classify cho alert |
| `BatteryAssetId` | Guid | — |
| `Classification` | enum (Normal=1, Degrading=2, Failed=3) | — |
| `AnomalyScore` | decimal(8,6) | Isolation Forest score |
| `Confidence` | decimal(4,3) | — |
| `ModelVersion` | string(20) | — |
| `ClassifiedAt` | DateTime | — |
| `LatencyMs` | int | — |
| `StaffFeedback` | enum? (Correct=1, FalsePositive=2, FalseNegative=3) | Staff confirm sau khi resolve |
| `StaffFeedbackByUserId` | Guid? | — |
| `StaffFeedbackAt` | DateTime? | — |

### 30.4. AI Bridge service (BatteryService.Application)

```csharp
public interface IAiInferenceClient {
    Task<SohPredictionResult> PredictSohAsync(Guid assetId, IReadOnlyList<SensorReading> window, CancellationToken ct);
    Task<AnomalyClassificationResult> ClassifyAnomalyAsync(Guid assetId, IReadOnlyList<SensorReading> window, CancellationToken ct);
    Task<HealthCheckResult> HealthAsync(CancellationToken ct);
}

// HTTP impl
public class AiInferenceClient : IAiInferenceClient {
    private readonly HttpClient _http;  // base URL: http://ai-module:8000

    public async Task<SohPredictionResult> PredictSohAsync(Guid assetId, ...) {
        var payload = new {
            asset_id = assetId,
            readings = window.Select(r => new { time = r.Time, v = r.Voltage, i = r.Current, t = r.Temperature, soc = r.SocPercent })
        };
        // Polly retry 2 lần, timeout 200ms (vì SLA P1 < 100ms)
        var resp = await _http.PostAsJsonAsync("/predict/soh", payload, ct);
        var result = await resp.Content.ReadFromJsonAsync<SohResponse>(cancellationToken: ct);
        return new SohPredictionResult {
            SohPercent = result.soh_percent,
            Confidence = result.confidence,
            ModelVersion = result.model_version
        };
    }
}
```

**Polly config:**
- Timeout: 200ms (degrade gracefully — không block ingest pipeline)
- Retry: 2 lần exponential backoff
- Circuit breaker: 50% fail rate trong 30s → mở 60s

### 30.5. Background services AI

#### `SohPredictionBackgroundService`
- Frequency: **hourly per asset** (configurable).
- Cho mỗi asset Active → lấy 30 sensor readings gần nhất → call `PredictSohAsync` → lưu `SohPrediction`.
- Sau khi predict → so sánh với `previous.SohPercent`:
  - Giảm > 5% trong 24h → publish `SohRapidDegradationEvent`
  - Giảm xuống dưới 80% → publish `SohWarningEvent` (auto-tạo ticket Warning)
  - Giảm xuống dưới 60% → publish `SohCriticalEvent` (auto-tạo ticket Critical)

#### `AnomalyClassificationOnAlertConsumer`
- Internal consumer (in-process) khi `ThresholdAnomalyDetector` trigger.
- Call `ClassifyAnomalyAsync` → enrich Alert + publish `BatteryAnomalyDetectedV2Event` theo rollout
  versioned ở §30.6; không thêm field bắt buộc trực tiếp vào V1.

### 30.6. Updated `BatteryAnomalyDetectedEvent`
Không thay đổi in-place positional contract hiện tại trong cùng deployment. Khi AI enrichment sẵn sàng,
publish contract V2 và migrate subscriber trước khi ngừng V1:

```csharp
public record BatteryAnomalyDetectedV2Event(
    Guid AlertId,
    Guid BatteryAssetId,
    Guid CustomerId,
    string AssetSerialNumber,
    int AnomalyType,
    int Severity,
    decimal ThresholdValue,
    decimal ActualValue,
    string Unit,
    DateTime DetectedAt,
    int? Classification,            // wire value khớp AnomalyClassificationEnum §30.3: 1=Normal, 2=Degrading, 3=Failed; null = AI chưa classify hoặc unavailable (xem §30.11 fallback)
    decimal? AnomalyScore,
    decimal? CurrentSohPercent,
    string? AiModelVersion
) : IntegrationEvent;
```

Giữ primitive wire value; không đưa `AnomalyClassificationEnum` từ BatteryService.Domain vào
SharedContracts. Trong giai đoạn chuyển tiếp producer có thể dual-publish V1/V2 nhưng mỗi subscriber
chỉ xử lý một version theo feature flag, có contract test trước rollout.

**Saga interop:** `AlertTicketSagaStateMachine` (§8.3, §53.5) subscribe **cả** `BatteryAnomalyDetectedEvent`
và `BatteryAnomalyDetectedV2Event`. Cả hai dùng cùng `CorrelateById(x => x.Message.AlertId)` và route về
cùng state machine; V2 chỉ enrich thêm Classification/SOH/AnomalyScore vào payload snapshot nhưng không
đổi initial transition. Mapping wire-value `AnomalyType` ở §53.7 áp dụng cho cả hai version. Khi cutover
xong, deprecate V1 endpoint trước khi xóa V1 subscription để tránh mất event in-flight.

### 30.7. New endpoints

```
GET    /api/battery-assets/{id}/soh-prediction              (Customer own / Staff / Manager)
GET    /api/battery-assets/{id}/soh-history?from=&to=       (— same —)
GET    /api/battery-assets/{id}/anomaly-classifications     (— same —)
POST   /api/v1/anomaly-classifications/{id}/feedback           (Staff — confirm correct / false positive)
GET    /api/v1/ai/model-info                                   (Admin — current model version + last retrain)
GET    /api/v1/ai/inference-latency-stats                      (Admin — P50/P95/P99 latency)
GET    /api/v1/ai/health                                       (Internal proxy to AI /health)
```

### 30.8. Caching strategy AI

| Data | TTL | Lý do |
|------|-----|-------|
| SohPrediction latest per asset | 5 phút | Đỡ load AI |
| AnomalyClassification per alert | 1 giờ | Stable sau khi classify |
| Model info | 10 phút | Đổi không thường xuyên |

### 30.9. AI service docker compose
```yaml
ai-module:
  build:
    context: ./ai-module
    dockerfile: Dockerfile
  container_name: solar-ai
  environment:
    MODEL_VERSION: "1.0"
    SCALER_PATH: /app/models/weights/scaler.pkl
    LSTM_PATH: /app/models/weights/soh_lstm_v1.0.pth
    ISO_FOREST_PATH: /app/models/weights/isolation_forest_v1.0.pkl
  ports: ["8000:8000"]
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:8000/health"]
    interval: 10s
    retries: 5
  networks: [solar-net]
```

### 30.10. Performance monitoring
Prometheus metrics:
- `ai_inference_latency_milliseconds` histogram (label: endpoint=soh|classify)
- `ai_inference_total` counter (label: endpoint, status=success|timeout|error)
- `ai_model_version_info` gauge (label: version)

**Alert:** `ai_inference_latency_p95 > 100ms` for 5min → notify team.

### 30.11. Fallback khi AI down
- BatteryService phải vận hành được khi AI Module down:
  - Threshold detector vẫn chạy bình thường (rule-based).
  - Alert vẫn được tạo nhưng `Classification = Unknown`, không có SOH%.
  - Banner UI "AI service unavailable — basic detection only".
  - Circuit breaker mở → ngừng gọi AI 60s.

### 30.12. AI feedback loop (cho retraining)
- Staff resolve ticket → UI hỏi "Phân loại Failed của AI có đúng không?"
- POST `/api/v1/anomaly-classifications/{id}/feedback` lưu `StaffFeedback`.
- Background export hàng tháng → CSV → AI team retrain.
- Endpoint Admin xem accuracy: `GET /api/v1/ai/feedback-stats` (true positive rate, false positive rate).

### 30.13. Tests bắt buộc
- `AiInferenceClientTests`: timeout/retry/circuit-breaker behavior
- `SohPredictionBackgroundServiceTests`: trigger events khi SOH giảm
- `AnomalyClassificationOnAlertConsumerTests`: enrich alert correctly
- Integration test với mocked AI server (WireMock.Net)
- Performance test: 100 concurrent classify call P95 < 100ms

---

## 31. Site entity — P0

> Solar farm thực tế cụm pin theo site. Mô hình hiện tại `Customer → Asset` trực tiếp sai về business reality. Sửa từ đầu rẻ hơn refactor sau.
>
> **Cập nhật (post-Sprint 5B reconcile):** `BatteryGroup` entity được **deferred khỏi scope hiện tại** — capstone implement Site-level grouping/aggregation là đủ cho 4 role × 6 phase. Nếu sau này cần cluster con trong site (Block A/String 1) thì add sau, không block. Asset hiện chỉ link trực tiếp Site (không qua BatteryGroup).

### 31.1. New entities

#### `Site` (BatteryService)
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `Name` | string(200) | "Solar Farm An Giang #1" |
| `CustomerId` | Guid (FK) | Owner |
| `Address` | string(500)? | — |
| `Latitude`, `Longitude` | decimal? | GPS center |
| `InstallDate` | DateTime | — |
| `Status` | enum (Active=1, UnderMaintenance=2, Decommissioned=3) | — |
| `ContactPersonName` | string? | Người liên hệ tại site |
| `ContactPersonPhone` | string? | — |

> `BatteryGroup` (cluster trong site) — **DEFERRED**, không trong scope hiện tại. Spec field gốc (Id/SiteId/Name/BatteryTypeId/BatteryCount) giữ lại trong git history nếu cần dùng lại.

### 31.2. Update existing entity
- `BatteryAsset.SiteId` nullable (backward compatible).
- Migration: `AddSiteAndGroup` — tạo bảng Site + thêm `BatteryAsset.SiteId` nullable. `BatteryGroup` table và `BatteryAsset.BatteryGroupId` column **không tạo** (deferred).

### 31.3. New endpoints
```
POST   /api/v1/sites                                      (Admin)
GET    /api/v1/sites?customerId=&status=                  (Admin/Manager — Customer own)
GET    /api/v1/sites/{id}                                 (— same —)
GET    /api/v1/sites/{id}/assets                          (list asset trong site)
GET    /api/v1/sites/{id}/dashboard                       (aggregated health của site)
GET    /api/v1/sites/{id}/alerts                          (all alerts của site)
PUT    /api/v1/sites/{id}
DELETE /api/v1/sites/{id}                                 (block nếu còn asset)

GET    /api/v1/customers/me/sites                         (Customer — list sites mình sở hữu)
```

> `/api/battery-groups` endpoints **deferred** cùng entity BatteryGroup.

### 31.4. Site-aggregated alert (giảm noise)
Khi nhiều asset cùng site cùng anomaly trong 5 phút → tạo 1 `SiteAlert` thay vì N `Alert`:
- Entity `SiteAlert` (parentSiteId, anomalyType, affectedAssetIds[], severity, detectedAt).
- Push notification 1 lần với title "5 pin tại Block A overheat" thay vì spam 5 push.
- Customer/Staff drill down xem assets cụ thể.

### 31.5. Site dashboard endpoint
```json
GET /api/v1/sites/{id}/dashboard
{
  "siteId": "...",
  "name": "Solar Farm An Giang #1",
  "totalAssets": 50,
  "activeAssets": 48,
  "assetsWithActiveAlerts": 3,
  "averageSohPercent": 92.5,
  "criticalAlerts": 1,
  "ticketsOpen": 2,
  "ticketsResolved30d": 12,
  "lastAlertAt": "...",
  "healthScore": 87           // computed: weighted avg SOH + alert penalty
}
```

### 31.6. Migration impact
- `BatteryAsset` migration `AddSiteAndGroup` (Sprint 2 hoặc 3).
- Seed: tạo Site mặc định "Default Site" cho Customer chưa có site, gán assets cũ vào đó.
- Sprint 5B: migration `RemoveSiteCapacityKw` drop column `sites.capacity_kw` (Up/Down + rollback test) — xem §53.3 task `#234`. `Site.CapacityKw` không còn tồn tại trong Domain entity, DTO, mapping, validation và seed sau migration này.

### 31.7. Cascade Risk Assessment (B4) — rule-based propagation analysis

**Bối cảnh:** Văn bản yêu cầu AI phân tích "pin hỏng có lây lan sang pin khác không?". Ví dụ: 10 pin/site, 1 cục hỏng → P3. Nhưng nếu cục hỏng đó lây ảnh hưởng sang cục khác → upgrade P1 ngay.

**Approach:** Rule-based (không phải ML — out-of-scope capstone). Trigger sau khi 1 Alert/Anomaly được tạo, đánh giá risk lan rộng.

**New field trong `BatteryAsset` (B4):**

| Field | Type | Note |
|-------|------|------|
| `CascadeRiskScore` | `decimal(4,3)` NOT NULL default 0 | 0.0–1.0, computed |
| `CascadeRiskUpdatedAt` | `DateTime?` | Khi nào tính lần cuối |
| `ElectricalTopology` | `ElectricalTopologyEnum` NOT NULL default `Independent` | 1=Independent, 2=SeriesString, 3=ParallelBank, 4=SeriesParallel |

**Logic tính score:**

```csharp
public class CascadeRiskCalculator : ICascadeRiskCalculator
{
    public async Task<decimal> CalculateAsync(Guid assetId, CancellationToken ct)
    {
        var asset = await _unitOfWork.BatteryAssets.GetByIdAsync(assetId);
        if (asset == null) return 0m;

        decimal score = 0m;

        // Rule 1: Topology factor — series = mất 1 pin có thể ngắt cả string
        score += asset.ElectricalTopology switch
        {
            ElectricalTopologyEnum.Independent => 0.0m,
            ElectricalTopologyEnum.ParallelBank => 0.2m,
            ElectricalTopologyEnum.SeriesString => 0.6m,
            ElectricalTopologyEnum.SeriesParallel => 0.4m,
            _ => 0m
        };

        // Rule 2: Proximity — đếm asset cùng BatteryGroup có anomaly trong 1h
        if (asset.BatteryGroupId.HasValue)
        {
            var siblingAnomalies = await _unitOfWork.Alerts.GetAllAsync()
                .Where(a => !a.IsDeleted
                    && a.Status == AlertStatusEnum.Open
                    && a.DetectedAt >= DateTime.UtcNow.AddHours(-1)
                    && a.BatteryAssetId != assetId
                    && _unitOfWork.BatteryAssets.GetAllAsync()
                        .Any(b => b.Id == a.BatteryAssetId && b.BatteryGroupId == asset.BatteryGroupId))
                .CountAsync();

            if (siblingAnomalies >= 1) score += 0.2m;
            if (siblingAnomalies >= 3) score += 0.2m;  // cumulative
        }

        // Rule 3: Thermal proximity — overheat lây lan
        var hasThermalRunaway = await _unitOfWork.Alerts.GetAllAsync()
            .Where(a => a.BatteryAssetId == assetId
                && a.AnomalyType == AnomalyTypeEnum.Overheat
                && a.Severity == AlertSeverityEnum.Critical
                && a.Status == AlertStatusEnum.Open)
            .AnyAsync();
        if (hasThermalRunaway) score += 0.3m;

        return Math.Min(1.0m, score);  // clamp
    }
}
```

**Background service `CascadeRiskBackgroundService`:**
- Frequency: 5 phút.
- Scan toàn bộ asset có Open Alert → recompute `CascadeRiskScore`.
- Nếu score cross threshold:
  - `>= 0.7` → publish `BatteryCascadeRiskHighEvent` → upgrade Priority ticket liên quan lên P1 (auto)
  - `>= 0.5` → notify Manager dashboard, không auto-upgrade

**Integration với Priority Matrix (§2.4bis):**
- Khi `CascadeRiskScore >= 0.7` → override `ImpactScope` lên ít nhất `BatteryGroup` → `Priority` tính lại qua matrix.
- Group Alert (§31.4): nếu N asset cùng group có score cao → tạo Group Alert + ticket parent-child.

**Endpoint:**
```
GET    /api/v1/battery-assets/{id}/cascade-risk         (Manager/Staff/Customer own)
GET    /api/v1/sites/{id}/cascade-risk-summary          (Manager — heat map cho site)
POST   /api/v1/battery-assets/{id}/topology             (Admin — set electrical topology)
```

**Enum:**

```csharp
public enum ElectricalTopologyEnum {
    Independent = 1,     // Pin đơn lẻ, không kết nối với pin khác
    SeriesString = 2,    // Mắc nối tiếp (string voltage)
    ParallelBank = 3,    // Mắc song song (bank capacity)
    SeriesParallel = 4   // Hỗn hợp
}
```

**Out-of-scope capstone (post-graduation):**
- Graph Neural Network train trên cascade failure dataset (cần thực data, không khả thi).
- Real-time thermal simulation.

> **Lưu ý implementation:** B4 phụ thuộc Site/BatteryGroup (đã có Sprint 2). Đặt vào Sprint 7 cùng Reports + Observability.

---

## 32. Ticket relationships — parent-child, merge, watch — P0

### 32.1. New entity `TicketRelation`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `SourceTicketId` | Guid (FK) | — |
| `TargetTicketId` | Guid (FK) | — |
| `RelationType` | enum | 1=DuplicateOf, 2=RelatedTo, 3=CausedBy, 4=Blocks, 5=ChildOf, 6=ParentOf |
| `CreatedByUserId` | Guid | — |
| `CreatedAt` | DateTime | — |
| `Reason` | string? | — |

**Constraint:** unique `(SourceTicketId, TargetTicketId, RelationType)`.

### 32.2. New entity `TicketSubscription` (watch/follow)
| Field | Type | Note |
|-------|------|------|
| `TicketId` | Guid (FK, PK part) | — |
| `UserId` | Guid (PK part) | — |
| `SubscribedAt` | DateTime | — |
| `NotificationFrequency` | enum (Immediate=1, Daily=2) | — |

### 32.3. Auto-subscription rules
- Customer owner → auto subscribe.
- Assigned Staff → auto subscribe.
- Manager (assigned) → auto subscribe khi assign.
- Người comment → auto subscribe (giống GitHub).
- @mention → auto subscribe.

### 32.4. Merge & duplicate flow

#### Endpoint
```
POST   /api/v1/tickets/{id}/merge-into/{targetId}      (Manager)
```

#### Logic
```csharp
// Mark source ticket as DuplicateOf target, close source
// Move all comments + attachments + activity → target
// Notify subscribers cả 2 ticket
// Activity log "Merged into TKT-..."
```

### 32.5. Parent-child (Incident → child tickets)
- Khi Manager `DeclareIncident` cho ticket → ticket đó có thể có nhiều child.
- Endpoint `POST /api/v1/tickets/{parentId}/children` (Manager link child tickets).
- Khi parent closed → option auto-close all unclosed children.
- Báo cáo: "Incident X caused N tickets, total resolution time".

### 32.6. Endpoints
```
POST   /api/v1/tickets/{id}/relations                  (link tới ticket khác)
DELETE /api/v1/tickets/{id}/relations/{relationId}
GET    /api/v1/tickets/{id}/relations                  (list relations)

POST   /api/v1/tickets/{id}/subscribe                  (current user follow)
DELETE /api/v1/tickets/{id}/subscribe                  (unfollow)
GET    /api/v1/tickets/{id}/subscribers                (Manager view)
GET    /api/v1/me/subscriptions                        (my followed tickets)
```

### 32.7. UI impact (FE note)
- Ticket detail panel "Related tickets" sidebar.
- Activity feed mention "🔗 Merged with TKT-..."
- "Watch" toggle button.

### 32.8. Tests
- `TicketMergeCommandHandlerTests`: merge xong source closed + comments moved
- `TicketRelationCommandHandlerTests`: prevent circular relation (A duplicateOf B + B duplicateOf A)
- Auto-subscribe trigger khi comment

---

## 33. SLA pause limits & advanced — P0

### 33.1. Loophole hiện tại
BR-04 cho phép pause SLA không giới hạn → Staff gaming SLA bằng cách pause hoài.

### 33.2. New rules

#### BR-04-Extended: Pause limits per priority

| Priority | MaxTotalPauseMinutes | MaxPauseEpisodes | Auto-resume after |
|----------|---------------------|------------------|-------------------|
| P1 Critical | 60 (1h) | 2 | 30 phút chờ Customer |
| P2 High | 480 (8h) | 5 | 24h chờ Customer |
| P3 Normal | 1440 (24h) | 10 | 72h chờ Customer |

- Vượt `MaxTotalPauseMinutes` → SLA timer **auto-resume** + notify Manager + ghi activity.
- Mỗi pause cần `Reason` rõ ràng (đã có).
- Pause lần thứ N+1 (vượt MaxPauseEpisodes) → cần Manager approve (chuyển sang trạng thái `PausePendingApproval`).

#### BR-04-Extended: Customer auto-reply timeout
- `WAITING_CUSTOMER` pause SLA, nhưng nếu Customer không reply trong `AutoResumeAfter` time:
  - Auto-resume SLA.
  - Send reminder push cho Customer.
  - Sau 3 reminder không reply → auto-close ticket as "Resolved (no customer feedback)".

### 33.3. Update `SlaTimer` entity
Thêm fields:
- `MaxTotalPauseMinutes` (snapshot lúc start, theo priority)
- `MaxPauseEpisodes` (snapshot)
- `PauseEpisodesCount` (counter)
- `LastAutoResumeAt` (DateTime?)
- `ApprovalRequired` (bool) — nếu pause lần thứ N+1

### 33.4. Update `SlaPauseEvent` entity
Thêm:
- `IsApprovedByManager` (bool? — nullable cho lần 1)
- `ApprovedByManagerId` (Guid?)
- `AutoResumeReason` (enum? — TimeLimitExceeded / CustomerTimeout / ManagerForce)

### 33.5. Background service mới
`SlaPauseEnforcementBackgroundService` (every 5 phút):
- Scan active pause events.
- Nếu total pause vượt max → auto-resume.
- Nếu pause type `WaitingCustomer` vượt `AutoResumeAfter` → auto-resume + reminder.

### 33.6. Update `TicketHoldCommand`
Validation thêm:
- Reject nếu `PauseEpisodesCount >= MaxPauseEpisodes` AND không có Manager approval payload.

### 33.7. New endpoint
```
PUT    /api/v1/tickets/{id}/approve-pause              (Manager — approve pause lần N+1)
PUT    /api/v1/tickets/{id}/force-resume               (Manager — force resume khi Staff không resume)
```

### 33.8. Reporting impact
Thêm metric:
- `tickets_with_pause_limit_exceeded_total{priority}` counter
- Report "Top staff pause SLA nhiều nhất" — phát hiện gaming SLA.

---

## 34. Real-time updates (SSE / push channel) — P0

### 34.1. Lý do
- P1 Critical alert: 30s polling delay không chấp nhận được.
- Manager queue: live update tickets mới xuất hiện không cần F5.
- SLA countdown realtime cho Staff.

### 34.2. Quyết định technical
**Server-Sent Events (SSE)** — không phải full WebSocket, vì:
- One-way (server → client) đủ dùng.
- Built-in browser/RN support, không cần lib.
- Auto-reconnect.
- Tương thích HTTP/2 multiplexing.
- Đơn giản hơn WebSocket.

### 34.3. Architecture
```
Service publish event → MassTransit
       │
       ▼
NotificationService nhận event
       │
       ├──→ Push (Expo) — đã có
       ├──→ Email — đã có
       ├──→ SMS — đã có
       └──→ SSE Hub (new)
                │
                ├──→ Mobile subscribers
                └──→ Web subscribers
```

### 34.4. New SSE Hub service
Có thể là 1 module trong NotificationService HOẶC service riêng `RealtimeHub`. **Đề xuất module trong NotificationService**.

```
GET /api/v1/realtime/stream?topics=tickets,alerts,sla     (Server-Sent Events)
Headers: Authorization: Bearer {token}, Accept: text/event-stream
```

Server response:
```
event: ticket.assigned
data: {"ticketId":"...", "code":"TKT-2605-0001", "slaDueAt":"..."}

event: alert.critical
data: {"alertId":"...", "assetId":"...", "anomalyType":"Overheat"}

event: sla.warning
data: {"ticketId":"...", "remainingMinutes":45}

event: ping
data: {}
```

### 34.5. Subscriber → topic mapping
| Role | Auto-subscribe topics |
|------|----------------------|
| Customer | `alerts.own`, `tickets.own` |
| Staff | `tickets.assigned`, `sla.assigned`, `mentions` |
| Manager | `tickets.team`, `sla.team`, `escalations`, `incidents` |
| Admin | `system.health`, `incidents`, `audit.critical` |

### 34.6. Implementation
- ASP.NET Core SSE endpoint với `IAsyncEnumerable<SseEvent>`.
- Redis pub/sub backend (vì cần distribute giữa N instance NotificationService).
- Heartbeat 30s (event `ping`) để giữ connection alive.
- Reconnect: server gửi `Last-Event-ID` để client resume.

### 34.7. Endpoints
```
GET    /api/v1/realtime/stream?topics=...               (SSE — long-lived)
GET    /api/v1/realtime/topics                          (list available topics)
```

### 34.8. Fallback
- Mobile: nếu SSE fail → fallback Push (vẫn realtime qua Expo).
- Web: nếu SSE fail → fallback polling 30s.

### 34.9. Tests
- SSE end-to-end test: connect → publish event → assert received within 1s
- Reconnect test với Last-Event-ID
- Auth test: Customer A không nhận event của Customer B

---

## 35. Bulk operations + QR onboarding — P1

### 35.1. Bulk import endpoints

#### Bulk import battery assets (Admin)
```
POST   /api/battery-assets/bulk-import
Content-Type: multipart/form-data
  file: assets.csv
  fileFormat: csv | xlsx
```

CSV columns: `serial_number, battery_type_name, customer_email, install_date, site_name, warranty_end_date, location, notes`

Response:
```json
{
  "totalRows": 100,
  "successCount": 95,
  "failureCount": 5,
  "createdAssetIds": [...],
  "errors": [
    {"row": 23, "field": "customer_email", "value": "...", "error": "Customer not found"},
    {"row": 47, "field": "serial_number", "error": "Duplicate"}
  ]
}
```

#### Bulk invite users
```
POST   /api/v1/auth/users/bulk-invite
  file: users.csv  → email, role, full_name, department
```
- Mỗi row → tạo Account + send invite email (event `SendAdminInviteEvent`).
- Skip nếu email đã tồn tại.

#### Bulk reassign tickets
```
PUT    /api/v1/tickets/bulk-reassign
{
  "ticketIds": ["...", "..."],
  "newStaffId": "...",
  "reason": "Staff X nghỉ phép"
}
```
- Validate mỗi ticket có thể reassign.
- Atomic: hoặc all-or-nothing, hoặc per-ticket success/fail report.

### 35.2. QR code onboarding flow

#### Admin generate QR
1. Admin tạo BatteryAsset → system gen `ClaimCode` (JWT-like, signed, 1-time-use, 90d expiry).
2. Admin print QR sticker chứa URL: `https://app.gsu26se55.com/claim?code={claimCode}` hoặc deeplink `gsu26se55://claim?code=...`.

#### Customer claim
```
POST   /api/battery-assets/claim
{
  "claimCode": "eyJhbGc..."
}
```
- Validate signature + expiry + not-used.
- Link `BatteryAsset.CustomerId = currentUserId`.
- Mark code used.
- Activity log.

#### Entity update
- `BatteryAsset.ClaimCode` (string?, indexed)
- `BatteryAsset.ClaimedAt` (DateTime?)
- `BatteryAsset.ClaimCodeExpiresAt` (DateTime?)

### 35.3. Endpoints summary
```
POST   /api/battery-assets/bulk-import                (Admin)
POST   /api/v1/auth/users/bulk-invite                    (Admin)
PUT    /api/v1/tickets/bulk-reassign                     (Manager)
PUT    /api/v1/tickets/bulk-priority                     (Manager — chỉ cho ticket Open chưa assigned)
POST   /api/battery-assets/{id}/generate-claim-code   (Admin — re-gen QR)
GET    /api/battery-assets/{id}/claim-code-qr.png     (Admin — render QR PNG)
POST   /api/battery-assets/claim                      (Customer)
```

### 35.4. Tests
- Import 100 rows, 5 invalid → 95 created, 5 reported with row/field.
- QR claim flow end-to-end: gen → claim → assert ownership.
- Replay attack: claim code dùng 2 lần → second fail.

---

## 36. Comment / MaintenanceLog advanced — P1

### 36.1. Edit & delete comments

#### Endpoint
```
PUT    /api/v1/comments/{id}                            (author only, within 15min OR Admin always)
DELETE /api/v1/comments/{id}                            (author or Admin — soft delete)
GET    /api/v1/comments/{id}/history                    (edit history view)
```

#### Schema update
- `TicketComment.EditedAt` (DateTime?)
- `TicketComment.EditCount` (int default 0)
- `TicketComment.IsDeleted` (bool — soft delete, show as "deleted by user")

#### History
Lưu `TicketCommentHistory`:
- `Id, CommentId, OldBody, EditedAt, EditedByUserId`

### 36.2. @Mention parsing

#### Logic
- Body chứa `@username` hoặc `@{userId}` → parse khi save.
- Lookup user → tạo `Mention` record.
- Trigger notification → mention user.
- Auto-subscribe mentioned user.

#### Entity `CommentMention`
- `Id, CommentId, MentionedUserId, MentionedByUserId, CreatedAt`

#### Endpoint
```
GET    /api/v1/me/mentions                              (my mentions feed)
```

### 36.3. Reaction (emoji)
- Entity `CommentReaction` (CommentId, UserId, Emoji, ReactedAt).
- 6 emoji standard: 👍 👎 ❤️ 🎉 😕 🚀
- Endpoint `POST /api/v1/comments/{id}/reactions`, body `{"emoji": "👍"}`.

### 36.4. Pinned comments
- Manager pin important comment để hiện trên top.
- `TicketComment.IsPinned` bool, `PinnedAt`, `PinnedByUserId`.
- Max 3 pinned per ticket.

### 36.5. Comment templates (Staff reusable snippets)
Entity `CommentTemplate`:
- `Id, OwnerUserId (nullable for shared), Title, Body, Category, UsageCount`
- Shared templates (Manager tạo) vs personal (Staff tạo).
- Endpoint:
```
GET    /api/v1/comment-templates?scope=mine|shared
POST   /api/v1/comment-templates
PUT    /api/v1/comment-templates/{id}
DELETE /api/v1/comment-templates/{id}
```

### 36.6. MaintenanceLog advanced
Tương tự:
- Edit within 30 phút sau post (vì có thể nhớ ra sót chi tiết).
- `MaintenanceLogTemplate` cho Staff reuse.
- GPS check-in (xem §44).

### 36.7. Tests
- Edit window enforcement: thử edit sau 16 phút → reject (non-Admin).
- Mention parsing với 5 cases: valid user, invalid user, multiple mentions, escaped @, plain text.
- Reaction toggle: react 2 lần cùng emoji → remove.

---

## 37. Alert silence / snooze / ack escalation — P1

### 37.1. Silence (Manager mark known issue)
- Manager đánh dấu 1 anomaly type cho 1 asset/site là "known issue, không alert nữa trong N giờ/ngày".
- Entity `AlertSilenceRule`:
  - `Id, ScopeType (Asset=1|Site=2|BatteryType=3), ScopeId, AnomalyType, SilencedUntil, Reason, CreatedByUserId`
- ThresholdDetector check rule trước khi tạo alert.

### 37.2. Snooze (Customer "tôi biết, đừng push trong 1h")
- Customer mở alert detail → click "Snooze 1h".
- `Alert.SnoozeUntil` (DateTime?).
- Push channel skip alert nếu still snoozed.

### 37.3. Acknowledge escalation
- Critical alert tạo ra → push Customer.
- Nếu Customer không acknowledge trong 15 phút → push lần 2 (escalated to Staff).
- Nếu 30 phút vẫn không ack → auto-create ticket P1 + push Manager.

#### New entity `AlertAckTimeline`
- `Id, AlertId, EscalationLevel (1=Customer, 2=Staff, 3=Manager), EscalatedAt, ResolvedByAck (bool)`

#### Background service
`AlertAckEscalationBackgroundService` (every 5 phút):
- Scan critical alerts không có ack.
- Trigger next level escalation theo timing rule.

### 37.4. Endpoints
```
POST   /api/alerts/{id}/snooze                       (Customer — own)
{
  "durationMinutes": 60,
  "reason": "Đang sửa"
}

POST   /api/v1/alert-silence-rules                      (Manager)
GET    /api/v1/alert-silence-rules?scopeType=&scopeId=
DELETE /api/v1/alert-silence-rules/{id}
```

### 37.5. Group alerts dashboard
- Mobile/Web hiển thị "5 cảnh báo overheat tại Site An Giang" thay vì 5 row riêng.
- Backend: `GET /api/alerts/grouped?groupBy=site,anomaly` returns grouped response.

---

## 38. Edge case business rules matrix — P0

> Bảng này phải vào SRS và CLAUDE.md để mọi BE dev tham chiếu.

### 38.1. Matrix

| # | Edge case | Rule giải quyết | Implementation |
|---|-----------|----------------|----------------|
| EC-01 | Customer xóa account khi có ticket OPEN | Block delete, yêu cầu close hết ticket trước. Hoặc soft-delete + anonymize, tickets giữ nguyên với `CustomerName = "[Deleted]"`. | AuthService `AccountDeleteCommand` check TicketService API; nếu có open → return error |
| EC-02 | Staff nghỉ việc (Account.Status=Inactive) khi có ticket ASSIGNED/IN_PROGRESS | Auto-reassign tới Manager queue (status=Open, AssignedStaffId=null). Notify Manager. | `AccountStatusChangedConsumer` trong TicketService — scan ticket assigned tới staff đó |
| EC-03 | BatteryType bị xóa khi có Asset gắn | Block delete. Force Admin transfer assets sang type khác trước. | `BatteryTypeDeleteCommandHandler` check `Assets.AnyAsync(a => a.TypeId == id && !a.IsDeleted)` |
| EC-04 | ThresholdConfig đổi khi có Alert OPEN | Alert cũ giữ nguyên ngưỡng cũ (audit). Alert mới dùng ngưỡng mới. | Snapshot threshold values trong Alert entity (đã có `ThresholdValue` field) |
| EC-05 | Customer transfer asset khi có Alert OPEN | Alerts vẫn gắn với asset (không transfer). Notify cả old/new customer. | `BatteryAssetTransferOwnerCommandHandler` — không touch alerts |
| EC-06 | Customer transfer asset khi có Ticket OPEN | Block transfer. Yêu cầu close ticket trước. | Validation trong handler |
| EC-07 | Manager nghỉ phép khi có ticket cần approve | Approval timeout 24h → auto-escalate tới Admin hoặc Manager khác. | `TicketApprovalTimeoutBackgroundService` mỗi giờ |
| EC-08 | 2 Manager approve cùng 1 ticket (race condition) | Optimistic concurrency via `RowVersion`. First-write-wins, second gets 409 Conflict. | EF `[Timestamp] byte[] RowVersion` trên Ticket |
| EC-09 | Customer reopen đúng lúc Staff đang resolve song song | Optimistic concurrency. Resolve fail → Staff thấy "Ticket đã reopened, refresh". | Same as EC-08 |
| EC-10 | Alert auto-resolve trong khi Staff đang viết maintenance log | Alert vẫn auto-resolve. Staff tiếp tục log (ticket vẫn còn). Activity ghi "Alert auto-resolved during work". | Soft constraint, không block |
| EC-11 | Asset decommissioned khi có ticket history | Asset không xóa, set `Status=Decommissioned`. Tickets vẫn truy cập được nhưng không tạo mới được. | Validation `TicketCreateCommand` reject nếu asset status != Active |
| EC-12 | Customer hết warranty nhưng ticket vẫn open | Ticket xử lý bình thường (warranty là vấn đề billing, không phải support). Hiển thị warning trên UI. | No backend block |
| EC-13 | Sensor stop sending data 24h | Tạo Alert `DeviceOffline` auto. Notify Customer. | `DeviceOfflineDetectionBackgroundService` daily |
| EC-14 | Bulk import có row duplicate serial | Skip + report. Không atomic fail toàn batch. | Per-row try/catch |
| EC-15 | Customer claim mã QR đã hết hạn | Reject với code `BATTERY_CLAIM_001 — Code expired`. | Validation |
| EC-16 | Customer claim mã của Customer khác đã claim | Reject với `BATTERY_CLAIM_002 — Already claimed`. | Validation |
| EC-17 | Email gửi qua EmailService fail 3 lần | DLQ. Admin có endpoint reprocess. | `OutboxRelayBackgroundService` mark failed sau retry |
| EC-18 | TicketAssignCommand với Staff đang vượt MaxConcurrentTickets | Reject với `TICKET_ASSIGN_002`. Manager phải chọn Staff khác. | Validation trong handler |
| EC-19 | Customer rate ticket nhưng rating = 0 hoặc > 5 | Validation reject, range 1-5. | `IValidatable` |
| EC-20 | SLA timer drift do server restart | On startup, recalc DueAt từ StartedAt + SLA hours - PausedMinutes. | Startup migration check |

### 38.2. Implementation note
- Tất cả rules trên phải có **unit test** trong service tương ứng.
- Document trong `docs/architecture/edge-cases.md` để hội đồng có thể tra cứu.

---

## 39. GDPR & compliance — P1

### 39.1. Data export (right to data portability)
```
POST   /api/v1/auth/me/export-data                      (Customer/Staff/Manager)
```
- Async job: gọi tới Battery/Ticket/Notification để aggregate.
- Response: tạo `DataExportRequest` record, gửi email kèm signed URL download (24h expiry).
- Format: JSON gồm:
  - Profile
  - List battery assets + sensor data 90d
  - List tickets + comments + maintenance logs
  - List notifications
  - List audit logs

### 39.2. Right to be forgotten
```
DELETE /api/v1/auth/me                                  (Customer)
{
  "password": "...",
  "reason": "..."
}
```
- Confirm password.
- Trigger `AccountDeleteCommand` → 2-step process:
  1. Mark `Account.IsScheduledForDeletion=true`, set `DeleteScheduledAt=now+30d` (cooling-off).
  2. Background service after 30 ngày → anonymize:
     - `Account.Email = "deleted_{userId}@anonymized.local"`
     - `Account.FullName = "[Deleted User]"`
     - `Account.PhoneNumber = null`
     - `Account.Address = null`
     - Keep `Id` cho audit trail.
  3. Tickets/Comments giữ nguyên nhưng `CustomerName` show "[Deleted User]".
  4. **Sprint 5B — Saga/Alert reference:** `alert_ticket_saga_states.CustomerId` và `alerts.customer_id` KHÔNG anonymize (giữ GUID làm audit trail). API/UI khi render Saga/Alert detail sẽ resolve `CustomerId → "[Deleted User]"` từ AuthService read-model — cùng pattern với Ticket/Comment. Không có PII trực tiếp trong payload snapshot Saga (chỉ `AssetSerialNumber`, `AnomalyType`, `Severity`, `CustomerId` GUID).

### 39.3. Data retention policy

| Data type | Retention | After expire | Justify |
|-----------|-----------|--------------|---------|
| SensorReading raw | 90 ngày | Drop (TimescaleDB retention policy) | Volume lớn, có hourly aggregate |
| SensorReading hourly | 1 năm | Aggregate to daily | Trend analysis |
| SensorReading daily | 5 năm | Drop | Long-term trend |
| AuditLog (auth) | 2 năm | Archive to cold storage | Compliance |
| TicketActivity | Forever | — | Audit |
| Ticket + Comment | Forever (anonymized if user deleted) | — | Audit |
| Notification | 1 năm | Drop | UX cleanup |
| LoginAttempt | 6 tháng | Drop | Security baseline |
| OutboxMessage processed | 30 ngày | Drop | Cleanup |
| RefreshToken revoked | 30 ngày | Drop | — |
| Alert + AlertActivity | Forever (link với Ticket forever) | — | Audit, telemetry evidence; không chứa PII trực tiếp (chỉ `CustomerId` GUID) |
| `alert_ticket_saga_states` (Sprint 5B) | Forever (tombstone — xem §53.5) | — | Chống event cũ tạo lại Saga; payload snapshot chứa `CustomerId` GUID + `AssetSerialNumber`, KHÔNG chứa email/phone |
| `mt_inbox_state` / `mt_outbox_state` / `mt_outbox_message` (Sprint 5B) | 30 ngày sau khi processed | Drop | Same as OutboxMessage policy |
| `qrtz_*` (Sprint 5B) | Active triggers only | Quartz tự cleanup completed jobs | Operational metadata, không PII |

### 39.4. PII redaction trong logs
- Serilog enricher tự động mask:
  - Email → `a***@example.com`
  - Phone → `09**12345`
  - Password → `[REDACTED]`
- Audit log không mask (cần đầy đủ cho compliance).

### 39.5. Cookie consent (FE concern but BE provides)
- `GET /api/v1/legal/privacy-policy` returns markdown.
- `POST /api/v1/auth/me/consent` lưu consent record.

### 39.6. Endpoints summary
```
POST   /api/v1/auth/me/export-data                      (Customer/Staff/Manager — async)
GET    /api/v1/auth/me/export-data/{requestId}/status   (poll status)
GET    /api/v1/auth/me/export-data/{requestId}/download (signed URL)
DELETE /api/v1/auth/me                                  (right to be forgotten)
PUT    /api/v1/auth/me/cancel-deletion                  (within 30d cooling-off)
GET    /api/v1/legal/privacy-policy
GET    /api/v1/legal/terms-of-service
POST   /api/v1/auth/me/consent
```

### 39.7. Background services
- `DataExportBackgroundService`: process pending export requests.
- `AccountAnonymizationBackgroundService`: daily, scan accounts scheduled-for-deletion past cooling-off.
- `DataRetentionCleanupBackgroundService`: daily, drop expired records.

### 39.8. Tests
- Export data: assert all categories included.
- Cancel deletion in cooling-off → restore.
- After cooling-off → anonymized successfully, audit references intact.

---

## 40. Operational documents (ADR + DR + Runbook) — P1

> Capstone đánh giá cao "operational maturity". Đây là phần documentation đi kèm code.

### 40.1. Architecture Decision Records (ADR)
Folder `docs/adrs/`:

| ADR ID | Title |
|--------|-------|
| ADR-001 | Use Clean Architecture 4-layer per service |
| ADR-002 | CQRS + MediatR over service+repository pattern |
| ADR-003 | Custom `IValidatable<T>` over FluentValidation |
| ADR-004 | Outbox pattern for event publishing |
| ADR-005 | Redis-based Inbox for consumer idempotency |
| ADR-006 | TimescaleDB hypertable for sensor data (vs separate DB) |
| ADR-007 | Centralized NotificationService over per-service notification |
| ADR-008 | SSE over WebSocket for realtime |
| ADR-009 | Microservices per business capability (Auth/Battery/Ticket/Notification) |
| ADR-010 | API Gateway responsible for JWT validation + claim forwarding |
| ADR-011 | KnowledgeBase as module within TicketService, not separate service |
| ADR-012 | Polly for HTTP resilience (retry + circuit breaker + timeout) |
| ADR-013 | Hybrid threshold + AI anomaly detection |
| ADR-014 | Account profile extension tables in AuthService (vs stuffing `Account` / separate UserService) |
| ADR-015 | TestContainers over shared dev Postgres for integration tests |
| ADR-016 | IoT edge = **ESP32-S3** (pivot từ Raspberry Pi); transport **hybrid**: HTTPS REST (v1 — bulk/admin/firmware/flush) + **MQTT** (v2 — realtime <100ms, LWT offline, downlink command). BMS đọc qua RS485/Modbus RTU multi-drop. — xem §52.10 |
| ADR-017 | Remove Energy and CO2 analytics from BatteryService scope — xem §53.1 |
| ADR-018 | Orchestrated Alert–Ticket Saga + forward recovery (vs choreography hoặc 2PC) — xem §8.3, §53.4–§53.8 |

**ADR template:**
```markdown
# ADR-{ID}: {Title}

## Status
Accepted | Superseded by ADR-XXX | Deprecated

## Context
What is the problem we're solving?

## Decision
What did we decide?

## Consequences
- Positive: ...
- Negative: ...
- Neutral: ...

## Alternatives Considered
- Option B: rejected because ...

## Date
2026-05-XX
```

### 40.2. Disaster Recovery (DR) plan
`docs/operations/dr-plan.md`:

#### Backup strategy
- Postgres: `pg_dump` daily → MinIO bucket `backups/postgres/{date}.sql.gz`.
- Retention: 7 daily + 4 weekly + 12 monthly.
- Verify restore: weekly automated `pg_restore` to temp DB.

#### RTO/RPO targets
| Scenario | RTO (Recovery Time) | RPO (Data Loss) |
|----------|---------------------|-----------------|
| DB corrupt | 1 giờ | 24h (last backup) |
| Single service crash | 5 phút (k8s restart) | 0 (stateless; Saga state + Quartz triggers durable trong `ticket_db`) |
| RabbitMQ down | 30 phút | 0 (Outbox persistent + Saga forward recovery) |
| Redis down | 15 phút | Acceptable (cache only; Saga idempotency dùng EF Consumer Inbox, không phụ thuộc Redis) |
| Total cluster down | 4 giờ | 24h |
| Saga stuck (Failed/non-progressing) | 1 giờ | 0 (admin reprocess theo runbook `08-saga-failed.md`) |

#### Restore procedure
1. Provision new infra (terraform / docker-compose).
2. Restore Postgres backup: `gunzip < backup.sql.gz | psql`.
3. Verify migrations match: `dotnet ef database update --no-build`.
4. Restart services with feature flag `MAINTENANCE_MODE=true` (read-only).
5. Smoke test: health endpoints + sample query (+ `/health/saga` cho TicketService Sprint 5B).
6. Disable maintenance mode.

#### Sprint 5B — Saga/Quartz post-restore procedure (xem §53.5/53.8)
Restore Postgres ngụ ý restore cả `alert_ticket_saga_states` + `qrtz_*` tables. Cần xử lý đặc biệt:

1. **Trước khi enable `AlertTicketSagaEnabled`:** chạy `UPDATE qrtz_triggers SET next_fire_time = next_fire_time + (now - backup_time)` để dời timeout/retry sang tương lai, tránh flood timeout firing đồng loạt khi restore vào nhiều giờ/ngày sau backup. Quartz tự misfire policy có thể handle, nhưng chính sách "fire once now then reschedule" là acceptable behavior; chính sách "fire all missed" sẽ gây spike.
2. **Saga ở `TicketRequested`/`AlertLinkRequested` lúc backup:** sau restore vẫn ở state đó; participant consumer chưa nhận command vì RabbitMQ trống. Chạy reconciliation: query Saga `current_state IN ('TicketRequested', 'AlertLinkRequested') AND updated_at < restore_time` → admin reprocess theo runbook `08-saga-failed.md`.
3. **MassTransit `mt_outbox_message` chưa publish lúc backup:** OutboxRelay sẽ tự pickup khi service restart và RabbitMQ ready.
4. **`mt_inbox_state` đã processed lúc backup nhưng business commit chưa flush disk (rất hiếm):** EF Consumer Outbox đảm bảo atomic với business commit, nên restore phải consistent. Nếu phát hiện inconsistent, xóa Inbox row tương ứng để consumer xử lý lại (idempotent).
5. Verify post-restore: assert `qrtz_triggers` count > 0 (nếu có Saga active trước backup), `alert_ticket_saga_states` count khớp backup, `mt_outbox_message` count khớp.

### 40.3. Runbook per scenario
`docs/operations/runbook/`:
- `01-postgres-down.md`
- `02-rabbitmq-queue-backed-up.md`
- `03-outbox-lag-high.md`
- `04-sla-breach-rate-high.md`
- `05-ai-module-down.md`
- `06-disk-space-low.md`
- `07-secret-rotation.md`
- `08-saga-failed.md`              ← Sprint 5B, task `#240` — Alert–Ticket Saga state=Failed cần reprocess
- `09-saga-stuck.md`               ← Sprint 5B, task `#240` — Saga non-terminal không update > 10 phút
- `10-saga-duplicate-canonical.md` ← Sprint 5B — chọn Ticket canonical khi preflight phát hiện duplicate `OriginAlertId` hoặc duplicate active `(BatteryAssetId, Category)`

> **IoT device ops (Sprint IoT-1):** Không mint runbook đánh số riêng — quy trình xử lý sự cố device (offline triage, broker down, queue đầy, clock drift, reject spike) đã nằm ở **§52.15 Failure modes** + **§52.6 offline detection**, và setup/hardware runbook ở `newiot.md`/`overall.iot.md`/`wiring-diagram.md`. Nếu pilot phần cứng mở rộng, có thể tách `11-iot-device-offline.md` từ §52.15 (khi đó cập nhật count runbook ở §66/§67).

Sample structure:
```markdown
# Runbook: RabbitMQ queue backed up

## Symptoms
- AlertManager fires `RabbitMqQueueDepthHigh`
- Notification delays
- Outbox lag tăng

## Diagnose
1. Check Management UI: http://localhost:15673
2. Identify slow consumer: ...
3. Check log: ...

## Mitigation
1. Scale consumer service: `docker-compose up -d --scale notification=3`
2. If poison message: move to DLQ via management UI
3. If schema mismatch: ...

## Postmortem template
...
```

#### Sample structure cho 3 Saga runbook (Sprint 5B, task `#240`)

**`08-saga-failed.md`** — Saga state=Failed cần reprocess:

```markdown
# Runbook: Alert–Ticket Saga Failed

## Symptoms
- AlertManager fires `AlertTicketSagaFailedSpike` (alert_ticket_saga_failed_total tăng 5min)
- Admin notification: "❌ Saga Failed AlertId={id} step={FailedStep}"
- Customer thấy Alert chưa được auto-create Ticket

## Diagnose
1. Query Saga state: `SELECT correlation_id, current_state, failed_step, failure_code, last_error, ticket_attempt_count, alert_link_attempt_count, last_attempt_at_utc FROM alert_ticket_saga_states WHERE current_state = 'Failed' ORDER BY updated_at_utc DESC LIMIT 50;`
2. Group by `failure_code` — phân loại root cause:
   - `ALERT_NOT_FOUND` / `ASSET_NOT_FOUND` / `CUSTOMER_INVALID`: data inconsistency, không reprocess được
   - `ALERT_TICKET_CONFLICT`: manual investigation cần
   - timeout/transient: có thể reprocess
3. Check log: `docker logs ticket-service | grep "CorrelationId=<alert-id>"`
4. Check BatteryService health nếu `FailedStep=link-alert`

## Mitigation
1. **Reprocessable (timeout/transient)**: `POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess` với `Idempotency-Key: $(uuidgen)` + reason `"transient-retry-{date}"`. Saga sẽ resume từ failed step.
2. **Data inconsistency**: investigate (Alert/Asset/Customer record) trước; nếu xác nhận data sai → mark Saga Abandoned thay vì reprocess.
3. **Conflict TicketId**: gọi reconciliation theo runbook `10-saga-duplicate-canonical.md`.

## Verification
- Saga state chuyển `Completed` trong 30s sau reprocess.
- `Alert.TicketId` set value đúng (không null).
- AlertManager rule clear sau 5 phút.

## Postmortem template
...
```

**`09-saga-stuck.md`** — Saga non-terminal không update > 10 phút:

```markdown
# Runbook: Alert–Ticket Saga Stuck

## Symptoms
- AlertManager fires `AlertTicketSagaStuck` (gauge `alert_ticket_saga_stuck_count > 0` 10min)
- Saga state in `TicketRequested` hoặc `AlertLinkRequested` không tiến

## Diagnose
1. Query stuck saga: `SELECT correlation_id, current_state, updated_at_utc, step_timeout_token_id, retry_token_id FROM alert_ticket_saga_states WHERE current_state IN ('TicketRequested', 'AlertLinkRequested') AND updated_at_utc < NOW() - INTERVAL '10 minutes';`
2. Check Quartz triggers: `SELECT trigger_name, next_fire_time, prev_fire_time, trigger_state FROM qrtz_triggers WHERE trigger_state != 'WAITING';`
3. Check RabbitMQ queue: management UI `ticket-create-ticket-from-alert` hoặc `battery-link-alert-to-ticket` queue depth.
4. Check Quartz scheduler running: `GET /health/saga` (TicketService).

## Mitigation
1. **Quartz scheduler dead**: restart TicketService; verify `/health/saga` 200 sau restart.
2. **Trigger missed fire**: Quartz auto-misfire policy "fire once now" → wait 1-2 phút; nếu vẫn stuck → manual reschedule qua admin endpoint.
3. **Queue stuck**: check consumer health, scale up nếu cần.
4. **Saga state corrupt**: gọi reprocess (runbook 08).

## Verification
- Stuck saga count gauge giảm về 0.
- `updated_at_utc` của stuck saga update trong 1 phút.

## Postmortem template
...
```

**`10-saga-duplicate-canonical.md`** — preflight duplicate Ticket canonicalization:

```markdown
# Runbook: Alert–Ticket Saga duplicate canonicalization

## Symptoms
- Sprint 5B migration `AddAlertTicketSagaFoundation` fail với "duplicate key value violates unique constraint"
- Hoặc query phát hiện duplicate `OriginAlertId` / duplicate active `(BatteryAssetId, Category)`

## Diagnose
1. Query duplicate `OriginAlertId`:
   ```sql
   SELECT origin_alert_id, COUNT(*) FROM tickets
   WHERE origin_alert_id IS NOT NULL AND is_deleted = false
   GROUP BY origin_alert_id HAVING COUNT(*) > 1;
   ```
2. Query duplicate active asset+category:
   ```sql
   SELECT battery_asset_id, category, COUNT(*) FROM tickets
   WHERE origin = 2 AND is_deleted = false
     AND status IN (1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12)
   GROUP BY battery_asset_id, category HAVING COUNT(*) > 1;
   ```

## Mitigation (manual reconciliation, BẮT BUỘC trước khi apply unique constraint)
1. Chọn **Ticket canonical** cho mỗi group duplicate:
   - Ticket có `Status` mới nhất (Resolved/Closed) > In Progress > Open.
   - Nếu cùng status: chọn Ticket có `CreatedAt` cũ nhất.
   - Nếu tie: chọn Ticket có nhiều activity nhất.
2. Update Alert link sang Ticket canonical:
   ```sql
   UPDATE alerts SET ticket_id = '<canonical-ticket-id>' WHERE ticket_id = '<duplicate-ticket-id>';
   ```
3. Mark duplicate Ticket `IsDeleted=true` + insert audit row vào TicketActivity với action `DuplicateCanonicalization` + reason.
4. Log canonicalization decision vào `logs/sprint-5b/duplicate-cleanup-<date>.md` (review by Leader trước migration).

## Verification
- Re-run query step 1 + 2: 0 row.
- Re-apply migration: success.

## Postmortem template
...
```

### 40.3bis. Postmortem template

Mọi runbook reference `## Postmortem template ...`. Đây là template chung dùng sau bất kỳ incident nào (Saga Failed, RabbitMQ down, DB corrupt, v.v.) — viết trong 7 ngày sau incident theo `docs/operations/postmortems/YYYY-MM-DD-<short-title>.md`:

```markdown
# Postmortem: <Title> — YYYY-MM-DD

## Tóm tắt
- **Incident ID**: GH-<issue-number>
- **Severity**: P1 / P2 / P3 (xem §40.4 severity matrix)
- **Detect time**: HH:MM UTC
- **Mitigate time**: HH:MM UTC (cumulative incident duration)
- **Resolve time**: HH:MM UTC (full recovery)
- **Customer impact**: số user affected + scope (read-only / write-blocked / data-loss)

## Timeline
| Time (UTC) | Event |
|-----------|-------|
| HH:MM | Alert fired: `<alert-name>` |
| HH:MM | On-call paged (Leader/Duy/Thắng/Thái) |
| HH:MM | Investigation start — checked dashboard X |
| HH:MM | Identified root cause: ... |
| HH:MM | Mitigation applied: ... |
| HH:MM | Verified recovery: ... |
| HH:MM | All-clear announced |

## Root cause
Mô tả kỹ thuật chi tiết. Cite log line / SQL query / commit SHA / config diff. Phân biệt **trigger** (sự kiện châm ngòi) vs **root cause** (lỗ hổng nền cho phép trigger gây hậu quả).

Ví dụ Sprint 5B:
- Trigger: BatteryService instance crash do OOM
- Root cause: `LinkAlertToTicketConsumer` không có rollback khi DbContext lỗi transaction → message bị acked nhưng business chưa commit → Saga timeout sau 10 phút → Failed.

## Impact
- Customer: X người không thấy ticket auto-created cho alert Critical trong N phút
- SLA: Y ticket vi phạm SLA 4h vì delay
- Data: 0 data loss (Saga forward recovery) / hoặc Z row inconsistent

## What went well
- Alert `AlertTicketSagaStuck` fire đúng trong 10 phút sau trigger
- Runbook `09-saga-stuck.md` giúp diagnose trong 5 phút
- Reprocess endpoint giúp recover toàn bộ Saga Failed

## What went wrong
- OOM không trigger health check → k8s không restart instance
- Postman collection thiếu Idempotency-Key example → admin gọi reprocess thiếu header → 400 lần đầu
- Log không có `MessageId` field → khó match RabbitMQ message với Saga state

## Action items
| # | Action | Owner | Due | Severity |
|---|--------|-------|-----|----------|
| 1 | Add OOM kill detection vào `/health/saga` | Thắng | YYYY-MM-DD | P1 |
| 2 | Postman collection update `Idempotency-Key` example | Leader | YYYY-MM-DD | P2 |
| 3 | Structured log thêm `MessageId` field | Thắng | YYYY-MM-DD | P2 |

## Lessons learned
- Saga forward recovery hoạt động đúng — không có data loss dù 10 phút delay.
- Runbook là first thing reviewer check — phải maintain up-to-date sau mỗi sprint.
- Health check phải verify deep state (Quartz running, DB reachable), không chỉ HTTP 200.

## References
- Runbook used: `docs/operations/runbook/09-saga-stuck.md`
- Related ADR: ADR-018 (Saga forward recovery)
- Related PR fix: GH-<pr-number>
```

### 40.4. On-call & incident response
`docs/operations/incident-response.md`:
- Severity levels (xem severity matrix dưới đây).
- Communication channel (Slack #incidents).
- Escalation path: Staff → Manager → Admin → Tech Lead.
- Postmortem within 48h cho SEV1, 7 ngày cho SEV2/SEV3 (xem template §40.3bis).

#### Severity matrix

| Sev | Trigger criteria | Response time | Escalation path | Postmortem |
|-----|------------------|---------------|-----------------|-----------|
| **SEV1 / P1** | Service down toàn hệ thống · Customer-facing API 5xx > 50% · data loss · security breach · **Saga Failed spike + data inconsistency** | < 15 phút page on-call | Tech Lead + Leader page ngay | **48h** required |
| **SEV2 / P2** | Service degraded (1-50% error) · 1 service down nhưng fallback OK · SLA breach P1 ticket · **Saga stuck/Failed nhưng forward recovery hoạt động** · partial Customer impact | < 1 giờ page | Tech Lead trong business hours | **7 ngày** required |
| **SEV3 / P3** | Single endpoint slow · 1 background service lag · alert noise · **Saga edge case mỗi tuần 1-2 lần, không impact** · cosmetic bug | < 1 ngày | Async trong daily standup | Optional |

**On-call roster (capstone scope):**
- Sprint 5B–8: Thắng (Saga + Battery domain primary), Duy (BE Lead backup), Leader (escalation)
- Demo Sprint 8 day: Leader + all 5 dev on standby

**SEV1 escalation cho Saga (Sprint 5B+):**
1. Page Thắng + Leader đồng thời.
2. Slack #incidents post template: `[SEV1] Saga Failed Spike — AlertManager fired at HH:MM — investigating`.
3. Open shared call (Discord/Meet) trong 5 phút.
4. Assign roles: **Incident commander** (Leader), **Investigator** (Thắng), **Communicator** (Leader update mỗi 15 phút).
5. Mitigate first → root cause sau. Reprocess endpoint là first action (xem runbook `08-saga-failed.md`).
6. All-clear khi Saga Failed count < 5 trong 5 phút liên tiếp.

### 40.5. SLOs (Service Level Objectives)
| Service | Availability | Latency P95 | Error rate |
|---------|--------------|-------------|------------|
| AuthService login | 99.9% | < 200ms | < 0.1% |
| BatteryService realtime | 99.5% | < 150ms | < 1% |
| TicketService write | 99.9% | < 300ms | < 0.5% |
| NotificationService send | 99% | < 500ms | < 2% |
| AI Inference | 99% | < 100ms | < 5% |
| Alert–Ticket Saga happy-path (Sprint 5B) | 99% | < 4s end-to-end | < 1% Failed (terminal) |
| Saga reprocess Failed → Completed | 95% | < 30s p95 | manual fallback acceptable |

#### Error budget per service (monthly window — 30 ngày)

| Service | Target | Error budget | Action khi consume hết budget |
|---------|--------|--------------|-------------------------------|
| AuthService login | 99.9% | 43.2 phút/tháng | Freeze release; tập trung fix availability |
| BatteryService realtime | 99.5% | 3h 36m/tháng | Review deploy cadence; tăng test coverage |
| TicketService write | 99.9% | 43.2 phút/tháng | Freeze release |
| NotificationService send | 99% | 7h 12m/tháng | Review consumer scaling |
| AI Inference | 99% | 7h 12m/tháng | Investigate model latency |
| Alert–Ticket Saga happy-path | 99% | 7h 12m/tháng | Review Quartz scheduler health + RabbitMQ depth |
| Saga reprocess | 95% | 36h/tháng | Tolerate cao vì manual ops, monitor không freeze |

**Burn rate alert** (Prometheus):
- **Fast burn** (consuming 2% budget trong 1h) → page on-call immediately.
- **Slow burn** (consuming 5% budget trong 6h) → notify in #incidents, không page.
- Track via metric `slo_error_budget_remaining{service=...} / slo_error_budget_total{service=...}`.

**Capstone simplification:** Vì project chỉ chạy ~4 tháng, error budget chỉ tracking informational. KHÔNG enforce release freeze trong Sprint 5B–8 (đang active development). Hội đồng KLTN có thể hỏi "tại sao không freeze?" — trả lời: "Capstone scope, không production traffic; freeze policy là post-capstone activation."

### 40.6. Onboarding doc
`docs/onboarding/`:
- `be-newcomer.md` — 1 ngày đầu setup, run local, chạy test.
- `fe-newcomer.md`
- `ai-newcomer.md`
- `glossary.md` — domain terms.

**Local dev machine requirements** (cho cả `be-newcomer.md` + `fe-newcomer.md`):

| Resource | Minimum | Recommended | Lý do |
|----------|---------|-------------|-------|
| RAM | 8 GB | **16 GB** | Docker stack: TimescaleDB + Postgres + Redis + RabbitMQ + MinIO + Prometheus + Grafana + Loki + Alertmanager + Tempo + AI module + 4 BE service = ~6-8GB |
| Disk free | 30 GB | **50 GB** | Docker images (~5GB) + volumes data (~10-15GB after few weeks) + IDE + dev tools |
| CPU | 4 cores | **8 cores** | Container orchestration smooth |
| OS | macOS / Linux / WSL2 | Linux native | Docker Desktop trên macOS/Windows = thêm overhead |
| Internet | 10 Mbps | 50 Mbps | Pull Docker image, sync repo, OpenMeteo API |

**Disk cleanup script** (Sprint 5B nên thêm vì Quartz schema tăng dung lượng):
```bash
# tools/dev-cleanup.sh — Sprint 5B addition
docker volume prune -f
docker system prune -af --volumes  # ⚠️ xóa hết Docker — backup trước
# Hoặc selective:
docker exec ticket-service-db psql -U postgres -d ticket_db -c "DELETE FROM qrtz_fired_triggers WHERE fired_time < extract(epoch from now() - interval '7 days')*1000;"
docker exec ticket-service-db psql -U postgres -d ticket_db -c "DELETE FROM alert_ticket_saga_states WHERE current_state IN ('Completed','Failed') AND updated_at_utc < now() - interval '30 days';"  # ⚠️ chỉ dùng local dev, không production
```

**Sprint 5B additions cho `be-newcomer.md`** (sau Sprint 5B merge):
- Section "Saga local setup":
  1. Pull latest dev branch + verify `services/TicketService/src/TicketService.Infrastructure/Persistence/Migrations/` có file `*AddAlertTicketSagaFoundation*` + `*AddQuartzPersistenceSchema*`.
  2. Run `dotnet ef database update -p ../TicketService.Infrastructure -s .` — apply cả 2 migration vào `ticket_db`.
  3. Verify `ticket_db` có 11 `qrtz_*` tables + `alert_ticket_saga_states` table: `\dt qrtz_*` trong psql.
  4. Set environment override trong `.env.Docker.local`: `AlertTicketSagaEnabled=true` để Saga endpoint active local.
  5. Run `dotnet test --filter Category=Saga` từ `tests/TicketService.IntegrationTests/` — expect ≥ 21 case pass.
- Section "Debug Saga state machine":
  1. Query state: `SELECT correlation_id, current_state, ticket_id, failed_step, last_error FROM alert_ticket_saga_states WHERE correlation_id = '<alert-id>'`.
  2. Query active triggers: `SELECT trigger_name, next_fire_time FROM qrtz_triggers WHERE sched_name = 'TicketServiceScheduler'`.
  3. Tail Saga log: `docker logs ticket-service -f | grep CorrelationId=<alert-id>`.
  4. Force re-trigger: `POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess` với header `Idempotency-Key: $(uuidgen)`.
- Section "Common mistakes" (chỉnh sửa thường gặp):
  - Quên override `AlertTicketSagaEnabled=true` → endpoint không register → test integration fail mơ hồ.
  - Quên apply `AddQuartzPersistenceSchema` → MassTransit báo "qrtz_triggers does not exist" khi schedule timeout.
  - Set `AlertTicketDispatchEnabled=false` quên revert → BatteryService không publish event → Saga không start.

---

## 41. Preventive maintenance schedule — P2

### 41.1. Entity `MaintenanceSchedule`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `BatteryAssetId` | Guid (FK) | — |
| `MaintenanceType` | enum (Cleaning=1, Inspection=2, SohCheck=3, Calibration=4, FullService=5) | — |
| `IntervalDays` | int | Mỗi N ngày |
| `LastPerformedAt` | DateTime? | — |
| `NextDueAt` | DateTime | computed |
| `IsActive` | bool | — |
| `CreatedByUserId` | Guid | Manager set |

### 41.2. Background service
`PreventiveMaintenanceBackgroundService` (daily):
- Scan schedule with `NextDueAt < now + 7d`.
- Tạo Ticket origin `PreventiveMaintenance` (new origin enum value) tự động.
- Title: "Preventive: {MaintenanceType} - {AssetSerial}".
- Manager auto-assign theo schedule.

### 41.3. Endpoints
```
POST   /api/battery-assets/{id}/maintenance-schedules     (Manager)
GET    /api/battery-assets/{id}/maintenance-schedules
GET    /api/v1/maintenance-schedules/upcoming?within=30d     (Manager)
PUT    /api/v1/maintenance-schedules/{id}/complete           (Staff — mark done, updates LastPerformedAt)
DELETE /api/v1/maintenance-schedules/{id}
```

### 41.4. Reports
- "Assets quá hạn maintenance > 30 ngày"
- "Maintenance compliance rate per Staff"

---

## 42. Parts inventory — P2

### 42.1. Entity `Part`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Sku` | string(50) UNIQUE | — |
| `Name` | string(200) | "BMS Module 12V" |
| `Description` | string? | — |
| `Manufacturer` | string? | — |
| `UnitCost` | decimal? | — |
| `StockCount` | int | — |
| `MinStockThreshold` | int | Alert khi xuống |
| `Status` | enum (Active=1, Discontinued=2) | — |

### 42.2. Entity `PartTransaction` (audit)
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `PartId` | Guid (FK) | — |
| `TransactionType` | enum (StockIn=1, Used=2, Adjusted=3, Disposed=4) | — |
| `Quantity` | int | + or - |
| `RelatedTicketId` | Guid? | Nếu dùng cho ticket |
| `PerformedByUserId` | Guid | — |
| `PerformedAt` | DateTime | — |
| `Note` | string? | — |

### 42.3. Integration với MaintenanceLog
- Khi Staff add MaintenanceLog với `PartsUsed` → tự động tạo `PartTransaction` type=Used, deduct stock.
- Stock < MinStockThreshold → notify Manager "Cần nhập linh kiện X".

### 42.4. Endpoints
```
POST   /api/v1/parts                                    (Admin)
GET    /api/v1/parts?lowStock=true                      (Manager/Admin)
PUT    /api/v1/parts/{id}/stock-in                      (Manager — nhập kho)
GET    /api/v1/parts/{id}/transactions
GET    /api/v1/reports/parts-usage?from=&to=
```

> Out of scope chính của capstone nhưng nếu kịp thời gian thì làm — academic bonus.

---

## 43. Public Knowledge Base + Customer self-help — P2

### 43.1. Update `KnowledgeBaseArticle`
Thêm fields:
- `IsPublic` (bool) — visible cho Customer + public
- `PublicTitle` (string?) — version Customer-friendly
- `PublicBody` (string?) — version Customer-friendly (đơn giản hơn Staff version)

### 43.2. Public endpoint (no auth)
```
GET    /api/v1/public/knowledge-base?q=&category=
GET    /api/v1/public/knowledge-base/{slug}
POST   /api/v1/public/knowledge-base/{id}/helpful       (anonymous count — rate limit per IP)
```

### 43.3. Self-help suggest khi Customer tạo ticket
- Form tạo ticket → khi Customer chọn Category → suggest 3 KB articles.
- Nếu Customer click "Đã giải quyết bằng article này" → ticket không được tạo, increment `HelpfulCount`.
- Báo cáo "Articles giảm ticket bao nhiêu".

### 43.4. Endpoint hỗ trợ flow
```
POST   /api/v1/tickets/suggest-articles
{
  "category": "Charging",
  "description": "Pin sạc rất chậm"
}
→ Response: [{articleId, title, snippet}, ...]
```

---

## 44. Mobile deep linking + Staff field features — P1

### 44.1. Deep link URL scheme
| Resource | URL pattern |
|----------|-------------|
| Ticket detail | `gsu26se55://tickets/{id}` |
| Alert detail | `gsu26se55://alerts/{id}` |
| Asset detail | `gsu26se55://assets/{id}` |
| Claim QR | `gsu26se55://claim?code={code}` |
| Notification | `gsu26se55://notifications/{id}` |

### 44.2. Universal Links (iOS) / App Links (Android)
- Web URL `https://app.gsu26se55.com/tickets/{id}` → mở app nếu installed, fallback web.
- Apple file: `/.well-known/apple-app-site-association`
- Android file: `/.well-known/assetlinks.json`
- BatteryService/TicketService endpoint expose 2 file static.

### 44.3. Push payload có deep link
```json
{
  "to": "ExponentPushToken[...]",
  "title": "🔴 Cảnh báo nghiêm trọng",
  "body": "Pin BAT-001 overheat",
  "data": {
    "url": "gsu26se55://alerts/abc-123",
    "type": "alert.critical",
    "alertId": "abc-123"
  }
}
```

### 44.4. Staff field features (Mobile cho Staff đi field)

Mặc dù scope mobile chính là Customer, Staff đi on-site cần:

#### GPS check-in
- `MaintenanceLog.CheckInLatitude/Longitude/At`
- Endpoint `POST /api/v1/maintenance-logs/check-in`
- Verify check-in trong bán kính 100m từ site đăng ký.

#### Offline mode (sync queue)
- Staff không có mạng tại site → log work offline.
- Mobile lưu queue local, sync khi có mạng.
- Backend hỗ trợ `Idempotency-Key` (đã có) cho retry.

#### Photo upload tối ưu
- Resize ảnh client-side trước upload (max 1920px).
- Compress JPEG quality 80.
- Endpoint `POST /api/v1/files/upload` (FileStorageService).

#### Quick actions
- "Mark as Resolved + photo" (1 step thay vì 3).
- Voice-to-text cho maintenance summary.

### 44.5. Endpoints bổ sung
```
POST   /api/v1/maintenance-logs/check-in
GET    /.well-known/apple-app-site-association          (static)
GET    /.well-known/assetlinks.json                     (static)
```

---

## 45. Webhook outbound + public API — P2

### 45.1. Webhook outbound

#### Entity `WebhookSubscription`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `CreatedByUserId` | Guid | Admin only |
| `Url` | string(500) | HTTPS only |
| `Secret` | string(64) | HMAC sign payload |
| `EventTypes` | string[] | Array of event names |
| `IsActive` | bool | — |
| `FailureCount` | int | Disable sau N fail liên tiếp |

#### Logic
- Admin register webhook + chọn events to subscribe.
- Internal consumer `WebhookDispatcherConsumer` subscribe tất cả events.
- Khi event xảy ra → tìm subscriptions match → POST payload + HMAC header `X-Signature`.
- Retry 3 lần exponential, fail → log + increment failure count.
- 10 consecutive failures → auto-disable + notify Admin.

#### Endpoints
```
POST   /api/v1/admin/webhooks                           (Admin)
GET    /api/v1/admin/webhooks
PUT    /api/v1/admin/webhooks/{id}
DELETE /api/v1/admin/webhooks/{id}
POST   /api/v1/admin/webhooks/{id}/test                 (send test payload)
GET    /api/v1/admin/webhooks/{id}/deliveries           (last 100 attempts)
```

### 45.2. Public API (cho partner integration)

#### API Key management (Admin)
```
POST   /api/v1/admin/api-keys                           (Admin tạo key)
{
  "name": "IoT Gateway #1",
  "scopes": ["sensor.ingest", "asset.read"]
}
→ Response: { "apiKey": "sb_live_..." }  (chỉ show 1 lần)

GET    /api/v1/admin/api-keys
DELETE /api/v1/admin/api-keys/{id}                      (revoke)
PUT    /api/v1/admin/api-keys/{id}/rotate               (gen new secret, old works 24h)
```

#### Auth via API Key
- Header `X-Api-Key: sb_live_...`
- Middleware validate + load scopes vào HttpContext.
- Limit per key: rate limit + scope check per endpoint.

#### Public endpoints (scope-gated)
```
POST   /api/v1/public/sensor-readings/batch             (scope: sensor.ingest)
GET    /api/v1/public/assets/{id}                       (scope: asset.read)
```

### 45.3. Tests
- Webhook signature verification.
- Auto-disable sau 10 fail.
- API key rotation grace period (old + new đều work 24h).

---

## 46. Advanced testing & chaos engineering — P2

### 46.1. Contract testing (Pact)

#### Setup
- Producer BatteryService viết contract cho `BatteryAnomalyDetectedEvent`; Saga endpoint TicketService verify khi build.
- TicketService/BatteryService verify tiếp các Saga command, success, rejection và failure contracts trong
  `SharedContracts/Saga/AlertTicket`; breaking change phải tạo version mới, không sửa payload âm thầm.
- Lưu contracts ở `tests/contracts/`.

#### Tools
- `PactNet` cho .NET.
- CI step: `dotnet test --filter Category=Contract`.

### 46.2. Load testing (k6 detailed)

`tools/load-test/`:
- `sensor-ingest.k6.js`: 1000 readings/s for 5 phút.
- `customer-realtime.k6.js`: 100 concurrent users polling 30s.
- `manager-queue.k6.js`: 50 Manager đồng thời xem queue.
- `ticket-create.k6.js`: 100 Customer tạo ticket / phút.

Acceptance criteria (per §13.4):
```javascript
export const options = {
  scenarios: { /* ... */ },
  thresholds: {
    http_req_duration: ['p(95)<300', 'p(99)<500'],
    http_req_failed: ['rate<0.01'],
  },
};
```

### 46.3. Chaos engineering

#### Scenarios
| Scenario | Tool/Method | Expected behavior |
|----------|-------------|-------------------|
| Kill RabbitMQ 30s | `docker stop solar-rabbitmq && sleep 30 && docker start` | Outbox accumulates, replay khi up |
| Kill TicketService sau khi Alert commit | stop container trước create/reuse response | Saga tiếp tục sau restart, không tạo Ticket trùng |
| Kill BatteryService sau khi Ticket commit | stop container trước link callback | Saga giữ `TicketId`, retry link sau restart |
| Restart scheduler khi Saga đang chờ timeout | restart TicketService/Quartz | Timeout schedule được recover từ DB |
| Kill Redis | similar | Cache miss fallback DB. **Saga/participant endpoints không bị ảnh hưởng** vì idempotency đã chuyển sang MassTransit EF Consumer Outbox/Inbox trên service DbContext (xem §8.2) — Redis Inbox chỉ còn dùng cho consumer không thay đổi DB. |
| Kill 1 BatteryService instance (3-replica) | k8s pod delete | LB redirect, no error to client |
| Network partition AI Module | `tc qdisc add` | Circuit breaker open, fallback threshold-only |
| Disk fill 95% | `dd` fill | AlertManager fires, services degrade gracefully |
| Postgres slow query (sleep 30s) | `pg_sleep` | Timeout, Polly retry, response 503 sau retry exhaust |

#### Automation
- `tools/chaos/`:
  - `kill-service.sh <name> <duration>`
  - `network-partition.sh <service> <duration>`
  - Run trong staging env (không phải local dev).

### 46.4. Mutation testing (Stryker.NET)

```bash
cd services/TicketService
dotnet tool install -g dotnet-stryker
dotnet stryker --project TicketService.Application.csproj
```
- Target: kill rate ≥ 60% cho state machine class.
- CI optional (run weekly nightly).

### 46.5. Visual regression (FE concern, BE provides stable data)
- Seed data deterministic (fixed UUIDs + dates).
- Endpoint `GET /api/v1/test/fixtures/snapshot-data` (only in non-prod).

---

## 47. Security hardening additional — P1

### 47.1. Password policy
- Minimum length 12 chars (đã có 8 — nâng cấp).
- Password history: không reuse 5 password gần nhất.
- Password expiry: 180 ngày (Admin/Manager), không bắt Customer.
- Force change on first login.

#### Entity update `Account`
- `PasswordChangedAt` (DateTime)
- `MustChangePassword` (bool)

#### Entity `PasswordHistory`
- `Id, AccountId, PasswordHash, CreatedAt`
- Keep last 5 per account.

### 47.2. Concurrent session limit
- Account max 3 active session (3 device).
- Login lần thứ 4 → revoke session cũ nhất.
- Customer mobile + Web simultaneously = 2 session OK.

#### Update `Session` entity (đã có RefreshToken)
- Logic trong `LoginCommandHandler`: count active sessions, if >= 3 → revoke oldest.

### 47.3. IP whitelist cho Admin endpoint
- Config `AdminIpWhitelist` (env var, comma-separated CIDR).
- Middleware `AdminIpRestrictionMiddleware` apply cho `/api/v1/admin/*`.
- Fail returns 403.

### 47.4. CSRF protection
- Cookie-based auth (Web) → need CSRF token.
- JWT Bearer (Mobile) → not needed.
- Implementation: ASP.NET Core built-in `AddAntiforgery`.

### 47.5. Brute force lockout policy refined
- Current LoginAttempt entity tracks attempts.
- Policy:
  - 5 failed in 10 min → lock 15 min.
  - 10 failed in 1 hour → lock 1 hour.
  - 20 failed in 24h → lock 24h, notify Admin.
- Per IP + per account separately.

### 47.6. Audit sensitive actions
Force re-auth (password re-confirm) for:
- Delete account
- Change email
- Change password
- Revoke all sessions
- Admin: delete user, transfer asset

### 47.7. CSP headers (đã có `SecurityHeadersMiddleware`)
Tighten:
```
Content-Security-Policy: default-src 'self'; img-src 'self' data: https:; script-src 'self'; style-src 'self' 'unsafe-inline'
X-Frame-Options: DENY
X-Content-Type-Options: nosniff
Strict-Transport-Security: max-age=31536000; includeSubDomains
Referrer-Policy: strict-origin-when-cross-origin
```

### 47.8. Secret rotation
- JWT signing key: rotate every 90d, support 2 keys simultaneously (old + new) for grace period.
- Database password: rotate quarterly (out of scope manual).
- API keys: Admin trigger via §45.2 endpoint.

### 47.9. Dependency scanning (đã có Trivy)
Bổ sung:
- `dotnet list package --vulnerable` weekly trong CI.
- Dependabot rules priority HIGH cho security updates (đã có ví dụ RestSharp PR #45).

---

## 48. AI feedback loop & analytics — P1

### 48.1. Staff feedback on AI predictions
- Khi Staff resolve ticket auto-created từ alert → UI ask:
  - "AI classified này là Failed. Có đúng không?" [Đúng] [Sai - false positive] [Sai - false negative]
  - "SOH AI predict 65%. Thực tế bạn đo được bao nhiêu?" (optional input)

#### Endpoint
```
POST   /api/v1/anomaly-classifications/{id}/feedback
{
  "isCorrect": true,
  "actualClassification": "Failed",
  "actualSohPercent": 62.5,
  "note": "Đúng, BMS module failed"
}
```

### 48.2. AI accuracy reporting
```
GET    /api/v1/ai/feedback-stats?from=&to=
→ Response:
{
  "totalPredictions": 1250,
  "totalFeedback": 320,
  "feedbackRate": 0.256,
  "truePositiveRate": 0.85,
  "falsePositiveRate": 0.10,
  "falseNegativeRate": 0.05,
  "sohMaePercent": 1.8,           // Mean Absolute Error
  "modelVersion": "1.0"
}
```

### 48.3. Export training data
Monthly background job exports labeled data → MinIO bucket `ai-training-data/{year-month}.parquet`:
- Features: sensor readings 30 timestep
- Labels: Staff-confirmed classification + actual SOH
- AI team download để retrain.

### 48.4. A/B testing AI model
- Feature flag `AI_MODEL_VERSION` (1.0 vs 1.1).
- Route X% traffic mới to v1.1, compare accuracy.
- Out of scope chính nhưng nice if time.

### 48.5. Drift detection
- Compare prediction distribution week-over-week.
- Nếu shift > 20% → notify AI team (model có thể drift).
- Background job weekly.

---

## 49. Notification advanced (digest + batching) — P1

### 49.1. Digest email (daily/weekly)

#### Entity update `NotificationPreference`
Thêm:
- `DigestEnabled` (bool default true cho Manager/Admin)
- `DigestFrequency` (enum: Daily=1, Weekly=2, None=3)
- `DigestSendHour` (int, 0-23, default 8 — 8AM local time)

#### Background service
`NotificationDigestBackgroundService` (every hour):
- Tìm user có digest due (theo timezone + send hour).
- Aggregate notification 24h/7d gần nhất.
- Render template `digest-daily.hbs` / `digest-weekly.hbs`.
- Publish `SendEmailRequestedEvent`.

### 49.2. Notification batching
- Khi nhiều alert cùng asset trong 5 phút → gộp 1 push.
- Logic trong `NotificationDispatcher`:
  ```
  Before send push:
  - Check Redis key `notif_batch:{userId}:{assetId}` trong 5 phút gần nhất.
  - Nếu tồn tại → append to batch, không send mới.
  - Nếu không → tạo batch + schedule "flush" sau 30s.
  - Sau 30s → send 1 push "Pin X có {count} cảnh báo mới".
  ```

**Sprint 5B — Saga notification debounce (R-22 mitigation):**
- `BatteryAlertEscalationPending` (per AlertId): debounce 5 phút — Redis key `notif_debounce:escalation:{alertId}` TTL 5min; duplicate event trong window bỏ qua (chỉ in-app silent log).
- `AlertTicketSagaFailed` (per AlertId): debounce 5 phút — Redis key `notif_debounce:saga-failed:{alertId}` TTL 5min. Nếu Saga Failed → admin reprocess → lại Failed trong 5 phút: vẫn chỉ 1 push tới Admin (chống loop spam khi root cause chưa fix).
- KHÔNG batch cross-AlertId vì mỗi Saga Failed cần action riêng từ admin.

### 49.3. Snooze notification per user
- User trên Mobile click "Don't notify me for 1h about this asset".
- Backend `POST /api/v1/notification-snooze`:
  ```json
  {
    "scopeType": "asset",
    "scopeId": "...",
    "durationMinutes": 60
  }
  ```
- NotificationDispatcher check snooze trước khi send.

### 49.4. In-app notification grouping
- Mobile/Web list notification → group by type + entity.
- Backend endpoint `GET /api/v1/notifications/grouped`:
  ```json
  {
    "groups": [
      { "key": "ticket-TKT-2605-0001", "title": "Ticket TKT-2605-0001", "count": 5, "latestAt": "...", "items": [...] },
      ...
    ]
  }
  ```

### 49.5. Webhook outbound từ NotificationService
Tách `WebhookDispatcher` thành 1 channel mới (xem §45.1).

---

## 50. Updated sprint backlog impact

> Các section §30–49 thêm khá nhiều việc. Đây là phân bổ lại sprint backlog có cập nhật.

### Sprint impact summary

| Sprint | Original scope | Bổ sung | Tổng effort |
|--------|---------------|---------|-------------|
| Sprint 1 | Stabilize foundations | + ADR setup, Edge case doc, **B5 (ADR-0005 ITIL stance), B2-draft (AI refs skeleton), B11 (§26 ref update)** | 1.2× |
| Sprint 2 | BatteryService MVP | + **Site/BatteryGroup entities**, + **AI Bridge client skeleton** | 1.4× — cần thêm 1 dev hoặc kéo dài 3 ngày |
| Sprint 3 | BatteryService anomaly engine | + **AI Hybrid pipeline**, + **AlertSilence + Snooze**, + **Bulk import**, + **QR claim** | 1.6× — cân nhắc tách thành Sprint 3a + 3b |
| Sprint 4 | TicketService foundation only | + **TicketRelation**, + **TicketSubscription**, + **Comment edit/mention** giữ trong backlog, + **B3 (Priority Matrix Impact×Urgency)** | 1.2× |
| Sprint 5 | TicketService SLA + workflow integration | + **SLA pause limits**, + auto-create từ Battery anomaly, + MaintenanceLog/comment/attachment, + **B6 (StaffSkillTierEnum), B7 (Escalation closure rule)** | 1.4× |
| Sprint 5B | Battery scope cleanup + Alert–Ticket Saga | + bỏ Energy/CO2 + `Site.CapacityKw`, harden Outbox/Inbox, Saga orchestration, ambient/environmental/tier-2 sau P0 | **1.8× — bắt buộc defer scope phụ, xem mục cuối** |
| Sprint IoT-1 | IoT Edge Device backend + device lifecycle | + Device provisioning, heartbeat, per-device API key, offline detection, ESP32 simulator/hardware guide, MQTT P3 optional (§52.14), + **B9 (SensorReading.SourceType BMS/IoT)** | 1.1× sprint song song Sprint 6 |
| Sprint 6 | NotificationService + KB | + **Notification digest/batching**, + **SSE realtime**, + **Public KB**, + **B8 (KB Code + TicketKbReference)**, + **Sprint 5B carryover** (verify 2 Saga consumer + 2 template + dispatcher debounce — pass-14 add) | 1.6× (up from 1.5×) |
| Sprint 7 | Reports + Gateway + Observability | + **GDPR endpoints**, + **Webhook outbound**, + **API key management**, + **B4 (Cascade Risk rule-based), B10 (SensorMismatch anomaly)**, + **Sprint 5B carryover** (verify Saga panel/alert/swagger/tracing/seed/E2E — pass-15 add) | 1.6× (up from 1.5×) |
| Sprint 8 | Demo prep + polish | + **ADR/DR/Runbook finalize**, + **Chaos test**, + **AI feedback report**, + **Sprint 5B carryover** (Saga demo script + Mermaid diagram + architecture publish — pass-16 add) | 1.1× (up from giữ nguyên) |

### ⚠️ Sprint overload mitigation (B1-B11 impact)

**Sprint 5B effort 1.8×** — scope cleanup và Saga `#233–#241` là release gate, không thể triển khai
an toàn nếu vẫn giữ toàn bộ ambient/tier-2/environmental trong 7 ngày. Mitigation bắt buộc:
- Hoàn thành `#233–#241` trước.
- Defer `B2-finalize`, OpenMeteo/ambient và `MarkFalseAlarmEnvironmentalIncidentCommand` sang Sprint 6/7 nếu thiếu capacity.
- Không defer Outbox/Inbox hardening, unique constraint, timeout hoặc failure-path tests.
- Chỉ kéo Sprint 5B lên 9 ngày khi không ảnh hưởng owner của Sprint 6; kéo dài không thay thế việc giảm scope.

**Sprint 7 effort 1.6×** (sau pass-15 sync với Sprint 5B carryover) — đã 1.3× với GDPR + Webhook + API key. Thêm B4 + B10 + Sprint 5B verify items → cân nhắc:
- B10 (SensorMismatch) chỉ tốn ~0.1× — không vấn đề.
- B4 (Cascade Risk) ~0.3× — nếu Sprint 7 quá tải → defer Webhook outbound (§45.1) sang post-capstone backlog.
- Sprint 5B carryover verify (~0.1×) — lightweight, không drop được vì là acceptance gate cho Saga production-ready.

### Re-prioritization recommendation

**Phải có cho capstone demo (MUST):**
1. AI Module integration (§30) — Sprint 2-3
2. Site entity (§31) — Sprint 2
3. Edge case rules (§38) — Sprint 4-5 (lúc implement state machine)
4. SLA pause limits (§33) — Sprint 5
5. Battery scope cleanup: bỏ Energy/CO2 và `Site.CapacityKw` (§53.1–§53.3) — Sprint 5B
6. Alert–Ticket Saga + Outbox/Inbox hardening (§8.1–§8.3, §53.4–§53.12) — Sprint 5B
7. IoT Edge Device backend + device lifecycle (ESP32 + hybrid HTTPS/MQTT) (§52/§52bis, `newiot.md`/`overall.iot.md`) — Sprint IoT-1
8. SSE realtime (§34) — Sprint 6
9. ADR + Runbook (§40) — Sprint 7-8

**Nên có nếu kịp (SHOULD):**
1. Ticket relations (§32) — Backlog sau Sprint 5, không đưa vào Sprint 4 foundation
2. QR onboarding (§35) — Sprint 3
3. Comment edit/mention (§36) — Backlog sau Sprint 5, chỉ làm comment cơ bản ở #143
4. Alert silence/snooze (§37) — Sprint 3
5. GDPR endpoints (§39) — Sprint 7
6. AI feedback loop (§48) — Sprint 8

**Có thì tốt, không có thì giữ trong backlog (COULD):**
1. Preventive maintenance (§41)
2. Parts inventory (§42)
3. Public KB (§43)
4. Webhook outbound (§45)
5. Chaos testing (§46.3)
6. Mutation testing (§46.4)

### Updated Definition of Done (DOD)
Thêm vào §18:
- [ ] **ADR cập nhật** cho mọi quyết định kiến trúc lớn.
- [ ] **Edge case rule** từ §38 có test cover.
- [ ] **AI integration** smoke test (BatteryService gọi AI predict thành công).
- [ ] **Realtime SSE** demo được trong scope test.
- [ ] **GDPR export** trả về data đầy đủ cho 1 sample user.
- [ ] **Runbook** cho ít nhất 5 scenario thường gặp.
- [ ] **Alert–Ticket Saga** hoàn tất create/reuse/link và reprocess được failure.
- [ ] **Scope guard:** không còn Energy/CO2 contract hoặc `Site.CapacityKw`.

---

## 51. Tóm tắt cập nhật quan trọng nhất

### So với phiên bản đầu, đây là những thay đổi RIPPLE EFFECT:

1. **Entity count: 17 → 50+** (đồng bộ §67 stats — sau Sprint 5B reconcile)
   - Mới: SohPrediction, AnomalyClassification, Site, BatteryGroup, AlertSilenceRule, TicketRelation, TicketSubscription, CommentMention, CommentReaction, CommentTemplate, MaintenanceSchedule, Part, PartTransaction, WebhookSubscription, PasswordHistory, AlertAckTimeline, DataExportRequest, AmbientReading, AmbientThresholdConfig, EnvironmentalIncident, IotDevice, IotDeviceHeartbeat, IotDeviceCalibration, IotFirmwareRelease, IotFirmwareUpdateLog, NoiseBreachEvent, CustomerAccount/StaffAccount read-model, AlertTicketSagaState, mt_inbox_state/mt_outbox_state/mt_outbox_message, qrtz_* (11 tables Sprint 5B).

2. **Endpoints: 100+ → 220+** (đồng bộ §67 stats)

3. **Integration events: 17 → 30+**
   - Mới: `SohRapidDegradationEvent`, `SohWarningEvent`, `SohCriticalEvent`, `SiteAlertAggregatedEvent`, `WebhookEventPublishedEvent`, `BatteryAlertEscalationRequestedEvent`, `BatteryAnomalyDetectedV2Event`, `EnvironmentalIncidentDetectedEvent`, `EnvironmentalIncidentResolvedEvent`, `IotDeviceWentOfflineEvent` (§52.6).
   - Alert–Ticket Saga bổ sung 8 command/event contracts trong `SharedContracts/Saga/AlertTicket/` (CreateTicketFromAlertCommand, TicketProvisionedForAlertEvent, TicketProvisionForAlertRejectedEvent, LinkAlertToTicketCommand, AlertLinkedToTicketEvent, AlertLinkToTicketRejectedEvent, ReconcileAlertTicketSagaCommand, AlertTicketSagaFailedEvent).

4. **Background services per service tăng**
   - BatteryService: 4 → 7 (thêm SohPrediction, DeviceOfflineDetection, AlertAckEscalation)
   - TicketService: 4 → 6 (thêm SlaPauseEnforcement, ApprovalTimeout, PreventiveMaintenance); Sprint 5B thêm Quartz scheduler endpoint (in-process, dùng `qrtz_*` schema) cho Saga retry/timeout.

5. **Migration impact**
   - BatteryService cần migration mới: `AddSiteAndGroup`, `AddSohPredictionTables`, `AddAlertSilenceRule`, `AddClaimCode`, `RemoveSiteCapacityKw`, `AddDurableMessagingFoundation`, `AddAlertTicketLinkIndex`, `AddIotDeviceManagement` (Sprint IoT-1 — 5 IoT entity + heartbeat hypertable + `SensorReading.SourceType`/`SensorSourceCode`).
   - TicketService cần: `AddTicketRelations`, `AddTicketSubscriptions`, `AddCommentAdvanced`, `AddSlaPauseLimits`, `AddMaintenanceSchedule`, `AddDurableMessagingFoundation`, `AddAlertTicketSagaFoundation`, `AddQuartzPersistenceSchema` (11 bảng `qrtz_*` cho durable scheduler — chạy bằng official Quartz.NET SQL script, không dùng EF migration sinh từ model).
   - AuthService cần: `AddGdprFields`, `AddPasswordHistory`, `AddSessionLimit`

6. **Docker compose updates**
   - Add `ai-module` service
   - Add `tempo` for tracing
   - Add persistent Saga scheduler configuration; current RabbitMQ image does not include delayed-message plugin.
   - **(IoT P3)** Add MQTT broker (EMQX/Mosquitto) qua `infra/mqtt/docker-compose.yml` + TLS 8883 + credential/ACL per-device — chỉ khi triển khai MQTT realtime (§52.14).

7. **Documentation deliverables tăng**
   - `docs/adrs/` — 15 ADR files
   - `docs/operations/` — DR plan, runbooks, incident response, SLOs
   - `docs/architecture/edge-cases.md`
   - `docs/onboarding/`

8. **Team capacity check**
   - Original effort: 8 sprint với 3 BE dev — realistic
   - With additions P0: 8 sprint với 3 BE dev — tight nhưng doable nếu drop COULD items
   - Recommendation: **MUST items + SHOULD items 50%**, COULD items vào backlog sau capstone

---

---

# Phần VIII — Bổ sung lần 2 (Final completeness)

> Phần này bổ sung sau khi review lần 3. Scope review ngày 10/6/2026 giữ **IoT Edge Device (ESP32, pivot từ RPi — ADR-016), K8s deployment, App management, Demo prep**, loại Solar Energy/CO2 metrics và bổ sung Alert–Ticket Saga.

---

## 52. IoT Edge Device & Device Management — P0

> Solar battery context: backend phải **giao tiếp với IoT edge device thực tế**. Theo pivot v2 (ADR-016, `newiot.md`/`overall.iot.md`), edge device chuẩn là **ESP32-S3** (`DeviceType=StandaloneSensor`) đọc BMS qua **RS485/Modbus RTU multi-drop**, gửi backend qua **hybrid HTTPS + MQTT**. Bản v1 (Raspberry Pi, folder `iot/`, Python) vẫn được tham chiếu nhưng KHÔNG còn là đường triển khai chính.
>
> "Gateway" trong các tên cũ (DeviceType `Gateway=1`, `firmware-check`, …) vẫn giữ giá trị enum/route — chỉ hiểu lại: ESP32 node = `StandaloneSensor=2`, quản nhiều pin qua multi-drop chứ không phải gateway tập trung.

### 52.1. Architecture overview

```
Battery + BMS (mỗi pin 1 unitId)
    │
    │ RS485 / Modbus RTU (multi-drop, 1 bus nhiều BMS)
    ▼
ESP32-S3 node  (poll BMS → normalize → calibration → local queue → publish)
    │
    ├───── MQTT (v2, realtime <100ms, 2 chiều) ─────┐
    │        solar/{site}/{dev}/telemetry           │
    │        solar/{dev}/heartbeat                   ▼
    │        solar/{dev}/status (LWT offline)   MQTT Broker (EMQX/Mosquitto)
    │        solar/{dev}/cmd (downlink)              │ push
    │                                                ▼
    │                                   MqttBridgeBackgroundService (subscribe)
    │                                                │
    └───── HTTPS REST (v1, bulk/admin/firmware) ─────┤
             POST /api/sensor-readings/batch         │ cùng đổ vào ▼
             provision / heartbeat / firmware-check  │
                                                     ▼
                                          BatteryService API / Ingest
    │
    ├──→ Validate device API key + (MQTT) credential per-device, device phải Active
    ├──→ Validate timestamp (within 5min skew)
    ├──→ Dedup via Idempotency-Key
    ├──→ Apply calibration (raw*scale + offset)
    ├──→ Insert sensor_readings (TimescaleDB)
    ├──→ Update device.last_seen_at
    ├──→ (MQTT LWT status=offline → mark Offline tức thì)
    └──→ Trigger threshold check → anomaly → alert/ticket/notification
```

### 52.2. New entities

#### `IotDevice`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | PK |
| `DeviceCode` | string(64) UNIQUE | "GW-001234" |
| `DeviceType` | enum (Gateway=1, StandaloneSensor=2) | — |
| `Model` | string(100) | "ESP32-S3-N16R8" (chuẩn v2) / legacy "RaspberryPi-4B" |
| `FirmwareVersion` | string(20) | "1.2.3" |
| `MacAddress` | string(17)? | — |
| `SiteId` | Guid? (FK) | Site mà gateway đặt tại |
| `Status` | enum (Provisioning=1, Active=2, Offline=3, Decommissioned=4) | — |
| `ApiKeyId` | Guid (FK) | Link tới API key |
| `LastSeenAt` | DateTime? | Update mỗi heartbeat |
| `LastFirmwareUpdateAt` | DateTime? | — |
| `BatteryAssetIds` | jsonb | Array — 1 device quản nhiều battery (multi-drop RS485) |
| `ConfigJson` | jsonb? | Per-device config: pollingInterval, heartbeatInterval, ngưỡng client-side, **và `batteryMappings[]`** |

**`ConfigJson.batteryMappings[]` (multi-drop RS485 — mỗi BMS 1 `unitId`):**
```json
"batteryMappings": [
  { "batteryAssetSerial": "BAT-2026-001", "unitId": 1, "sensorSourceCode": "primary" },
  { "batteryAssetSerial": "BAT-2026-002", "unitId": 2, "sensorSourceCode": "primary" }
]
```
> Firmware ESP32 dùng `unitId` để poll đúng BMS trên bus RS485, `batteryAssetSerial` để backend map về `BatteryAsset`, `sensorSourceCode` để phân biệt nguồn (primary/redundant). Backend validate device chỉ được gửi reading cho battery nằm trong mapping của nó.

**API key per-device — scope (§7.2):** `sensor.ingest` + `device.heartbeat` + (nếu device có cảm biến môi trường SHT31/MQ-2/water) `environmental.ingest` — để cùng device key gọi được `/api/ambient-readings/batch` và `/api/environmental-incidents` (§1.8). Chỉ lưu **hash**, key hiện 1 lần khi tạo/provision, hỗ trợ rotate/revoke; MQTT (v2) cấp thêm credential riêng + ACL topic per-device (§52.14).

#### `IotDeviceHeartbeat` (time-series, append-only)
| Field | Type |
|-------|------|
| `Time` | DateTime (hypertable column) |
| `DeviceId` | Guid |
| `Cpu` | decimal(5,2)? |
| `MemoryUsageMb` | int? |
| `DiskFreeMb` | int? |
| `Temperature` | decimal? (chassis/chip temp) |
| `ConnectedSensorCount` | int |
| `LocalQueueDepth` | int (số reading chưa upload) |
| `IpAddress` | string(45)? |
| `SignalStrengthDbm` | int? |

**Retention:** 30 ngày.

> **ESP32 field mapping:** ESP32 không có CPU/disk theo nghĩa Linux → gửi `Cpu`=null, `DiskFreeMb`=null. Map: chip temp → `Temperature`, free heap → `MemoryUsageMb`, WiFi RSSI → `SignalStrengthDbm`, độ sâu queue NVS/LittleFS/SD → `LocalQueueDepth`. Các field nullable nên RPi (legacy) gửi đầy đủ, ESP32 gửi tập con — không cần migration khác.

#### `IotDeviceCalibration`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `DeviceId` | Guid | — |
| `SensorMetric` | enum (Voltage=1, Current=2, Temperature=3) | — |
| `OffsetValue` | decimal(8,4) | hiệu chuẩn |
| `ScaleFactor` | decimal(6,4) | default 1.0 |
| `CalibratedAt` | DateTime | — |
| `CalibratedByUserId` | Guid | Staff/Admin |
| `CalibrationStandard` | string? | "Fluke 87V multimeter" |
| `Notes` | string? | — |
| `ValidUntil` | DateTime | Calibration expiry (1 năm default) |

#### `IotFirmwareRelease`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Version` | string(20) UNIQUE | "1.3.0" |
| `DeviceModel` | string(100) | Compatible model |
| `Channel` | enum (Stable=1, Beta=2) | — |
| `FileId` | Guid (FK) | FileStorageService — .bin/.img |
| `Sha256` | string(64) | Integrity check |
| `ReleaseNotes` | string? | — |
| `IsRequired` | bool | Force update nếu true |
| `MinimumPreviousVersion` | string? | — |
| `ReleasedAt` | DateTime | — |

#### `IotFirmwareUpdateLog`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `DeviceId` | Guid | — |
| `FromVersion` | string | — |
| `ToVersion` | string | — |
| `Status` | enum (Pending=1, Downloading=2, Installing=3, Success=4, Failed=5, RolledBack=6) | — |
| `InitiatedAt` | DateTime | — |
| `CompletedAt` | DateTime? | — |
| `ErrorMessage` | string? | — |

### 52.3. Device provisioning flow

```
Step 1: Admin tạo IotDevice + APIKey (scope: sensor.ingest, device.heartbeat)
   POST /api/v1/admin/iot-devices
   → Response: { deviceCode, apiKey, provisioningQrCode }

Step 2: Technician nạp deviceCode + apiKey + WiFi + brokerUrl vào ESP32, ESP32 boot → NTP sync → provision
   $ curl -X POST https://api/api/v1/iot-devices/provision \
       -H "X-Api-Key: $KEY" \
       -d '{"deviceCode":"GW-001234","macAddress":"...","model":"ESP32-S3-N16R8","firmwareVersion":"1.0.0"}'

Step 3: Backend validate + activate device, return device-specific config
   → { configJson, ntpServer, syncIntervalSec, supportedSensors, ... }

Step 4: Gateway start sending heartbeat + readings
```

### 52.4. Heartbeat endpoint

```http
POST /api/v1/iot-devices/heartbeat
X-Api-Key: ...
X-Device-Code: GW-001234
{
  "timestamp": "2026-05-12T10:15:30Z",
  "cpu": 35.5,
  "memoryUsageMb": 512,
  "diskFreeMb": 14000,
  "connectedSensorCount": 4,
  "localQueueDepth": 0,
  "signalStrengthDbm": -65
}
```
- Backend: insert IotDeviceHeartbeat + update `IotDevice.LastSeenAt`.
- Frequency: every 60s.

### 52.5. Sensor ingest endpoint (updated)

```http
POST /api/sensor-readings/batch
X-Api-Key: ...
X-Device-Code: GW-001234
Idempotency-Key: <uuid>           # tránh duplicate khi gateway retry
Content-Type: application/json
{
  "deviceTimestamp": "2026-05-12T10:15:30Z",     # NTP-synced edge device time (ESP32 không có RTC → NTP bắt buộc)
  "readings": [
    {
      "batteryAssetSerial": "BAT-2026-001",
      "time": "2026-05-12T10:15:30Z",
      "voltage": 12.6, "current": -5.2, "temperature": 35.4, "socPercent": 78.5,
      "cycleCount": 120, "sohPercent": 94.2, "chargingState": 3,
      "bmsErrorCode": null, "sensorSourceCode": "primary", "sourceType": 2
    },
    ...
  ]
}
```
> Các field `cycleCount/sohPercent/chargingState/bmsErrorCode/sensorSourceCode/sourceType` đều optional — BMS/sensor có thì gửi, không thì để null (entity §1.3.4). MQTT (v2) gửi cùng schema payload qua topic `solar/{site}/{dev}/telemetry`.

**Validation:**
- Reject nếu `deviceTimestamp` skew > 5 phút so với server (edge device clock issue → log + alert + metric `reason=clock_drift`).
- Reject nếu reading values vô lý: voltage âm hoặc > 1000V, temperature < -50°C hoặc > 150°C, socPercent ngoài 0–100, sohPercent ngoài 0–100 (sensor lỗi → `reason=sensor_outlier`).
- `BmsErrorCode` ≤ 64 ký tự nếu có.
- Device phải `Active` + có quyền với battery (mapping trong `IotDevice.BatteryAssetIds` / `batteryMappings`).
- Apply calibration offset/scale per device + metric (`calibrated = raw*scale + offset`) trước khi insert.
- Insert into TimescaleDB batch (single SQL `COPY` cho performance).
- **Backward compatibility (MVP):** vẫn chấp nhận payload legacy dùng `items[].batteryAssetId` để demo nhanh với simulator.

### 52.6. Device offline detection (2 cơ chế)

**Cơ chế 1 — MQTT Last Will & Testament (LWT, v2 — tức thì):**
- ESP32 đăng ký LWT lúc connect broker: "nếu mất kết nối → broker tự publish `offline` lên `solar/{dev}/status`".
- Broker phát hiện rớt qua keep-alive (~60s) → `LastWillHandler` (trong `MqttBridgeBackgroundService`) nhận → mark `Status=Offline` **NGAY** (qua `IotDeviceMarkOfflineCommand`) → tạo alert + publish event.
- Nhanh hơn nhiều so với job 5 phút bên dưới.

**Cơ chế 2 — `IotDeviceOfflineDetectionBackgroundService` (every 2 phút — backup, luôn chạy kể cả khi chưa bật MQTT):**
- Scan devices `Status=Active AND LastSeenAt < now - 5min` → mark `Status=Offline`.
- Publish `IotDeviceWentOfflineEvent` → NotificationService notify Customer + Staff.
- Tạo Alert `DeviceOffline` cho mọi battery gắn với device đó (severity Warning).

> Hai cơ chế bổ trợ: LWT cho phản ứng tức thì khi có broker; job là backup cho cả HTTPS-only (P0–P2) lẫn trường hợp LWT miss.

**Phân vai notification (tránh double-notify):**
- **Customer** được báo qua **`DeviceOffline` Alert** (AnomalyType=7, Warning) đi đường BatteryAlert sẵn có (§3.4 `DeviceOffline (Customer)`).
- **Staff/ops** được báo qua **`IotDeviceWentOfflineEvent`** (§1.7) → `IotDeviceWentOfflineConsumer` ở NotificationService → đi kiểm tra device tại site.
- Cả hai dedup theo `DeviceId` trong cửa sổ offline để không spam khi device chập chờn.

### 52.7. OTA firmware update flow

#### Admin upload firmware
```
POST /api/v1/admin/iot-firmware-releases
multipart/form-data:
  version: "1.3.0"
  deviceModel: "ESP32-S3-N16R8"
  channel: stable
  file: firmware.bin
  releaseNotes: "..."
  isRequired: true
```

#### Gateway pull
```
GET /api/v1/iot-devices/firmware-check
X-Device-Code: GW-001234
→ {
    "hasUpdate": true,
    "version": "1.3.0",
    "downloadUrl": "<signed URL>",
    "sha256": "...",
    "isRequired": true,
    "releaseNotes": "..."
  }
```

#### Gateway report progress
```
PUT /api/v1/iot-devices/firmware-update-log/{id}
{ "status": "Installing" }    # → "Success" / "Failed"
```

#### Rollback
- Nếu update fail → gateway revert to previous firmware (stored locally).
- Report `Status=RolledBack`.
- Admin alert.

### 52.8. Calibration management

```
POST   /api/v1/iot-devices/{id}/calibrations              (Staff/Admin)
GET    /api/v1/iot-devices/{id}/calibrations
GET    /api/v1/iot-devices/calibrations-expiring?within=30d   (Manager)
```
- `CalibrationExpiryNotificationService` (background) alert Manager khi calibration sắp hết hạn.
- Công thức áp dụng lúc ingest: `calibrated_value = raw_value * ScaleFactor + OffsetValue`.

### 52.9. Multi-sensor per battery support
Cập nhật `SensorReading` entity (xem §1.3.4):
- Thêm `SensorSourceCode` (string(20)?) — "primary", "redundant", "external-temp".
- 1 battery có thể có nhiều readings cùng timestamp (nhiều sensor riêng).
- Query realtime: chọn "primary" làm display value, redundant để verify.

**Quy ước tag nguồn khi ESP32 đọc nhiều sensor (đồng bộ `overall.iot.md` §A5 + §1.6.6):**

| Nguồn vật lý | `SourceType` | `SensorSourceCode` |
|--------------|--------------|--------------------|
| BMS đọc qua RS485/Modbus (ESP32 relay) | `Bms` (1) | `primary` |
| INA226 (đo V/I độc lập qua I2C) | `IotGateway` (2) | `redundant` |
| DS18B20 (nhiệt thân pin qua 1-Wire) | `IotGateway` (2) | `external-temp` |

> ESP32 chỉ *relay* dữ liệu BMS → vẫn tag `SourceType=Bms` (nguồn gốc là BMS chip), KHÔNG phải `IotGateway`. Nhờ vậy **cross-source validation §1.6.6** (so `Bms` vs `IotGateway` trong cửa sổ 60s) chính là so BMS-relayed vs INA226 → phát hiện `SensorMismatch` đúng nghĩa khi 1 trong 2 nguồn đo sai.

### 52.9bis. Ambient & Environmental ingest từ ESP32

ESP32 node trong BOM (`overall.iot.md` §A6) còn gắn **SHT31** (nhiệt-ẩm môi trường), **MQ-2** (khói), **water leak**. Các nguồn này KHÔNG đi vào `sensor_readings` mà tái dùng model môi trường sẵn có (§1.3.7–§1.3.9, §1.8):
- SHT31 → `POST /api/ambient-readings/batch` → `AmbientReading` (`Source=IotSensor`, `SourceDeviceId`=DeviceCode ESP32).
- MQ-2 / water → `POST /api/environmental-incidents` → `EnvironmentalIncident` (`SmokeDetected`/`WaterLeak`) → alert Critical + `EnvironmentalIncidentDetectedEvent`.
- Dùng **cùng device API key** (scope thêm `environmental.ingest`, §52.2) — không cần global key riêng.

> Không thêm entity mới cho môi trường — chỉ nối phần cứng ESP32 vào đường ingest ambient/incident đã có. Ticket IoT-1 chỉ cần đảm bảo device key scope + firmware gọi đúng 2 endpoint này.

### 52.10. Protocol decision (ADR-016)

> Cập nhật theo pivot IoT v2 (`newiot.md`/`overall.iot.md`): edge device đổi từ **Raspberry Pi → ESP32-S3**,
> transport đổi từ "chỉ HTTPS" sang **hybrid HTTPS + MQTT**.

**Decision — hybrid 2 kênh:**

| Kênh | Dùng cho | Giai đoạn |
|------|----------|-----------|
| **HTTPS REST** | provision device, heartbeat, firmware download/OTA, flush queue tồn đọng khi mất mạng dài, admin CRUD, **MVP ingest (P0–P2)** | v1 (làm trước) |
| **MQTT** (EMQX/Mosquitto) | telemetry realtime (<100ms), heartbeat, offline tức thì qua **Last Will & Testament (LWT)**, lệnh **downlink** (đổi config/OTA trigger) | v2 (P3 — nâng cấp) |

**Lý do giữ HTTPS (v1):**
- Đơn giản, không cần broker; Idempotency-Key + Polly retry đã có; TLS đơn giản hơn MQTT-over-TLS.
- Đủ cho monitoring (latency 1–2s OK, không phải control-plane).

**Lý do thêm MQTT (v2):**
- Latency <100ms, kết nối thường trực (tiết kiệm pin/băng thông), 2 chiều (downlink command), phát hiện offline tức thì (LWT) thay vì chờ job 5 phút.

**Trade-off MQTT:** thêm hạ tầng broker (deploy/bảo mật/monitor), ESP32 vẫn phải giữ local queue + fallback HTTPS flush khi broker down (SPOF). Vì vậy MQTT là **scope mở rộng P3** — chỉ làm sau khi flow HTTPS (P0–P2) chạy ổn; nếu thiếu thời gian, HTTPS đủ cho MVP/demo.

**Edge device:** ESP32-S3 (N16R8, có PSRAM cho MQTT-over-TLS), `DeviceType=StandaloneSensor`, đọc nhiều pin qua **RS485/Modbus RTU multi-drop** (mỗi BMS 1 `unitId`). Firmware C++ (PlatformIO/Arduino) — **không** dùng lại code Python của bản RPi v1. Chi tiết phần cứng/firmware: `newiot.md`, `overall.iot.md`, `wiring-diagram.md`, `hardware-bom.csv`.

> **CAN bus (tùy chọn):** Một số BMS dùng CAN thay RS485. ESP32-S3 hỗ trợ qua TWAI + transceiver SN65HVD230 (`overall.iot.md` §A3, `wiring-diagram.md` §5). Backend KHÔNG đổi — firmware đọc CAN rồi gửi cùng contract/payload như Modbus. Capstone ưu tiên RS485; CAN chỉ làm nếu có BMS CAN thật.

### 52.11. Endpoints summary

```
# Admin
POST   /api/v1/admin/iot-devices                         (provision)
GET    /api/v1/admin/iot-devices?status=&siteId=
GET    /api/v1/admin/iot-devices/{id}
PUT    /api/v1/admin/iot-devices/{id}/config             (push config update)
DELETE /api/v1/admin/iot-devices/{id}                    (decommission)
POST   /api/v1/admin/iot-firmware-releases
GET    /api/v1/admin/iot-firmware-releases

# Device-side (X-Api-Key + X-Device-Code)
POST   /api/v1/iot-devices/provision                     (one-time)
POST   /api/v1/iot-devices/heartbeat
GET    /api/v1/iot-devices/firmware-check
PUT    /api/v1/iot-devices/firmware-update-log/{id}
POST   /api/sensor-readings/batch

# Calibration
POST   /api/v1/iot-devices/{id}/calibrations             (Staff/Admin)
GET    /api/v1/iot-devices/{id}/calibrations
GET    /api/v1/iot-devices/calibrations-expiring         (Manager)

# Monitoring
GET    /api/v1/iot-devices/{id}/heartbeat-history?from=&to=
GET    /api/v1/iot-devices/{id}/uptime-stats
```

### 52.12. Metrics

```
iot_device_heartbeats_total{device_id, status}
iot_devices_online_count gauge
iot_devices_offline_total counter
iot_sensor_readings_ingested_total{device_id}
iot_sensor_readings_rejected_total{reason=clock_drift|sensor_outlier|...}
iot_firmware_updates_total{from_version, to_version, status}
```

### 52.12bis. Boundary với Alert–Ticket Saga (§53)

> Ánh xạ rõ ràng trách nhiệm giữa IoT track và Saga để producer/consumer không chồng lấn. Bổ sung sau gap analysis với `iot/tasksprint.md`.

**IoT track (BatteryService) chịu trách nhiệm:**
- Ingest reading từ ESP32 → threshold check → tạo `Alert` → publish `BatteryAnomalyDetectedEvent` V2 (schema đầy đủ `AlertId/AnomalyType/Severity/Source/BatteryAssetId/Site` theo §53.7) vào outbox.
- Publish `IotDeviceWentOfflineEvent` khi device offline (§52.6).
- Publish `EnvironmentalIncidentDetectedEvent` khi smoke/water trigger (§1.7).

**IoT track KHÔNG chịu trách nhiệm:**
- Tạo `Ticket` trực tiếp từ event — Saga §53 orchestrate qua 8 message (`CreateTicketFromAlertCommand` → `TicketProvisionedForAlertEvent` → `LinkAlertToTicketCommand` → …).
- Link `Alert.TicketId` — Saga set qua `LinkAlertToTicketCommand` consumer trong BatteryService.
- Retry/compensation logic — thuộc Quartz endpoint + MassTransit State Machine §53.8.

**Tránh anti-pattern:** ESP32/IoT firmware KHÔNG được publish event nào ngoài `BatteryAnomalyDetectedEvent` qua outbox của BatteryService. Không tự gọi `POST /api/v1/tickets` từ device — sẽ bỏ qua Saga và tạo Ticket trùng.

> IoT planning document (`iot/tasksprint.md`) chỉ liệt kê task **emit event** ở phía producer; phần consumer Saga + state machine thuộc backend Sprint 5B `#237`. Tham chiếu §53 trước khi review PR IoT S6.

### 52.13. Tests bắt buộc
- Provisioning flow end-to-end (gen QR → curl provision → device active)
- Heartbeat → LastSeenAt updated
- Offline detection: stop heartbeat 6 phút → status auto Offline + alert created
- Clock skew rejection
- Sensor outlier rejection
- Calibration offset applied correctly
- Firmware OTA flow with rollback simulation
- **(MQTT v2)** LWT `status=offline` → device mark Offline tức thì + alert created
- **(MQTT v2)** Telemetry qua broker đi đúng `SensorReadingBatchIngestCommand` (reuse, không viết lại validate/insert/anomaly)

### 52.14. MQTT realtime channel (v2 — P3)

> Nâng cấp transport khi cần latency <100ms + 2 chiều. **Scope mở rộng** — chỉ làm sau khi HTTPS (P0–P2) ổn. Chi tiết firmware/broker: `newiot.md` §8.

**MQTT sống ở 3 nơi:**

| Nơi | Vai trò | Viết code? |
|-----|---------|-----------|
| **MQTT Broker** (EMQX/Mosquitto, Docker) | trung chuyển pub/sub | ❌ chỉ deploy + config (`infra/mqtt/`) |
| **ESP32 firmware** | publisher telemetry/heartbeat + subscriber cmd | C++ (`PubSubClient`) — codebase `firmware-esp32/` |
| **Backend bridge** | subscriber telemetry/status + publisher cmd | C# (`MQTTnet`) — `BatteryService.Infrastructure/Mqtt/` |

**Topic design:**
```
solar/{siteId}/{deviceCode}/telemetry   ← ESP32 publish reading (uplink)
solar/{deviceCode}/heartbeat            ← ESP32 publish trạng thái thiết bị
solar/{deviceCode}/status               ← Last Will: "online"/"offline" tự động
solar/{deviceCode}/cmd                  ← Backend publish lệnh xuống (downlink: đổi config, trigger OTA)
solar/{deviceCode}/cmd/ack              ← ESP32 báo đã thực thi
```

**Backend bridge** (`MqttBridgeBackgroundService`, đăng ký `AddHostedService`):
- Subscribe `solar/+/+/telemetry`, `solar/+/heartbeat`, `solar/+/status` (TLS 8883, credential `backend-bridge`).
- Telemetry → tạo scope → `SensorReadingBatchIngestCommand` (**reuse** logic validate/insert/anomaly của HTTPS, chỉ đổi "nguồn vào").
- `status=offline` → `IotDeviceMarkOfflineCommand` (§52.6 cơ chế 1).
- Downlink: `IMqttBridgePublisher.Publish(solar/{dev}/cmd, ...)`.

**Bảo mật per-device:**
- Ngoài API key (HTTPS), cấp **MQTT credential per-device** gắn với `IotDevice` + **ACL phân quyền topic per-device** ở broker (`infra/mqtt/acl.conf`) — device chỉ publish/subscribe topic của chính nó.

**Broker deploy:** `infra/mqtt/` (docker-compose EMQX/Mosquitto, `mosquitto.conf`, `acl.conf`, `certs/` cho TLS 8883). Xem `hardware-bom.csv` A13 + `overall.iot.md` A13.

**Trap (xem `newiot.md` §12):** NTP bắt buộc (ESP32 không có RTC); MQTT-over-TLS tốn RAM → dùng S3 có PSRAM; broker là SPOF → firmware vẫn giữ local queue + fallback HTTPS flush.

### 52.15. Failure modes & resilience (ESP32 + MQTT)

> Bổ sung từ "bẫy ESP32" (`newiot.md` §12) + luồng chống mất data (`overall.iot.md` §B7) — ánh xạ sang hành vi backend. Không tạo EC numbered mới (xem EC-21/24/25 §58 đã cover ingest); đây là checklist resilience cho IoT-1/P3.

| Failure mode | Hành vi mong đợi | Backend xử lý |
|--------------|------------------|---------------|
| ESP32 mất WiFi/4G kéo dài | Reading vào local queue (NVS/LittleFS/SD) + retry exponential backoff, không xóa tới khi backend 2xx | Khi mạng lại, ESP32 flush kèm `Idempotency-Key` cũ → backend dedup, không tạo bản ghi trùng (EC-21) |
| Queue flash đầy (ESP32 buffer nhỏ hơn RPi) | Chấp nhận drop data cũ nhất hoặc giảm tần suất poll | `IotDeviceHeartbeat.LocalQueueDepth` cao → dashboard cảnh báo (§9.2 #5); không có rule backend khác |
| MQTT broker down (SPOF) | Firmware fallback HTTPS flush + giữ local queue | Bridge reconnect (ManagedMqttClient); ingest HTTPS vẫn nhận bình thường |
| NTP sync fail → `deviceTimestamp` lệch | — | Reject clock skew > 5 phút (EC-24) + metric `reason=clock_drift` + `IotDevice.ClockDriftIncidentCount` |
| Sai mapping / unitId conflict (device gửi reading cho battery không thuộc `batteryMappings`) | — | Reject reading không nằm trong mapping/`BatteryAssetIds` của device + log |
| Sensor outlier (V=1200V, temp ngoài giới hạn) | — | Reject + `reason=sensor_outlier`, auto-disable device sau N outlier (EC-25) |
| LWT miss (broker chưa kịp phát) | — | Job `IotDeviceOfflineDetectionBackgroundService` 2 phút là backup (§52.6) |

---

## 52bis. IoT implementation plan

> Chi tiết triển khai phần cứng, firmware ESP32, payload mẫu, BOM mua thiết bị, sơ đồ đấu dây và runbook demo nằm ở bộ tài liệu IoT v2: [`newiot.md`](./newiot.md) (thiết kế tổng thể ESP32+MQTT), [`overall.iot.md`](./overall.iot.md) (BOM + luồng vận hành), [`wiring-diagram.md`](./wiring-diagram.md) (đấu dây + GPIO), [`hardware-bom.csv`](./hardware-bom.csv) (bảng mua sắm). Bản v1 [`iot.md`](./iot.md) (Raspberry Pi, Python) **deprecated** — giữ tham khảo logic queue/calibration/validation. Section này chỉ giữ phần backend work cần phản ánh trong master roadmap.

### 52bis.1. Current backend state

Đã có nền tảng để demo IoT MVP:
- `POST /api/sensor-readings/batch` dùng `X-Api-Key` global cho ingest.
- `SensorReading` lưu TimescaleDB hypertable.
- `BatteryAsset.LastSensorReadingAt` được cập nhật khi ingest thành công.
- `ThresholdCheckBackgroundService` quét reading mới, tạo `Alert`, dedup, outbox `BatteryAnomalyDetectedEvent` cho alert Critical.

Chưa đủ cho hệ thống IoT thật:
- Chưa có `IotDevice` / device lifecycle.
- Chưa có provision gateway.
- Chưa có heartbeat và offline detection theo device.
- Chưa có API key riêng từng device, rotate/revoke key.
- Chưa có `X-Device-Code`, `deviceTimestamp`, `Idempotency-Key` trong contract ingest production.
- Chưa có calibration, firmware OTA, ESP32 simulator/hardware runbook.
- Chưa có kênh MQTT (broker + bridge + LWT + downlink) cho realtime <100ms (v2/P3 — §52.14).

### 52bis.2. Implementation tracks

| Track | Mục tiêu | Deliverable | Map roadmap `newiot.md` |
|-------|----------|-------------|--------------------------|
| IoT MVP | Chạy được flow backend bằng simulator/laptop/ESP32 mock | Simulator/ESP32 `mock_bms` gửi batch (HTTPS) vào endpoint hiện có, dashboard thấy latest/history/alert | P0–P1 |
| IoT Backend Production | Quản lý edge device thật | `IotDevice`, provision, heartbeat, per-device auth, offline detection | P2 |
| IoT MQTT/Realtime | Streaming <100ms + 2 chiều | MQTT broker (`infra/mqtt/`) + `MqttBridgeBackgroundService` + LWT offline tức thì + downlink cmd + ACL per-device (§52.14) | **P3 (mới)** |
| IoT Hardware Pilot | Thay simulator bằng **ESP32-S3** đọc BMS qua RS485/Modbus | ESP32 + MAX485 multi-drop đọc Modbus thật (nhiều pin/unitId) và gửi production payload | P4 |
| IoT Hardening | Sẵn sàng demo/production-lite | Retry/idempotency, local queue (NVS/LittleFS/SD), calibration, metrics, OTA, runbook | P5 |

### 52bis.3. Backend tasks to add

1. **Data model**
   - `IotDevice`: device code, model, firmware, site, status, battery asset mapping, config JSON, last seen.
   - `IotDeviceHeartbeat`: hypertable 30 ngày retention.
   - `IotDeviceCalibration`: offset/scale theo metric voltage/current/temperature.
   - `IotFirmwareRelease` + `IotFirmwareUpdateLog`: OTA pull model.
   - API key hash table hoặc embedded key metadata, không lưu plaintext key.

2. **API contract**
   - Admin create/list/update/decommission IoT device.
   - Device provision one-time.
   - Device heartbeat mỗi 60 giây.
   - Sensor ingest production với `X-Device-Code`, `Idempotency-Key`, `deviceTimestamp`, readings theo `batteryAssetSerial` hoặc mapped asset.
   - Firmware check/report progress.

3. **Processing**
   - Validate device status Active trước khi ingest/heartbeat.
   - Reject clock skew > 5 phút.
   - Reject outlier sensor value rõ ràng.
   - Apply calibration offset/scale trước khi insert `sensor_readings`.
   - Update `IotDevice.LastSeenAt` và metric ingest count.
   - Giữ backward compatibility cho MVP payload dùng `batteryAssetId`.

4. **Background jobs**
   - `IotDeviceOfflineDetectionBackgroundService`: Active + `LastSeenAt < now - 5 phút` => Offline.
   - Tạo `DeviceOffline` alert cho các battery thuộc device.
   - Publish `IotDeviceWentOfflineEvent` cho NotificationService.
   - Calibration expiry notification cho Manager.

5. **Observability**
   - `iot_device_heartbeats_total`
   - `iot_devices_online_count`
   - `iot_sensor_readings_ingested_total`
   - `iot_sensor_readings_rejected_total{reason}`
   - dashboard riêng cho gateway uptime, queue depth, reject reason.

### 52bis.4. Sprint placement

| Sprint | Scope |
|--------|-------|
| Sprint 3 | Đã có ingest MVP + anomaly engine. Dùng simulator/ESP32 mock (HTTPS) để test flow end-to-end (P0–P1). |
| Sprint 5B | Release gate `#233–#241`: scope cleanup Energy/CO2 + Alert–Ticket Saga; ambient/environmental/tier-2 chỉ làm sau P0. |
| Sprint IoT-1 | Backend device management + ESP32 simulator/prototype (provision, heartbeat, per-device key, offline) — P2. Foundation cho Sprint IoT-2. |
| **Sprint IoT-2** | **Task-level refinement của backend IoT** — đầy đủ contract production, MQTT bridge, cross-source, Saga boundary, calibration, OTA, observability. 38 task `#IoT2-01..38` (Phase A–F). Owner Thắng. Đây là **single source of truth** cho mọi BE task IoT — `iot/tasksprint.md` chỉ mention dependency. |
| Sprint 7 | Hardware pilot (ESP32-S3 + RS485 multi-drop) + Grafana IoT metrics + E2E test — P4. **MQTT/Realtime (P3) làm trước trong sprint này nếu đủ nhân lực; nếu thiếu → để backlog, HTTPS đủ cho demo.** |
| Sprint 8 | IoT demo runbook, polish, failure scenario: stop ESP32 => `DeviceOffline` (LWT tức thì hoặc job 5 phút). |

### 52bis.5. Acceptance checklist

- [ ] Admin tạo device, nhận `deviceCode` + API key một lần.
- [ ] Gateway provision thành công và nhận config.
- [ ] Gateway gửi heartbeat, backend cập nhật `LastSeenAt`.
- [ ] Gateway gửi sensor batch, backend lưu TimescaleDB.
- [ ] Reading vượt threshold tạo Alert.
- [ ] Critical alert publish event cho Ticket/Notification flow.
- [ ] Dừng gateway > 5 phút tạo `DeviceOffline`.
- [ ] Gateway retry cùng `Idempotency-Key` không tạo duplicate.
- [ ] Bộ runbook IoT v2 (`newiot.md`/`overall.iot.md`/`wiring-diagram.md`/`hardware-bom.csv`) đủ để người khác setup simulator và **ESP32-S3 pilot** (nạp firmware, đấu RS485 multi-drop, cấu hình broker).
- [ ] (P3 — nếu làm MQTT) Broker `infra/mqtt/` chạy, bridge subscribe telemetry, LWT mark Offline tức thì, ACL per-device hoạt động.

---

## 53. Battery scope reduction & Alert–Ticket Saga — P0

> Quyết định ngày **10/6/2026**: BatteryService chỉ quản lý tài sản pin, telemetry phục vụ sức khỏe pin,
> anomaly/alert và monitoring môi trường. Hệ thống **không** triển khai Energy/CO2 analytics.
> Sprint 5B dùng phần effort tiết kiệm được để hoàn thiện consistency của luồng Critical Alert → Ticket.

### 53.1. Scope decision: bỏ Energy và CO2

#### In scope của BatteryService

- Battery asset/type/site/group và ownership.
- Raw telemetry: voltage, current, temperature, SOC, SOH, charging state, cycle count,
  internal resistance, cell-voltage delta và BMS error code.
- Threshold/anomaly detection, noise suppression, Alert lifecycle.
- Ambient/environmental monitoring có ảnh hưởng trực tiếp đến an toàn/sức khỏe pin.
- Liên kết `Alert.TicketId` để trace từ cảnh báo sang quy trình maintenance.

#### Out of scope chính thức

- Tích phân điện năng sạc/xả theo kWh và round-trip efficiency.
- Chi phí điện, time-of-use tariff, savings/revenue.
- CO2 emission factor, carbon saving hoặc báo cáo ESG.
- Energy session/cycle reconstruction từ raw current.
- Site/asset energy dashboard, energy/cost/carbon reports và recommendation tối ưu giờ sạc.

Không tạo `EnergyService` thay thế trong capstone. Nếu business mở lại scope sau này, phải có ADR mới,
nguồn meter đáng tin cậy và service boundary riêng; không nhét lại vào BatteryService.

> **Lưu ý IoT hardware (đồng bộ `overall.iot.md` §A14/§D + `newiot.md`):** Bộ tài liệu phần cứng IoT v2 (ESP32)
> có liệt kê module **INA226** (đo V/I độc lập) và một path demo "energy metrics" (charge/discharge kWh, cost,
> CO2) ở **Cấp 4 — tùy chọn**. Để tránh mâu thuẫn với quyết định này:
> - **INA226 / sensor đo độc lập** trong scope chỉ phục vụ **cross-source/redundant validation** (`SensorMismatch`,
>   §1.3.4 + §1.6.6) và đo telemetry sức khỏe pin — **KHÔNG** dùng để tích phân năng lượng.
> - **Energy/CO2 demo vẫn nằm NGOÀI software scope** (ADR-017, CI scope-guard §53.2bis giữ nguyên). Nếu pilot
>   muốn trình diễn energy metrics ở Cấp 4, đó là **stretch hardware-only**, phải mở ADR mới trước khi thêm bất kỳ
>   entity/report/endpoint energy nào vào backend. Không có ngoại lệ "nhét tạm để demo".

### 53.2. Inventory cần xóa hoặc không được triển khai

| Nhóm | Xóa/không tạo | Ghi chú |
|------|---------------|---------|
| Entity/table | `EnergySession`, `BatteryCycleLog`, `EnergyDailySummary`, `SiteEnergySummary`, `ElectricityRate`, `CarbonEmissionFactor` | Không tạo migration/schema |
| Background job | `EnergyCalculationBackgroundService`, `EnergyDailyAggregateBackgroundService`, `EnergyCostUpdateBackgroundService` | Không đăng ký DI |
| API | `/energy/*`, `/savings`, `/cycles` theo nghĩa energy cycle, admin electricity/carbon config | Xóa khỏi Swagger/Postman/SRS |
| Report | energy throughput, cost saving, carbon saving, top asset by energy | Không nằm trong Sprint 7 |
| Dashboard/demo | kWh, VND saving, CO2 saving, round-trip efficiency | Thay bằng SOH, alert, SLA, environmental safety |
| Site model | `Site.CapacityKw` | Xóa bằng migration `RemoveSiteCapacityKw` |

Các field `Voltage`, `Current`, `SocPercent`, `SohPercent`, `CycleCount`,
`NominalCapacityAh` và `NominalVoltage` vẫn được giữ khi chúng phục vụ
health/anomaly. Không được suy diễn thành tính năng Energy/CO2 nếu chưa có scope mới.

### 53.2bis. CI scope-guard rule (task `#233`)

Thêm step vào `.github/workflows/ci.yml` chạy trên mọi PR vào `main`/`dev`:

```yaml
- name: Energy/CO2 scope guard (ADR-017)
  run: |
    set -e
    # Search keywords trong active source (exclude historical migrations, ADR, scope-removal docs, citations)
    HITS=$(rg -n \
      --glob '!**/Migrations/202605*_AddSiteAndBatteryGroup*' \
      --glob '!docs/adrs/ADR-017-*' \
      --glob '!overall.md' \
      --glob '!.claude/**' \
      'EnergySession|EnergyDaily|EnergyKwh|ElectricityRate|CarbonEmission|Co2Saved|CostSaved|CapacityKw|/api/.*/energy|/api/.*/savings|/api/.*/cycles\b' \
      services/ shared/ || true)
    if [ -n "$HITS" ]; then
      echo "❌ Scope guard violation (ADR-017): Energy/CO2 contracts must not be added back."
      echo "$HITS"
      exit 1
    fi
    echo "✅ Scope guard clean."
```

Allow-list path: historical EF migration files giữ tên cũ; ADR markdown chứa từ "Energy" trong tiêu đề; citation/reference text (vd "Frontiers in Energy Research"). Reviewer phải đọc context khi unmask, không xóa máy móc.

### 53.2ter. Pre-commit hook + PR template (task `#233`/`#240`)

CI scope-guard (§53.2bis) chạy sau push — phát hiện chậm. Pre-commit hook bắt lỗi local trước commit, giảm CI run wasted:

```yaml
# .pre-commit-config.yaml — thêm hook mới:
- id: energy-co2-scope-guard
  name: Energy/CO2 scope guard (ADR-017)
  entry: bash -c 'rg --quiet "EnergySession|EnergyDaily|EnergyKwh|ElectricityRate|CarbonEmission|Co2Saved|CostSaved|CapacityKw|/api/.*/energy|/api/.*/savings|/api/.*/cycles\b" --glob "!Migrations/202605*_AddSiteAndBatteryGroup*" --glob "!docs/adrs/ADR-017-*" --glob "!overall.md" --glob "!.claude/**" services/ shared/ && exit 1 || exit 0'
  language: system
  pass_filenames: false
  stages: [commit]
```

`.github/PULL_REQUEST_TEMPLATE.md` (task `#240`) — thêm Saga PR checklist section:

```markdown
## Saga PR checklist (chỉ tick nếu PR thuộc Sprint 5B `#237`–`#239`)
- [ ] Đã đọc §18.2bis (34 PR review item)
- [ ] State machine: cancel cả 2 token khi tiến bước? (xem checklist mục 1-9)
- [ ] Participant: reuse path KHÔNG overwrite `OriginAlertId`? (xem checklist mục 10-17)
- [ ] Cutover: feature flag default đúng §53.9? (xem checklist mục 18-21)
- [ ] Test: ≥21 case khớp §53.10? (xem checklist mục 22-25)
- [ ] Observability: 8 metric + 2 alert rule + structured log non-PII? (xem checklist mục 26-29)
- [ ] Documentation: ADR + runbook + Mermaid + Postman? (xem checklist mục 30-34)
```

### 53.3. Scope-cleanup implementation và acceptance criteria

#### Current implementation audit ngày 10/6/2026

Repository hiện **chưa có** entity/job/API Energy hoặc CO2. Phần cần xóa thật trong code là
`Site.CapacityKw`/`TotalCapacityKw` và contract liên quan:

```text
services/BatteryService/src/BatteryService.Domain/Entities/Site.cs
services/BatteryService/src/BatteryService.Infrastructure/Persistence/Configurations/SiteConfiguration.cs
services/BatteryService/src/BatteryService.Infrastructure/Persistence/Seeders/BatteryDataSeeder.cs
services/BatteryService/src/BatteryService.Application/CQRS/Command/Site/*
services/BatteryService/src/BatteryService.Application/CQRS/Handler/Site/*
services/BatteryService/src/BatteryService.Application/DTOs/SiteDto.cs
services/BatteryService/src/BatteryService.Application/DTOs/SiteDashboardDto.cs
services/BatteryService/src/BatteryService.Application/Mapping/BatteryMapper.cs
services/BatteryService/src/BatteryService.Api/Controllers/Admin/AdminSitesController.cs
services/BatteryService/src/BatteryService.Api/Controllers/SitesController.cs
services/BatteryService/tests/**/*Site*
services/BatteryService/tests/BatteryService.UnitTests/Application/CommandValidationFullTests.cs
docs/api-battery.md
```

Các migration lịch sử đã tạo `capacity_kw` phải được giữ nguyên để bảo toàn migration chain.
Migration mới drop column và regenerate `ApplicationDbContextModelSnapshot`; không sửa tay
`20260514044305_AddSiteAndBatteryGroup*` hoặc các historical Designer file.

#### Task `#233` — documentation/contract cleanup

1. Tìm toàn repository theo các từ khóa:
   `EnergySession`, `EnergyDaily`, `EnergyKwh`, `ElectricityRate`, `CarbonEmission`,
   `Co2Saved`, `CostSaved`, `/energy`, `/savings`, `CapacityKw`.
2. Xóa plan/API/report/UI/demo/seed/test chưa triển khai; không xóa raw telemetry health.
3. Cập nhật SRS, OpenAPI/Postman và frontend/mobile types nếu đã khai báo contract.
4. Thêm ADR `Remove Energy and CO2 Analytics from BatteryService`.

#### Task `#234` — remove `Site.CapacityKw`

1. Xóa property ở Domain, EF configuration, command/query DTO, mapping, validation và seed.
2. Tạo migration `RemoveSiteCapacityKw`:
   - `Up`: drop column `sites.capacity_kw`.
   - `Down`: add nullable column trước; không tự bịa lại dữ liệu cũ.
3. Update API contract để request/response không còn `capacityKw`.
4. Chạy migration apply → rollback → re-apply trên Timescale/Postgres test container.

#### Acceptance criteria

- Chạy `rg` trên active source, tests, API docs và frontend/mobile contract với các từ khóa trên:
  không được trả về implementation/contract đang hoạt động.
- Citation/reference text như tên tạp chí “Frontiers in Energy Research” hoặc URL chứa `/energy-research`
  được phép giữ; scope guard phải review context thay vì xóa false positive máy móc.
- Historical EF migration files được phép giữ `capacity_kw`; current model snapshot và mọi file ngoài
  historical migrations không được còn `CapacityKw`/`TotalCapacityKw`.
- Không có route hoặc OpenAPI schema Energy/CO2.
- Existing battery health ingest, anomaly và dashboard tests vẫn pass.
- Migration rollback pass và không làm mất Site/BatteryGroup/BatteryAsset relationship.

### 53.4. Saga problem và business invariants

Luồng cần atomic về mặt nghiệp vụ nhưng đi qua hai database:

```text
BatteryService.Alert
    → TicketService.Ticket
    → BatteryService.Alert.TicketId
```

Không dùng distributed transaction/2PC. Dùng **orchestrated Saga + local transaction + Outbox/Inbox**
và forward recovery.

#### Current implementation gap audit ngày 10/6/2026

- TicketService đang có direct `BatteryAnomalyDetectedConsumer`; chưa có Saga state/repository.
- `Alert.TicketId` (nullable `Guid?`) **đã** tồn tại trong Battery schema/DTO; direct flow chưa callback để set field này. Sprint 5B chỉ thêm non-unique index `alerts(ticket_id) WHERE ticket_id IS NOT NULL` qua migration `AddAlertTicketLinkIndex`; **không** thêm/đổi column.
- `Ticket.OriginAlertId` chưa có unique filtered index; dedup hiện tại chưa khóa category/concurrency.
- TicketService DI đăng ký Outbox writer trước `AddMessageBus`, có nguy cơ bị direct producer override.
- Redis Inbox hiện tại ghi processed key trước business action commit.
- Shared MassTransit setup chưa cấu hình retry/redelivery hoặc durable scheduler.
- `AlertEscalationService` đang serialize lại `BatteryAnomalyDetectedEvent` dưới type
  `BatteryAnomalyEscalatedEvent`; relay vẫn publish CLR type `BatteryAnomalyDetectedEvent`, gây duplicate
  Saga-start/notification sau 5 phút dù Critical Alert đã publish event lúc tạo.

Đây là các gap bắt buộc giải quyết trong `#235–#239`; không được chỉ thêm state machine class rồi giữ
nguyên messaging foundation hiện tại.

Business invariants:

1. Mỗi `AlertId` có tối đa một Saga.
2. Mỗi `AlertId` liên kết tối đa một Ticket.
3. Retry/redelivery không tạo Ticket thứ hai.
4. Nếu đã có Ticket active cho cùng `(BatteryAssetId, Category)`, Saga reuse Ticket đó.
5. Saga chỉ `Completed` khi BatteryService xác nhận `Alert.TicketId == TicketId`.
6. Không xóa Ticket để compensation; lỗi được retry/reprocess cho đến khi link hoàn tất.
7. Mỗi Critical Alert chỉ phát Saga-start event một lần; escalation chưa-ack dùng contract riêng và
   không được start/restart Alert–Ticket Saga.

### 53.5. Ownership và state model

- Orchestrator: TicketService.
- Correlation: `CorrelationId = AlertId`.
- Persistence: MassTransit EF Saga repository trong `ticket_db`.
- Table: `alert_ticket_saga_states`.
- Initial correlation: `AlertId`; mọi response/fault/timeout dùng cùng `CorrelationId`.
- `Completed` row được giữ làm durable tombstone, không auto-delete sau terminal transition.
- Ticket lifecycle vẫn do `TicketStateMachine` quản lý; Saga không thay đổi status nghiệp vụ Ticket.

| State | Ý nghĩa |
|-------|---------|
| `Initial` | Chưa xử lý anomaly event |
| `TicketRequested` | Đã gửi command tạo/reuse Ticket |
| `TicketProvisioned` | Đã tạo/reuse và xác định được `TicketId` |
| `AlertLinkRequested` | Đã gửi command link Alert |
| `Completed` | BatteryService xác nhận link thành công |
| `Failed` | Hết retry/timeout hoặc business rejection cần operator xử lý |

### 53.6. Saga persistence schema

`AlertTicketSagaState` tối thiểu:

Đây là MassTransit persistence model trong Infrastructure, không phải Domain entity; deliberate exception:
không kế thừa `AuditableEntity`, dùng các timestamp/state field riêng và MassTransit concurrency contract.

| Field | Type | Constraint/usage |
|-------|------|------------------|
| `CorrelationId` | Guid | PK, bằng `AlertId` |
| `CurrentState` | string | MassTransit state |
| `AlertId` | Guid | unique/business key |
| `BatteryAssetId` | Guid | trace/dedup |
| `CustomerId` | Guid | payload snapshot |
| `AssetSerialNumber` | string? | initial-event snapshot; null cho reconciliation |
| `AnomalyType` | int? | wire value snapshot; null cho reconciliation |
| `Severity` | int? | wire value snapshot; null cho reconciliation |
| `ThresholdValue` | decimal? | initial-event snapshot; null cho reconciliation |
| `ActualValue` | decimal? | initial-event snapshot; null cho reconciliation |
| `Unit` | string? | initial-event snapshot; null cho reconciliation |
| `DetectedAt` | DateTime? | initial-event snapshot; null cho reconciliation |
| `TicketId` | Guid? | set sau create/reuse |
| `TicketCode` | string? | ops/debug |
| `CreatedNewTicket` | bool? | audit |
| `TicketAttemptCount` | int | budget/audit bước create-or-reuse |
| `AlertLinkAttemptCount` | int | budget/audit bước link Alert |
| `ManualReprocessCount` | int | số lần operator reprocess |
| `LastReprocessedBy` | Guid? | audit actor |
| `LastReprocessReason` | string? | sanitized operator reason |
| `FailedStep` | string? | create-ticket / link-alert |
| `FailureCode` | string? | machine-readable |
| `LastError` | string? | sanitize, không chứa secret |
| `LastAttemptAtUtc` | DateTime? | retry/reprocess audit |
| `StartedAtUtc` | DateTime | latency metric |
| `UpdatedAtUtc` | DateTime | stuck detection |
| `CompletedAtUtc` | DateTime? | terminal success |
| `StepTimeoutTokenId` | Guid? | timeout của attempt đang chờ participant response |
| `RetryTokenId` | Guid? | delayed retry command; không dùng chung với timeout token |
| `RowVersion` | uint | PostgreSQL `xmin` optimistic concurrency token |

Migration `AddAlertTicketSagaFoundation` đồng thời thêm:

- PK/unique trên Saga `CorrelationId`.
- Index `(CurrentState, UpdatedAtUtc)` cho stuck scan.
- Unique filtered index
  `tickets.origin_alert_id WHERE origin_alert_id IS NOT NULL AND is_deleted = false`.
- Non-unique index BatteryService `alerts(ticket_id) WHERE ticket_id IS NOT NULL`.
- Index `tickets(battery_asset_id, category, status)` để query reuse.
- Partial unique guard cho auto-ticket active trên `(battery_asset_id, category)` với
  `origin = AutoFromAlert AND is_deleted = false AND status IN (active statuses)`.
  Predicate phải liệt kê rõ giá trị `New`, `Open`, `Assigned`, `InProgress`, `WaitingCustomer`,
  `WaitingParts`, `WaitingOnsiteSchedule`, `Resolved`, `Escalated`, `Incident`, `Approved`; không dùng
  range enum.
  Nếu EF migration khó biểu diễn predicate, dùng migration SQL có test apply/rollback.
- Trước khi tạo hai unique index, deployment preflight phải query duplicate `origin_alert_id` và
  duplicate active `(battery_asset_id, category)`. Chọn Ticket canonical, link các Alert liên quan qua
  reconciliation và mark duplicate `IsDeleted=true` kèm audit/migration log theo runbook trước khi apply
  constraint; migration không được fail giữa rollout vì dữ liệu cũ chưa dọn.

### 53.7. Contracts và transaction boundary

Contracts nằm trong `SharedContracts`, chỉ dùng primitive/string và version-safe enum values:

```text
BatteryAnomalyDetectedEvent
CreateTicketFromAlertCommand
TicketProvisionedForAlertEvent
TicketProvisionForAlertRejectedEvent
LinkAlertToTicketCommand
ReconcileAlertTicketSagaCommand
AlertLinkedToTicketEvent
AlertLinkToTicketRejectedEvent
AlertTicketSagaFailedEvent
```

Mapping wire `AnomalyType` sang `TicketCategoryEnum` phải deterministic và có unit test:

| AnomalyType wire value | Battery anomaly (§1.3.6) | Ticket category |
|------------------------|--------------------------|-----------------|
| 1 | Overheat | `Overheat` |
| 2 | Overvoltage | `Charging` |
| 3 | Undervoltage | `NoPower` |
| 4 | LowSoc | `Performance` |
| 5 | RapidDischarge | `Performance` |
| 6 | AbnormalCharging | `Charging` |
| 7 | DeviceOffline | `Other` |
| 8 | SohDegradation | `Performance` |
| 9 | HighInternalResistance | `Performance` |
| 10 | CellImbalance | `Performance` |
| 11 | HighAmbientTemp | `Environment` |
| 12 | HighHumidity | `Environment` |
| 13 | HighTempHumidityCombo | `Environment` |
| 14 | EnvironmentalIncident | `Environment` |
| 15 | SensorMismatch | `Other` |
| unknown | Forward-compatible fallback | `Other` + warning metric |

> **Wire value = `AnomalyTypeEnum` integer ở §1.3.6** — không phải custom Saga numbering. Khi mở rộng `AnomalyTypeEnum` (vd thêm value 16), wire value tự động extend; subscriber handle unknown an toàn cho rolling deploy.
> Wire values 9–15 ambient/environmental/tier-2 chỉ active khi entity tương ứng (AmbientReading, EnvironmentalIncident, BMS extension) đã được kích hoạt.
> Producer (BatteryService) chỉ publish wire value khi entity tương ứng đã được kích hoạt; subscriber luôn handle unknown an toàn.

Không giữ behavior hiện tại gán mọi auto-ticket thành `Repair`; nếu không, guard
`(BatteryAssetId, Category)` sẽ reuse sai ticket giữa các nhóm anomaly.

Transaction boundary bắt buộc:

- BatteryService: `Alert` + `BatteryAnomalyDetectedEvent` cùng transaction/Outbox.
- HTTP/background handlers tiếp tục dùng custom `IIntegrationEventOutboxWriter`.
- Saga và participant consumers dùng MassTransit EF Consumer Outbox/Inbox trên service DbContext:
  Ticket/Activity + response event, hoặc Alert update + response event, commit atomically với consumed message.
- Saga state transition và outgoing command commit atomically qua TicketService EF Consumer Outbox.
- Rejection response cũng phải đi qua cùng Outbox; participant không được nuốt validation error.

Không inject một interface producer mà lúc runtime resolve thành direct RabbitMQ publisher trong business handler.
DI phải tách rõ `IIntegrationEventOutboxWriter` và transport publisher của relay.

### 53.8. Idempotency, retry, timeout và compensation

#### Idempotency

- Inbox/durable consumer record chỉ Completed sau business commit.
- `CreateTicketFromAlertConsumer` lookup `OriginAlertId` trên Ticket `is_deleted=false` trước, sau đó
  mới lookup active Ticket theo BR-02.
- Concurrent Alert khác nhau nhưng cùng asset/category được serialize bởi partial unique guard; consumer bắt
  unique violation, reload Ticket winner và trả `CreatedNew=false`.
- Chỉ xử lý PostgreSQL `23505` khi constraint name đúng một trong các unique guard đã biết. Transaction
  lỗi phải rollback và `DbContext` phải clear/dispose trước khi query Ticket winner bằng scope/context
  mới; lỗi constraint khác phải rethrow, không biến thành reuse thành công.
- Khi reuse, giữ nguyên `Ticket.OriginAlertId` của Alert đầu tiên. Quan hệ đầy đủ many-alerts-to-one-ticket
  nằm ở `Alert.TicketId`; không overwrite OriginAlertId bằng Alert mới và không gán OriginAlertId
  cho Ticket manual được reuse.
- `LinkAlertToTicketConsumer`:
  - `TicketId == null`: set value.
  - `TicketId == command.TicketId`: no-op success và publish confirmation idempotently.
  - `TicketId` khác: reject với conflict, không overwrite âm thầm.
- Unique constraints là bắt buộc; Redis Inbox không phải lớp bảo vệ duy nhất.

#### Retry/timeout

- Participant endpoint immediate retry tối đa 3 lần cho transient DB/network lỗi ngắn hạn.
- Saga schedule tối đa 3 lần gửi lại command với delay 5s, 30s, 2m; tính cả lần gửi đầu là tối đa
  4 attempt cho từng step. Hết budget chuyển `Failed`, không loop vô hạn.
- Dùng persistent Quartz scheduler endpoint trong TicketService vì RabbitMQ image hiện tại không có
  delayed-message plugin. Quartz schema/config phải được version-control và test restart recovery.
  - NuGet: `MassTransit.Quartz` + `Quartz.AspNetCore` + `Quartz.Serialization.Json`.
  - Schema: 11 bảng `qrtz_*` chạy qua migration `AddQuartzPersistenceSchema` trên `ticket_db`
    (dùng official `tables_postgres.sql` của Quartz.NET; không sinh từ EF model snapshot).
  - Cluster mode bật để hai instance TicketService không double-fire schedule.
  - Job store cấu hình `quartz.jobStore.driverDelegateType=Quartz.Impl.AdoJobStore.PostgreSQLDelegate`.
- `StepTimeoutTokenId` và `RetryTokenId` tách riêng. Mỗi loại chỉ có một token active; trước khi
  retry/reschedule và ngay khi nhận success phải unschedule token không còn hợp lệ. Late timeout/retry
  được state guard bỏ qua và ghi metric.
- Saga timeout mặc định: 10 phút cho mỗi bước; timeout chuyển `Failed` và publish failure event.
- Late response sau timeout phải được ghi log/metric; manual reprocess có thể tiếp tục từ bước thiếu,
  không tạo Saga/Ticket mới.

#### Compensation

Không hard-delete Ticket đã tạo khi bước link Alert lỗi. Compensation theo **forward recovery**:
retry link, rồi operator reprocess. Xóa Ticket sẽ làm mất audit/activity và có thể phá SLA workflow.

### 53.9. Implementation + merge order trong Sprint 5B

**Implementation order (= PR merge order vào `dev`):**

| # | Task | Phụ thuộc PR | Owner | Có thể parallel với |
|---|------|--------------|-------|---------------------|
| 1 | `#233` Battery scope cleanup + ADR-017 | — | Thắng | `#234` |
| 2 | `#234` Remove `Site.CapacityKw` | — | Thắng | `#233`, `#235` |
| 3 | `#235` Messaging hardening + Quartz schema | — | Thắng | `#233`, `#234`, `#241` |
| 4 | `#241` AuthService permission seed | — (khác DB) | Thắng | `#233`–`#238` (khác service) |
| 5 | `#236` Saga contracts + Saga foundation migration | `#235` (cần EF Consumer Outbox tables) | Thắng | — |
| 6 | `#237` Saga state machine + persistence | `#236` (cần contracts + schema) | Thắng | — |
| 7 | `#238` Participants + cutover flags | `#237` (cần Saga endpoint registered) | Thắng | — |
| 8 | `#239` Test + metrics + admin endpoints + runbook | `#238` (cần participant active) | Thắng | — |
| 9 | `#240` Documentation sync | `#239` (cần code stable) | Thắng | — |

**Merge rule cho Sprint 5B:**
- PR phải merge theo thứ tự trên — `#237` không được merge vào `dev` trước `#236`.
- Nếu 2 PR cùng dependency-level (vd `#233` + `#234` + `#235` + `#241`), merge theo thứ tự PR approval; mỗi merge xong, các PR còn lại rebase trên `dev` mới.
- Hai Saga PR back-to-back (`#237`→`#238`→`#239`) phải có integration test pass trên feature branch trước khi merge — KHÔNG để Saga code half-implemented stay trên `dev` quá 1 ngày.

**Không bắt đầu `#237` trước khi `#235` và `#236` pass integration tests** — nếu không Saga chỉ che đi reliability bug của messaging foundation.

**Không enable `AlertTicketSagaEnabled=true` trên `dev` env trước khi `#239` merge** — chỉ test local trong feature branch của `#237/#238`.

#### Deployment/cutover từ direct consumer hiện tại

**Feature flag default trong `appsettings.json` cho Sprint 5B deploy đầu:**
```json
{
  "AlertTicket": {
    "AlertTicketDispatchEnabled": true,    // BatteryService giữ direct flow tới khi maintenance window
    "AlertTicketSagaEnabled": false,       // TicketService chưa enable Saga endpoint khi deploy đầu
    "AlertTicketReconciliationEnabled": false  // bật cuối cutover khi smoke test pass
  }
}
```
Sau cutover thành công 3 flag chuyển `true/true/true`. Production config (vault/secret manager) override appsettings.

1. Deploy contract backward-compatible và feature flag `AlertTicketDispatchEnabled`; mặc định vẫn giữ
   direct flow, Saga chưa active.
2. Mở maintenance window ngắn: set `AlertTicketDispatchEnabled=false` để Critical Alert mới vẫn được
   lưu cùng Outbox nhưng chưa dispatch sang Ticket flow.
3. Drain queue direct `BatteryAnomalyDetectedConsumer` đến depth/unacked = 0, sau đó stop endpoint cũ.
4. Chạy duplicate preflight/canonicalization, rồi apply Saga schema, unique indexes, EF Consumer
   Outbox/Inbox schema và Quartz schema. Không có direct consumer chạy trong khoảng này.
5. Deploy Battery link participant, `CreateTicketFromAlertConsumer`, Saga và scheduler với
   `AlertTicketSagaEnabled=false`; verify health/endpoint topology.
6. Enable Saga, sau đó enable lại dispatch và inject một Critical Alert smoke test; xác nhận Saga
   `Completed`. Không để direct consumer và Saga cùng active.
7. Chạy reconciliation cho dữ liệu cũ:
   - TicketService đọc Ticket
     `Origin=AutoFromAlert AND OriginAlertId IS NOT NULL AND IsDeleted=false`.
   - `Send` `ReconcileAlertTicketSagaCommand` tới Saga endpoint theo từng
     `(AlertId, BatteryAssetId, CustomerId, TicketId, TicketCode)`.
   - Saga khởi tạo ở `TicketProvisioned`, chỉ thực hiện bước link Alert; không tạo Ticket mới.
8. Chỉ xóa queue cũ sau khi smoke test, held Outbox backlog và reconciliation đều pass. Rollback trước
   bước 6 giữ dispatch off; rollback sau bước 6 phải disable Saga trước, không bật lại hai consumer song song.

### 53.10. Test matrix bắt buộc

| Case | Expected |
|------|----------|
| Happy path | 1 Alert, 1 Ticket, `Alert.TicketId` set, Saga Completed |
| Existing active Ticket | Reuse Ticket, `CreatedNew=false`, Saga Completed |
| Duplicate start event | 1 Saga, không thêm Ticket |
| Duplicate start sau Saga Completed | Completed tombstone ignore/no-op, không tạo Saga/Ticket mới và không đẩy `_skipped` |
| Alert chưa ack quá 5 phút | Chỉ publish escalation event cho Notification; không start Saga lần hai |
| Dispatch flag off | Anomaly Outbox row vẫn pending; event type khác vẫn relay, không bị batch starvation |
| Concurrent duplicate command | Unique constraint chặn duplicate; consumer trả Ticket đã có |
| Concurrent different Alerts, same asset/category | Chỉ 1 active auto-ticket; cả hai Alert link cùng Ticket |
| Ticket DB transient failure | Retry/redelivery rồi tiếp tục |
| BatteryService unavailable | Saga giữ state, retry link; không mất TicketId |
| Timeout | Saga Failed + metric/failure event |
| Late response | Không corrupt state; có audit log |
| Conflicting Alert.TicketId | Không overwrite; Saga Failed để operator xử lý |
| Business rejection | Rejection event có code/reason; Saga Failed hoặc retry theo `IsRetryable` |
| Retryable rejection lặp lại | Tối đa 4 attempt/step, chỉ một schedule token active, sau đó Saga Failed |
| `Fault<T>` sau endpoint retry | Correlate bằng `Fault<T>.Message.CorrelationId`, update đúng Saga |
| Manual reprocess | Resume từ failed step và kết thúc Completed |
| Existing direct-consumer Ticket | Reconciliation chỉ link Alert, không tạo Ticket mới |
| Service restart khi đang chờ timeout | Persistent scheduler khôi phục timeout/redelivery |
| Broker restart giữa commit/publish | Outbox relay publish sau restart |
| Consumer crash trước/ sau commit | Redelivery idempotent, không duplicate |
| Cutover direct → Saga | Held anomaly backlog được xử lý đúng một lần sau enable, không có hai consumer active |
| Feature flag mis-config (cả `AlertTicketDispatchEnabled` + direct consumer cùng on) | Unique constraint chặn duplicate; tối đa 1 Ticket được tạo và 1 Saga; log warning để ops phát hiện mis-config |
| Reconciliation chạy 2 lần | Saga tombstone/Ticket reuse path đảm bảo idempotent, không tạo Saga/Ticket thừa |

Test levels:

- Unit: state machine transition/fault/timeout và participant handlers.
- Integration: Postgres/Timescale + MassTransit TestHarness.
- E2E: RabbitMQ thật trong Docker Compose, kill/restart service tại từng transaction boundary.

### 53.11. Observability và operations

Metrics:

```text
alert_ticket_saga_started_total
alert_ticket_saga_completed_total
alert_ticket_saga_failed_total{step,reason}
alert_ticket_saga_duration_seconds
alert_ticket_saga_stuck_count{state}
alert_ticket_ticket_reused_total
outbox_unprocessed_count{service}
inbox_processing_failed_total{consumer}
```

Tracing dùng cùng `CorrelationId/AlertId` qua mọi message. Log structured fields:
`CorrelationId`, `AlertId`, `TicketId`, `CurrentState`, `MessageId`, `TicketAttemptCount`,
`AlertLinkAttemptCount`; không log payload chứa PII.

Admin/internal API:

```text
GET  /api/v1/admin/sagas/alert-ticket?state=&olderThan=&page=
GET  /api/v1/admin/sagas/alert-ticket/{alertId}
POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess
```

`reprocess` yêu cầu permission admin, idempotency key và AuditLog; chỉ resume step lỗi, không reset/xóa history.
Alert rule: Saga non-terminal không update > 10 phút hoặc `Failed` count > 0 trong 5 phút.

### 53.12. Definition of Done

- [ ] Không còn Energy/CO2 entity, API, report, UI contract, seed hoặc demo claim.
- [ ] `Site.CapacityKw` được remove bằng migration có rollback test.
- [ ] Shared contracts không reference domain enum assembly.
- [ ] Business handlers ghi Outbox; DI không resolve nhầm direct producer.
- [ ] Saga/participant endpoints dùng EF Consumer Outbox/Inbox; consumed message chỉ Completed sau commit.
- [ ] Unique constraints cho Saga `AlertId`, `Ticket.OriginAlertId` và active auto-ticket asset/category đã apply.
- [ ] Preflight duplicate data và runbook chọn Ticket canonical đã chạy trước khi apply unique index.
- [ ] `Completed` Saga được giữ làm tombstone; duplicate event sau completion không tạo Saga mới.
- [ ] Duplicate/late message ở `Completed`/`Failed` được handle explicit, không phát sinh `_skipped` ngoài dự kiến.
- [ ] Retry có budget hữu hạn; `StepTimeoutTokenId`/`RetryTokenId` cũ được unschedule khi Saga tiến bước hoặc Completed.
- [ ] Commands dùng `Send` tới endpoint name cố định; events dùng `Publish`; queue/error queue quan sát được.
- [ ] `AlertEscalationService` không còn map type giả về `BatteryAnomalyDetectedEvent`; escalation chưa-ack không start Saga lần hai.
- [ ] Direct `BatteryAnomalyDetectedConsumer` không còn được register; queue cũ đã drain/decommission.
- [ ] Auto-ticket cũ được reconciliation để `Alert.TicketId` không còn null khi Ticket tồn tại.
- [ ] Happy path và toàn bộ failure matrix §53.10 pass.
- [ ] `Alert.TicketId` được xác nhận trước khi Saga Completed.
- [ ] Admin xem/reprocess Saga Failed được và có AuditLog.
- [ ] Metrics/tracing/dashboard/alert rule hoạt động trên Docker Compose.
- [ ] ADR-017 (Energy/CO2 removal) và ADR-018 (Saga forward recovery) merged vào `docs/adrs/`; ADR registry §40 + summary count §67 updated.
- [ ] NotificationService consume `BatteryAlertEscalationRequestedEvent` + `AlertTicketSagaFailedEvent`; `NotificationTypeEnum` 16, 17 active; template registered §15.2.
- [ ] Quartz `qrtz_*` schema deployed qua `AddQuartzPersistenceSchema`; cluster mode enable; restart-recovery integration test pass.
- [ ] `PermissionCodes.TicketSagaView` cấp cho Manager read-only; `TicketSagaReprocess` chỉ Admin.
- [ ] AuthService seed permission `ticket.saga.view` + `ticket.saga.reprocess` đã apply (task `#241`); JWT mới có claim đúng; `PermissionsChangedEvent` được publish để các service invalidate cache.
- [ ] Mapping wire-value §53.7 và auto-derivation §2.4 đồng bộ; unit test mapping cho 15 wire value + unknown.
- [ ] Swagger/Postman không còn endpoint Energy/CO2/`capacityKw`; có Saga admin endpoints + permission scope chính xác.
- [ ] Runbook `saga-failed.md` + `saga-stuck.md` committed vào `docs/runbooks/`.
- [ ] Runbook + Swagger/Postman/SRS cập nhật đồng bộ.

---

## 54. Production Deployment (K8s + Helm) — P1

> Docker compose đủ dev. Production demo nên có K8s nếu muốn show "production-ready".

### 54.1. Decision
- **Local dev:** docker compose (giữ nguyên).
- **Demo / staging:** Kubernetes (k3s / minikube / managed).
- **Helm charts:** một chart umbrella chứa tất cả services.

> ⚠️ **Sprint risk — K8s không nằm trong backlog Sprint 7/8 ban đầu.** §54 đánh nhãn P1 và checklist "K8s Helm charts per service" trong §60 nhưng Sprint 7 (Reports + Gateway + Observability) và Sprint 8 (demo prep + bug bash) đều full. Quyết định:
> 1. Thêm task `[Optional P1] Deploy staging K8s` vào **cuối Sprint 7** (xem §17 Sprint 7) — best effort, không block các task P0 khác.
> 2. **Fallback:** nếu Sprint 7 không kịp → Sprint 8 demo bằng `docker compose -f docker-compose.staging.yml` trên 1 VM. Điểm chức năng capstone không bị ảnh hưởng (rubric không bắt buộc K8s).
> 3. Helm chart per service vẫn được viết Sprint 7 dù không deploy — để có artifact cho hồ sơ "production-ready".
>
> Team **PHẢI** quyết định fallback hay full-K8s **trước ngày 17/8/2026** (giữa Sprint 7) để Sprint 8 không bị bất ngờ.

### 54.2. Cấu trúc deploy folder

```
deploy/
├── helm/
│   ├── umbrella/                        # Parent chart deploy all services
│   │   ├── Chart.yaml
│   │   ├── values.yaml                  # default
│   │   ├── values.dev.yaml
│   │   ├── values.staging.yaml
│   │   ├── values.prod.yaml
│   │   └── templates/
│   │       ├── namespace.yaml
│   │       └── _helpers.tpl
│   ├── auth-service/
│   │   ├── Chart.yaml
│   │   ├── values.yaml
│   │   └── templates/
│   │       ├── deployment.yaml
│   │       ├── service.yaml
│   │       ├── configmap.yaml
│   │       ├── secret.yaml
│   │       ├── hpa.yaml                 # Horizontal Pod Autoscaler
│   │       ├── pdb.yaml                 # Pod Disruption Budget
│   │       ├── networkpolicy.yaml
│   │       ├── serviceaccount.yaml
│   │       ├── servicemonitor.yaml      # Prometheus operator
│   │       └── _helpers.tpl
│   ├── battery-service/
│   ├── ticket-service/
│   ├── notification-service/
│   ├── file-storage-service/
│   ├── api-gateway/
│   │   └── templates/
│   │       ├── ingress.yaml             # gateway-only ingress
│   │       └── (same as above)
│   └── ai-module/
├── k8s-raw/                             # Non-helm manifests (optional)
│   ├── postgres-statefulset.yaml
│   ├── redis-statefulset.yaml
│   ├── rabbitmq-cluster.yaml
│   └── minio-deployment.yaml
├── argocd/                              # GitOps (optional)
│   └── application.yaml
└── scripts/
    ├── deploy-staging.sh
    ├── deploy-prod.sh
    └── rollback.sh
```

### 54.3. Deployment manifest template (sample auth-service)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: {{ include "auth-service.fullname" . }}
  labels:
    {{- include "auth-service.labels" . | nindent 4 }}
spec:
  replicas: {{ .Values.replicaCount }}
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0       # Zero downtime
  selector:
    matchLabels:
      {{- include "auth-service.selectorLabels" . | nindent 6 }}
  template:
    metadata:
      labels:
        {{- include "auth-service.selectorLabels" . | nindent 8 }}
      annotations:
        prometheus.io/scrape: "true"
        prometheus.io/port: "8080"
        prometheus.io/path: "/metrics"
    spec:
      serviceAccountName: {{ include "auth-service.serviceAccountName" . }}
      initContainers:
        - name: wait-for-postgres
          image: busybox:1.36
          command: ['sh', '-c', 'until nc -z postgres 5432; do sleep 1; done']
        - name: migrate
          image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
          command: ["dotnet", "AuthService.Api.dll", "migrate"]
          envFrom:
            - secretRef:
                name: {{ include "auth-service.fullname" . }}-secrets
            - configMapRef:
                name: {{ include "auth-service.fullname" . }}-config
      containers:
        - name: app
          image: "{{ .Values.image.repository }}:{{ .Values.image.tag }}"
          ports:
            - name: http
              containerPort: 8080
              protocol: TCP
            - name: metrics
              containerPort: 8080
          envFrom:
            - secretRef:
                name: {{ include "auth-service.fullname" . }}-secrets
            - configMapRef:
                name: {{ include "auth-service.fullname" . }}-config
          startupProbe:
            httpGet: { path: /health/startup, port: http }
            failureThreshold: 30
            periodSeconds: 5
          livenessProbe:
            httpGet: { path: /health/live, port: http }
            initialDelaySeconds: 30
            periodSeconds: 10
            failureThreshold: 3
          readinessProbe:
            httpGet: { path: /health/ready, port: http }
            initialDelaySeconds: 10
            periodSeconds: 5
            failureThreshold: 3
          resources:
            requests:
              cpu: 100m
              memory: 256Mi
            limits:
              cpu: 500m
              memory: 512Mi
          securityContext:
            runAsNonRoot: true
            runAsUser: 1000
            readOnlyRootFilesystem: true
            allowPrivilegeEscalation: false
            capabilities:
              drop: ["ALL"]
```

### 54.4. Resource sizing per service

| Service | Replicas | CPU req | CPU limit | Memory req | Memory limit |
|---------|----------|---------|-----------|------------|--------------|
| AuthService | 2 | 100m | 500m | 256Mi | 512Mi |
| BatteryService | 2 | 200m | 1000m | 512Mi | 1Gi |
| TicketService | 2 | 200m | 800m | 512Mi | 1Gi |
| NotificationService | 2 | 100m | 500m | 256Mi | 512Mi |
| FileStorageService | 1 | 100m | 500m | 256Mi | 512Mi |
| ApiGateway | 2 | 100m | 500m | 256Mi | 512Mi |
| AI Module | 1 (CPU) / 1 (GPU) | 500m | 2000m | 1Gi | 2Gi |
| Postgres (TimescaleDB) | 1 (PVC) | 500m | 2000m | 1Gi | 4Gi |
| Redis | 1 | 100m | 500m | 256Mi | 512Mi |
| RabbitMQ | 1 (or 3 cluster) | 200m | 1000m | 512Mi | 1Gi |

### 54.5. HPA rules

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: battery-service-hpa
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: battery-service
  minReplicas: 2
  maxReplicas: 10
  metrics:
    - type: Resource
      resource:
        name: cpu
        target: { type: Utilization, averageUtilization: 70 }
    - type: Resource
      resource:
        name: memory
        target: { type: Utilization, averageUtilization: 80 }
    - type: Pods
      pods:
        metric: { name: rabbitmq_queue_depth }
        target: { type: AverageValue, averageValue: "500" }
```

### 54.6. PDB (Pod Disruption Budget)

```yaml
apiVersion: policy/v1
kind: PodDisruptionBudget
metadata: { name: battery-service-pdb }
spec:
  minAvailable: 1
  selector:
    matchLabels:
      app.kubernetes.io/name: battery-service
```

### 54.7. Ingress (gateway only)

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: api-gateway-ingress
  annotations:
    nginx.ingress.kubernetes.io/rate-limit-rpm: "300"
    cert-manager.io/cluster-issuer: letsencrypt-prod
spec:
  ingressClassName: nginx
  tls:
    - hosts: ["api.gsu26se55.com"]
      secretName: api-tls
  rules:
    - host: api.gsu26se55.com
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: api-gateway
                port:
                  number: 80
```

### 54.8. Secrets management

**Local dev:** Đọc `.env` (đã có `EnvFileLoader`).
**K8s staging:** Kubernetes Secrets (sealed-secrets cho commit-safe).
**K8s prod:** External Secrets Operator → AWS Secrets Manager / Azure Key Vault.

```yaml
apiVersion: external-secrets.io/v1beta1
kind: ExternalSecret
metadata: { name: auth-service-secrets }
spec:
  refreshInterval: 1h
  secretStoreRef:
    name: aws-secrets-manager
    kind: ClusterSecretStore
  target:
    name: auth-service-secrets
  dataFrom:
    - extract:
        key: prod/gsu26se55/auth-service
```

### 54.9. Zero-downtime migration (Expand → Migrate data → Contract)

Pattern khi đổi schema không downtime:

**Phase 1 — Expand (deploy version N+1):**
- Migration add column nullable.
- Code N writes both old + new column.
- Code N+1 reads new column with fallback.
- Deploy N+1.

**Phase 2 — Backfill:**
- Background script populate new column for existing rows.

**Phase 3 — Contract (deploy version N+2):**
- Code N+2 reads + writes new column only.
- Migration drop old column.
- Deploy N+2.

→ Document trong runbook khi cần migration phức tạp.

### 54.10. Deployment scripts

```bash
# deploy/scripts/deploy-staging.sh
#!/bin/bash
set -euo pipefail

NAMESPACE="${NAMESPACE:-staging}"
IMAGE_TAG="${IMAGE_TAG:-$(git rev-parse --short HEAD)}"

helm upgrade --install gsu26se55 ./deploy/helm/umbrella \
    --namespace $NAMESPACE \
    --create-namespace \
    --values ./deploy/helm/umbrella/values.staging.yaml \
    --set global.image.tag=$IMAGE_TAG \
    --atomic \
    --timeout 10m

# Smoke test sau deploy
kubectl rollout status deployment/auth-service -n $NAMESPACE
curl -fsSL https://staging-api.gsu26se55.com/health/ready
```

### 54.11. CI/CD pipeline updates

GitHub Actions step thêm:
```yaml
- name: Build & push Docker images
  run: |
    docker buildx build --platform linux/amd64,linux/arm64 \
      -t ${REGISTRY}/auth-service:${{ github.sha }} \
      --push services/AuthService/src/AuthService.Api

- name: Deploy to staging
  if: github.ref == 'refs/heads/main'
  run: ./deploy/scripts/deploy-staging.sh
  env:
    IMAGE_TAG: ${{ github.sha }}
```

### 54.12. Monitoring stack on K8s
- Prometheus Operator + ServiceMonitor CRD per service.
- Grafana via Helm chart.
- Loki via Helm chart.
- Tempo via Helm chart.
- AlertManager → PagerDuty/Slack webhook.

---

## 55. Mobile/Web App Management — P1

### 55.1. App version compatibility

#### Entity `AppVersion`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Platform` | enum (iOS=1, Android=2, Web=3) | — |
| `Version` | string(20) | "1.2.3" |
| `BuildNumber` | int | — |
| `MinSupportedVersion` | string | Server reject nếu app < này |
| `LatestVersion` | string | UI suggest update nếu < này |
| `ForceUpdate` | bool | If true → app blocks usage |
| `ReleaseNotes` | string? | Multi-language JSON |
| `StoreUrl` | string | App Store / Play Store URL |
| `ReleasedAt` | DateTime | — |

#### Endpoint
```
GET /api/v1/app-config?platform=ios&version=1.2.3
→ {
    "compatible": true,
    "shouldUpdate": false,
    "forceUpdate": false,
    "latestVersion": "1.2.5",
    "minSupportedVersion": "1.0.0",
    "releaseNotes": "...",
    "storeUrl": "https://apps.apple.com/..."
  }
```

Mobile call this **on every app launch**. Backend response in < 50ms (cached 5 phút).

### 55.2. Feature flags

#### Entity `FeatureFlag`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Key` | string UNIQUE | "ai-prediction-enabled", "new-ticket-flow" |
| `Description` | string? | — |
| `IsEnabled` | bool | Global toggle |
| `EnabledForRoles` | string[] | Specific roles |
| `EnabledForUserIds` | Guid[] | Specific users (beta testing) |
| `EnabledForAppVersionMin` | string? | Only versions >= |
| `RolloutPercent` | int (0-100) | Gradual rollout |

#### Evaluation logic
```csharp
public bool IsEnabled(string flagKey, ICurrentUser user) {
    var flag = await _cache.Get($"flag:{flagKey}");
    if (!flag.IsEnabled) return false;
    if (flag.EnabledForUserIds.Contains(user.Id)) return true;
    if (flag.EnabledForRoles.Any(r => user.Role == r)) {
        if (flag.RolloutPercent < 100) {
            var hash = Hash($"{user.Id}:{flagKey}") % 100;
            return hash < flag.RolloutPercent;
        }
        return true;
    }
    return false;
}
```

#### Endpoints
```
GET    /api/v1/feature-flags                            (Admin all)
PUT    /api/v1/feature-flags/{key}                      (Admin)
GET    /api/v1/feature-flags/my                         (any user — only flags applicable to them)
```

### 55.3. Maintenance broadcast

#### Entity `MaintenanceAnnouncement`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Title` | string | "Bảo trì hệ thống 2-4h sáng 15/5" |
| `Body` | string | Markdown |
| `Severity` | enum (Info=1, Warning=2, Critical=3) | — |
| `StartAt` | DateTime | Show banner from |
| `EndAt` | DateTime | Hide banner |
| `MaintenanceWindowStart` | DateTime? | Actual downtime |
| `MaintenanceWindowEnd` | DateTime? | — |
| `AffectedServices` | string[] | "All", "Battery only" |
| `ShowToRoles` | string[] | All roles by default |
| `IsActive` | bool | Admin toggle |

#### Endpoints
```
POST   /api/v1/admin/maintenance-announcements          (Admin)
GET    /api/v1/admin/maintenance-announcements
PUT    /api/v1/admin/maintenance-announcements/{id}
DELETE /api/v1/admin/maintenance-announcements/{id}

GET    /api/v1/maintenance-announcements/active         (any role — get current banner)
```

App display banner on top of screen during active period.

### 55.4. In-app announcement (release notes / promotion)

#### Entity `InAppAnnouncement`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | — |
| `Title` | string | — |
| `Body` | string | — |
| `ImageFileId` | Guid? | — |
| `TargetRoles` | string[] | — |
| `StartAt`, `EndAt` | DateTime | Active period |
| `PinAtTop` | bool | — |
| `ActionUrl` | string? | Deep link nếu user click |

#### Endpoints
```
POST   /api/v1/admin/announcements                      (Admin)
GET    /api/v1/announcements/active                     (user — list current)
PUT    /api/v1/announcements/{id}/dismiss               (user — hide from feed)
```

### 55.5. Crash reporting integration

Option A: Sentry SaaS — Mobile/Web tự gửi tới Sentry. BE chỉ cần endpoint xem stats.

Option B: Self-host crash collector:
```
POST   /api/v1/crashes
{
  "platform": "ios",
  "version": "1.2.3",
  "errorType": "TypeError",
  "message": "Cannot read property 'x' of undefined",
  "stackTrace": "...",
  "deviceInfo": { "model": "iPhone15,3", "osVersion": "17.4" },
  "userId": "...",
  "occurredAt": "..."
}

GET    /api/v1/admin/crashes?platform=&version=&errorType=
GET    /api/v1/admin/crashes/stats                      # group by errorType
```

> **Đề xuất:** Option A (Sentry free tier) cho scope capstone.

### 55.6. Analytics events tracking

Mobile/Web gửi user action events:
```
POST   /api/v1/analytics/events
{
  "events": [
    { "name": "view_battery_realtime", "properties": {"assetId":"..."}, "timestamp": "..." },
    { "name": "create_ticket", "properties": {"category":"Charging"}, "timestamp": "..." }
  ]
}
```

Lưu in `AnalyticsEvent` table:
| Field | Type |
|-------|------|
| Time | DateTime (hypertable) |
| UserId | Guid? |
| SessionId | string |
| EventName | string |
| Properties | jsonb |
| Platform | enum |
| AppVersion | string |

Analytics endpoints:
```
GET /api/v1/admin/analytics/event-counts?from=&to=&groupBy=name
GET /api/v1/admin/analytics/funnel?steps=login,view_battery,create_ticket
GET /api/v1/admin/analytics/active-users-daily
```

### 55.7. Push tokens cleanup

Background service `PushTokenCleanupBackgroundService` (weekly):
- Mark token expired if `LastSeenAt < now - 90d`.
- Remove permanently after 180d.

### 55.8. App rating prompt logic (backend coordination)

```
GET /api/v1/users/me/should-prompt-rating
→ { "shouldPrompt": true, "reason": "completed_5_tickets_no_issues" }
```

Backend logic:
- Customer rate ticket ≥ 4 sao 3 lần liên tiếp → eligible.
- Đã prompt < 90d trước → skip.

### 55.9. Mobile-optimized endpoints

Một số endpoint trả về payload nhẹ hơn cho mobile:
```
GET    /api/battery-assets/{id}/realtime-lite        (chỉ V, I, T, SOC — bỏ metadata)
GET    /api/v1/tickets/me/lite                          (preview list, không includes)
```

---

## 56. Demo & Presentation Deliverables — P0

> Capstone không chỉ là code — hội đồng đánh giá cả presentation. Phần này là **deliverable chuẩn bị cho demo day**.

### 56.1. Demo script (`docs/demo/demo-script.md`)

Cấu trúc đề xuất (90 phút demo):

```markdown
# Demo Script — GSU26SE55 Solar Battery Monitor

## Pre-demo setup (5 phút)
- Reset demo data: `./tools/reset-demo.sh`
- Verify: docker compose ps shows all green
- Open browser tabs: Web Admin, Web Manager, Web Staff
- Open phones: Customer A app, Customer B app
- Login mỗi account, tab Grafana ready

## Scene 1 — Admin onboarding (10 phút)
1. Admin login Web
2. Show Audit log (đã có activity từ trước)
3. Create new BatteryType "LiFePO4 24V 200Ah" with threshold config
4. Create Site "Solar Farm Long An"
5. Bulk import 10 batteries from CSV → show import result
6. Generate QR code for asset BAT-DEMO-001
7. **Switch to Customer A phone:** scan QR → asset claimed

## Scene 2 — Realtime monitoring (10 phút)
1. Customer A app: view dashboard, see 1 active battery
2. Switch to chart: voltage/current/temp/SOC realtime
3. Show battery health: SOH, charging state, active alerts và 30-day health trend
4. Run sensor simulator script that emits normal data
5. View Grafana battery health dashboard

## Scene 3 — Critical alert + auto ticket (15 phút)
1. Run `./tools/inject-anomaly.sh BAT-DEMO-001 overheat 75`
2. Within 30s: Customer A phone push notification 🔴
3. AI classifies as "Failed" with 92% confidence
4. Alert–Ticket Saga creates/reuses Ticket và cập nhật `Alert.TicketId`
5. Show Saga trace `Started → TicketProvisioned → AlertLinkRequested → Completed`
6. Manager web: queue refreshes (SSE live update)
7. Manager assigns Staff Long with priority P1
8. SLA timer starts (4h countdown banner)
9. Staff Long mobile: push notification "Bạn được giao ticket TKT-2605-0042"

## Scene 4 — Staff workflow (15 phút)
1. Staff opens ticket detail
2. KB suggest: "Overheat troubleshooting article"
3. Staff comments asking Customer for photo
4. Hold ticket: WaitingCustomer → SLA pause
5. Customer A: reply with photo (file upload)
6. SLA resumes
7. Staff resolves with maintenance summary
8. Switch back to Manager: approves ticket

## Scene 5 — Customer rate + reopen (5 phút)
1. Customer rates 5 stars + comment
2. Show ticket CLOSED in activity timeline

## Scene 6 — SLA breach scenario (10 phút) — pre-recorded simulation
1. Show pre-prepared P1 ticket near SLA breach (90% mark)
2. Push warning sent to Staff + Manager
3. Time travel to breach → ESCALATED state, Admin notified
4. Manager reassigns to senior staff

## Scene 7 — Reports & analytics (10 phút)
1. Manager opens reports dashboard
2. SLA compliance per priority
3. CSAT trend
4. Top reopen issues
5. Battery health by type
6. Export PDF report

## Scene 8 — AI feedback loop (5 phút)
1. After Scene 4 resolve → Staff confirms AI prediction was correct
2. Admin opens AI dashboard: 85% accuracy, last retrain 30 days ago
3. Trigger export training data for retrain

## Scene 9 — Operational visibility (5 phút)
1. Open Grafana: business + system dashboards (+ **IoT Device Monitoring** dashboard §9.2 #5: online/offline, ingest/reject, queue depth)
2. Show traces in Tempo for ticket flow
3. Show maintenance announcement publish flow
4. **IoT device demo:** show provisioned device `GW-DEMO-001` (heartbeat LastSeenAt cập nhật), rồi **dừng ESP32/simulator > 5 phút → `DeviceOffline` alert** tự sinh + notification (LWT tức thì nếu bật MQTT). Đây là điểm "đúng chất IoT" của hệ thống.

## Q&A buffer (15 phút)
```

### 56.2. Demo data reset script

`tools/reset-demo.sh`:
```bash
#!/bin/bash
set -euo pipefail

# Stop services
docker compose --env-file .env.Docker down

# Drop & recreate databases
docker compose --env-file .env.Docker up -d postgres
sleep 5
for db in auth_db file_storage_db battery_db ticket_db notification_db; do
    docker exec solar-postgres psql -U postgres -c "DROP DATABASE IF EXISTS $db; CREATE DATABASE $db;"
done

# Bring up all services
docker compose --env-file .env.Docker up -d

# Wait for migrations
sleep 30

# Run seed script
./tools/seed.sh

# Inject demo scenarios (pre-prepared tickets in various states)
./tools/seed-demo-scenarios.sh

echo "Demo environment ready ✅"
```

### 56.3. Demo scenarios pre-prepared

`tools/seed-demo-scenarios.sh` creates:
- 3 tickets in different states (OPEN, IN_PROGRESS, RESOLVED awaiting approval)
- 1 ticket near SLA breach (90% mark) for breach demo
- 1 ticket already breached + escalated (for showing audit)
- 1 ticket reopened (showing BR-06/BR-07 flow)
- 7 days of sensor history with 2 pre-injected anomalies
- 50 audit log entries
- 5 sample KB articles published
- 10 sample notifications across roles
- 1 Saga state=`Failed` (FailedStep=link-alert) để demo admin reprocess flow → Completed (Sprint 5B, §53)
- 1 Saga state=`Completed` + 2 Alert link cùng 1 Ticket (reuse path, §53.6/53.8)

### 56.4. Demo helper scripts

```bash
# tools/inject-anomaly.sh — push abnormal sensor reading
./tools/inject-anomaly.sh <asset-serial> <anomaly-type> <value>

# tools/fast-forward-sla.sh — simulate time skip for SLA breach demo
./tools/fast-forward-sla.sh <ticket-id> <minutes>

# tools/trigger-incident.sh — declare incident across multiple tickets
./tools/trigger-incident.sh

# Sprint 5B — Saga demo helpers (xem §53)
# tools/simulate-saga-failure.sh — stop BatteryService, inject anomaly, show Saga Failed → restart → admin reprocess → Completed
./tools/simulate-saga-failure.sh <asset-serial>

# tools/inspect-saga.sh — query Saga state machine for a given AlertId
./tools/inspect-saga.sh <alert-id>
```

### 56.5. Sample data realistic

Seed dùng tên thật Việt Nam:
- Customer: Nguyễn Văn An, Trần Thị Bình, Lê Minh Châu, ...
- Staff: Phạm Hữu Long, Hoàng Thị Mai, ...
- Manager: Đỗ Quốc Tuấn
- Asset locations: "Solar Farm Long An", "Trang trại Bình Thuận"
- Realistic timestamps spread over 3 tháng

### 56.6. Architecture poster

`docs/demo/architecture-poster.pdf` (A1 print):
- System overview diagram (4 microservices + AI module + clients + **IoT edge layer**: ESP32-S3 ↔ RS485/Modbus BMS ↔ hybrid HTTPS/MQTT broker → BatteryService — xem §52.1)
- Tech stack icons (bao gồm ESP32-S3, EMQX/Mosquitto, Modbus/RS485)
- Key metrics: 50+ entities, 220+ endpoints, 30+ integration events + 8 Saga contracts, ≥80% coverage, Saga state machine (5 state), 10 runbook
- (tùy chọn) Sơ đồ IoT data flow 1 dòng: BMS → ESP32 → MQTT/HTTPS → TimescaleDB → anomaly → alert/ticket/notification
- Sponsor logos / team photo

Source file: `docs/demo/architecture-poster.drawio` (commit + export PDF on each major update).

### 56.7. Demo video (5-10 phút intro)

`docs/demo/intro-video.md` — script:
- 30s: problem statement (solar battery monitoring nightmare)
- 60s: solution overview
- 3 phút: feature highlights with screen recording
- 60s: technical architecture
- 30s: team intro + GitHub link

Use **OBS Studio** for recording, host on YouTube unlisted.

### 56.8. Postman collection

`docs/api/postman-collection.json`:
- All 220+ endpoints grouped by service (đồng bộ §67 stats; bao gồm Saga admin endpoints + IoT device management + ambient/environmental)
- Environment file with `{{baseUrl}}, {{authToken}}, {{customerId}}, ...`
- Pre-request script auto-refresh token
- Example responses saved

```bash
# Generate from OpenAPI
openapi2postmanv2 -s docs/api/openapi.json -o docs/api/postman-collection.json
```

### 56.9. API documentation hosting

- Swagger UI aggregated at gateway: `https://api.gsu26se55.com/swagger`
- Redoc alternative: `https://api.gsu26se55.com/redoc`
- Or host on GitHub Pages: `https://gsu26se55.github.io/api-docs/`

### 56.10. Q&A preparation document

`docs/demo/qa-preparation.md`:
30+ câu hỏi hội đồng thường hỏi + câu trả lời chuẩn bị, ví dụ:

```markdown
## Q1: Tại sao chọn microservices thay vì monolith?
A: ...

## Q2: SLA pause loophole — Staff có thể gaming không?
A: Đã có guard BR-04-Extended (xem §33). Max pause minutes per priority, ...

## Q3: AI fail thì sao?
A: Hybrid pipeline (xem §30.2) — threshold detector vẫn chạy độc lập, ...

## Q4: Bảo mật đường truyền IoT edge device (ESP32) → backend?
A: TLS (HTTPS + MQTT-over-TLS 8883), **API key per-device chỉ lưu hash + rotate/revoke**, `X-Device-Code` + device phải Active, anti-spoofing (reject clock skew/outlier, device chỉ gửi cho battery trong mapping), MQTT có credential + **ACL topic per-device**, rate limit 60 req/phút/device, OTA verify SHA-256 + signed URL — xem §14.8, §52.10/§52.14.

## Q5: Scale 10,000 batteries thì sao?
A: HPA (§54.5), TimescaleDB hypertable partition by time, ...

## Q6: Tại sao TimescaleDB thay vì InfluxDB?
A: ADR-006 — ...

## Q7: Tại sao Outbox?
A: ADR-004 — ...

(continue 30+ questions)
```

### 56.11. Test demo dry-run

- 1 tuần trước demo: full dry-run với mock hội đồng (mentor).
- Time all scenes.
- Identify weak transitions.
- Backup recording if live demo fails.

### 56.12. Tech setup checklist demo day

- [ ] Laptop có power + adapter
- [ ] HDMI/USB-C dongle
- [ ] Spare laptop pre-loaded same env
- [ ] Internet backup: 4G hotspot
- [ ] Local-first mode (docker compose, không cần internet)
- [ ] Backup video recorded
- [ ] Mobile phones charged 100%
- [ ] Test projector resolution
- [ ] Browser bookmarks pre-set
- [ ] Demo data seeded
- [ ] Reset script tested
- [ ] **Power outage contingency**: Powerbank cho laptop + phone (≥ 20,000 mAh); biết vị trí breaker phòng demo
- [ ] **Pre-demo health check** (30 phút trước demo): chạy `tools/smoke-test.sh` verify mọi service xanh + Saga endpoint reachable
- [ ] **Saga state pre-warmed**: 1-2 Saga đã ở state `Completed` + 1 đã ở `Failed` (để demo cả happy path + recovery — xem §56.3 seed)
- [ ] **Mid-demo recovery script**: Nếu service crash, `tools/restart-stack.sh <service-name>` restart cụ thể service đó < 30s
- [ ] **Audio check**: Microphone test (nếu recording dry-run hoặc remote audience)
- [ ] **Time zone check**: Laptop clock đồng bộ NTP — log timestamps không lệch

### 56.13. Slide deck (PowerPoint/Google Slides)

Max 20 slides:
1. Title + team
2. Problem
3. Stakeholders (4 role personas)
4. Solution overview
5. Architecture diagram
6. Tech stack
7. Key features 1: AI integration
8. Key features 2: ITIL ticket lifecycle
9. Key features 3: Real-time alert + SLA
10. Demo flow overview
11. (Live demo placeholder)
12. Technical highlights: Ticket state machine + **Alert–Ticket Saga (ADR-018)** + state diagram (Mermaid)
13. Technical highlights: TimescaleDB + AI
14. Technical highlights: Observability + SRE practice (SLO + error budget + 10 runbook)
15. Test coverage + quality gates (Saga test ≥21 case + restart-recovery)
16. Sprint timeline (8 sprint + Sprint 5B P0 release gate)
17. Challenges + how solved (vd: distributed consistency → Saga forward recovery)
18. Future work
19. Team contributions
20. Q&A

### 56.14. Post-Sprint 8 timeline (Demo prep → Defense)

Sprint 8 kết thúc 6/9/2026. Defense thường 3-4 tuần sau (cuối tháng 9 hoặc đầu tháng 10 tùy lịch khoa).

| Tuần | Mốc | Owner | Deliverable |
|------|-----|-------|-------------|
| 7/9 — 13/9 | Bug fix critical + polish slide | Toàn team | Slide deck v1 + demo script v1 |
| 14/9 — 20/9 | Mentor review (GVHD Trương Long) + dry-run lần 1 | Leader + Mentor | Feedback feedback + fix list |
| 21/9 — 27/9 | Dry-run lần 2 + 3 (mock defense) + final slide polish | Toàn team | Slide deck v2 + backup recording |
| 28/9 — Defense day | Tech setup check (§56.12) + final rehearsal | Toàn team | Standby cho defense |

**Buffer dependencies:**
- Mentor schedule: Leader xác nhận GVHD lịch review trước Sprint 8 kết thúc.
- **Backup mentor**: Nếu GVHD Trương Long busy/sick, Leader contact 1 trong các thầy khoa làm second-opinion review (đặt trước Sprint 8).
- School calendar: Vietnam school year start ~5/9; team members có thể bận lớp học chính khóa từ 7/9. Mitigation: dry-run sau giờ học hoặc cuối tuần.
- **Backup defense slot**: Nếu defense slot chính bị reschedule (mentor/khoa lý do), Leader đăng ký slot dự phòng 1 tuần sau.
- Defense slot: Khoa thường thông báo trước 2 tuần. Leader theo dõi và coordinate.

**Code freeze policy post-Sprint 8:**
- Bug fix critical only (SEV1/SEV2 theo §40.4 severity matrix).
- KHÔNG thêm feature mới.
- KHÔNG refactor lớn.
- Mọi commit phải có Leader approve.
- Tag `v1.0-defense-ready` khi đóng băng cuối cùng.

### 56.15. External dependency register

Tổng hợp toàn bộ external services capstone depends on. Mỗi item có **quota** + **fallback** + **liên hệ trước**:

| Service | Tier | Quota | Risk demo day | Fallback | Liên hệ trước |
|---------|------|-------|---------------|---------|---------------|
| OpenMeteo (weather API) | Free | 10,000 calls/day (100 sites OK xem §1.10) | Quota hết khi demo Sprint 8 | Cache 1h + mock client trong demo seed | Không cần liên hệ — free tier |
| Expo Push Notification | Free | Unlimited cho dev sandbox | Token expire hoặc sandbox throttle | In-app notification fallback (xem R-09) | Tạo Expo account + verify trước Sprint 6 |
| SendGrid / Mailgun (email) | Free | 100 emails/day SendGrid; 1000 Mailgun | Demo gửi nhiều email → quota hết | EmailService log + mock template render | Đăng ký account + verify domain Sprint 6 đầu |
| SMS provider (eSMS/Twilio) | Trial credit | Twilio $15 credit, eSMS 100 SMS trial | Demo OTP nhiều → hết credit | OTP fallback in-app (xem Q-14) | Đăng ký + nạp credit Sprint 6 |
| Google OAuth | Free | 100 users/day cho test app | Demo nhiều user login → block | Manual create account fallback | Setup OAuth client Sprint 1 |
| Sentry (error tracking) | Free tier | 5k errors/month | Demo error rate spike → quota | Self-host hoặc disable Sentry trong demo | Sprint 7 trước observability deploy |
| Statuspage.io (status page) | Free | 1 page + 10 components | Không có | Static HTML self-host | Sprint 7 |
| NASA Ames dataset | Public | N/A | Download fail | Mirror trên team Drive | Sprint 2 (AI training) |
| Hardware partner (ESP32-S3 pilot + RS485/BMS) | TBD | N/A | Partner delay hardware delivery | Pure simulator/ESP32 `mock_bms` demo (đã có) | **Sprint 5B kết thúc** (xem §17 Sprint IoT-1) |
| RabbitMQ delayed-message plugin | N/A — không dùng | N/A | N/A | Đã chọn Quartz alternative (xem §53.8) | Không cần |
| Cloudflare (HTTPS proxy) | Free | Unlimited | DDoS during demo | Direct origin fallback | Sprint 7 nếu deploy K8s |

**Action item Leader:**
- Đăng ký tất cả service trước Sprint 5B kết thúc (40 ngày tới).
- Lưu credentials vào team password manager (1Password free tier hoặc Bitwarden).
- Document setup steps vào `docs/external-deps-setup.md`.
- Monitor quota hàng tuần qua Grafana — set alert "quota > 80% used".

---

## 57. AI advanced — deployment, retrain, batching — P1

### 57.1. Model deployment CI/CD

`ai-module/.github/workflows/deploy-model.yml`:
- Trigger: manual / on push to `models/v*` tag
- Step 1: Run validation tests (MAE < 2%, F1 > 0.80).
- Step 2: Build container with new weights.
- Step 3: Deploy to staging.
- Step 4: Canary test (5% traffic) for 24h.
- Step 5: Promote to prod.
- Rollback: keep previous 2 versions, manual rollback.

### 57.2. Retraining trigger criteria

Auto-trigger retrain job when:
- Drift detected: prediction distribution shift > 20% (KL divergence) week-over-week.
- Accuracy degradation: feedback rate true_positive < 75% over 100 samples.
- Schedule: every 3 months minimum.

Endpoint: `POST /api/v1/admin/ai/retrain-trigger` (Admin manual).

### 57.3. Inference batching

```python
# FastAPI batch endpoint
@router.post("/predict/soh/batch")
async def predict_batch(req: BatchPredictRequest):
    """
    Input: list of {asset_id, readings: [...]}
    Output: list of {asset_id, soh_percent, confidence}
    Batch up to 32 items → single GPU forward pass
    """
```

Backend BatteryService modify `SohPredictionBackgroundService`:
- Collect up to 32 pending predictions trong 100ms window → single batch call.
- Latency tăng nhẹ but throughput 32× higher.

### 57.4. Model versioning storage

```
ai-module/models/
├── weights/
│   ├── current → symlink to v1.2/
│   ├── v1.0/
│   │   ├── scaler.pkl
│   │   ├── soh_lstm.pth
│   │   └── isolation_forest.pkl
│   ├── v1.1/
│   └── v1.2/
└── metadata/
    └── versions.json   # registry with metrics, training data, hash
```

### 57.5. Multi-replica AI scaling

K8s HPA cho AI module:
- Scale based on `ai_inference_queue_depth` metric.
- Scale 1 → 5 replicas dynamically.
- Shared model loaded in memory per replica (read-only).

### 57.6. Drift detection background job

`AiDriftDetectionBackgroundService` weekly:
- Compare predicted distribution last 7 days vs previous 7 days.
- KL divergence calc.
- If > 0.2 → publish `AiModelDriftDetectedEvent` → notify AI team.

### 57.7. A/B test framework

Feature flag `AI_MODEL_VERSION_VARIANT`:
- 90% traffic → v1.1 (control)
- 10% traffic → v1.2 (variant)
- Compare metrics 2 weeks.
- Promote winner.

### 57.8. Endpoints
```
GET    /api/v1/admin/ai/models                          (list versions + metrics)
PUT    /api/v1/admin/ai/models/{version}/promote        (Admin)
POST   /api/v1/admin/ai/models/{version}/rollback
POST   /api/v1/admin/ai/retrain-trigger
GET    /api/v1/admin/ai/drift-status
GET    /api/v1/admin/ai/inference-stats?from=&to=
```

---

## 58. Edge cases extension (EC-21..EC-34) — P0

Bổ sung 14 edge cases vào §38 matrix:

| # | Edge case | Rule giải quyết | Implementation |
|---|-----------|----------------|----------------|
| EC-21 | Concurrent IoT data ingest từ cùng device (request gửi 2 lần do retry) | Idempotency-Key dedup | `IdempotencyKeyMiddleware` đã có |
| EC-22 | Customer dưới 18 (children data protection) | Block registration; require legal guardian | Validation `RegisterCommand`: nếu `birthDate < 18 years ago` → reject |
| EC-23 | Cross-timezone Customer (Mỹ) vs Staff (VN) | Tất cả timestamp lưu UTC, FE convert theo `AccountProfile.TimeZone` | Đã có TimeZone in NotificationPreference, đồng bộ thêm vào AccountProfile |
| EC-24 | Device clock drift > 5 phút | Reject reading + tăng `IotDevice.ClockDriftIncidentCount` | Validation trong sensor batch ingest |
| EC-25 | Sensor reading vô lý (V=1200V) | Reject + log `SensorOutlier` event + auto-disable device sau N outlier | Validation + threshold per metric |
| EC-26 | Customer sở hữu 1000+ asset (enterprise) | Pagination mandatory + cache aggressively | Query handler always paginate, max 100 |
| EC-27 | Ticket spam (1 customer tạo 50 ticket/ngày) | Rate limit per user 10 ticket/day, alert Manager | RateLimiter middleware |
| EC-28 | Attachment có malware | Virus scan (ClamAV integration) trước khi store | FileStorageService middleware |
| EC-29 | Customer thay đổi email khi có active sessions | Revoke all sessions sau email change + require re-login | `ChangeEmailCommandHandler` revoke RT_* keys |
| EC-30 | Daylight Saving Time transition (mặc dù VN không DST) | UTC mọi nơi, FE convert | Convention enforced |
| EC-31 | `BatteryAnomalyDetectedEvent` bị redeliver sau Saga Completed | Giữ Completed tombstone theo `AlertId`, ignore idempotently | Saga repository không auto-delete Completed row |
| EC-32 | Hai Critical Alert khác nhau cùng asset/category đến đồng thời | Chỉ 1 active auto-ticket, cả hai Alert link cùng Ticket | Partial unique guard + reload winner |
| EC-33 | Ticket đã commit nhưng BatteryService down trước link | Không xóa Ticket; retry/reprocess bước link | Forward recovery + durable scheduler |
| EC-34 | Callback success/rejection đến trễ sau timeout | Không corrupt state; log metric và chỉ reprocess step hợp lệ | State guard + correlation + audit |

---

## 59. GDPR + security additional — P1

### 59.1. Children's data protection
- Đăng ký yêu cầu `birthDate`.
- < 18 tuổi → require email parent → parent consent qua link.
- `Account.IsMinor` (bool) → giới hạn data collected, no marketing email.

### 59.2. Cookie consent banner (BE provides config)
```
GET /api/v1/legal/cookie-consent-config
→ {
    "categories": [
      {"key":"essential","name":"Thiết yếu","required":true},
      {"key":"analytics","name":"Phân tích","required":false},
      {"key":"marketing","name":"Marketing","required":false}
    ],
    "privacyPolicyUrl": "...",
    "lastUpdated": "..."
  }

POST /api/v1/auth/me/cookie-consent
{ "essential": true, "analytics": true, "marketing": false }
```

### 59.3. Privacy Impact Assessment (PIA)
`docs/legal/privacy-impact-assessment.md`:
- Data types collected per role
- Legal basis (consent, contract, legitimate interest)
- Data flow diagram
- Risks identified + mitigations
- Reviewed annually

### 59.4. Data Processing Agreement (DPA)
`docs/legal/data-processing-agreements/`:
- Expo (push notification) — sub-processor
- SendGrid / Mailgun (email) — sub-processor
- AWS / Azure (hosting) — sub-processor
- Each has signed DPA template.

### 59.5. Cross-border data transfer
- Vietnam: data subject to local regulations.
- If hosting outside VN → SCC (Standard Contractual Clauses) or equivalent.
- Document in PIA.

### 59.6. WAF rules
Application-level WAF (since Cloudflare costs):
```csharp
// Middleware WafMiddleware:
- Block requests với SQL injection pattern: regex `(?i)(union\s+select|drop\s+table|--\s|;\s*--)`
- Block XSS pattern: regex `<script|onerror=|javascript:`
- Block path traversal: `\.\./`
- Block null byte: `%00`
- Log + return 400 Bad Request
```

### 59.7. DDoS protection minimal
- ApiGateway rate limit per IP (đã có §10.2).
- IP block list cho IPs hit rate limit > 5 times in 1h.
- Cloudflare Free tier for prod (HTTPS proxy → DDoS mitigation).

### 59.8. Security incident response playbook
`docs/security/incident-response.md`:
1. **Detect** — alert from monitoring.
2. **Contain** — isolate affected service, revoke compromised credentials.
3. **Eradicate** — patch vulnerability.
4. **Recover** — restore from backup, validate.
5. **Lessons learned** — postmortem within 7 days.
6. **Notify** — affected users within 72h (GDPR), regulator if required.

### 59.9. Bug bounty / responsible disclosure
`SECURITY.md` in repo root:
- Email security@gsu26se55.com for vulnerability reports.
- 90-day disclosure timeline.
- Hall of fame for valid reports.
- No legal action for good-faith research.

### 59.10. Penetration test schedule
- Sprint 8: internal pen test (team member from different role).
- Post-launch (if production): hire external pen test firm annually.

---

## 60. Internal admin tools — P2

### 60.1. User impersonation (debug only)

#### Flow
- Admin opens user detail in admin panel.
- Click "Impersonate" → generates short-lived (15 min) JWT with `impersonatedBy` claim.
- Admin uses this token to debug user issue.
- All actions logged with both original Admin + impersonated user.

#### Endpoint
```
POST   /api/v1/admin/users/{id}/impersonate            (Admin only)
→ { "token": "...", "expiresAt": "...", "warning": "All actions audited" }

GET    /api/v1/admin/impersonation-sessions             (list active)
POST   /api/v1/admin/impersonation-sessions/{id}/end
```

#### Audit
- Entity `ImpersonationSession`: `Id, AdminUserId, ImpersonatedUserId, StartedAt, EndedAt, Reason, ActionsCount`
- Every action while impersonating → audit log with `Impersonator: <adminId>`.

### 60.2. Feature flag management UI endpoints
Đã có §55.2. Admin UI:
- List all flags + status.
- Toggle on/off.
- Set rollout percent slider.
- Add/remove user from allowlist.

### 60.3. Database read-only console (very limited)

```
POST   /api/v1/admin/db-query                           (Admin — heavily restricted)
{ "service": "battery", "query": "SELECT COUNT(*) FROM battery_assets WHERE..." }
```
**Safeguards:**
- Whitelist tables (no `users.password_hash`).
- Block keywords: UPDATE/DELETE/INSERT/DROP/ALTER.
- Read-only DB user.
- Audit every query.
- Timeout 10s.
- Result max 1000 rows.

> Cẩn thận! Rủi ro cao — chỉ implement nếu có time + good test.

### 60.4. Background job monitor

Endpoint trả status các background services:
```
GET /api/v1/admin/background-jobs
→ [
    {
      "name": "OutboxRelayBackgroundService",
      "service": "BatteryService",
      "lastRunAt": "...",
      "lastRunDurationMs": 234,
      "status": "Running",
      "queueDepth": 12,
      "successCount24h": 1245,
      "failureCount24h": 3
    },
    ...
  ]
```

### 60.4bis. Saga admin UI (Sprint 5B — xem §53.10/53.11)

Web Admin portal phải implement page `/admin/sagas/alert-ticket` với:
- **List view:** filter `state=Failed/All`, sort by `UpdatedAtUtc DESC`, paginate 50/page; cột hiển thị `AlertId`, `CurrentState`, `TicketId` (nếu có), `FailedStep`, `LastAttemptAtUtc`, `ManualReprocessCount`, action `Reprocess`.
- **Detail view:** state machine timeline (`StartedAtUtc` → state transitions → `CompletedAtUtc`/`FailedAtUtc`), payload snapshot (`AlertId`, `BatteryAssetId`, `CustomerId` → resolve qua AuthService read-model), error context (`LastError`, `FailureCode`), audit (`LastReprocessedBy`, `LastReprocessReason`, `ManualReprocessCount`).
- **Reprocess action:** require confirmation modal + reason input (sanitize XSS), generate UUID v4 client-side cho `Idempotency-Key`, gửi `POST /api/v1/admin/sagas/alert-ticket/{alertId}/reprocess` với header `Idempotency-Key`.
- **Permission:** check `ticket.saga.view` (page visible) + `ticket.saga.reprocess` (button enable). Manager chỉ thấy read-only.
- **Realtime update:** SSE channel `saga-state-changed` (xem §34) hoặc poll 10s.

FE owner: Trí + Minh (Sprint 6 hoặc Sprint 7 — không phải Sprint 5B vì 5B chỉ làm BE). BE Sprint 5B chỉ implement endpoint, FE consume sau.

### 60.5. Cache management

```
GET    /api/v1/admin/cache/stats                        (hit rate, key count)
DELETE /api/v1/admin/cache/keys?pattern=user:*          (Admin invalidate)
```

### 60.6. Session viewer

```
GET    /api/v1/admin/sessions?userId=&active=true
POST   /api/v1/admin/sessions/{id}/revoke
POST   /api/v1/admin/users/{id}/revoke-all-sessions     (force logout)
```

### 60.7. System config UI (dynamic config)

```
GET    /api/v1/admin/system-config
PUT    /api/v1/admin/system-config/{key}
```

Editable runtime config:
- `sla_warning_threshold_percent` (default 80)
- `alert_dedup_window_minutes` (default 30)
- `sensor_reading_retention_days` (default 90)
- `max_concurrent_sessions_per_user` (default 3)

Stored in Redis + DB, hot-reload across services.

---

## 61. Search functionality — P1

### 61.1. Strategy
- **Phase 1 (capstone scope):** Postgres `tsvector` full-text search.
- **Phase 2 (future):** Elasticsearch if scale demands.

### 61.2. Searchable entities

#### Tickets
```sql
ALTER TABLE tickets ADD COLUMN search_vector tsvector;
CREATE INDEX idx_tickets_search ON tickets USING GIN(search_vector);

-- Trigger to update
CREATE FUNCTION tickets_search_trigger() RETURNS trigger AS $$
BEGIN
  NEW.search_vector :=
    setweight(to_tsvector('simple', coalesce(NEW.code, '')), 'A') ||
    setweight(to_tsvector('simple', coalesce(NEW.title, '')), 'B') ||
    setweight(to_tsvector('simple', coalesce(NEW.description, '')), 'C');
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
```

#### KB Articles, Battery Assets (serial), Comments — similar pattern.

### 61.3. Search endpoints

```
GET /api/v1/search?q=overheat&types=tickets,kb&from=&to=
→ {
    "results": [
      { "type": "ticket", "id": "...", "code": "TKT-2605-0042", "title": "Overheat alert", "highlight": "...<b>overheat</b>...", "score": 0.85 },
      { "type": "kb", "id": "...", "title": "Overheat troubleshooting", ... }
    ],
    "facets": {
      "byType": { "tickets": 15, "kb": 3 },
      "byPriority": { "P1": 5, "P2": 7, "P3": 3 }
    },
    "took": 45
  }
```

### 61.4. Search-as-you-type (typeahead)
```
GET /api/v1/search/suggest?q=over
→ ["overheat", "overvoltage", "over capacity"]
```

### 61.5. Saved searches
Entity `SavedSearch`:
- `Id, UserId, Name, Query, Filters (jsonb), CreatedAt, LastUsedAt`

```
POST   /api/v1/saved-searches
GET    /api/v1/saved-searches/me
DELETE /api/v1/saved-searches/{id}
```

### 61.6. Search analytics

Log search queries (anonymized):
- Track common queries → identify content gap (lots of "battery swap" search but no article → write one).
- Track zero-result queries → improve KB.

```
GET /api/v1/admin/search-analytics/top-queries?from=&to=
GET /api/v1/admin/search-analytics/zero-result-queries
```

---

## 62. Media pipeline + accessibility — P2

### 62.0. File metadata foundation

FileStorageService không nên chỉ trả raw `objectKey`. Các service nghiệp vụ phải tham chiếu file bằng `fileId` ổn định.

#### Entity `UploadedFile`
| Field | Type | Note |
|-------|------|------|
| `Id` | Guid | `fileId` trả về cho Auth/Ticket/MaintenanceLog |
| `BucketName` | string(100) | MinIO/S3 bucket |
| `ObjectKey` | string(500) | đường dẫn object storage, internal detail |
| `OriginalFileName` | string(255) | tên file client upload |
| `ContentType` | string(100) | whitelist theo purpose |
| `SizeBytes` | long | validate max size |
| `Purpose` | enum/string | `Avatar`, `TicketAttachment`, `MaintenancePhoto`, `KbImage`, `Firmware` |
| `UploadedByUserId` | Guid? | null nếu system/internal |
| `Status` | enum | Uploaded=1, Processing=2, Ready=3, Quarantined=4, Deleted=5 |
| `ChecksumSha256` | string(64)? | integrity/dedup sau này |
| `CreatedAt`, `DeletedAt` | DateTime? | audit + soft delete |

Upload response chuẩn:
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "message": "Upload file thành công.",
  "data": {
    "fileId": "6c9f6e5d-bf26-49e0-a2f4-7e1d2e3a5c90",
    "objectKey": "avatars/6c9f6e5dbf2649e0a2f47e1d2e3a5c90.png",
    "fileName": "avatar.png",
    "contentType": "image/png",
    "sizeBytes": 123456,
    "purpose": "Avatar",
    "status": "Ready",
    "publicUrl": null
  },
  "listErrors": []
}
```

`objectKey` vẫn có thể trả cho debug/backward compatibility, nhưng Auth/Ticket/Battery không được lưu `objectKey` làm foreign reference. Các service chỉ lưu `fileId`.

### 62.1. Image upload pipeline

Khi user upload ảnh attachment:
```
Client upload → FileStorageService
              │
              ▼
         Original stored (private) + UploadedFile metadata created
              │
              ▼
    Background job ImageProcessingJob:
              │
              ├─→ Strip EXIF (privacy — remove GPS, device info)
              ├─→ Resize:
              │     • Thumbnail 200×200
              │     • Medium 800×800
              │     • Large 1920×max
              ├─→ Optimize (jpeg quality 80, webp alternative)
              └─→ Virus scan (ClamAV)
                    │
                    └─→ If clean → mark Status=Ready
                        If infected → quarantine + notify Admin + delete original
```

### 62.2. Endpoints
```
POST   /api/v1/files/upload                             (multipart, max 10MB)
→ { "fileId": "...", "objectKey": "...", "status": "Processing|Ready" }

GET    /api/v1/files/{id}?variant=thumbnail|medium|large|original
GET    /api/v1/files/{id}/metadata
GET    /api/v1/files/{id}/presigned-url?variant=original
```

### 62.3. Content moderation (optional)
- ML-based image moderation (offensive content detection).
- Out of scope cho capstone, note for future.

### 62.4. Accessibility (a11y) backend support

#### Alt text for images
- `TicketAttachment.AltText` field (Customer/Staff cung cấp khi upload).
- API response includes alt text.

#### Color blind friendly
- Status enum response includes both `code` (Critical/Warning/Info) and `colorHint` (`red`, `orange`, `blue`) — FE chọn cách hiển thị.

#### Screen reader metadata
- API responses cho list view có `ariaLabel` field for important rows.

---

## 63. Customer success metrics — P2

### 63.1. NPS survey

Trigger:
- 30 ngày sau registration đầu tiên.
- Sau 5 ticket close với rating ≥ 4.

```
GET /api/v1/users/me/nps-eligibility
POST /api/v1/users/me/nps-response
{
  "score": 9,                  // 0-10
  "comment": "Rất tốt..."
}
```

NPS score = % Promoters (9-10) − % Detractors (0-6).

### 63.2. Customer health score

Computed daily per Customer:
- Asset count active
- Last login recency
- Ticket reopen rate
- Average rating
- Notification engagement
- = Score 0-100

```
GET /api/v1/admin/customers/{id}/health-score
GET /api/v1/admin/customers/health-scores?segment=at-risk
```

### 63.3. Churn prediction (advanced)
- Customer health < 30 for 30 days → "at risk".
- Trigger outreach campaign.

### 63.4. Feature adoption tracking
- Track per feature: % active users have used in last 30d.
- Endpoint `GET /api/v1/admin/analytics/feature-adoption`.

### 63.5. User journey funnel

```
Registration → Activate Account → Claim First Asset → View Dashboard → Create First Ticket
   100%       →     80%          →       65%          →     60%        →       30%
```

Identify drop-off → optimize onboarding.

---

## 64. Status page + maintenance broadcast — P1

### 64.1. Public status page

`status.gsu26se55.com` — accessible without login.

Show:
- Overall status (Operational / Degraded / Down)
- Per-service status:
  - AuthService ✅
  - BatteryService ✅
  - TicketService ⚠️ Degraded (high latency)
  - NotificationService ✅
  - AI Module ❌ Outage
- Active incidents (with timeline)
- Past incidents (last 30 days)
- Uptime % (last 90 days)
- Scheduled maintenance announcements

### 64.2. Implementation
- Static site (Hugo / Next.js export) hosted on GitHub Pages / Vercel.
- Backend endpoint `GET /api/v1/public/status` returns JSON, status page polls every 1 phút.
- Or use **Statuspage.io** free tier (recommended).

### 64.3. Incident lifecycle on status page
- Admin manually creates incident on status page (linked from internal IncidentDeclared event).
- Updates posted as incident progresses.
- Resolution + postmortem link.

### 64.4. Subscribers
- Customer/Staff subscribe email/SMS for status updates.
- `POST /api/v1/public/status/subscribe { email }`.

---

## 65. Documentation auto-generation — P2

### 65.1. API docs from OpenAPI
- Each service exports `swagger.json` on build.
- ApiGateway aggregates.
- Redoc UI hosted public.

### 65.2. ERD from EF Core migrations
```bash
dotnet tool install -g dotnet-erd
dotnet erd --project services/BatteryService/src/BatteryService.Infrastructure --output docs/architecture/battery-erd.svg
```

Run in CI on each migration change, commit SVG.

### 65.3. Architecture diagrams as code (Mermaid/PlantUML)
`docs/architecture/`:
- `microservices.mmd` (Mermaid)
- `event-flow.mmd`
- `state-machine-ticket.mmd`
- `sequence-ticket-create.mmd`
- `state-machine-alert-ticket-saga.mmd` (Sprint 5B — xem §53.5; Initial → TicketRequested → TicketProvisioned → AlertLinkRequested → Completed/Failed + timeout/fault transitions)
- `sequence-alert-ticket-saga-happy.mmd` (Sprint 5B — happy path: Alert → CreateTicketFromAlertCommand → TicketProvisionedForAlertEvent → LinkAlertToTicketCommand → AlertLinkedToTicketEvent → Completed)
- `sequence-alert-ticket-saga-failure.mmd` (Sprint 5B — failure path: BatteryService down → timeout → Failed → admin reprocess → recovery)

Auto-render to SVG via GitHub Actions on push. Sprint 5B `#240` documentation sync task verify 3 Mermaid file mới đã render đúng và link từ `docs/adrs/ADR-018-*`.

### 65.4. Code coverage report hosted
- CI publishes coverage report to GitHub Pages.
- Per-service breakdown.
- Trend chart over time.

### 65.5. Changelog auto-gen

`tools/release-notes.sh`:
- Parse commits since last tag.
- Group by type: feat/fix/refactor/docs/test.
- Output `CHANGELOG.md`.

**CHANGELOG.md format** (Keep-a-Changelog convention):

```markdown
# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
### Changed
### Deprecated
### Removed
### Fixed
### Security
```

**Sprint 5B entry template** (task `#240`):

```markdown
## [1.5.0-beta] — 2026-07-26 (Sprint 5B)

### Added
- **Alert–Ticket Saga** orchestration (ADR-018): durable forward-recovery cho luồng Critical Alert → auto-create Ticket → link `Alert.TicketId`. PR #237, #238, #239.
- Saga admin endpoints `/api/v1/admin/sagas/alert-ticket/*` (View + Reprocess). PR #239.
- 2 NotificationType: `BatteryAlertEscalationPending` (16), `AlertTicketSagaFailed` (17). PR #238.
- 8 Prometheus metric + 2 AlertManager rule cho Saga ops. PR #239.
- 3 runbook: `08-saga-failed.md`, `09-saga-stuck.md`, `10-saga-duplicate-canonical.md`. PR #240.
- 3 Mermaid diagram trong `docs/architecture/` cho Saga. PR #240.
- ADR-017 (Remove Energy/CO2 scope), ADR-018 (Orchestrated Saga). PR #233, #239.
- AuthService permission `ticket.saga.view` + `ticket.saga.reprocess`. PR #241.

### Changed
- BatteryService `AlertEscalationService` publish `BatteryAlertEscalationRequestedEvent` (thay vì republish `BatteryAnomalyDetectedEvent`). PR #238.
- MassTransit Outbox/Inbox sang EF Consumer Outbox cho Saga endpoints (Redis Inbox vẫn dùng cho no-DB-change consumer). PR #235.
- TicketService dùng Quartz persistent scheduler cho Saga retry/timeout. PR #235.

### Removed
- **BREAKING**: `Site.CapacityKw` column và toàn bộ API contract liên quan. PR #234.
- **BREAKING (scope)**: tất cả Energy/CO2 analytics khỏi roadmap (ADR-017). PR #233.

### Fixed
- DI overwrite bug: `IIntegrationEventOutboxWriter` bị direct producer override. PR #235.
- Redis Inbox `TryMarkProcessedAsync` ghi key trước business commit → giờ tách thành EF Consumer Inbox cho DB consumer. PR #235.
```

**Sprint IoT-1 entry template** (IoT v2 — ESP32 + MQTT, ADR-016):

```markdown
## [1.6.0-iot] — 2026-08-09 (Sprint IoT-1)

### Added
- **IoT device management** (§52): `IotDevice`, `IotDeviceHeartbeat` (hypertable), `IotDeviceCalibration`, `IotFirmwareRelease`, `IotFirmwareUpdateLog` + migration `AddIotDeviceManagement`. PR #154.
- API key per-device (hash + rotate/revoke) + provision/heartbeat/firmware-check/calibration endpoints + admin device CRUD. PR #154.
- `SensorReading.SourceType` (Bms/IotGateway/External — B9) + `SensorReading.SensorSourceCode` (primary/redundant/external-temp). PR #154.
- `IotDeviceWentOfflineEvent` + `IotDeviceWentOfflineConsumer` (NotificationService) + routing DeviceOffline. PR #154.
- `IotDeviceOfflineDetectionBackgroundService` + `CalibrationExpiryNotificationService`. PR #154.
- AuthService permission `iot.device.view/manage`, `iot.firmware.manage`, `iot.calibration.manage`. PR #154.
- 6 IoT Prometheus metric + IoT Device Monitoring Grafana dashboard + 2 AlertManager rule. PR #154.
- **(P3, optional)** MQTT realtime: broker `infra/mqtt/` (EMQX/Mosquitto + TLS 8883 + ACL per-device), `MqttBridgeBackgroundService`, LWT offline, downlink cmd (§52.14).

### Changed
- IoT edge device chuẩn đổi Raspberry Pi → **ESP32-S3** + RS485/Modbus multi-drop; transport hybrid HTTPS + MQTT (ADR-016 reframe).
- `POST /api/sensor-readings/batch`: thêm `X-Device-Code`/`Idempotency-Key`/`deviceTimestamp`, mapping `batteryAssetSerial`; giữ legacy `batteryAssetId` cho simulator/MVP.

### Deprecated
- `iot.md` (Raspberry Pi v1, Python) — thay bằng `newiot.md`/`overall.iot.md`/`wiring-diagram.md`/`hardware-bom.csv`.
```

**Commit message convention** (Conventional Commits) — `tools/release-notes.sh` parse:
- `feat(saga): ...` → Added section
- `fix(saga): ...` → Fixed section
- `refactor(messaging): ...` → Changed section
- `BREAKING CHANGE: ...` footer → Removed/Breaking section
- `docs(adr): ...` → not in CHANGELOG (in ADR registry §40.1)

**Git tag convention:**
- Sprint 5B release: `v1.5.0-beta-sprint5b` sau khi `#239` merge.
- Post-cutover smoke test pass: `v1.5.0`.
- Sprint IoT-1 release: `v1.6.0-iot` sau khi `#154` merge (provision + heartbeat + ingest + offline pass).

---

## 66. Final completeness checklist

> Tổng hợp tất cả các thứ phải xong cho **production-ready demo capstone**.

### 66.1. Code completeness
- [x] AuthService (DONE)
- [x] AuthService profile/staff extension + uploaded/Google avatar source flow
- [ ] BatteryService — entity + CQRS + background jobs + AI bridge + sites + IoT
- [ ] TicketService — entity + state machine + SLA + relationships + escalation
- [ ] NotificationService — consumers + Expo + email + SMS + SSE + digest
- [ ] KnowledgeBase module + public articles
- [ ] AI Module integration (BatteryService → AI HTTP client)
- [x] FileStorageService metadata foundation (`UploadedFile`, `fileId` APIs)
- [ ] FileStorageService — media pipeline (resize, EXIF strip, virus scan)
- [ ] Reports endpoints (Ticket + Battery)
- [ ] Search functionality
- [ ] Alert–Ticket Saga create/reuse/link + failure recovery
- [ ] Energy/CO2 scope cleanup verified; `Site.CapacityKw` removed
- [ ] App config + feature flags + maintenance announcements
- [ ] Status page integration
- [ ] Admin tools (impersonation, feature flag, system config)

### 66.2. Database
- [x] AuthService migration `AddAccountProfileExtensionTables` created
- [x] FileStorageService migration `AddUploadedFileMetadata` created
- [x] Docker Compose logical DB split documented/configured (`auth_db`, `file_storage_db`)
- [ ] All migrations tested rollback
- [ ] Seed data realistic + 3-month history
- [ ] TimescaleDB hypertable + retention + continuous aggregate
- [ ] Indexes verified per query plan
- [ ] Zero-downtime migration pattern documented
- [ ] **Sprint 5B**: `RemoveSiteCapacityKw` applied (`#234`)
- [ ] **Sprint 5B**: `AddDurableMessagingFoundation` applied per service (`#235`)
- [ ] **Sprint 5B**: `AddQuartzPersistenceSchema` applied trên `ticket_db` (`#235`)
- [ ] **Sprint 5B**: `AddAlertTicketSagaFoundation` + `AddAlertTicketLinkIndex` applied (`#236`)
- [ ] **Sprint 5B**: `SeedSagaPermissions` + `BindSagaPermissionsToRoles` applied trên `auth_db` (`#241`)
- [ ] **Sprint 5B**: preflight duplicate cleanup runbook executed trước unique constraint apply

### 66.3. Infrastructure
- [x] Docker Compose config validated with `--env-file .env.Docker`
- [x] `postgres-init` idempotent service database creation added
- [ ] Docker compose all green start < 60s
- [ ] K8s Helm charts per service
- [ ] CI/CD: build + test + lint + scan + deploy
- [ ] Secrets management
- [ ] Monitoring (Prometheus + Grafana + Loki + Tempo)
- [ ] AlertManager rules
- [ ] AI Module deployed + health check
- [ ] **Sprint 5B**: Quartz persistent scheduler started + cluster checkin OK (xem §8.3.11bis)
- [ ] **Sprint 5B**: MassTransit EF Consumer Outbox/Inbox tables active per service
- [ ] **Sprint 5B**: Feature flag `AlertTicketDispatchEnabled`/`AlertTicketSagaEnabled` config đúng default (xem §53.9)
- [ ] **Sprint 5B**: Saga endpoint runtime config apply (PrefetchCount 4/8/8/16 — §8.3.11bis)
- [ ] **Sprint 5B**: CI `.github/workflows/ci.yml` có step "Energy/CO2 scope guard" (§53.2bis)

### 66.4. Quality
- [ ] Test coverage ≥ 80% all services
- [ ] State machine matrix tested
- [ ] Contract tests producer/consumer
- [ ] Load test results documented
- [ ] Smoke test suite < 2 phút
- [ ] Pen test internal done
- [ ] **Sprint 5B**: `AlertTicketSagaStateMachineTests` ≥ 21 case pass (đồng bộ §53.10 matrix)
- [ ] **Sprint 5B**: Restart-recovery integration test pass (kill TicketService giữa transaction)
- [ ] **Sprint 5B**: Saga PR review checklist 34 mục đã tick (§18.2bis) cho mỗi PR `#237`–`#239`
- [ ] **Sprint 5B**: Contract test cho 8 Saga message + V1/V2 BatteryAnomaly pass
- [ ] **Sprint 5B**: DI test assert outbox writer không bị direct producer override

### 66.5. Documentation
- [ ] OpenAPI / Swagger aggregated at gateway (bao gồm Saga admin endpoints)
- [ ] Postman collection (có Saga folder + Idempotency-Key example)
- [ ] README per service (TicketService có Saga ops section)
- [ ] CLAUDE.md updated (bao gồm pattern Orchestrated Saga + EF Consumer Outbox/Inbox — §0bis.2)
- [ ] 18 ADRs documented (xem §40.1), đặc biệt ADR-016/017/018 cho Sprint 5B
- [ ] DR plan + 10 runbooks (7 baseline + 3 Saga: `08-saga-failed.md`, `09-saga-stuck.md`, `10-saga-duplicate-canonical.md`)
- [ ] PIA + DPA + cookie config
- [ ] SECURITY.md
- [ ] CHANGELOG.md auto-generated (entry Sprint 5B liệt kê tất cả task `#233`–`#241`)
- [ ] ERD diagrams committed
- [ ] Edge cases matrix (34+)
- [ ] **Sprint 5B**: 3 Mermaid diagram trong `docs/architecture/` (state-machine + 2 sequence — §65.3)
- [ ] **Sprint 5B**: `docs/onboarding/be-newcomer.md` có 3 Saga section (Local setup + Debug + Common mistakes — §40.6)
- [ ] **Sprint 5B**: GDPR retention table cập nhật cho Alert/Saga/MassTransit/Quartz tables (§39.3)

### 66.6. Demo deliverables
- [ ] Demo script (9 scenes, 90 phút)
- [ ] Reset + inject anomaly + fast-forward scripts
- [ ] Pre-prepared demo scenarios (5+)
- [ ] Architecture poster A1 print
- [ ] Intro video 5-10 phút
- [ ] Slide deck 20 slides
- [ ] Q&A prep doc 30+ questions
- [ ] Backup recording
- [ ] Dry-run with mentor done
- [ ] Tech setup checklist verified

### 66.7. Business value showcase
- [ ] Customer dashboard with realtime battery health, SOH trend và active alerts
- [ ] Alert → Ticket traceability visible từ cả hai phía
- [ ] AI prediction visible + explainable (confidence + classification)
- [ ] SLA compliance reports
- [ ] CSAT score
- [ ] Top assets / top issues / top staff reports

### 66.8. "Bonus points" academic
- [ ] AI feedback loop demonstrable
- [ ] Drift detection working
- [ ] Real IoT device sending data (even if ESP32 `mock_bms`)
- [ ] Saga failure/recovery trace demonstrable without duplicate Ticket
- [ ] GDPR export demo
- [ ] Postmortem template ready (if incident happens during demo, recover gracefully)

---

## 67. Tóm tắt final — file đầy đủ chưa?

### Đánh giá lần 3 (sau §52-66)

| Khía cạnh | Coverage trước | Coverage giờ |
|-----------|---------------|--------------|
| Business flow & domain | 9.5/10 | **10/10** |
| Microservices architecture | 9.5/10 | **10/10** |
| AI integration | 9/10 | **10/10** (deployment + retrain) |
| Cross-cutting | 9/10 | **10/10** |
| Security & Compliance | 8/10 | **9.5/10** |
| Testing strategy | 8.5/10 | **9.5/10** |
| IoT/Device management | 4/10 | **10/10** ✅ |
| Distributed consistency / Saga | 2/10 | **10/10** ✅ |
| Production deployment | 5/10 | **9/10** ✅ |
| Mobile/Web app mgmt | 5/10 | **9/10** ✅ |
| Demo/Presentation | 3/10 | **10/10** ✅ |

### Stats final

| Metric | Value |
|--------|-------|
| Total sections | **67** numbered sections (§0..§18, §20..§67 — §19 reserved, not used) + **8 bis/ter sub-sections** (§6bis, §7.5bis, §8.3.11bis, §18.2bis, §40.3bis, §53.2bis, §53.2ter, §60.4bis) + **sequential additions Sprint 5B** (§11.7 CI time, §27.9-11 Saga troubleshooting, §56.14 post-Sprint 8 timeline, §56.15 external deps) |
| Total entities defined | **50+** |
| Total commands | **90+** |
| Total queries | **60+** |
| Total integration events | **30+** |
| Total endpoints | **220+** |
| Total background services | **25+** |
| ADRs documented | **18** (+ ADR-016 IoT protocol, ADR-017 Energy/CO2 scope removal, ADR-018 Alert–Ticket Saga) |
| Runbooks | **10** (7 baseline + 3 Saga: `08-saga-failed`, `09-saga-stuck`, `10-saga-duplicate-canonical`) |
| Edge case rules | **34** (EC-01..EC-34) |
| Risk register items | **29** (R-01..R-29, bao gồm Saga risks R-14..R-22, capacity/ext-deps R-23..R-27 và IoT v2 pivot R-28..R-29) |
| Q&A thống nhất | **25** (Q-01..Q-25, bao gồm Saga design Q-19..Q-25) |
| Troubleshooting playbook | **11** (8 baseline + 3 Saga case ở §27.9-11) |
| Demo scenes scripted | **9** |
| Performance SLAs | per endpoint (xem §13.4 + §40.5 SLO + error budget) |
| Sprint backlog | **8 sprint chính** + **Sprint 5B** (release gate `#233-#241`) + **Sprint IoT-1** (song song Sprint 6) + **post-Sprint 8 → defense timeline** (xem §56.14) |
| External dependency register | **11 services** (xem §56.15) |

---

**End of OVERALL.md (Final Complete Edition)**

**Document lifecycle:**
- v1 (2026-05-12 morning): §0-29 initial roadmap
- v2 (2026-05-12 afternoon): §30-51 gap analysis addendum
- v3 (2026-05-12 evening): §52-67 final completeness — IoT, K8s, app mgmt, demo prep, intra-section additions
- v4 (2026-06-10): bỏ Energy/CO2 + `Site.CapacityKw`; bổ sung Alert–Ticket Saga và Sprint 5B tasks `#233–#241`
- v4.1 (2026-06-10): completeness pass — ADR-017/018 vào registry; NotificationService consume escalation/failure event + enum 16/17; PriorityCalculator mapping đồng bộ wire-value §53.7 (sau v4.5 reconcile: mở rộng đến 15 đồng bộ §1.3.6 domain enum, KHÔNG phải custom Saga numbering 1-11); làm rõ `Alert.TicketId` đã có sẵn (chỉ thêm index); Saga subscribe V1+V2; Quartz NuGet + `AddQuartzPersistenceSchema`; Manager đọc Saga; thêm task `#240` doc sync; owner mapping P0; mở rộng test matrix & DoD; glossary Tombstone/EF Consumer Outbox/Wire value
- v4.2 (2026-06-10): observability + ops completeness — Grafana metric/alert rule Saga; DR plan + SLO + rate limit; structured logging convention; migration ordering với preflight cleanup gate; demo seed/helper script Saga; troubleshooting playbook 3 case mới; risk register R-19..R-22; câu hỏi Q-19..Q-25; cache strategy Saga; task `#241` AuthService permission seed; AlertTicketSagaStateMachineTests ≥ 21 cases đồng bộ test matrix; Phase 2 checklist publish event + escalation BG service
- v4.3 (2026-06-10): execution + planning completeness — Saga PR review checklist 34 mục (§18.2bis); endpoint runtime config (§8.3.11bis); newcomer onboarding Saga sections (§40.6); merge order + 9-task matrix (§53.9); §28 paths recap orphan cleanup; §66 final checklist 22+ Saga acceptance items; §17 Sprint 5B/6/7/8 task descriptions cross-ref sync; Saga runbook samples 3 file (§40.3); postmortem template (§40.3bis); severity matrix + on-call playbook (§40.4); pre-commit hook scope-guard + PR template (§53.2ter); CHANGELOG format + Sprint 5B entry + commit convention (§65.5); SLO error budget + burn rate YAML (§40.5 + §9.2); capacity warning Duy overload + R-23 (§17); Sprint IoT-1 owner Thái + R-24; Sprint 6/7/8 owner explicit; Bus factor warning + KT plan + R-25; FE work song song Sprint 5B; slide deck Saga slide; post-Sprint 8 → defense timeline (§56.14); external dependency register 11 services + R-26/R-27 (§56.15); TOC §67 + §0.2 4 row mới
- v4.4 (2026-06-10): physical reality + meta-tích lũy — Sprint 5B working days clarification (5 dev-day, 4 mitigation option); local dev hardware requirement table + cleanup script (§40.6); CI execution time budget §11.7 (16 phút Sprint 5B end, 5 mitigation); demo day contingency 6 item (power outage, smoke test, Saga pre-warm, mid-demo recovery, audio, NTP) + backup mentor + backup defense slot (§56.12/56.14); §28 Scripts recap 11 orphan cleanup; document header v4.3 reflect đầy đủ scope; §23 risk register intro 4-group breakdown
- v4.5 (2026-06-10): final full-file review (multi-pass) — **fix wire value/domain enum reconcile**: §53.7 Saga wire value table giờ khớp `AnomalyTypeEnum` §1.3.6 (1-15 thay vì 1-11 cũ); §2.4 PriorityCalculator table mở rộng 15 row; §1.3.5 Alert.AnomalyType note "1–14" → "1–15"; §1.6 ThresholdAnomalyDetector "14 rule check" → "15 rule check"; §1.7 BatteryAnomalyDetectedEvent comment đổi từ "không reference Domain enum" sang "wire value = AnomalyTypeEnum integer §1.3.6"; §26 Glossary Wire value redefined "bằng integer của Domain enum"; §1.9 ThresholdAnomalyDetectorTests 14→15 case; §2.9 CreateTicketFromAlertConsumerTests "đủ 8 anomaly" → "đủ 15 anomaly"; §51 Entity count 17→50+ đồng bộ §67 stats; §56.6 Architecture poster metrics update đồng bộ §67; §56.8 Postman "150+ endpoints" → "220+ endpoints" đồng bộ §67; §50 Sprint 7 mitigation "1.5×" → "1.6×" đồng bộ table; §30.6 V2 Classification comment làm rõ "wire value khớp AnomalyClassificationEnum §30.3 (1=Normal/2=Degrading/3=Failed), null = AI chưa classify"; thay "pass 55" thành "v4.5" trong glossary để tránh dependency vào pass numbering nội bộ; **§5.2 Reports endpoint catalog stale fix**: TicketService 8→9 endpoints (thêm `GET /reports/saga-failed-rate` đồng bộ §17 Sprint 7 task #114), BatteryService 5→7 endpoints (thêm `GET /reports/environmental-incidents` + `GET /reports/ambient-trend` đồng bộ Sprint 5B ambient + §17 carryover).

- v4.6 (2026-06-11): **IoT v2 pivot — Raspberry Pi → ESP32-S3 + hybrid HTTPS/MQTT** (đồng bộ `newiot.md`/`overall.iot.md`/`wiring-diagram.md`/`hardware-bom.csv`). ADR-016 reframe (ESP32 + hybrid transport, RS485/Modbus multi-drop); §52 đổi "Gateway"→"Edge Device", sơ đồ §52.1 thêm MQTT broker/bridge; §52.5 payload đầy đủ + §52.6 LWT offline tức thì + §52.8 CalibrationExpiryNotificationService; **§52.9 cross-source tag table** (BMS/primary vs INA226/redundant) + **§52.9bis** ESP32 feed ambient/incident + **§52.14 MQTT realtime** (broker/bridge/LWT/downlink/ACL per-device) + **§52.15 failure modes**; §1.3.4 thêm `SensorSourceCode` (đang thiếu); §52.2 `batteryMappings[]` multi-drop + heartbeat ESP32 field mapping + key scope `environmental.ingest`; §0bis.3 route IoT; §1.7 khai báo `IotDeviceWentOfflineEvent` + §3.4 routing + §16.3 consumer; §20 permission `iot.device/firmware/calibration.*`; §12.1 seed 1 IoT device + §12.2/§51 migration `AddIotDeviceManagement`; §14.8 IoT device security; §9.2 dashboard #5 IoT Device Monitoring + 2 alert rule; §1.8 reconcile global key vs per-device; §26 glossary 12 thuật ngữ IoT + IoT references; §23 R-28/R-29 (ESP32 firmware/BMS procurement). **Energy/CO2 conflict resolved** (§53.1): INA226 = cross-source validation, energy demo optional ngoài software scope (ADR-017 giữ nguyên). ADR count giữ 18 (mở rộng ADR-016, không mint mới); EC/consumer/template count baseline giữ nguyên (IoT-1 attribute +1).

**Maintained by:** Leader. Cập nhật mỗi cuối sprint khi `/kltn-sprint` chạy. Multi-pass extended review (50+ pass) chỉ dùng khi major architectural change (vd Sprint 5B Saga, IoT v2 pivot).
**Last major update:** 2026-06-11 (v4.6) — IoT v2 pivot ESP32 + MQTT (5-pass review, đồng bộ 4 file IoT mới).

**Recommended reading order for newcomer:**
1. §0-0bis (context — 10 phút)
2. §1, §2, §3 (3 main services — 30 phút)
3. §30, §52, §53 (AI, IoT, scope cleanup và Saga — 25 phút)
4. §38 + §58 (edge cases matrix — 10 phút)
5. §17 (sprint backlog + capacity warnings — 15 phút)
6. **§23 (29 risk items — 10 phút) ← bắt buộc nắm trước khi join sprint**
7. **§40 (ops: ADR + DR + runbook + postmortem — 15 phút) ← critical cho on-call**
8. §56 (demo prep — when nearing deadline)
9. **§60.4bis (Saga admin UI spec — 5 phút) ← FE Trí + Minh required reading**
10. **§66 (final completeness checklist — 10 phút) ← Leader weekly review**

Total ~2 giờ cho newcomer onboarding đọc canonical sections.

**Total reading time end-to-end:** ~3-4 hours for complete understanding.
