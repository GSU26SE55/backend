# OVERALL.AI.md — Backend tasks phục vụ AI Module

> File này liệt kê **toàn bộ công việc Backend (BatteryService) phải xây dựng** để tích hợp với AI Module.
> Mọi task có cross-reference đến `overall.md` (section + sprint + issue number nếu có).
> File AI module riêng: `ai.md`.

---

## Mục lục

- [Phần I — Tổng quan](#phần-i--tổng-quan)
  - [1. Vai trò Backend trong tích hợp AI](#1-vai-trò-backend-trong-tích-hợp-ai)
  - [2. Bức tranh 10 nhóm component](#2-bức-tranh-10-nhóm-component)
  - [3. So sánh AI vs Backend làm gì](#3-so-sánh-ai-vs-backend-làm-gì)
- [Phần II — Chi tiết các nhóm](#phần-ii--chi-tiết-các-nhóm)
  - [Nhóm 1 — AI Bridge Client](#nhóm-1--ai-bridge-client)
  - [Nhóm 2 — Database (Entity + Migration)](#nhóm-2--database-entity--migration)
  - [Nhóm 3 — Sensor Reading Provider](#nhóm-3--sensor-reading-provider)
  - [Nhóm 4 — Background Services](#nhóm-4--background-services)
  - [Nhóm 5 — Integration Events](#nhóm-5--integration-events)
  - [Nhóm 6 — REST Endpoints](#nhóm-6--rest-endpoints)
  - [Nhóm 7 — Caching Layer](#nhóm-7--caching-layer)
  - [Nhóm 8 — Fallback Logic](#nhóm-8--fallback-logic)
  - [Nhóm 9 — Reporting & Analytics](#nhóm-9--reporting--analytics)
  - [Nhóm 10 — Testing](#nhóm-10--testing)
- [Phần III — Backlog Matrix theo Sprint](#phần-iii--backlog-matrix-theo-sprint)
- [Phần IV — Configuration & DI](#phần-iv--configuration--di)
- [Phần V — Definition of Done](#phần-v--definition-of-done)
- [Phần VI — Risk & Mitigation](#phần-vi--risk--mitigation)
- [Phần VII — Cross-reference Index](#phần-vii--cross-reference-index)

---

# Phần I — Tổng quan

## 1. Vai trò Backend trong tích hợp AI

**Điểm quan trọng nhất:** AI Module chỉ là 1 inference endpoint thuần (`POST /predict/soh`, `POST /classify/anomaly`). **Backend phải làm phần lớn "plumbing"** — chiếm khoảng **70% công sức tích hợp AI vào sản phẩm**:

- Lấy data từ DB cho AI
- Gọi AI qua HTTP với resilience
- Lưu kết quả vào DB
- Publish event cho các service khác
- Cache để giảm load AI
- Expose REST endpoint cho FE/Mobile
- Feedback loop để retrain
- Monitor + fallback khi AI down

→ Nếu xem AI Module = não, Backend = **toàn bộ hệ thần kinh + cơ + xương**.

## 2. Bức tranh 10 nhóm component

```
┌──────────────────── BatteryService (BE side) ────────────────────┐
│                                                                    │
│  1. Bridge Client → 2. Database → 3. Sensor Provider              │
│  → 4. Background Services → 5. Events → 6. Endpoints              │
│  → 7. Cache → 8. Fallback → 9. Reporting → 10. Testing            │
│                                                                    │
│                          ↕ HTTP                                    │
└────────────────────────────────────────────────────────────────────┘
                          ↕
              ┌────────────────────┐
              │   AI Module        │
              │   (FastAPI)        │
              └────────────────────┘
```

## 3. So sánh AI vs Backend làm gì

| Việc | AI Module (Python) | Backend (BatteryService) |
|------|---------------------|-------------------------|
| Train model | ✅ Toàn bộ | ❌ |
| Serve model | ✅ FastAPI | ❌ |
| Quản lý artifact .pth/.pkl | ✅ | ❌ |
| **Lấy data từ DB** | ❌ | ✅ Query TimescaleDB |
| **Gọi AI qua HTTP** | ❌ | ✅ Polly client |
| **Lưu prediction vào DB** | ❌ | ✅ 2 entity |
| **Trigger định kỳ** | ❌ | ✅ Background service |
| **Publish event** | ❌ | ✅ RabbitMQ |
| **Endpoint cho FE** | ❌ | ✅ 13+ endpoints |
| **Cache** | ❌ | ✅ Redis |
| **Fallback khi AI down** | ❌ | ✅ Circuit breaker logic |
| **Feedback từ Staff** | ❌ | ✅ Endpoint + DB |
| **Export training data** | ❌ | ✅ Background service → Parquet |
| **Retrain model** | ✅ AI team chạy thủ công | ❌ |
| **CI/CD deploy model** | ✅ GitHub Actions trong ai-module | ❌ |
| **Drift detection** | ⚠️ Split | ⚠️ Split (overall.md gán cho BE — §48.5) |

---

# Phần II — Chi tiết các nhóm

## Nhóm 1 — AI Bridge Client

> Lớp HTTP gọi AI từ BatteryService. Đây là **boundary** giữa BE và AI.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.4 (AI Bridge service) |
| Sprint | **Sprint 2** (`§17` — "+ **AI Bridge client skeleton**") |
| Sprint impact | §50 Sprint impact summary line 5899 |
| Trùng task overall.md? | ❌ Không trùng — task riêng |

### Files mới phải tạo

| File | Tác dụng |
|------|---------|
| `BatteryService.Application/AI/IAiInferenceClient.cs` | Interface |
| `BatteryService.Application/AI/Models/SohPredictionResult.cs` | DTO response từ AI |
| `BatteryService.Application/AI/Models/AnomalyClassificationResult.cs` | DTO response |
| `BatteryService.Application/AI/Models/HealthCheckResult.cs` | DTO health |
| `BatteryService.Application/AI/Models/SensorReadingPayload.cs` | DTO payload gửi AI |
| `BatteryService.Infrastructure/AI/AiInferenceClient.cs` | HttpClient impl |
| `BatteryService.Infrastructure/AI/AiInferenceOptions.cs` | Config bind |
| `BatteryService.Infrastructure/DependencyInjection/AiInferenceDependencyInjection.cs` | DI registration với Polly |

### Interface signature (theo overall.md §30.4)

```csharp
public interface IAiInferenceClient {
    Task<SohPredictionResult> PredictSohAsync(Guid assetId, IReadOnlyList<SensorReading> window, CancellationToken ct);
    Task<AnomalyClassificationResult> ClassifyAnomalyAsync(Guid assetId, IReadOnlyList<SensorReading> window, CancellationToken ct);
    Task<HealthCheckResult> HealthAsync(CancellationToken ct);
}
```

### Polly resilience policies (overall.md §30.4)

| Policy | Cấu hình | Tác dụng |
|--------|---------|---------|
| **Timeout** | 200ms | SLA P1 < 100ms, +100ms network buffer |
| **Retry** | 2 lần exponential backoff (200ms, 400ms) | Transient error |
| **Circuit Breaker** | 50% fail trong 30s → mở 60s | Tránh cascade failure |

```csharp
services.AddHttpClient<IAiInferenceClient, AiInferenceClient>(c => {
    c.BaseAddress = new Uri(config["AI:BaseUrl"]);
})
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(200)))
.AddPolicyHandler(Policy
    .Handle<HttpRequestException>()
    .WaitAndRetryAsync(2, n => TimeSpan.FromMilliseconds(200 * Math.Pow(2, n))))
.AddPolicyHandler(Policy
    .Handle<HttpRequestException>()
    .AdvancedCircuitBreakerAsync(0.5, TimeSpan.FromSeconds(30), 5, TimeSpan.FromSeconds(60)));
```

### API contract (ký với AI team — phải đồng bộ với `ai.md` §28)

Payload gửi AI (theo `overall.md §30.4`):
```json
{
  "asset_id": "...",
  "readings": [
    {"time": "...", "v": 3.72, "i": 1.50, "t": 25.3, "soc": 85.0},
    ...
  ]
}
```

Response từ AI:
```json
{ "soh_percent": 87.3, "confidence": 0.92, "model_version": "1.0" }
```

### Acceptance criteria

- [ ] Interface định nghĩa đầy đủ 3 method
- [ ] HttpClient registered với base URL từ config
- [ ] Polly 3 policy đều active
- [ ] Test mock HttpClient verify timeout/retry/circuit-breaker
- [ ] Health endpoint trả về status đúng từ AI

---

## Nhóm 2 — Database (Entity + Migration)

> 2 entity mới + migration. Phải extend `AuditableEntity` theo `.claude/rules/tech/be.md §2`.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.3 (New entities) |
| Sprint | **Sprint 3** (theo `§17` "+ **AI Hybrid pipeline**") |
| Migration name overall.md | `AddSohPredictionTables` (§50 line 5975) |
| Trùng task? | ⚠️ **Migration `AddSohPredictionTables` được liệt kê trong §50** — overall.md đã ghi nhận |
| Issue tracker | Sprint 3 chưa có issue number cụ thể — sẽ tạo khi `/kltn-sprint` |

### Entity 1: `SohPrediction` (overall.md §30.3)

```csharp
public class SohPrediction : AuditableEntity
{
    public Guid BatteryAssetId { get; set; }
    public BatteryAsset? BatteryAsset { get; set; }
    public decimal PredictedSohPercent { get; set; }  // (5,2) 0-100
    public decimal Confidence { get; set; }            // (4,3) 0-1
    public string ModelVersion { get; set; } = string.Empty;  // "1.0", "1.1"
    public DateTime InputWindowStartUtc { get; set; }
    public DateTime InputWindowEndUtc { get; set; }
    public DateTime PredictedAt { get; set; }          // indexed DESC
    public int LatencyMs { get; set; }                 // Monitoring
    public string? RawResponse { get; set; }           // jsonb? — debug
}
```

### Entity 2: `AnomalyClassification` (overall.md §30.3)

```csharp
public class AnomalyClassification : AuditableEntity
{
    public Guid? AlertId { get; set; }
    public Alert? Alert { get; set; }
    public Guid BatteryAssetId { get; set; }
    public AnomalyClassificationEnum Classification { get; set; }
    public decimal AnomalyScore { get; set; }          // (8,6)
    public decimal Confidence { get; set; }            // (4,3)
    public string ModelVersion { get; set; } = string.Empty;
    public DateTime ClassifiedAt { get; set; }
    public int LatencyMs { get; set; }
    public StaffFeedbackEnum? StaffFeedback { get; set; }
    public Guid? StaffFeedbackByUserId { get; set; }
    public DateTime? StaffFeedbackAt { get; set; }
}
```

### Enum

```csharp
public enum AnomalyClassificationEnum
{
    Unknown = 0,       // Fallback khi AI down (overall.md §30.11)
    Normal = 1,
    Degrading = 2,
    Failed = 3
}

public enum StaffFeedbackEnum
{
    Correct = 1,
    FalsePositive = 2,
    FalseNegative = 3
}
```

### Files mới

| File | Tác dụng |
|------|---------|
| `BatteryService.Domain/Entities/SohPrediction.cs` | Entity |
| `BatteryService.Domain/Entities/AnomalyClassification.cs` | Entity |
| `BatteryService.Domain/Enums/AnomalyClassificationEnum.cs` | Enum |
| `BatteryService.Domain/Enums/StaffFeedbackEnum.cs` | Enum |
| `BatteryService.Infrastructure/Persistence/Configurations/SohPredictionConfiguration.cs` | EF config |
| `BatteryService.Infrastructure/Persistence/Configurations/AnomalyClassificationConfiguration.cs` | EF config |
| `BatteryService.Infrastructure/Migrations/{timestamp}_AddSohPredictionTables.cs` | Migration |

### Database changes

```csharp
// ApplicationDbContext.cs
public DbSet<SohPrediction> SohPredictions => Set<SohPrediction>();
public DbSet<AnomalyClassification> AnomalyClassifications => Set<AnomalyClassification>();
```

### Indexes bắt buộc

```csharp
// SohPredictionConfiguration
builder.HasIndex(x => new { x.BatteryAssetId, x.PredictedAt })
    .IsDescending(false, true)
    .HasDatabaseName("ix_soh_predictions_asset_predicted_desc");

// AnomalyClassificationConfiguration
builder.HasIndex(x => x.AlertId).HasDatabaseName("ix_anomaly_classifications_alert");
builder.HasIndex(x => new { x.BatteryAssetId, x.ClassifiedAt })
    .IsDescending(false, true);
```

### Migration command (theo `.claude/rules/tech/be.md §13`)

```bash
dotnet ef migrations add AddSohPredictionTables \
    -p ../BatteryService.Infrastructure -s .

# Test rollback (bắt buộc theo §14)
dotnet ef database update PreviousMigration -p ../BatteryService.Infrastructure -s .
dotnet ef database update -p ../BatteryService.Infrastructure -s .
```

### Acceptance criteria

- [ ] 2 entity extend `AuditableEntity` (BR `.claude/rules/tech/be.md §2`)
- [ ] Enum bắt đầu từ 1 (Unknown = 0 chỉ cho fallback)
- [ ] Migration tested rollback PASS
- [ ] Indexes tạo đúng theo plan
- [ ] DbSet đăng ký trong `ApplicationDbContext`

---

## Nhóm 3 — Sensor Reading Provider

> Service feed data cho AI. Phải query TimescaleDB hiệu quả và validate trước khi gọi AI.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | Implicit trong §30.5 (background service cần data) + §53.7 (cycle log integration) |
| Sprint | **Sprint 3** (cùng với background service) |
| Trùng task overall.md? | ⚠️ **§53.7 (Sprint 5B/6) mở rộng — thêm `BatteryCycleLog.StressScore` làm input AI** — nâng cấp sau |
| Issue | Chưa có issue cụ thể |

### Files mới

| File | Tác dụng |
|------|---------|
| `BatteryService.Application/AI/Services/ISensorReadingProvider.cs` | Interface |
| `BatteryService.Infrastructure/AI/SensorReadingProvider.cs` | Impl query TimescaleDB |
| `BatteryService.Application/AI/Validation/SensorWindowValidator.cs` | Validate trước khi gọi AI |

### Logic chính

```csharp
public interface ISensorReadingProvider
{
    Task<IReadOnlyList<SensorReading>?> GetLastNReadingsAsync(
        Guid assetId, int count, CancellationToken ct);
}

public class SensorReadingProvider : ISensorReadingProvider
{
    private readonly IUnitOfWork _uow;

    public async Task<IReadOnlyList<SensorReading>?> GetLastNReadingsAsync(
        Guid assetId, int count, CancellationToken ct)
    {
        var readings = await _uow.SensorReadings.GetAllAsync()
            .Where(x => !x.IsDeleted && x.BatteryAssetId == assetId)
            .OrderByDescending(x => x.Timestamp)
            .Take(count)
            .ToListAsync(ct);

        if (readings.Count < count) return null;  // Pin chưa đủ data

        return readings.OrderBy(x => x.Timestamp).ToList();  // ASC cho AI
    }
}
```

### Validation rules

| Rule | Tác dụng |
|------|---------|
| Đủ 30 readings | Pin mới install → skip predict |
| Khoảng cách timestamp hợp lý | Gap > 1h → warn, > 24h → skip |
| Outlier check | V > 1000V hoặc V < 0 → reject (EC-25 overall.md §58) |
| Sensor không stale | Newest reading > 1h cũ → warn |

### NEW: Stress score từ CycleLog (overall.md §53.7)

**Sprint 5B/6 trở đi:** Nâng cấp `SensorReadingPayload` thêm `stress_score`:

```csharp
public class SensorReadingPayload
{
    // ... fields hiện có
    public decimal? StressScore { get; set; }  // Từ BatteryCycleLog
}
```

`StressScore` tính từ:
- High DOD (Depth of Discharge > 80%) → stress cao
- High temperature trong cycle → stress cao
- High C-rate (current/capacity) → stress cao

**Cross-ref:** `overall.md §53.7` line 6437 — field `BatteryCycleLog.StressScore`.

### Acceptance criteria

- [ ] Query TimescaleDB dùng index `(BatteryAssetId, Timestamp DESC)`
- [ ] Trả null nếu chưa đủ 30 readings (handle pin mới)
- [ ] Validate gap timestamp, reject outlier
- [ ] Performance: < 50ms cho query 30 readings

---

## Nhóm 4 — Background Services

> 2 service chạy nền cho prediction định kỳ + classification on alert.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.5 (Background services AI) |
| Sprint | **Sprint 3** ("AI Hybrid pipeline" §50 line 5900) |
| Trùng task overall.md? | ❌ Không trùng — riêng cho AI |
| Issue | Sprint 3 chưa có issue cụ thể |

### Service 1: `SohPredictionBackgroundService` (overall.md §30.5)

**Tần suất:** hourly per asset (configurable).

**Files:**
- `BatteryService.Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs`

**Pseudocode:**
```
Mỗi 1h:
  For each BatteryAsset có Status=Active:
    readings = SensorReadingProvider.GetLast30(asset.Id)
    if readings == null: continue  // Chưa đủ data

    result = await aiClient.PredictSohAsync(asset.Id, readings)

    Save SohPrediction entity với metadata

    // So sánh với previous
    previous = repo.GetLatestSohPrediction(asset.Id, before: now-24h)

    if previous != null and (previous.Soh - result.Soh) > 5:
      → publish SohRapidDegradationEvent

    if result.Soh < 80 and previous?.Soh >= 80:
      → publish SohWarningEvent      // auto ticket Warning (TicketService)

    if result.Soh < 60 and previous?.Soh >= 60:
      → publish SohCriticalEvent     // auto ticket Critical
```

### Service 2: `AnomalyClassificationOnAlertConsumer` (overall.md §30.5)

**Internal consumer (in-process)** khi `ThresholdAnomalyDetector` trigger alert.

**Files:**
- `BatteryService.Infrastructure/Consumers/AnomalyClassificationOnAlertConsumer.cs`

**Pseudocode:**
```
On Alert created (from ThresholdAnomalyDetector):
  readings = SensorReadingProvider.GetLast30(asset.Id)
  classification = await aiClient.ClassifyAnomalyAsync(asset.Id, readings)

  Save AnomalyClassification entity (link với Alert.Id)

  // Enrich Alert
  alert.AiClassification = classification.Classification
  alert.AiAnomalyScore = classification.AnomalyScore
  alert.Severity = MapToSeverity(classification.Classification)

  // Publish updated event
  publish BatteryAnomalyDetectedEvent { ..., Classification, AnomalyScore, AiModelVersion }
```

### Integration với existing `ThresholdAnomalyDetector`

⚠️ **Trùng với existing service** — phải **mở rộng**, không tạo mới:
- `ThresholdAnomalyDetector` đã có ở Sprint 3 (issue #76, theo `overall.md §17` line 3639)
- AI integration **nối thêm vào sau** detector — pattern Hybrid theo §30.2

### Acceptance criteria

- [ ] SohPredictionBackgroundService chạy hourly, có log structured
- [ ] AnomalyClassificationOnAlertConsumer trigger sau threshold (không trước)
- [ ] Save entity với đầy đủ metadata (LatencyMs, ModelVersion)
- [ ] Publish event với fields mới (Classification, AnomalyScore, AiModelVersion)
- [ ] Test mock IAiInferenceClient verify flow

---

## Nhóm 5 — Integration Events

> Update event hiện có + thêm 4 event mới.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.6 (Updated BatteryAnomalyDetectedEvent) |
| Sprint | **Sprint 3** (event existing) + **Sprint 5** (consumer ở TicketService) |
| Trùng task overall.md? | ⚠️ **`BatteryAnomalyDetectedEvent` đã được publish ở Sprint 3 issue #78** (`§17` line 3644) — chỉ thêm field, không tạo mới |
| Sprint 5 consumer | Issue #142 (`§17` line 3675) — `BatteryAnomalyDetectedConsumer` ở TicketService |

### Event 1: Update `BatteryAnomalyDetectedEvent` (overall.md §30.6)

**Đã tồn tại từ Sprint 3** — chỉ thêm fields:

```csharp
public record BatteryAnomalyDetectedEvent : IntegrationEvent
{
    // ... fields cũ ...
    public AnomalyClassificationEnum Classification { get; init; }  // NEW
    public decimal AnomalyScore { get; init; }                       // NEW
    public decimal? CurrentSohPercent { get; init; }                 // NEW
    public string AiModelVersion { get; init; } = string.Empty;      // NEW
}
```

**File:** `SharedContracts/Events/BatteryAnomalyDetectedEvent.cs`

⚠️ **Backward compat:** Service đã consume event này → phải đảm bảo TicketService có thể handle event với/không có AI fields (default values).

### Event 2: `SohRapidDegradationEvent` (NEW, overall.md §30.5)

```csharp
public record SohRapidDegradationEvent : IntegrationEvent
{
    public Guid BatteryAssetId { get; init; }
    public decimal PreviousSohPercent { get; init; }
    public decimal CurrentSohPercent { get; init; }
    public decimal DropPercent { get; init; }
    public string AiModelVersion { get; init; } = string.Empty;
}
```

### Event 3: `SohWarningEvent` (NEW, overall.md §30.5)

```csharp
public record SohWarningEvent : IntegrationEvent
{
    public Guid BatteryAssetId { get; init; }
    public decimal CurrentSohPercent { get; init; }  // < 80%
    public string AiModelVersion { get; init; } = string.Empty;
}
```

→ TicketService consume → auto-tạo ticket Warning.

### Event 4: `SohCriticalEvent` (NEW, overall.md §30.5)

```csharp
public record SohCriticalEvent : IntegrationEvent
{
    public Guid BatteryAssetId { get; init; }
    public decimal CurrentSohPercent { get; init; }  // < 60%
    public string AiModelVersion { get; init; } = string.Empty;
}
```

→ TicketService consume → auto-tạo ticket Critical.

### Event 5: `AiModelDriftDetectedEvent` (NEW, overall.md §48.5 + §57.6)

```csharp
public record AiModelDriftDetectedEvent : IntegrationEvent
{
    public decimal KlDivergence { get; init; }
    public DateTime DetectedAt { get; init; }
    public string CurrentModelVersion { get; init; } = string.Empty;
}
```

→ NotificationService consume → notify AI team.

### Consumer cần update bên TicketService

⚠️ **Trùng task overall.md Sprint 5 issue #142** (line 3675):
- `BatteryAnomalyDetectedConsumer` đã được lên kế hoạch consume event
- Phải **thêm logic** xử lý 3 SohXxxEvent mới
- 3 consumer mới: `SohRapidDegradationConsumer`, `SohWarningConsumer`, `SohCriticalConsumer` — tạo tickets

### Acceptance criteria

- [ ] 4 event mới định nghĩa trong `SharedContracts`
- [ ] `BatteryAnomalyDetectedEvent` thêm 4 field, backward compatible
- [ ] Publish event SAU `CommitTransactionAsync()` (BE rule §11 — không có Outbox)
- [ ] TicketService consume 4 event mới + auto-create ticket
- [ ] Inbox idempotency cho mỗi consumer

---

## Nhóm 6 — REST Endpoints

> 13+ endpoint cho FE/Mobile/Admin.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.7 (New endpoints) + §48.2 (feedback stats) + §57.8 (admin advanced) |
| Sprint | **Sprint 3** (basic GET endpoints) + **Sprint 5B/6** (feedback) + **Sprint 7+** (admin advanced) |
| Trùng task overall.md? | ❌ Không trùng — endpoints riêng AI |

### Files cần tạo

| File | Tác dụng |
|------|---------|
| `BatteryService.Api/Controllers/SohPredictionController.cs` | 2 GET endpoint |
| `BatteryService.Api/Controllers/AnomalyClassificationController.cs` | 2 endpoint (GET + POST feedback) |
| `BatteryService.Api/Controllers/AiManagementController.cs` | Admin endpoints |

### Endpoint catalog đầy đủ

#### Sprint 3 — Basic queries

| Method | Route | Auth | Tác dụng |
|--------|-------|------|---------|
| GET | `/api/battery-assets/{id}/soh-prediction` | Customer (own) / Staff / Manager | SOH mới nhất |
| GET | `/api/battery-assets/{id}/soh-history?from=&to=` | Customer (own) / Staff / Manager | Trend chart FE |
| GET | `/api/battery-assets/{id}/anomaly-classifications` | Customer (own) / Staff / Manager | List classification |

#### Sprint 5B/6 — Feedback loop

| Method | Route | Auth | Tác dụng |
|--------|-------|------|---------|
| POST | `/api/v1/anomaly-classifications/{id}/feedback` | Staff | Confirm correct / false positive (overall.md §48.1) |

#### Sprint 7+ — Admin & monitoring

| Method | Route | Auth | Tác dụng |
|--------|-------|------|---------|
| GET | `/api/v1/ai/model-info` | Admin | Current version + last retrain |
| GET | `/api/v1/ai/inference-latency-stats` | Admin | P50/P95/P99 (overall.md §30.7) |
| GET | `/api/v1/ai/health` | Admin/Internal | Proxy to AI `/health` |
| GET | `/api/v1/ai/feedback-stats?from=&to=` | Admin | TP/FP/FN rates (overall.md §48.2) |
| GET | `/api/v1/admin/ai/models` | Admin | List versions (overall.md §57.8) |
| PUT | `/api/v1/admin/ai/models/{ver}/promote` | Admin | Promote version |
| POST | `/api/v1/admin/ai/models/{ver}/rollback` | Admin | Rollback |
| POST | `/api/v1/admin/ai/retrain-trigger` | Admin | Manual trigger retrain |
| GET | `/api/v1/admin/ai/drift-status` | Admin | Drift status |
| GET | `/api/v1/admin/ai/inference-stats?from=&to=` | Admin | Detailed stats |

### CQRS commands/queries cần tạo

```
Application/Queries/
├── GetSohPrediction/GetSohPredictionQuery + Handler
├── GetSohHistory/GetSohHistoryQuery + Handler
├── GetAnomalyClassifications/GetAnomalyClassificationsQuery + Handler
├── GetAiModelInfo/GetAiModelInfoQuery + Handler
├── GetAiFeedbackStats/GetAiFeedbackStatsQuery + Handler  // §48.2
├── GetAiInferenceLatencyStats/Query + Handler
├── GetAiModels/Query + Handler                          // §57.8
├── GetAiDriftStatus/Query + Handler                     // §57.6
└── GetAiInferenceStats/Query + Handler

Application/Commands/
├── SubmitAnomalyFeedback/Command + Handler              // §48.1
├── PromoteAiModel/Command + Handler                     // §57.8
├── RollbackAiModel/Command + Handler
└── TriggerAiRetrain/Command + Handler
```

### Authorization

- Customer endpoints: ownership check (chỉ pin của mình) — cross-cutting rule `overall.md §20`
- Admin endpoints: policy `AdminOnly` (theo `.claude/rules/tech/be.md §12`)

### Acceptance criteria

- [ ] Tất cả endpoints theo `[ApiController]` + `[Route("api/...")]` pattern
- [ ] Controller chỉ gọi `_mediator.Send()` — no business logic
- [ ] Authorization đúng theo role
- [ ] Swagger documented đầy đủ
- [ ] Integration test cho mỗi endpoint (happy + error)

---

## Nhóm 7 — Caching Layer

> Redis cache để giảm load AI 90%.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.8 (Caching strategy AI) |
| Sprint | **Sprint 3** (cùng với endpoint) |
| Trùng task overall.md? | ❌ Không trùng — ICacheService đã có ở SharedInfrastructure |

### Cache keys & TTL (overall.md §30.8)

| Cache key | TTL | Tác dụng |
|-----------|-----|---------|
| `soh:latest:{assetId}` | **5 phút** | Cache trong `GetSohPredictionQueryHandler` |
| `anomaly:alert:{alertId}` | **1 giờ** | Cache trong `GetAnomalyClassificationQueryHandler` |
| `ai:model-info` | **10 phút** | Cache trong `GetAiModelInfoQueryHandler` |

### Implementation pattern

```csharp
public class GetSohPredictionQueryHandler : IRequestHandler<GetSohPredictionQuery, ...>
{
    private readonly ICacheService _cache;
    private readonly IUnitOfWork _uow;

    public async Task<...> Handle(GetSohPredictionQuery req, CancellationToken ct)
    {
        var cacheKey = $"soh:latest:{req.AssetId}";
        var cached = await _cache.GetAsync<SohPredictionDTO>(cacheKey);
        if (cached != null) return new CommonResponse<SohPredictionDTO> { IsSuccess = true, Data = cached };

        var entity = await _uow.SohPredictions.GetAllAsync()
            .Where(x => !x.IsDeleted && x.BatteryAssetId == req.AssetId)
            .OrderByDescending(x => x.PredictedAt)
            .FirstOrDefaultAsync(ct);

        if (entity == null) return new CommonResponse<SohPredictionDTO> { IsSuccess = false, Message = "Not found" };

        var dto = MapToDto(entity);
        await _cache.SetAsync(cacheKey, dto, TimeSpan.FromMinutes(5));
        return new CommonResponse<SohPredictionDTO> { IsSuccess = true, Data = dto };
    }
}
```

### Cache invalidation

| Khi nào | Invalidate key |
|---------|---------------|
| `SohPredictionBackgroundService` save mới | `soh:latest:{assetId}` |
| `AnomalyClassificationOnAlertConsumer` save mới | `anomaly:alert:{alertId}` |
| Admin promote model version | `ai:model-info` |

### Acceptance criteria

- [ ] 3 cache strategy implement đúng TTL
- [ ] Invalidate khi data thay đổi
- [ ] Test cache hit miss

---

## Nhóm 8 — Fallback Logic

> Đảm bảo BE vận hành khi AI down.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.11 (Fallback khi AI down) |
| Sprint | **Sprint 3** (cùng AI Bridge) |
| Trùng task overall.md? | ❌ Không trùng — fallback riêng AI |
| ADR | ADR-013 (Hybrid threshold + AI anomaly detection) — `overall.md §40` line 5290 |
| Q&A demo | Q3 trong `overall.md §56.10` line 7310 |
| DR runbook | `05-ai-module-down.md` — `overall.md §40` line 5350 |

### Logic chính

```csharp
public class AnomalyClassificationOnAlertConsumer
{
    public async Task ConsumeAsync(AlertCreated alert)
    {
        try
        {
            var classification = await _aiClient.ClassifyAnomalyAsync(alert.AssetId, readings, ct);
            // Save với classification từ AI
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("AI circuit breaker open — fallback to threshold-only");
            // Save với Classification = Unknown
            SaveAnomalyClassification(alert.AssetId, AnomalyClassificationEnum.Unknown, null);
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogWarning("AI timeout — fallback");
            SaveAnomalyClassification(alert.AssetId, AnomalyClassificationEnum.Unknown, null);
        }
    }
}
```

### Health endpoint cho FE banner

Endpoint `GET /api/v1/ai/health` trả status:
- `{"status": "healthy", ...}` → FE không hiển thị banner
- `{"status": "unavailable", ...}` → FE hiển thị **"AI service unavailable — basic detection only"**

### Chaos test (overall.md §40 — line 5683)

`tc qdisc add` simulate network partition AI Module → verify:
- Circuit breaker open
- Fallback threshold-only chạy đúng
- Alert vẫn tạo (với Classification=Unknown)

### Acceptance criteria

- [ ] Catch `BrokenCircuitException`, `TimeoutRejectedException`, `HttpRequestException`
- [ ] Fallback save Classification=Unknown
- [ ] Health endpoint trả status đúng
- [ ] Chaos test PASS

---

## Nhóm 9 — Reporting & Analytics

> Feedback stats + Export training data + Drift detection.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §48.1, §48.2, §48.3, §48.5, §57.6 |
| Sprint | **Sprint 5B/6** (B2-finalize, feedback UI) + **Sprint 8** (feedback report) |
| Sprint impact line | §50 line 5907 — "Sprint 8 + AI feedback report" |
| Issue Sprint 5B | #153 (B2-finalize) |
| Trùng task overall.md? | ⚠️ **§48 P1 task — coverage Sprint 5B/6, finalize Sprint 8** |

### 9.1. Staff feedback endpoint (overall.md §48.1)

**Đã đề cập ở Nhóm 6** — endpoint `POST /api/v1/anomaly-classifications/{id}/feedback`.

Schema body:
```json
{
  "isCorrect": true,
  "actualClassification": "Failed",
  "actualSohPercent": 62.5,
  "note": "Đúng, BMS module failed"
}
```

### 9.2. AI accuracy reporting query (overall.md §48.2)

`GetAiFeedbackStatsQueryHandler`:

```sql
SELECT
    COUNT(*) as totalPredictions,
    COUNT(staff_feedback) as totalFeedback,
    SUM(CASE WHEN staff_feedback = 1 THEN 1 ELSE 0 END) * 1.0
        / NULLIF(COUNT(staff_feedback), 0) as truePositiveRate,
    AVG(ABS(predicted_soh - actual_soh)) as sohMae
FROM anomaly_classifications
WHERE classified_at BETWEEN @from AND @to;
```

Response:
```json
{
  "totalPredictions": 1250,
  "totalFeedback": 320,
  "feedbackRate": 0.256,
  "truePositiveRate": 0.85,
  "falsePositiveRate": 0.10,
  "falseNegativeRate": 0.05,
  "sohMaePercent": 1.8,
  "modelVersion": "1.0"
}
```

### 9.3. Export training data background service (overall.md §48.3)

`AiTrainingDataExportBackgroundService` monthly:
- Lấy tất cả `AnomalyClassification` có `StaffFeedback != null` trong tháng
- Join với sensor readings tương ứng (30 timestep)
- Export **Parquet** file → MinIO bucket `ai-training-data/{year-month}.parquet`
- AI team download để retrain

**File:** `BatteryService.Infrastructure/BackgroundServices/AiTrainingDataExportBackgroundService.cs`

**Lib:** [Parquet.Net](https://github.com/aloneguid/parquet-dotnet) (NuGet).

⚠️ **Trùng task overall.md:** MinIO đã setup từ Sprint 1 cho FileStorageService (`§17` line 3596). Tái dùng bucket riêng `ai-training-data`.

### 9.4. Drift detection background service (overall.md §48.5 + §57.6)

`AiDriftDetectionBackgroundService` weekly:
- Lấy distribution prediction tuần này vs tuần trước
- Compute KL divergence
- Nếu > 0.2 → publish `AiModelDriftDetectedEvent`

**File:** `BatteryService.Infrastructure/BackgroundServices/AiDriftDetectionBackgroundService.cs`

> Có thể chia: BE compute statistics, AI team handle KL math. Quyết định khi sprint planning.

### Acceptance criteria

- [ ] Feedback endpoint authorized Staff role
- [ ] Stats query handler tested với data fake
- [ ] Parquet export job tested local (MinIO docker)
- [ ] Drift detection chạy weekly + publish event đúng threshold

---

## Nhóm 10 — Testing

> Đảm bảo coverage ≥ 80% (BE rule) và performance.

### Cross-reference

| Item | Reference |
|------|-----------|
| Section overall.md | §30.13 (Tests bắt buộc) + §11 (Test strategy) |
| Sprint | Mỗi sprint song song với code |
| Coverage gate overall.md | §11.6 — line coverage ≥ 80% |
| Trùng task overall.md? | ❌ Không trùng — test riêng AI integration |

### 10.1. Unit tests bắt buộc (overall.md §30.13)

```
BatteryService.Application.UnitTests/AI/
├── AiInferenceClientTests
│   - test_timeout_throws_timeoutexception
│   - test_retry_3_times_on_transient_error
│   - test_circuit_breaker_opens_after_50_percent_fail
│   - test_deserialize_valid_response
│   - test_handle_500_error
│   - test_health_endpoint_returns_status

├── SohPredictionBackgroundServiceTests
│   - test_publishes_warning_when_soh_drops_below_80
│   - test_publishes_critical_when_soh_drops_below_60
│   - test_publishes_rapid_degradation_when_drop_over_5_in_24h
│   - test_skips_asset_with_less_than_30_readings
│   - test_saves_with_correct_metadata

├── AnomalyClassificationOnAlertConsumerTests
│   - test_enriches_alert_with_ai_classification
│   - test_creates_anomaly_classification_entity
│   - test_fallback_to_unknown_on_circuit_breaker
│   - test_publishes_battery_anomaly_event_with_ai_fields

├── SensorReadingProviderTests
│   - test_returns_null_when_insufficient_readings
│   - test_orders_ascending_by_timestamp
│   - test_rejects_outlier_readings

├── LabelingTests (mapping rule)
│   - (Phía AI làm, BE chỉ cần verify response handling)

└── Cqrs Handler tests
   - GetSohPredictionQueryHandlerTests
   - SubmitAnomalyFeedbackCommandHandlerTests
   - GetAiFeedbackStatsQueryHandlerTests
```

### 10.2. Integration tests (overall.md §30.13)

```
BatteryService.IntegrationTests/AI/
├── AiIntegrationTests (với WireMock.Net mock AI server)
│   - test_full_flow_ingest_threshold_ai_publish_event
│   - test_ai_down_fallback_threshold_only
│   - test_caching_works_5min_ttl

├── EndToEndScenarios
│   - test_battery_degradation_over_30_days_creates_warning_ticket
│   - test_critical_anomaly_creates_critical_ticket_within_5min
```

### 10.3. Performance test (overall.md §30.13)

```csharp
[Fact]
public async Task Performance_100_concurrent_classify_p95_under_100ms()
{
    var tasks = Enumerable.Range(0, 100)
        .Select(_ => _aiClient.ClassifyAnomalyAsync(assetId, readings, ct))
        .ToList();

    var stopwatch = Stopwatch.StartNew();
    await Task.WhenAll(tasks);
    stopwatch.Stop();

    var p95 = tasks.Select(t => t.Result.LatencyMs).OrderBy(x => x).ElementAt(95);
    Assert.True(p95 < 100, $"P95 latency {p95}ms > 100ms");
}
```

### 10.4. Chaos test (overall.md §40 — line 5683)

```bash
# Simulate AI module partition
tc qdisc add dev eth0 root netem loss 100%

# Verify BE:
# - Circuit breaker mở
# - Alert vẫn tạo với Classification=Unknown
# - No exception leaks to controller
```

### Acceptance criteria

- [ ] Coverage ≥ 80% line cho code AI integration
- [ ] WireMock.Net mock AI server cho integration test
- [ ] Performance test P95 < 100ms PASS
- [ ] Chaos test (network partition) PASS

---

# Phần III — Backlog Matrix theo Sprint

> Bảng tổng hợp tất cả task BE cho AI, mapping với sprint trong `overall.md §17` và issue number.

## Sprint 1 (11/5–24/5/2026) — Foundations

| Task | Status overall.md | Issue | Trùng task overall.md? |
|------|-------------------|-------|----------------------|
| B2-draft: skeleton `.claude/docs/ai-research-references.md` | overall.md §17 line 3609 | **#147** | ⚠️ **Đã trong overall.md** |

## Sprint 2 (25/5–7/6/2026) — BatteryService MVP

| Task | Section overall.md | Issue | Trùng task overall.md? |
|------|-------------------|-------|----------------------|
| **AI Bridge client skeleton** (Nhóm 1) | §30.4, Sprint impact line 5899 | TBD | ⚠️ Đã list trong sprint impact, chưa có issue chính thức |

**Lưu ý Sprint 2:** Theo `overall.md §50` line 5899 — Sprint 2 phải làm thêm "AI Bridge client skeleton" → **multiplier 1.4× — cần thêm 1 dev hoặc kéo dài 3 ngày**.

## Sprint 3 (8/6–21/6/2026) — Anomaly engine + AI Hybrid

| Task | Section overall.md | Issue | Trùng task overall.md? |
|------|-------------------|-------|----------------------|
| `ThresholdAnomalyDetector` (8 anomaly types) | §17 line 3639 | **#76** | ✅ Đã có |
| `AlertDeduplicationService` | §17 line 3640 | **#76** | ✅ Đã có |
| `ThresholdCheckBackgroundService` | §17 line 3641 | **#77** | ✅ Đã có |
| Publish `BatteryAnomalyDetectedEvent` | §17 line 3644 | **#78** | ✅ Đã có |
| **Nhóm 1 — AI Bridge full impl** | §30.4 | TBD | Tiếp tục từ Sprint 2 |
| **Nhóm 2 — `SohPrediction` + `AnomalyClassification` entities + migration `AddSohPredictionTables`** | §30.3, §50 line 5975 | TBD | ⚠️ **Migration name đã định trong §50** |
| **Nhóm 3 — `SensorReadingProvider` + validation** | implicit §30.5 | TBD | ❌ Mới |
| **Nhóm 4 — `SohPredictionBackgroundService`** | §30.5 | TBD | ❌ Mới |
| **Nhóm 4 — `AnomalyClassificationOnAlertConsumer`** | §30.5 | TBD | ❌ Mới |
| **Nhóm 5 — Update `BatteryAnomalyDetectedEvent` + 3 SohXxxEvent + `AiModelDriftDetectedEvent`** | §30.6 | TBD | ⚠️ Event đã publish ở #78, chỉ thêm field |
| **Nhóm 6 — 3 basic GET endpoint** (soh-prediction, soh-history, anomaly-classifications) | §30.7 | TBD | ❌ Mới |
| **Nhóm 7 — Cache 3 strategy** | §30.8 | TBD | ❌ Mới |
| **Nhóm 8 — Fallback logic + health endpoint** | §30.11 | TBD | ❌ Mới |
| **Nhóm 10 — Unit + integration tests** | §30.13 | TBD | ❌ Mới |

**Lưu ý Sprint 3:** Theo `overall.md §50` line 5900 — Sprint 3 multiplier **1.6× — cân nhắc tách thành Sprint 3a + 3b**.

## Sprint 4 (22/6–5/7/2026) — TicketService foundation

Sprint 4 chủ yếu TicketService, không có task AI mới. Nhưng vẫn cần:

| Task | Lý do |
|------|-------|
| Tests Sprint 3 AI integration vẫn maintain ≥ 80% | BE rule |

## Sprint 5 (6/7–19/7/2026) — TicketService workflow

| Task | Section overall.md | Issue | Trùng task? |
|------|-------------------|-------|------------|
| `BatteryAnomalyDetectedConsumer` ở TicketService → auto-create ticket | §17 line 3675 | **#142** | ✅ **Đã có** — phải update consume thêm field AI |
| **3 consumer mới ở TicketService:** `SohRapidDegradationConsumer`, `SohWarningConsumer`, `SohCriticalConsumer` | §30.5 (events) | TBD | ❌ Mới — phải thêm vào Sprint 5 |

## Sprint 5B (20/7–26/7/2026) — Advanced monitoring

| Task | Section overall.md | Issue | Trùng task? |
|------|-------------------|-------|------------|
| **B2-finalize: AI research references paper cite** | §17 line 3713 | **#153** | ✅ Đã có |
| **Nhóm 3 update — Stress score từ CycleLog** (input AI nâng cao) | §53.7 | TBD | ⚠️ §53.7 thuộc Sprint cycle log — coordinate |
| **Nhóm 6 — Feedback endpoint** (`POST /api/v1/anomaly-classifications/{id}/feedback`) | §48.1 | TBD | ❌ Mới |
| **Nhóm 9 — Staff feedback UI integration** | §48.1 | TBD | ❌ Mới |

## Sprint 6 (27/7–9/8/2026) — NotificationService

| Task | Section overall.md | Issue | Trùng task? |
|------|-------------------|-------|------------|
| Notification consumer cho `SohWarningEvent`, `SohCriticalEvent`, `AiModelDriftDetectedEvent` | §17 line 3737 (15 consumers) | **#107** | ⚠️ **Đã list "15 consumers" trong §17** — phải confirm có cover AI events |

## Sprint 7 (10/8–23/8/2026) — Reports + Gateway + Advanced AI

| Task | Section overall.md | Issue | Trùng task? |
|------|-------------------|-------|------------|
| **Nhóm 6 — Admin AI endpoints** (model-info, latency-stats, models management) | §30.7, §57.8 | TBD | ❌ Mới |
| **Nhóm 9 — `GetAiFeedbackStatsQueryHandler` + endpoint** | §48.2 | TBD | ❌ Mới |
| **Nhóm 9 — `AiTrainingDataExportBackgroundService` (monthly Parquet)** | §48.3 | TBD | ❌ Mới |
| **Nhóm 9 — `AiDriftDetectionBackgroundService` (weekly)** | §48.5, §57.6 | TBD | ❌ Mới |
| **Nhóm 4 — Inference batching support** (modify `SohPredictionBackgroundService` collect 32 trong 100ms window) | §57.3 | TBD | ❌ Mới |
| Battery Health dashboard (gồm SOH/DCIR/Imbalance) | §17 line 3759 | **#117** | ⚠️ **Đã có** — phải tích hợp SOH data từ AI |
| AlertManager rule cho AI inference latency | §17 line 3760 | **#118** | ⚠️ Đã có — phải thêm rule `ai_inference_latency_p95 > 100ms` |
| **Nhóm 10 — Chaos test AI module down** | §40 line 5683 | TBD | ❌ Mới |

## Sprint 8 (24/8–6/9/2026) — Demo prep

| Task | Section overall.md | Issue | Trùng task? |
|------|-------------------|-------|------------|
| **AI feedback report finalize** | §50 line 5907 | TBD | ⚠️ Đã list Sprint impact |
| Demo Scene 8 — AI feedback loop | §56 line 7179 | TBD | ⚠️ Đã có script demo |
| Q&A preparation — Q3 "AI fail thì sao?" | §56.10 line 7310 | TBD | ⚠️ Đã có template |
| Bug fix AI integration | §17 line 3778 | **#124** | ✅ Đã có |

---

# Phần IV — Configuration & DI

## 4.1. `appsettings.json` (BatteryService.Api)

```json
{
  "AI": {
    "BaseUrl": "http://ai-module:8000",
    "TimeoutMs": 200,
    "RetryCount": 2,
    "CircuitBreakerFailureRatio": 0.5,
    "CircuitBreakerSamplingDurationSeconds": 30,
    "CircuitBreakerDurationSeconds": 60,
    "ModelVersionExpected": "1.0"
  },
  "AI:Cache": {
    "SohLatestTtlMinutes": 5,
    "AnomalyAlertTtlMinutes": 60,
    "ModelInfoTtlMinutes": 10
  },
  "AI:BackgroundServices": {
    "SohPredictionIntervalMinutes": 60,
    "DriftDetectionIntervalDays": 7,
    "TrainingDataExportSchedule": "0 0 1 * *"
  }
}
```

## 4.2. DI Registration

`BatteryService.Infrastructure/DependencyInjection/AiInferenceDependencyInjection.cs`:

```csharp
public static IServiceCollection AddAiInference(this IServiceCollection services, IConfiguration config)
{
    services.Configure<AiInferenceOptions>(config.GetSection("AI"));

    services.AddHttpClient<IAiInferenceClient, AiInferenceClient>((sp, c) => {
        var opts = sp.GetRequiredService<IOptions<AiInferenceOptions>>().Value;
        c.BaseAddress = new Uri(opts.BaseUrl);
    })
    .AddPolicyHandler((sp, _) => GetTimeoutPolicy(sp))
    .AddPolicyHandler((sp, _) => GetRetryPolicy(sp))
    .AddPolicyHandler((sp, _) => GetCircuitBreakerPolicy(sp));

    services.AddScoped<ISensorReadingProvider, SensorReadingProvider>();
    services.AddHostedService<SohPredictionBackgroundService>();
    services.AddHostedService<AiDriftDetectionBackgroundService>();
    services.AddHostedService<AiTrainingDataExportBackgroundService>();

    return services;
}
```

`Program.cs`:
```csharp
builder.Services.AddAiInference(builder.Configuration);
```

## 4.3. Docker compose (overall.md §30.9)

```yaml
services:
  battery-service:
    environment:
      AI__BaseUrl: http://ai-module:8000
      AI__ModelVersionExpected: "1.0"
    depends_on:
      ai-module:
        condition: service_healthy
```

---

# Phần V — Definition of Done

## 5.1. Per task

Theo `.claude/rules/workflow.md`:
- [ ] `/kltn-plan` → plan.md đã approve
- [ ] Code implement
- [ ] `/kltn-reviewcode` PASS
- [ ] `/kltn-test` PASS với coverage ≥ 80%
- [ ] `/kltn-ship` → PR + label `status: reviewing`
- [ ] Reviewer `/kltn-reviewpr` → APPROVE
- [ ] Author `/kltn-complete` → merge

## 5.2. Per nhóm component AI (production-ready)

| Nhóm | DoD |
|------|-----|
| 1. AI Bridge Client | Polly 3 policy active + unit test mock HttpClient + integration test với WireMock |
| 2. Database | Migration rollback test PASS + 2 entity extend AuditableEntity |
| 3. Sensor Provider | Query benchmark < 50ms + validate outlier/gap |
| 4. Background Services | Test logic publish event đúng threshold + handle failure gracefully |
| 5. Events | Backward compat khi thêm field + Inbox idempotency cho consumer |
| 6. Endpoints | Swagger docs + Authorization + integration test happy + error |
| 7. Caching | Hit miss test + invalidation đúng |
| 8. Fallback | Catch tất cả exception types + Classification=Unknown + health endpoint |
| 9. Reporting | Stats query tested + Parquet export tested local MinIO |
| 10. Testing | Coverage ≥ 80% + Chaos test PASS + P95 < 100ms |

## 5.3. End-to-end (Sprint 8 demo)

- [ ] Demo Scene 4 (alert → AI classify Failed) work
- [ ] Demo Scene 8 (Staff feedback) work
- [ ] Chaos test: tắt AI module → BE vẫn alert được
- [ ] Grafana dashboard "AI inference latency" có data thật
- [ ] Q&A doc Q3 "AI fail thì sao?" đầy đủ

---

# Phần VI — Risk & Mitigation

## 6.1. Risks

| Risk | Severity | Mitigation |
|------|---------|-----------|
| AI dev chậm Sprint 4 → BE không có AI để integrate Sprint 5 | High | BE viết WireMock.Net stub trước, integrate sau khi AI ship |
| Sprint 2 + Sprint 3 overload (1.4× + 1.6× theo §50) | High | Tách Sprint 3 → 3a/3b, hoặc thuê thêm dev |
| API contract đổi giữa sprint | High | **Ký contract trước Sprint 4**, freeze cho đến Sprint 8 |
| Latency AI > 100ms khi production data lớn | Medium | Inference batching Sprint 7 (§57.3) + HPA scaling (§57.5) |
| Migration `AddSohPredictionTables` xung đột với migrations song song | Medium | Migration ordering theo `.claude/rules/tech/be.md §14` |
| Feedback data ít (Staff không submit) → không retrain được | Medium | UI gentle nudge sau resolve + accuracy report cho Manager |

## 6.2. Dependency với AI team

| BE blocked by | AI team deliverable | Deadline |
|---------------|---------------------|----------|
| AI Bridge Client integration test | FastAPI endpoint stub | End of Sprint 4 |
| Full integration | Production-ready model v1.0 | Mid Sprint 5 |
| Inference batching | Endpoint `/predict/soh/batch` | Start of Sprint 7 |
| Drift detection logic | KL divergence implementation | Mid Sprint 7 |

---

# Phần VII — Cross-reference Index

## 7.1. Index theo section `overall.md`

| Overall.md section | Nhóm trong file này |
|-------------------|--------------------|
| §30.1 Bối cảnh | Phần I |
| §30.2 Hybrid pattern | Phần I.4 (qua reference `ai.md`) |
| §30.3 Entities | Nhóm 2 |
| §30.4 AI Bridge | Nhóm 1 |
| §30.5 Background services | Nhóm 4 |
| §30.6 Updated event | Nhóm 5 |
| §30.7 Endpoints | Nhóm 6 |
| §30.8 Caching | Nhóm 7 |
| §30.9 Docker compose | Phần IV |
| §30.10 Monitoring | Nhóm 10 (test) + ai.md §32 |
| §30.11 Fallback | Nhóm 8 |
| §30.12 Feedback loop | Nhóm 9 |
| §30.13 Tests | Nhóm 10 |
| §48.1 Staff feedback | Nhóm 9.1 |
| §48.2 Accuracy reporting | Nhóm 9.2 |
| §48.3 Export training data | Nhóm 9.3 |
| §48.4 A/B testing | (Optional sprint 7+) |
| §48.5 Drift detection | Nhóm 9.4 |
| §53.7 SOH cycle integration | Nhóm 3 (stress score) |
| §57.1 CI/CD model deploy | (Phía AI team, ai.md §35) |
| §57.2 Retraining trigger | Phần II.6 (admin trigger endpoint) |
| §57.3 Inference batching | Nhóm 4 (modify SohPredictionBackgroundService) |
| §57.4 Model versioning | (Phía AI team, ai.md §24) |
| §57.5 HPA scaling | (Phía AI team + K8s config) |
| §57.6 Drift detection job | Nhóm 9.4 |
| §57.7 A/B test framework | Phần II.6 admin endpoint |
| §57.8 Admin endpoints | Nhóm 6 (Sprint 7+) |

## 7.2. Index theo Sprint

| Sprint | Tasks AI cho BE | Multiplier |
|--------|----------------|-----------|
| Sprint 1 | B2-draft AI references (#147) | 1.2× |
| Sprint 2 | Nhóm 1 skeleton | **1.4× ⚠️** |
| Sprint 3 | Nhóm 1-8, 10 full impl | **1.6× ⚠️** |
| Sprint 4 | Maintenance | 1.0× |
| Sprint 5 | TicketService consumers AI events | 1.0× |
| Sprint 5B | B2-finalize (#153) + feedback endpoint + cycle stress | **1.3× ⚠️** |
| Sprint 6 | NotificationService consumers AI events | 1.0× |
| Sprint 7 | Advanced AI (admin endpoints, drift, batching, export) | 1.0× |
| Sprint 8 | AI feedback report finalize + demo prep | 1.0× |

## 7.3. Index theo Issue overall.md

| Issue # | Mô tả | Section overall.md | Liên quan AI? |
|---------|-------|-------------------|--------------|
| #76 | ThresholdAnomalyDetector | line 3639 | ✅ AI gọi sau threshold |
| #77 | ThresholdCheckBackgroundService | line 3641 | ✅ Tích hợp AI consumer |
| #78 | OutboxRelay + BatteryAnomalyDetectedEvent | line 3644 | ✅ Event sẽ thêm fields AI |
| #107 | 15 NotificationService consumers | line 3737 | ⚠️ Confirm AI events có cover |
| #117 | Grafana dashboards (gồm Battery Health) | line 3759 | ⚠️ Tích hợp SOH AI data |
| #118 | AlertManager rules | line 3760 | ⚠️ Thêm AI latency rule |
| #142 | BatteryAnomalyDetectedConsumer ở TicketService | line 3675 | ✅ Consume thêm SohXxxEvent |
| #147 | B2-draft AI references skeleton | line 3609 | ✅ Trực tiếp |
| #153 | B2-finalize AI references paper cite | line 3713 | ✅ Trực tiếp |

## 7.4. Index file liên quan

| File | Vai trò |
|------|---------|
| `ai.md` | Hướng dẫn xây dựng AI Module (Python) |
| `.claude/rules/tech/ai.md` | Coding convention AI bắt buộc |
| `.claude/rules/tech/be.md` | Coding convention BE bắt buộc |
| `.claude/rules/workflow.md` | Quy trình DoD + label tracking |
| `.claude/docs/ai-datasets.md` | NASA/CALCE dataset reference |
| `.claude/docs/ai-research-references.md` | Paper citation (B2 task) |
| `overall.md §30` | Spec tích hợp AI chính |
| `overall.md §48` | Feedback loop spec |
| `overall.md §53.7` | Cycle log integration |
| `overall.md §57` | AI advanced spec |
| `overall.md §40` | ADR-013 Hybrid + DR runbook 05 AI down |
| `overall.md §56.10` | Q&A Q3 "AI fail thì sao?" |

---

## Lời kết

Backend chiếm ~70% công sức tích hợp AI vào sản phẩm thực tế. AI team chỉ tạo 1 inference endpoint thuần, còn lại — feed data, schedule, persist, publish, cache, fallback, monitor, report, retrain pipeline — đều là việc của BE.

**Đọc song song:**
- `ai.md` để hiểu phía AI Module làm gì
- `overall.md §30/§48/§53.7/§57` để có context business
- File này để biết BE phải làm gì cụ thể

**Liên hệ khi có thắc mắc:**
- Leader → planning, sprint split
- AI dev → API contract, latency
- BE dev → integration patterns
