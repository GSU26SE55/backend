# newsprint — Min/Max dòng NẠP / XẢ theo pin (streaming lên UI)

> **Ngày:** 2026-07-08 · **Cập nhật:** 2026-07-14 · **Trạng thái:** ✅ APPROVED — quyết định chốt 2026-07-14 (xem "Quyết định đã chốt" bên dưới). Triển khai qua **Sprint Bonus** (`overall.md §17`, 27 task `#NS-01..27` — 25 issue BE đã tạo GitHub `#646..#670`, 2 FE (NS-05/NS-19) ở repo frontend).
> **Phạm vi:** BatteryService (BE) + FE Web/Mobile. **IoT firmware chỉ sửa 1 chỗ** (NS-24 — đổi nhãn MQ2 `Smoke`→`GasLeak`, quyết định Q10=B); phần còn lại thuần BE + FE.
> **Yêu cầu gốc:** UI hiển thị min/max dòng nạp (charge) và dòng xả (discharge) của từng cục pin, dữ liệu dạng **streaming** (push realtime, không chỉ polling).

---

## Quyết định đã chốt (2026-07-14)

> User chốt 13 quyết định (Q1–Q13). Triển khai qua **Sprint Bonus** (`overall.md §17`, 27 task `#NS-01..27` — 25 issue BE đã tạo GitHub `#646..#670`, 2 FE (NS-05/NS-19) ở repo frontend).

| Q | Chủ đề | Quyết định | Tác động |
|---|---|---|---|
| Q1 | Scope đợt này | **C — Làm toàn bộ** (dàn nhiều phase) | Sprint Bonus gồm cả 27 NS: 6 phase active + 1 nhóm deferred |
| Q2 | Thứ tự noise vs feature | **A — Fix noise TRƯỚC/CÙNG min/max** | NS-07/08/09 vào Phase 1, trước khi lên hardware thật |
| Q3 | Window cho stats | **Chốt chỉ `1h` + `today`** (Claude quyết) | NS-01/03 chỉ 2 window; thêm sau nếu FE cần (rẻ) |
| Q4 | Mở rộng min/max V/T | **A — Làm luôn** min/max Voltage + Temperature | NS-02 thêm 4 field V/T cùng lúc sửa DTO |
| Q5 | N2 `PromotedToAlertId` | **A — Sửa** (gán + retention filter, giữ audit) | NS-10 KHÔNG xoá field |
| Q6 | Ngữ nghĩa `Count` | **A — "5 reading"** (không đổi sang "đợt") | NS-10 chỉ fix dedup + copy SourceType, không thêm cooldown |
| Q7 | NS-11 (N6) | **B — Làm đợt này** (không dời) | NS-11 vào Phase 1 |
| Q8 | R2 khi không có ticket | **A — Auto-tạo ticket P1** | NS-13 tạo ticket `TicketOrigin=System` |
| Q9 | Cách ly pin | **D — CHƯA làm đợt này** (📌 NOTE LÀM SAU) | NS-17/18/19/20 chuyển sang nhóm **Deferred** |
| Q10 | Nhãn MQ2 | **B — Đổi firmware `Smoke`→`GasLeak`** | NS-24 đụng repo `iot` (`mq2.cpp`); `Smoke` giữ cho sensor khói quang học tương lai |
| Q11 | Rule Undertemp | **A — Thêm, `AnomalyTypeEnum.Undertemp = 16`** | NS-25 wire value 16 — ⚠️ đồng bộ FE + mọi service |
| Q12 | Lưu AI classification | **A — Theo spec §30 đầy đủ** (2 bảng + feedback) | NS-26 = `AnomalyClassification` + `SohPrediction` + `StaffFeedback` |
| Q13 | PA-4 continuous agg | **B — Làm đợt này** | NS-06 vào Phase 2 (không còn "optional") |

**⚠️ 2 quyết định cross-service phải báo team ngay:**
- **Q11:** `AnomalyTypeEnum.Undertemp = 16` là **wire value** dùng chung TicketService/NotificationService/FE — phải đồng bộ enum ở mọi nơi trước khi merge.
- **Q12:** Vì chọn spec §30 (bảng riêng), **KHÔNG** thêm `PredictedSohDegradation = 16` vào `AnomalyTypeEnum` như plan `aibeiotrealtime.md` cũ đề xuất → giá trị 16 dành cho `Undertemp`. Kết quả AI đi vào bảng `AnomalyClassification.Classification` (Normal/Degrading/Failed), KHÔNG vào `Alerts.AnomalyType`. Cập nhật lại `aibeiotrealtime.md` ở NS-27.

**Nhóm Deferred (Q9=D — làm sau):** NS-17 (`Isolated` + workflow), NS-18 (telemetry verify), NS-19 (FE checklist), NS-20 (ISO-B BMS FET stretch). Giữ nguyên thiết kế §11 để tái dùng khi mở lại.

---

## 0. TL;DR — Khuyến nghị

Kết hợp **PA-2 + PA-3**:

1. **PA-2 (backfill/chart):** mở rộng `GET /api/sensor-readings/{id}/aggregate` — thêm min/max tách 2 chiều nạp/xả cho từng bucket, và **lọc source `primary`** (hiện đang trộn 3 nguồn/pin — bug tiềm ẩn).
2. **PA-3 (streaming):** tính rolling min/max lúc ingest (state trong Redis), publish **SSE event mới `stats`** trên hạ tầng SSE sẵn có (`GET /api/sensor-readings/stream`). FE nhận push mỗi ~5s, không cần polling.

Firmware đã gửi đủ dữ liệu thô (current có dấu, 5s/mẫu) — mọi thứ làm ở tầng backend + FE.

---

## 1. Hiện trạng đã xác minh (đọc code 2026-07-08)

### 1.1 Dữ liệu thô ĐÃ có sẵn — IoT → Backend

| Thành phần | File / bằng chứng | Ghi chú |
|---|---|---|
| Dòng điện **có dấu** từ BMS thật | `iot/firmware-esp32/src/bms/bms_register_map.cpp:37` — `decodeCurrent()` int16 signed | **`+` = nạp, `−` = xả** (convention thống nhất firmware ↔ backend) |
| Dòng điện từ INA226 (shunt độc lập) | `iot/firmware-esp32/src/sensor/ina226.cpp:66` — `getCurrent()` signed | Nguồn "redundant" cross-check BMS |
| `chargingState` từ BMS | `bms_register_map.cpp:66` — Idle/Charging/Discharging/Float/Bypass | Optional theo model BMS |
| Payload gửi backend | `iot/firmware-esp32/src/core/payload.cpp` — `buildProductionBatchPayload` có `current`, `chargingState` | Khớp `BatchIngestSensorReadingsCommand` |
| Tần suất | `INGEST_INTERVAL_MS = 5000` (`config.example.h:68`) | **1 batch / 5 giây**, 3 reading/pin/tick |
| Lưu trữ | `sensor_readings` — TimescaleDB **hypertable** (migration `20260514051016`) | `SensorReading.Current` decimal, signed |

### 1.2 Hạ tầng streaming ĐÃ có (Sprint BE-IoT-Realtime #614–#618)

```
Ingest (POST /api/sensor-readings/batch)
  → BatchIngestSensorReadingsCommandHandler
      (calibration → outlier reject → save hypertable)
  → SAU SaveChangesAsync: ITelemetryPublisher.PublishAsync(LiveReadingDto[])   ← soft-dep, handler line ~401
  → RedisTelemetryPublisher: fan-out Redis pub/sub
      telemetry:asset:{id} · telemetry:customer:{id} · telemetry:site:{id} · telemetry:type:{id} · telemetry:all
  → RedisTelemetryStream (subscribe theo scope)
  → SSE: GET /api/sensor-readings/stream?scope=asset:{id}|customer:{id}|site:{id}|...
      event: reading  — LiveReadingDto (scope 1 pin) — ĐÃ có `current` + `chargingState`
      event: summary  — BatterySummaryDto (multi-asset, coalesce primary, throttle 3–5s)
      event: ping     — keep-alive
```

- Auth: JWT (EventSource dùng `?access_token=`), authz theo scope (`BatteryRealtimeAuthorizationService`).
- Toggle: `Realtime:Enabled` (`RealtimeOptions`). Redis lỗi → chỉ log warning, không chặn ingest.

### 1.3 Cái CHƯA có

| Thiếu | Chi tiết |
|---|---|
| Min/max nạp/xả — **mọi tầng** | Firmware không tính (đã quét src — chỉ có cellMin/cellMax cho `CellVoltageDeltaMv`). Backend không tính. FE không có gì để gọi. |
| `/aggregate` chỉ có AVG | `SensorReadingAggregateDto` = Avg{Voltage,Current,Temperature,SocPercent,SohPercent}. `AvgCurrent` **trộn dấu** (nạp + xả trong cùng bucket → gần 0, vô nghĩa để hiển thị). |
| `/aggregate` không lọc nguồn | Handler gộp **cả 3 source/pin** (BMS primary + INA226 redundant + DS18B20 mirror) → mỗi giá trị bị đếm ~3 lần, DS18B20 còn mirror nguyên current của BMS. |

### 1.4 Bẫy dữ liệu BẮT BUỘC xử lý khi tính min/max

1. **3 reading/pin/tick:** BMS (`sourceType=1 Bms`, `sensorSourceCode="primary"`), INA226 (`sourceType=2`, `"redundant"`), DS18B20 (`"external-temp"` — **mirror current của BMS** để qua validation, xem `iot/.../mock_bms.cpp:228` + `ina226.cpp:120`). → Tính min/max **CHỈ trên `sensorSourceCode = "primary"`** (fallback: null/empty coi như primary — cùng quy ước coalescer trong `RedisTelemetryStream`). Nếu không lọc: INA226 noise ±0.05A có thể tạo max giả, và mọi extremum bị nhân 3.
2. **Quy ước dấu:** `+` = nạp, `−` = xả (`SensorReadingDto.cs:14`, `iot/.../core/reading.h:43`). Xả là giá trị âm → "max xả" = `MAX(ABS(current))` với `current < 0`.
3. **Outlier đã chặn ở ingest:** `|current| > 1000A` bị reject trước khi lưu → không cần lọc lại khi aggregate.
4. **Dev đang chạy mock:** firmware default `USE_MOCK_BMS = 1` (current = random −5..2A), simulator Python random ±2A. Pipeline thật sẵn sàng, nhưng số liệu demo là ngẫu nhiên — đừng "tune" ngưỡng theo mock.
5. **Ngưỡng cấu hình ≠ số đo:** `ThresholdConfig.CurrentMaxCharge/CurrentMaxDischarge` (per BatteryType, `GET /api/thresholds`) là **ngưỡng cảnh báo admin đặt** — UI nên vẽ làm đường giới hạn tham chiếu, không nhầm với min/max thực đo.

---

## 2. Định nghĩa metric (chốt trước khi code)

Trong 1 cửa sổ thời gian (bucket hoặc rolling window), trên readings `primary` của 1 pin:

| Metric | Công thức | Đơn vị | Ý nghĩa UI |
|---|---|---|---|
| `maxChargeCurrent` | `MAX(current)` với `current > 0` | A (dương) | Dòng nạp đỉnh |
| `minChargeCurrent` | `MIN(current)` với `current > 0` | A (dương) | Dòng nạp thấp nhất (khi đang nạp) |
| `maxDischargeCurrent` | `MAX(ABS(current))` với `current < 0` | A (dương) | Dòng xả đỉnh |
| `minDischargeCurrent` | `MIN(ABS(current))` với `current < 0` | A (dương) | Dòng xả thấp nhất (khi đang xả) |
| `chargeSampleCount` / `dischargeSampleCount` | COUNT theo chiều | — | FE biết bucket có dữ liệu chiều đó không |

Quy ước trả về:

- **Luôn trả giá trị DƯƠNG** cho cả 2 chiều (FE không phải xử lý dấu) — chiều đã nằm trong tên field.
- Field **nullable**: bucket không có mẫu chiều nào → `null` (pin idle cả bucket → cả 4 field null). Không trả `0` (0A là giá trị đo hợp lệ ≠ không có dữ liệu).
- Sample `current == 0` (idle) không thuộc chiều nào — bỏ qua khỏi cả 2 phía.
- **(Q4=A — LÀM luôn):** `minVoltage`/`maxVoltage`, `minTemperature`/`maxTemperature` cùng khuôn, cùng 1 lần sửa DTO với min/max Current.

---

## 3. TOÀN BỘ phương án (6 phương án, đánh giá từng cái)

### PA-1 — FE tự tính từ SSE stream hiện có (client-side only)

**Cách làm:** FE subscribe `GET /api/sensor-readings/stream?scope=asset:{id}` (đã chạy), nghe event `reading` (đã có `current`), tự giữ running min/max trong state (Zustand/useRef), tự lọc `sensorSourceCode === "primary"`.

- ✅ **Zero thay đổi BE/IoT** — làm được ngay hôm nay. Đúng nghĩa streaming.
- ❌ Mất min/max khi reload/re-mount — không có backfill (mở trang lúc 15h không biết đỉnh lúc 10h).
- ❌ Mỗi client tính riêng → 2 người mở 2 thời điểm thấy 2 con số khác nhau; không dùng được cho report.
- ❌ Logic lọc source + dấu bị đẩy xuống từng client (Web + Mobile viết 2 lần).
- **Verdict:** chỉ chấp nhận làm **demo tạm**. Không phải giải pháp sprint.

### PA-2 — Mở rộng REST `/aggregate` với min/max (pull / backfill)

**Cách làm:** thêm các field §2 vào `SensorReadingAggregateDto` + tính trong `GetSensorReadingAggregateQueryHandler` (đang bucket in-memory bằng LINQ → thêm `g.Where(c>0).Max(...)` là xong), đồng thời **fix filter `primary`**.

- ✅ Thay đổi nhỏ (2 file + tests), giải quyết backfill chart mọi range, tận dụng endpoint FE đã biết.
- ✅ Sửa luôn 2 bug tiềm ẩn hiện tại (trộn source, AvgCurrent trộn dấu — thêm `avgChargeCurrent`/`avgDischargeCurrent` tách chiều).
- ❌ Là **polling** — một mình nó không đạt yêu cầu streaming.
- ⚠️ Handler materialize toàn bộ rows vào RAM (comment trong code tự nhận "bounded ~7d"): 5s/mẫu × 3 source = ~363k rows/pin/tuần — sát giới hạn. Chưa chết ngay nhưng là lý do có PA-4 phase 2.
- **Verdict:** **LÀM — bắt buộc**, là nền backfill cho mọi phương án streaming.

### PA-3 — Backend tính rolling min/max lúc ingest → SSE event `stats` (push) ⭐ khuyến nghị

**Cách làm:** móc vào đúng chỗ `ITelemetryPublisher` đang được gọi (sau `SaveChangesAsync`, soft-dep):

```
Ingest batch (mỗi 5s/device)
  → lọc readings primary, tách chiều theo dấu current
  → merge min/max vào state Redis:  HASH telemetry:stats:{assetId}:{window}   (TTL)
      windows đề xuất: "1h" (bucket giờ hiện tại, key kèm yyyyMMddHH) + "today" (từ 00:00 UTC)
  → publish Redis channel:  telemetry:stats:asset:{id} (+ customer/site/type/all)
  → RedisTelemetryStream forward → SSE event mới:  event: stats
```

Payload SSE `stats` (đề xuất):

```json
{
  "batteryAssetId": "…", "customerId": "…", "siteId": "…",
  "window": "1h", "windowStart": "2026-07-08T09:00:00Z",
  "maxChargeCurrent": 1.92, "minChargeCurrent": 0.31,
  "maxDischargeCurrent": 4.75, "minDischargeCurrent": 0.42,
  "chargeSampleCount": 210, "dischargeSampleCount": 385,
  "updatedAt": "2026-07-08T09:41:05Z"
}
```

- ✅ **Streaming đúng nghĩa:** UI nhận push mỗi lần có batch mới (~5s), min/max nhất quán mọi client (tính 1 nơi, server-side).
- ✅ Tái dùng 100% hạ tầng SSE/Redis/authz/scope sẵn có — không thêm công nghệ mới.
- ✅ Redis state sống qua restart client; TTL tự dọn; soft-dep như telemetry hiện tại (Redis chết không chặn ingest).
- ❌ Tốn công hơn PA-2: service tính + merge state + kênh mới + stream forward (ước 2–3 ngày dev + test).
- ⚠️ State Redis mất khi Redis flush → min/max window đang chạy bị reset. Chấp nhận được (tự hồi sau vài mẫu; FE có backfill PA-2 để đối chiếu). Không cần warm-up từ DB cho scope capstone.
- **Verdict:** **LÀM — đây là phần "streaming" của sprint.**

### PA-4 — TimescaleDB Continuous Aggregate (materialized view `time_bucket`)

**Cách làm:** raw SQL migration tạo continuous aggregate trên hypertable:

```sql
CREATE MATERIALIZED VIEW sensor_readings_agg_1h
WITH (timescaledb.continuous) AS
SELECT time_bucket('1 hour', time) AS bucket, battery_asset_id,
       max(current) FILTER (WHERE current > 0)       AS max_charge_current,
       min(current) FILTER (WHERE current > 0)       AS min_charge_current,
       max(abs(current)) FILTER (WHERE current < 0)  AS max_discharge_current,
       min(abs(current)) FILTER (WHERE current < 0)  AS min_discharge_current,
       count(*) FILTER (WHERE current > 0)           AS charge_samples,
       count(*) FILTER (WHERE current < 0)           AS discharge_samples
FROM sensor_readings
WHERE sensor_source_code = 'primary' OR sensor_source_code IS NULL
GROUP BY bucket, battery_asset_id;

SELECT add_continuous_aggregate_policy('sensor_readings_agg_1h',
  start_offset => INTERVAL '3 hours', end_offset => INTERVAL '5 minutes',
  schedule_interval => INTERVAL '1 minute');
```

- ✅ Giải bài toán scale range dài (tháng/năm) mà PA-2 in-memory sẽ chết; query gần như O(số bucket).
- ✅ TimescaleDB extension đã bật sẵn (migration `20260514051016`) — không cần hạ tầng mới.
- ❌ **Pull-based** — không phải streaming; refresh policy có lag (end_offset). Không thay được PA-3.
- ❌ Raw SQL migration nằm ngoài EF model → cần test rollback kỹ (checklist rule 14); query phải qua raw SQL (`FromSqlRaw`)/Dapper vì view không map entity — lệch pattern UnitOfWork hiện tại.
- **Verdict:** **Phase 2** — làm khi FE cần chart range > 7 ngày hoặc `/aggregate` bắt đầu chậm. Không nằm gọn sprint này.

### PA-5 — SignalR hub (clone khuôn `TicketCommentHub` bên TicketService)

- ❌ BatteryService đã chuẩn hoá **SSE** cho telemetry (#614, ADR ngầm §34.10); thêm SignalR = 2 hạ tầng realtime song song cho cùng 1 loại dữ liệu — thừa, tốn authz/scale/docs gấp đôi.
- SSE một chiều là đủ (telemetry không cần client→server).
- **Verdict:** **LOẠI.** Chỉ cân nhắc nếu tương lai cần bi-directional (điều khiển thiết bị từ UI).

### PA-6 — Firmware ESP32 tự tính min/max rồi gửi kèm payload

- ❌ Phải sửa firmware + contract ingest + mock + simulator + backend validation (nhiều repo, nhiều điểm vỡ).
- ❌ Không hồi tố cho dữ liệu lịch sử đã nằm trong DB; min/max cửa sổ dài (1h/1d) buộc device giữ state qua reboot/offline — phức tạp vô ích khi backend đã có toàn bộ mẫu 5s.
- ❌ Device offline → queue flush trễ → min/max device-side lệch cửa sổ server-side.
- **Verdict:** **LOẠI.** Nguyên tắc giữ nguyên: device gửi số đo thô, backend tính toán.

### Bảng so sánh

| | Streaming? | Backfill? | Nhất quán multi-client | Effort | Verdict |
|---|---|---|---|---|---|
| PA-1 FE-only | ✅ | ❌ | ❌ | 0.5d | Demo tạm |
| PA-2 REST aggregate | ❌ (poll) | ✅ | ✅ | 1–1.5d | **Làm** |
| PA-3 SSE `stats` | ✅ | ➖ (nhờ PA-2) | ✅ | 2–3d | **Làm** ⭐ |
| PA-4 Continuous agg | ❌ | ✅✅ (range dài) | ✅ | 2d | Phase 2 |
| PA-5 SignalR | ✅ | ❌ | ✅ | 3d+ | Loại |
| PA-6 Firmware | ✅ | ❌ | ⚠️ | 3d+ (3 repo) | Loại |

---

## 4. Kiến trúc khuyến nghị chi tiết (PA-2 + PA-3)

### 4.1 Luồng tổng

```
                        ┌────────────── PA-2 (backfill khi mở trang) ──────────────┐
FE mở chart ──────────► GET /api/sensor-readings/{id}/aggregate?interval=1h        │
                        (min/max per bucket, primary-only)                          │
                                                                                    ▼
ESP32 ─5s─► POST /batch ─► Ingest handler ─► hypertable                      [Chart + Cards]
                              │ (sau SaveChangesAsync, soft-dep)                    ▲
                              ├─► TelemetryPublisher (reading — như cũ)             │
                              └─► TelemetryStatsService (MỚI)                       │
                                    ├─ merge Redis HASH telemetry:stats:…           │
                                    └─ publish telemetry:stats:asset:{id}…          │
                                          └─► RedisTelemetryStream ─► SSE ──────────┘
                                                event: stats  (PA-3, push ~5s)
```

### 4.2 API contract

**REST (PA-2)** — `GET /api/sensor-readings/{batteryAssetId}/aggregate?from=&to=&interval=1h`
Mỗi bucket thêm: `maxChargeCurrent`, `minChargeCurrent`, `maxDischargeCurrent`, `minDischargeCurrent`, `avgChargeCurrent`, `avgDischargeCurrent`, `chargeSampleCount`, `dischargeSampleCount` (nullable — xem §2). Giữ nguyên các field Avg cũ (backward-compat; FE đang dùng `avgCurrent` không vỡ).

**SSE (PA-3)** — `GET /api/sensor-readings/stream?scope=…` thêm `event: stats` (payload §PA-3). Event `reading`/`summary`/`ping` giữ nguyên. Client cũ không nghe `stats` → không ảnh hưởng (EventSource bỏ qua event lạ).

### 4.3 Redis design (PA-3)

| Key | Kiểu | TTL | Nội dung |
|---|---|---|---|
| `telemetry:stats:{assetId:N}:1h:{yyyyMMddHH}` | HASH | 2h | maxCharge, minCharge, maxDischarge, minDischarge, chargeCount, dischargeCount |
| `telemetry:stats:{assetId:N}:today:{yyyyMMdd}` | HASH | 26h | như trên |

- Merge atomic bằng **Lua script** (so sánh & set min/max trong 1 round-trip) — tránh race giữa 2 pod ingest. Đơn giản hơn (chấp nhận sai số hiếm): `HGETALL` → merge in-proc → `HSET`, vì cùng 1 asset gần như chỉ 1 device ghi.
- Channel publish: `telemetry:stats:asset:{id:N}` + `:customer:` + `:site:`/`:site:none` + `:type:` + `:all` — **thêm prefix `stats` vào `RedisTelemetryChannels`**, KHÔNG publish chung channel `telemetry:asset:{id}` cũ.

### 4.4 Files thay đổi (dự kiến — sẽ chốt lại trong plan.md từng issue)

| File | Action | Cho |
|---|---|---|
| `BatteryService.Application/DTOs/SensorReadingAggregateDto.cs` | modify — thêm min/max fields | PA-2 |
| `BatteryService.Application/CQRS/Handler/SensorReading/GetSensorReadingAggregateQueryHandler.cs` | modify — filter primary + tính min/max tách chiều | PA-2 |
| `BatteryService.Application/DTOs/Realtime/LiveStatsDto.cs` | create | PA-3 |
| `BatteryService.Application/Interfaces/ITelemetryStatsService.cs` | create — `AccumulateAndPublishAsync(readings)` | PA-3 |
| `BatteryService.Infrastructure/Realtime/RedisTelemetryStatsService.cs` | create — merge HASH + publish channel `stats` | PA-3 |
| `BatteryService.Infrastructure/Realtime/RedisTelemetryChannels.cs` | modify — thêm `StatsAsset(id)`… + `StatsChannelsFor(scope)` | PA-3 |
| `BatteryService.Infrastructure/Realtime/RedisTelemetryStream.cs` | modify — subscribe thêm channel stats, forward `SseMessage("stats", …)` | PA-3 |
| `BatteryService.Application/CQRS/Handler/SensorReading/BatchIngestSensorReadingsCommandHandler.cs` | modify — gọi stats service cạnh `_telemetryPublisher` (soft-dep, try/catch riêng) | PA-3 |
| `BatteryService.Infrastructure/DependencyInjection/ManageDependencyInjection.cs` | modify — đăng ký DI | PA-3 |
| `BatteryService.Infrastructure/Observability/RealtimeMetrics.cs` | modify — label event `stats` | PA-3 |
| `docs/api-battery.md`, `CHANGELOG.md` | modify — contract mới cho FE | cả 2 |
| UnitTests + IntegrationTests tương ứng | create | cả 2 |

### 4.5 Pitfall triển khai (đúc từ code hiện tại — đọc trước khi code)

1. **KHÔNG publish stats vào channel telemetry cũ.** `RedisTelemetryStream.Handler` hiện coi *mọi* message trên channel subscribed là `reading` (deserialize `LiveReadingDto`, đẩy vào coalescer summary) — nhét stats vào sẽ vỡ parser/summary. Bắt buộc channel prefix riêng, phân loại event theo channel nhận.
2. **Filter primary theo đúng quy ước coalescer:** `sensorSourceCode == "primary" || null/empty` (single-source device không gắn tag). Copy quy ước từ `RedisTelemetryStream` (comment line ~32).
3. **Stats service là soft-dependency** y như `ITelemetryPublisher`: try/catch riêng, lỗi Redis chỉ log — tuyệt đối không làm fail ingest (thiết bị sẽ retry + queue làm double data).
4. **Bucket theo UTC** (toàn hệ thống đang UTC — `ToUtc()` trong aggregate handler). "today" = từ 00:00 UTC; FE tự quy đổi hiển thị.
5. **Đơn vị & dấu:** trả dương cả 2 chiều (§2). Đừng trả `minCurrent`/`maxCurrent` trộn dấu — chính là cái bẫy khiến `AvgCurrent` hiện tại vô nghĩa.
6. **`Realtime:Enabled=false`** → stats service phải no-op giống publisher (đọc cùng `RealtimeOptions`).
7. **Unit test theo khuôn repo:** mock `IBatteryUnitOfWork` (`MockQueryable` chạy in-memory — nav nullable phải null-safe ternary, xem memory note team); integration test SSE có sẵn khuôn từ #617.
8. **Ăn khớp DoD:** coverage BE ≥ 80%, `/kltn-reviewcode` PASS, `/kltn-test` PASS trước khi ship.

---

## 5. Đề xuất chia issue cho sprint

| # | Task | Role | Ước | Phụ thuộc |
|---|---|---|---|---|
| NS-01 | Chốt metric §2 + update `docs/api-battery.md` (contract REST + SSE `stats`) cho FE bắt đầu song song | BE | 0.5d | — |
| NS-02 | PA-2: mở rộng `/aggregate` min/max nạp/xả tách chiều + **min/max Voltage/Temperature (Q4)** + filter primary + unit tests | BE | 1–1.5d | NS-01 |
| NS-03 | PA-3a: `LiveStatsDto` + `ITelemetryStatsService` + `RedisTelemetryStatsService` (merge HASH + TTL + publish) + unit tests | BE | 1d | NS-01 |
| NS-04 | PA-3b: `RedisTelemetryChannels` stats + `RedisTelemetryStream` forward event `stats` + metrics + wire vào ingest handler + integration test SSE | BE | 1d | NS-03 |
| NS-05 | FE: card "Nạp/Xả đỉnh (1h / hôm nay)" nghe event `stats`; chart min/max band từ `/aggregate`; vẽ đường ngưỡng từ `/api/thresholds` (`currentMaxCharge/Discharge`) | FE | 1.5d | NS-01 (contract), NS-02/04 (data) |
| NS-06 | **(Q13=B — LÀM đợt này)** PA-4 continuous aggregate `1h` + endpoint đọc view + rollback test migration | BE | 2d | NS-02 chạy ổn |

Workflow từng issue theo chuẩn repo: `/kltn-task` → `/kltn-plan` (chờ approve) → `/kltn-implement` → `/kltn-reviewcode` → `/kltn-test` → `/kltn-ship`.

---

## 6. Test plan (tối thiểu)

- **Unit (PA-2):** bucket có cả nạp+xả → tách đúng 2 chiều; bucket chỉ idle (current=0) → 4 field null; reading redundant/external-temp bị loại; giá trị xả trả dương.
- **Unit (PA-3):** merge min/max qua nhiều batch (max mới > cũ → update, nhỏ hơn → giữ); sang giờ mới → key bucket mới; Redis down → không throw ra ingest; `Realtime:Enabled=false` → no-op.
- **Integration:** POST `/batch` (ApiKey) với current âm/dương → subscribe SSE nhận `event: stats` đúng payload; `/aggregate` trả min/max khớp dữ liệu bơm vào.
- **E2E dev:** chạy `iot/tools/simulator/esp32_simulator.py` (current random ±2A) → mở SSE bằng `curl -N` xem stats nhảy mỗi ~5s.

## 7. Rủi ro & câu hỏi mở

| Rủi ro / câu hỏi | Hướng xử lý |
|---|---|
| Redis flush → mất state window đang chạy | Chấp nhận (tự hồi sau vài mẫu); nếu cần chính xác tuyệt đối → warm-up từ DB khi key miss (để phase 2) |
| Nhiều pod ingest ghi cùng asset | Lua script atomic; scope capstone 1 pod → HGETALL/HSET đủ |
| FE cần window nào ngoài `1h` + `today`? | **Q3: CHỐT chỉ `1h` + `today`** — thêm window sau chỉ là thêm key+TTL nếu FE cần |
| `/aggregate` in-memory chậm khi range dài | Trigger làm NS-06 (PA-4) |
| Số liệu demo là mock (`USE_MOCK_BMS=1`) | Ghi rõ trong demo script; không tune ngưỡng theo mock |
| Pipeline noise hiện tại có 6 điểm lệch (2 bug Cao) | Xem **Phụ lục B (§9)** — đề xuất fix TRƯỚC hoặc TRONG sprint này vì min/max streaming đọc chung dữ liệu với pipeline noise |
| Chuỗi phản ứng cascade risk đứt 3 mắt xích (SLA timer không được tạo, event rơi khi không có ticket, không notify) | Xem **Phụ lục C (§10)** — R1..R8 + issue NS-12..NS-16 |
| Chưa có tính năng cách ly pin nguy hiểm (chỉ đọc & báo, không điều khiển) | Xem **Phụ lục D (§11)** — audit hiện trạng + 3 phương án ISO-A/B/C + issue NS-17..NS-20 |
| Sự cố môi trường: định nghĩa đủ nhưng 4 dây nối thiếu (ambient detect chưa cắm, không auto-ticket, không report tay, MQ2 hardcode Smoke) | Xem **Phụ lục E (§12)** — E1..E4 + issue NS-21..NS-24 |
| Anomaly classification: rule-based xong; bảng AI `AnomalyClassification`/`SohPrediction` (spec §30.3 P0) chưa có code; plan `aibeiotrealtime.md` đang bỏ qua 2 bảng + feedback loop | Xem **Phụ lục F (§13)** — F1 (thiếu rule Undertemp) + F2 (lệch spec vs plan) + issue NS-25..NS-27 |

---

## 8. Phụ lục A — Giải phẫu xử lý NOISE hiện tại trong BatteryService

> Đọc code trực tiếp 2026-07-08. Mục đích: (1) hiểu min/max streaming (PA-2/PA-3) đứng ở đâu trong pipeline làm sạch dữ liệu; (2) làm nền cho danh sách điểm lệch ở Phụ lục B.

Backend chống noise ở **4 lớp**, mỗi lớp nhắm 1 loại nhiễu khác nhau:

```
[Lớp 1 — INGEST]      nhiễu ĐO LƯỜNG (sensor rác, lệch chuẩn, gửi trùng, lệch giờ)
        ↓ dữ liệu vào DB đã "sạch vật lý"
[Lớp 2 — DETECT]      nhiễu VI PHẠM NGƯỠNG thoáng qua (chớm ngưỡng 1-2 lần rồi thôi)
        ↓ chỉ vi phạm lặp lại đủ tần suất mới thành Alert
[Lớp 3 — ALERT]       nhiễu TRÙNG LẶP (cùng 1 sự cố sinh N alert) + alert "tự khỏi"
        ↓ Staff chỉ thấy alert đại diện, alert hết triệu chứng tự đóng
[Lớp 4 — CROSS-SOURCE] nhiễu HỆ THỐNG (1 trong 2 sensor nói dối)
```

### 8.1 Lớp 1 — Làm sạch lúc ingest (`BatchIngestSensorReadingsCommandHandler`)

Chạy trên từng item của `POST /api/sensor-readings/batch`, theo thứ tự:

| Bước | Cơ chế | Tham số (file:line) | Hành vi khi dính |
|---|---|---|---|
| 1 | **Idempotency dedup** — device retry sau timeout gửi lại cùng `Idempotency-Key` | TTL 24h (`:47`) | Trả lại kết quả cũ, KHÔNG insert đôi |
| 2 | **Clock skew check** — `DeviceTimestamp` lệch server | > 5 phút (`:40`) | Reject 400 field-level + metric |
| 3 | **Calibration** — nắn sensor lệch chuẩn `raw × Scale + Offset` per device/channel | `IotDeviceCalibration`, cache Redis TTL 5' (`:206-231`) | Nắn TRƯỚC khi check outlier (thứ tự quan trọng) |
| 4 | **Outlier bounds cứng** (`IsOutlier` `:439-452`, spec §52.5) | V ∉ [0,1000], \|I\| > 1000A, T ∉ [-50,150], SOC/SOH ∉ [0,100] (`:29-37`) | Loại im lặng: `Skipped++` + metric `sensor_outlier`, reading sạch cùng batch vẫn lưu, **không throw** |
| 5 | **Auto-decommission device** (#IoT2-17) | > 50 outlier / cửa sổ trượt 1h per device (`:43-44`, `:330-368`) | Device → `Decommissioned` + Alert **Critical** báo Admin (dedup 6h) — cách ly nguồn phun rác |

Triết lý lớp 1: *reading rác thì loại từng viên, nguồn rác thì cắt cả vòi* — và không bao giờ làm gateway fail cả batch vì 1 viên hỏng. `Skipped` trong response là kênh để gateway tự phát hiện calibration sai.

**Liên quan sprint này:** PA-2/PA-3 đọc dữ liệu SAU lớp 1 → min/max không cần lo outlier vật lý; chỉ còn phải tự lọc source `primary` (INA226 noise ±0.05A — xem §1.4).

### 8.2 Lớp 2 — Noise suppression frequency-based (Sprint 5B B1 #152) — phần "Noise" đúng nghĩa

**Bài toán:** reading chớm vượt ngưỡng 1–2 lần (gợn tải, nhiễu đo còn sót) mà raise Alert ngay → Staff bị spam → alert fatigue → bỏ sót alert thật.

**Giải pháp:** chỉ raise Alert khi vi phạm **lặp lại ≥ N lần trong cửa sổ W giờ**. Config per `BatteryType` trong `ThresholdConfig` (`:35-37`):

| Field | Default | Nghĩa |
|---|---|---|
| `NoiseSuppressionEnabled` | `true` | Công tắc |
| `NoiseSuppressionCount` | `5` | Ngưỡng tần suất |
| `NoiseSuppressionWindowHours` | `24` | Cửa sổ đếm |

**Luồng chạy** — `AnomalyDetectionService.ShouldSuppressByNoiseAsync` (`:286-321`), được `ThresholdCheckBackgroundService` gọi mỗi `ScanIntervalSeconds=30s` với lookback = 2×interval = 60s:

```
Reading vi phạm ngưỡng (AnomalyRules.Detect)
 │
 ├─ BYPASS raise ngay (an toàn > chống spam):
 │    • AnomalyType == EnvironmentalIncident            (:293)
 │    • AnomalyType == Overheat && Severity == Critical  (:295)
 │    • NoiseSuppressionEnabled == false hoặc Count <= 1 (:299)
 │
 └─ Còn lại:
     1. AddAsync 1 row NoiseBreachEvent                  (:303-311)
        (hypertable append-only: time, assetId, anomalyType,
         thresholdValue, actualValue, unit)
     2. COUNT breach cùng (assetId, anomalyType)
        trong WindowHours gần nhất — query DB            (:313-317)
     3. (count_DB + 1) <  Count → SUPPRESS  (AlertsSuppressed++, không có Alert)
        (count_DB + 1) >= Count → cho phép raise Alert   (:320)
```

Ví dụ timeline với default (5 lần / 24h): vi phạm #1 → ghi breach, count=0+1=1 < 5 → nén. #2,#3,#4 tương tự. Vi phạm #5 (DB đã có 4 breach) → 4+1=5 ≥ 5 → **Alert nổ**. `NoiseBreachEvent` là "sổ ghi nợ" — vi phạm bị nén vẫn để lại vết audit ("pin này chớm ngưỡng 4 lần tuần trước").

**Dọn dẹp:** `NoiseBreachRetentionBackgroundService` — tick 6h, xoá rows > 7 ngày (`:15-16`, `:57-59`).

### 8.3 Lớp 3 — Nén nhiễu tầng Alert

- **Dedup/merge BR-03** (`FindActiveAlertToMergeAsync` `:262-274`): alert mới cùng `(assetId, anomalyType)` với alert Open/Acknowledged còn trong `DedupWindowEndUtc` (default `DedupWindowMinutes=30`) → tạo record `Status=Merged` trỏ `MergedIntoAlertId`, KHÔNG thành alert mới. Pin kẹt lỗi 1 giờ → 2 alert thật thay vì ~120.
- **Auto-resolve B10 #158** (`AlertAutoResolveBackgroundService`, mỗi `AutoResolveIntervalSeconds=300`): alert Open mà anomaly không còn xuất hiện trong `AutoResolveLookbackMinutes=10` → tự đóng. Nhiễu thoáng qua tự dọn.

### 8.4 Lớp 4 — Cross-source validation (phát hiện sensor nhiễu/lệch hệ thống)

Tận dụng 2 nguồn đo độc lập per pin (BMS primary + INA226 redundant). Ngưỡng: **ΔV > 0.5V** hoặc **ΔT > 5°C** → `SensorMismatch` Warning. Hiện có **2 đường chạy song song** (xem lệch N6):

| | `AnomalyDetectionService.DetectSensorMismatches` (B10 #157) | `CrossSourceValidationService` (#IoT2-28) |
|---|---|---|
| Trigger | ThresholdCheck tick 30s, lookback 60s | Background riêng tick 30s, lookback 75s |
| Ghép cặp | Bucket phút, `FirstOrDefault` Bms + `FirstOrDefault` IotGateway (`:246-247`) | Mỗi reading ghép reading gần nhất **khác sourceType bất kỳ** trong ±60s (`:69-73`) |
| Dedup | Merge vào alert active, window 30' | Skip nếu có SensorMismatch < 15' (`:83-90`) |

---

## 9. Phụ lục B — 6 ĐIỂM LỆCH phát hiện khi đọc code noise (bug report chi tiết)

> Mức độ: 🔴 Cao (vô hiệu hoá cơ chế / spam Critical) · 🟡 Trung bình (sai so thiết kế, hậu quả giới hạn) · ⚪ Ghi nhận (nợ thiết kế).
> **N1 ảnh hưởng mock lẫn real. N4, N5 chỉ bùng khi chạy hardware thật (`USE_MOCK_BMS=0`) — mock đang CHE chúng, tức là test/demo hiện tại không bao giờ thấy.**

### 🔴 N1 — NoiseBreachEvent bị vứt bỏ khi tick không tạo Alert → suppression chặn alert VĨNH VIỄN

**Bằng chứng:** `AnomalyDetectionService.ScanRecentReadingsAsync:221-222`

```csharp
if (result.AlertsCreated + result.AlertsMerged > 0)
    await _unitOfWork.SaveChangesAsync(cancellationToken);
```

`SaveChangesAsync` CHỈ chạy khi tick có alert created/merged. `AlertsSuppressed` không nằm trong điều kiện. Trong khi đó `ShouldSuppressByNoiseAsync:303` mới chỉ `AddAsync` breach event (pending trên DbContext, chưa persist), và mỗi tick dùng scope DI mới (`ThresholdCheckBackgroundService:42` — `using var scope`) → **tick chỉ toàn anomaly bị nén = toàn bộ breach event pending bị huỷ cùng DbContext**.

**Kịch bản tái hiện (mặc định Count=5):**
1. Pin A vi phạm `Undervoltage` lai rai (đúng kịch bản noise suppression sinh ra để xử lý).
2. Tick 1: breach → count_DB=0 → 0+1<5 → nén. Không alert nào khác trong tick → **không SaveChanges → breach row bị vứt**.
3. Tick 2..∞: hệt tick 1 — count_DB **mãi mãi = 0**.
4. Kết quả: pin vi phạm hàng tuần liền, **Alert không bao giờ nổ**. Cơ chế "nén nhiễu" thành "nén luôn sự cố thật".

**Điều kiện thoát (tình cờ, không phải thiết kế):** tick đó *tình cờ* có alert khác (bypass Overheat-Critical, SensorMismatch, asset khác…) → SaveChanges flush ké breach events pending. Ở real mode, N5 (mismatch spam) vô tình flush giùm thường xuyên — bug này "tự che" bằng một bug khác.

**Hệ quả:** vô hiệu hoá chính cơ chế noise suppression với anomaly không-bypass: `Undervoltage`, `Overvoltage`, `LowSoc`, `RapidDischarge`, `AbnormalCharging`, `SohDegradation`, `HighInternalResistance`, `CellImbalance`, và `Overheat` mức Warning.

**Fix đề xuất (chọn 1):**
- (a) Đơn giản nhất: đổi điều kiện thành `if (result.AlertsCreated + result.AlertsMerged + result.AlertsSuppressed > 0)`.
- (b) Sạch hơn: `SaveChangesAsync` riêng ngay sau khi ghi breach trong `ShouldSuppressByNoiseAsync` (đánh đổi: N lần save/tick).
- Khuyến nghị **(a)** — 1 dòng, giữ nguyên số round-trip.

**Test phải viết:** scan tick chỉ có anomaly bị suppress → assert `NoiseBreachEvents` được persist; chạy 5 tick liên tiếp → tick 5 assert Alert created.

### 🔴 N4 — Không lọc source khi Detect: INA226 real gửi `SOC=0` → LowSoc **Critical** spam + auto-ticket (chỉ real mode)

**Bằng chứng chuỗi:**
- Firmware real: `iot/firmware-esp32/src/sensor/ina226.cpp:123-124` — reading redundant set cứng `temperature = 0.0f; socPercent = 0.0f` (INA226 không đo temp/SOC).
- Backend: `AnomalyDetectionService:40-44` load **mọi** reading trong lookback, không filter `SourceType`/`SensorSourceCode`; `AnomalyRules.Detect:50-55` — `SocPercent < SocCriticalThreshold` → `LowSoc` **Critical** (`SocCriticalThreshold` là field NOT NULL, luôn được cấu hình).
- Mock che bug: `iot/firmware-esp32/src/bms/mock_bms.cpp` — mock INA226/DS18B20 **mirror** SOC/temp từ BMS ("required field — mirror BMS") → mock không bao giờ có SOC=0.

**Kịch bản real mode:** mỗi 5s, reading INA226 (SOC=0) vào DB → mỗi tick scan 60s thấy ~12 reading INA226 → 12 anomaly `LowSoc Critical`/pin. Noise suppression áp lên (LowSoc không bypass) nhưng vì vi phạm LIÊN TỤC nên vượt `Count=5` gần như ngay khi count persist được (xem N1) → Alert Critical → outbox `BatteryAnomalyDetectedEvent` → **TicketService auto-tạo ticket** → sau mỗi dedup window 30' lại một alert Critical mới. Spam vô hạn trên MỌI pin có INA226.

**Fix đề xuất:** filter tại nguồn scan — `ScanRecentReadingsAsync` chỉ `Detect` trên reading primary (`SensorSourceCode == "primary" || null/empty`, cùng quy ước coalescer SSE); reading redundant/external-temp chỉ dùng cho cross-source (lớp 4). Cân nhắc thêm guard `SocPercent > 0` khi source không phải Bms nếu muốn giữ Detect đa nguồn.

**Test phải viết:** seed reading redundant SOC=0 + threshold SocCritical=20 → assert KHÔNG có anomaly LowSoc; reading primary SOC=10 → assert CÓ.

### 🔴 N5 — Backend không skip so-sánh nhiệt độ cho source không đo temp → SensorMismatch Warning liên tục (chỉ real mode); trái contract firmware §52.6

**Bằng chứng:**
- Firmware GHI RÕ kỳ vọng: `ina226.cpp:120-122` — *"Để cross-source validation chỉ so V/I, temp set 0 + sensorSourceCode lookup sẽ skip temp comparison ở backend §52.6"*.
- Backend KHÔNG làm điều đó ở cả 2 đường:
  - `AnomalyRules.DetectSensorMismatch:214-220` — so `Temperature` vô điều kiện.
  - `CrossSourceValidationService:78` — `tempDelta` tính cho mọi cặp, không nhìn `SensorSourceCode`.
- Tệ hơn, `CrossSourceValidationService:69-73` ghép cặp theo điều kiện `c.SourceType != reading.SourceType` — tức **mọi** cặp khác sourceType: INA226 (IotGateway, temp=0) vs DS18B20 (External, temp thật ~25°C) cũng bị ghép → ΔT=25°C > 5°C → mismatch giả không liên quan gì tới BMS.

**Kịch bản real mode:** BMS 25°C vs INA226 0°C → ΔT=25 > 5 → `SensorMismatch` Warning. Dedup 15' của CSVS + merge 30' của B10 giữ tần suất ~1 alert/15', **mãi mãi, trên mọi pin** — Staff sẽ học cách bỏ qua SensorMismatch, đúng loại alert fatigue mà noise suppression sinh ra để chống.

**Fix đề xuất:** (1) skip so temp khi 1 trong 2 reading có `SensorSourceCode == "redundant"` (hoặc tổng quát: skip metric mà source không đo — temp=0 + code redundant); (2) CSVS chỉ ghép cặp `Bms ↔ IotGateway` như B10 #157, không ghép External; (3) đồng bộ lại spec §52.6 vào docs backend.

**Test phải viết:** cặp BMS(25°C) + redundant(0°C, ΔV trong ngưỡng) → assert KHÔNG mismatch; cặp BMS(25°C) + redundant lệch ΔV=0.6V → assert CÓ mismatch (đường V vẫn phải sống).

### 🟡 N2 — `PromotedToAlertId` không bao giờ được gán + retention xoá cả breach đã promote (trái doc-comment)

**Bằng chứng:**
- Entity `NoiseBreachEvent.cs:23-27` doc: *"Set khi breach này được promote thành Alert thật… **Giữ vĩnh viễn (không bị purge bởi retention) để audit**"*.
- Grep toàn service: field chỉ xuất hiện ở entity, EF configuration (column + index) và seeder (`= null`). **Không một dòng code nào gán giá trị** khi alert được raise (`AnomalyDetectionService:108-122` tạo alert mà không đụng breach events).
- Retention: `NoiseBreachRetentionBackgroundService:57-59` — `Where(n => n.Time < cutoff).ExecuteDeleteAsync` — xoá **tất cả** rows > 7 ngày, không loại trừ `PromotedToAlertId != null`.

**Hệ quả:** (1) mất khả năng audit "alert này nổ từ chuỗi breach nào" — đúng mục đích field được thiết kế; (2) index `promoted_to_alert_id` tồn tại trong DB nhưng vô dụng; (3) doc-comment đánh lừa dev sau.

**Fix đề xuất:** khi raise alert qua đường suppression, update các breach event cùng `(assetId, anomalyType)` trong window: `PromotedToAlertId = alert.Id`; retention thêm `&& n.PromotedToAlertId == null`. Hoặc nếu team quyết định KHÔNG cần audit này → xoá field + index + sửa doc-comment (đừng để code nói dối).

### 🟡 N3 — Đếm breach lạm phát ~2× do scan overlap + `SourceType` breach không được copy

**Bằng chứng:**
- `ThresholdCheckBackgroundService:34` — `lookback = interval + interval` (60s cho interval 30s), comment thừa nhận *"Overlap 2x để không miss reading; dedup ở service handle duplicates"* — nhưng dedup chỉ tồn tại cho **Alert** (merge), KHÔNG cho **breach count**.
- Không có marker "reading đã scan" → cùng 1 reading vi phạm được scan ở 2 tick liên tiếp → `ShouldSuppressByNoiseAsync` ghi **2 breach event cho cùng 1 lần vi phạm** (khi persist được — xem N1).
- `AnomalyDetectionService:303-311` — tạo `NoiseBreachEvent` không set `SourceType` từ `reading.SourceType` → luôn default `IotGateway` (`NoiseBreachEvent.cs:30`), vô hiệu mục đích B9 "phân biệt breach từ BMS hay IoT cho cross-source analysis".

**Hệ quả:** ngưỡng `NoiseSuppressionCount=5` thực tế ứng xử như ~2–3 lần vi phạm thật → suppression yếu hơn cấu hình ~2 lần (ngược hướng với N1 — hai bug kéo hai phía, hành vi tổng hợp gần như không dự đoán được). Cột `source_type` trong bảng breach toàn giá trị sai.

**Lưu ý ngữ nghĩa (không phải bug nhưng nên chốt lại):** với sampling 5s, "Count=5 trong 24h" nghĩa là **5 reading vi phạm ≈ 25 giây vi phạm liên tục** — không phải "5 đợt sự cố riêng biệt". Nếu ý đồ nghiệp vụ là đếm *đợt*, cần thêm khoảng lặng tối thiểu giữa 2 lần đếm (cooldown per breach giống `MQ2_REARM_COOLDOWN_MS` bên firmware).

**Fix đề xuất:** (1) dedup breach theo `(assetId, anomalyType, reading.Time)` — check tồn tại trước khi Add, hoặc unique index; (2) copy `SourceType = reading.SourceType`; (3) chốt ngữ nghĩa Count với team (reading hay đợt).

### ⚪ N6 — Hai hệ thống SensorMismatch song song (B10 #157 vs #IoT2-28) — nợ thiết kế

**Bằng chứng:** bảng so sánh §8.4. Hai code path cùng tạo `AnomalyTypeEnum.SensorMismatch` với cùng ngưỡng 0.5V/5°C nhưng khác cách ghép cặp, khác dedup window (30' merge vs 15' skip), chạy đồng thời mỗi 30s.

**Hệ quả:** (1) chúng dedup chéo nhau *một phần* (CSVS check `DetectedAt >= now-15'` mọi status kể cả Merged; B10 merge vào alert active bất kể ai tạo) nên chưa thấy double-alert rõ ràng — nhưng đó là ăn may, không phải thiết kế; (2) sửa ngưỡng/logic phải nhớ sửa 2 nơi (`AnomalyRules:197-198` và `CrossSourceValidationService:16-17` — hằng số đang trùng giá trị nhưng là 2 bản copy); (3) N5 phải fix ở cả 2 chỗ.

**Fix đề xuất:** hợp nhất về 1 đường — giữ `CrossSourceValidationService` (ghép cặp linh hoạt hơn) nhưng sửa theo N5, xoá `DetectSensorMismatches` khỏi `AnomalyDetectionService`; hằng số ngưỡng dồn về `AnomalyRules`.

### Ghi chú nhỏ ngoài phạm vi noise (thấy khi đọc, ghi lại kẻo quên)

- `ThresholdConfig.TemperatureMin` tồn tại (entity `:15`) nhưng `AnomalyRules.Detect` **không có rule Undertemp** — không anomaly nào dùng nó. Hoặc thêm rule, hoặc field đang chết.

### 9.1 Tác động lên sprint min/max streaming (§4)

- **PA-2/PA-3 KHÔNG bị chặn** bởi các bug trên — min/max đọc `sensor_readings` sau lớp 1 (outlier đã sạch), không đi qua lớp 2–4.
- Nhưng N4/N5 củng cố đúng bài học §1.4: **mọi phép tính trên `sensor_readings` phải lọc source `primary`** — anomaly engine quên điều này và trả giá. PA-2/PA-3 đã đặt yêu cầu lọc primary từ đầu.
- Fix N1 (1 dòng) + N4 + N5 nên đi **trước hoặc cùng sprint** này: khi lên hardware thật mà chưa fix, alert spam sẽ chôn vùi mọi thứ UI mới hiển thị.

### 9.2 Đề xuất issue bổ sung vào sprint

| # | Task | Mức | Ước | Ghi chú |
|---|---|---|---|---|
| NS-07 | Fix N1 — persist breach khi suppressed (điều kiện SaveChanges) + tests | 🔴 | 0.5d | 1 dòng fix, tests là phần chính |
| NS-08 | Fix N4 — Detect chỉ chạy trên reading primary + tests | 🔴 | 0.5–1d | Chặn trước khi lên hardware thật |
| NS-09 | Fix N5 — skip temp-compare cho redundant + CSVS chỉ ghép Bms↔IotGateway + tests | 🔴 | 0.5–1d | Sửa cả 2 đường (hoặc gộp NS-11) |
| NS-10 | Fix N2 + N3 — PromotedToAlertId, retention filter, dedup breach, copy SourceType | 🟡 | 1d | Gộp 1 issue "noise bookkeeping" |
| NS-11 | Hợp nhất 2 đường SensorMismatch (N6) | ⚪ | 1d | **Q7=B: LÀM đợt này** (không dời) |

---

## 10. Phụ lục C — CASCADE RISK (rủi ro lan truyền): giải phẫu + chuỗi phản ứng + 8 điểm lệch

> Đọc code trực tiếp 2026-07-09 (Sprint 7 B4, §31.7). Liên quan trực tiếp Phụ lục B: đầu vào của cascade risk là **bảng Alert** — bug noise N1/N4 truyền hậu quả thẳng vào đây (xem R5).

### 10.1 Khái niệm & công thức tính

Trong 1 site, các pin **đấu nối điện với nhau** — 1 pin hỏng không chỉ là chuyện của nó: nối tiếp (series) chết → **ngắt cả string**; song song (parallel) chết → **pin còn lại gánh thêm tải**; quá nhiệt → **lan nhiệt sang pin bên cạnh** (thermal runaway). `BatteryAsset.CascadeRiskScore` (0.0–1.0) định lượng nguy cơ đó, tính **rule-based cộng dồn, clamp ≤ 1.0** (`CascadeRiskCalculator.cs`):

| Rule | Điều kiện | Điểm | Lý do vật lý |
|---|---|---|---|
| **1. Topology** (`:34-41`) | `Independent` / `ParallelBank` / `SeriesParallel` / `SeriesString` | +0.0 / +0.2 / +0.4 / +0.6 | Nối tiếp nguy hiểm nhất — mất 1 pin ngắt cả string |
| **2. Proximity** (`:43-69`) | Số pin **khác** cùng `SiteId` có alert `Open` với `DetectedAt` trong **1h**: ≥1 → +0.2, ≥3 → +0.2 nữa | +0.2 / +0.4 | Nhiều pin cùng site trục trặc = sự cố hệ thống |
| **3. Thermal runaway** (`:71-80`) | Chính pin này có alert `Overheat` + `Critical` + `Open` (không giới hạn thời gian) | +0.3 | Quá nhiệt là cơ chế lây trực tiếp nhất |

Phân mức (`CascadeRiskLevel`): **Low** < 0.5 · **Medium** 0.5–<0.7 · **High** ≥ 0.7.

Ví dụ: `SeriesString` (0.6) + 1 hàng xóm có alert 1h (0.2) = **0.8 High**. Ngược lại pin `Independent` cần tự nó Overheat Critical (0.3) + ≥3 hàng xóm cháy (0.4) mới chạm 0.7 — **topology là yếu tố nặng ký nhất và do Admin khai báo TAY** (`POST /api/battery-assets/{id}/topology`, default `Independent` = rule 1 luôn 0 nếu quên khai).

Lưu ý adaptation: spec gốc dùng `BatteryGroupId` cho proximity, project đã bỏ BatteryGroup → nhóm theo `SiteId` (comment đầu `CascadeRiskCalculator.cs`).

### 10.2 Vòng đời tính toán

```
CascadeRiskBackgroundService — tick mỗi 5' (AnomalyEngine:CascadeRiskIntervalSeconds=300)
  → CascadeRiskService.RecomputeAsync(batchSize=200)
      1. CHỈ chọn asset đang có ≥1 alert Open (:50-58) — pin "khỏe" KHÔNG được tính lại
      2. Score mới → lưu CascadeRiskScore + CascadeRiskUpdatedAt
      3. Guard chuyển tiếp: CHỈ khi oldScore < 0.7 && newScore ≥ 0.7 (:77)
         → outbox BatteryCascadeRiskHighEvent (atomic với SaveChanges, relay qua OutboxRelay)
      4. Medium (0.5–<0.7): chỉ log — không event, không notify
```

**API cho UI:**

| Endpoint | Quyền | Trả về |
|---|---|---|
| `GET /api/battery-assets/{id}/cascade-risk` | mọi role | score đã lưu + level + topology + `CascadeRiskUpdatedAt` — **không recompute on-demand** |
| `GET /api/sites/{id}/cascade-risk-summary` | Admin/Manager | heat map: đếm High/Medium/Low, `MaxScore`, `HighRiskAssets` sort giảm dần |
| `POST /api/battery-assets/{id}/topology` | Admin | set topology 1..4 → **recompute ngay** |

### 10.3 Chuỗi phản ứng khi High (≥ 0.7) — thiết kế vs thực tế

Triết lý: **nâng độ khẩn cấp của con người, không can thiệp vật lý vào pin** (không có lệnh MQTT ngắt pin/breaker — Staff cô lập thủ công). Nhất quán Priority Policy `design.md`: đây là ngoại lệ **safety-override duy nhất** được phép đổi Priority, có audit.

```
Score cross ≥ 0.7 → BatteryCascadeRiskHighEvent
  ▼
TicketService — BatteryCascadeRiskHighConsumer
  │ Tìm ticket: RelatedTicketId (alert Open mới nhất có TicketId)
  │            → fallback: ticket ACTIVE MỚI NHẤT của pin (:64-69)
  │ Không có ticket → log rồi RETURN (không làm gì)      ← đứt R2
  │ Đã P1 → bỏ qua (idempotent)
  ▼
Priority → P1Critical + TicketActivity
  (Actor=System, reason="CascadeRiskHigh safety override — score=…")
  ▼
Bộ máy SLA/escalation phía sau P1 (THIẾT KẾ):
  • SLA 4h (SlaCalculator: P1=4h · P2=24h · P3=72h)
  • SlaTimerBackgroundService tick 60s: 80% → SlaWarningEvent; hết giờ → SlaBreachedEvent
  • EscalationBackgroundService consume SlaBreachedEvent: P1/P2 → state machine
    → ESCALATED (EscalationReason=SlaBreach) + TicketEscalatedEvent; P3 → chỉ log
  • NotificationService SlaBreachedConsumer → notify
  ▼                                                      ← toàn tầng này chết vì R1
Con người: Manager xem heat map → điều Staff tier cao, cô lập pin, reassign;
Admin chỉnh topology. Mức Medium: KHÔNG hành động tự động — Manager tự poll.
```

### 10.4 — 8 điểm lệch R1..R8 (bug report chi tiết)

> Đánh số R (risk) để không lẫn N1..N6 (noise, Phụ lục B). R1/R2 làm chuỗi phản ứng **đứt ở giữa** — nghiêm trọng nhất.

#### 🔴 R1 — SLA timer KHÔNG BAO GIỜ được tạo trong luồng thật → toàn bộ tầng SLA/escalation phía sau P1 chết

**Bằng chứng (grep toàn TicketService):**
- `new SlaTimer` / `SlaTimers.Add*` chỉ xuất hiện trong **`TicketDataSeeder.cs`** (`:487`, `:504`) — không một command handler/consumer/saga nào tạo timer khi ticket được triage/assign.
- `SlaCalculator.CalculateSlaDueDate` (P1=4h/P2=24h/P3=72h) được đăng ký DI nhưng **0 call site**.
- `BatteryCascadeRiskHighConsumer` nâng P1 **không đụng** SlaTimer — kể cả nếu timer tồn tại, `DueAt` vẫn là deadline của priority cũ (72h của P3).

**Hệ quả dây chuyền:** ticket thật không có timer → `SlaTimerBackgroundService` (tick 60s) không có gì đếm → không `SlaWarningEvent` 80%, không `SlaBreachedEvent` → `EscalationBackgroundService` không bao giờ đẩy ticket sang `ESCALATED` → `NotificationService.SlaBreachedConsumer` không bao giờ notify. **"P1 = SLA 4h" hiện chỉ đúng trên dữ liệu seed demo.** Cascade risk nâng P1 xong… không có gì khác xảy ra.

**Fix đề xuất:** (1) tạo `SlaTimer` (dùng `SlaCalculator`) tại thời điểm ticket được **Assigned** (SLA start theo business flow) — 1 chỗ, mọi ticket hưởng; (2) khi Priority đổi (cascade override): recompute `DueAt` từ thời điểm timer start với giờ SLA mới, ghi kèm activity. **Test:** assign ticket → assert timer Running + DueAt đúng priority; consume CascadeRiskHigh → assert DueAt rút về 4h.

#### 🔴 R2 — High risk mà không có ticket active → KHÔNG hành động gì + cơ hội không quay lại

**Bằng chứng:** `BatteryCascadeRiskHighConsumer:71-77` — không tìm thấy ticket → log info, `return`. Không auto-tạo ticket, không notify.

**Kịch bản hoàn toàn khả thi:** pin `SeriesString` (0.6) + 3 hàng xóm có alert **Warning** (0.4) = score 1.0 High — nhưng alert Warning **không tạo ticket** (chỉ Critical mới publish `BatteryAnomalyDetectedEvent` → auto-ticket, xem `AnomalyDetectionService:124`). Event High bắn ra → consumer skip → **rơi vào hư không**. Tệ hơn: guard transition `oldScore < 0.7` nghĩa là event **không bắn lại** chừng nào score còn ≥ 0.7 — cơ hội xử lý biến mất cho đến khi score tụt xuống rồi vượt ngưỡng lần nữa.

**Fix đề xuất (chọn 1):** (a) consumer auto-tạo ticket P1 khi không có ticket active (nguồn `TicketOrigin=System`); (b) tối thiểu: publish notification cho Manager (gộp R3). Khuyến nghị (a) — đúng tinh thần "High = phải có người chịu trách nhiệm xử lý".

#### 🟡 R3 — NotificationService không consume `BatteryCascadeRiskHighEvent` (doc nói có)

**Bằng chứng:** doc-comment `CascadeRiskService.cs:15` — *"(TicketService upgrade Priority lên P1, **NotificationService notify Manager**)"*. Grep NotificationService: **không có consumer nào** cho event này (chỉ có `SlaBreachedConsumer` — mà breach không bao giờ xảy ra do R1). Kênh duy nhất Manager biết: tự mở dashboard poll heat map. Cùng họ lệch doc-vs-code với N2.

**Fix:** thêm `BatteryCascadeRiskHighConsumer` bên NotificationService (push + email Manager của site), khuôn có sẵn từ các consumer khác.

#### 🟡 R4 — Score bị ĐÓNG BĂNG, không có decay

**Bằng chứng:** `CascadeRiskService.RecomputeAsync:50-58` chỉ quét asset **đang có alert Open**. Alert đóng hết (kể cả auto-resolve sau 10' yên — B10 #158) → asset rớt khỏi danh sách → score **giữ nguyên giá trị cuối vĩnh viễn**.

**Hệ quả:** pin từng chạm 0.8 hiện "High" trên heat map **mãi mãi** dù đã khỏe → heat map mất niềm tin ("chỗ nào cũng đỏ"), Manager học cách bỏ qua — chính là alert fatigue mà hệ thống cố tránh. FE chỉ có `CascadeRiskUpdatedAt` để tự đoán score đã ôi.

**Fix đề xuất:** thêm nhánh quét thứ 2 trong `RecomputeAsync`: asset có `CascadeRiskScore > 0` nhưng **không còn** alert Open → recompute (score sẽ tự tụt vì rule 2/3 = 0, chỉ còn topology). Chi phí: 1 query thêm mỗi tick.

#### 🟡 R5 — Bug noise N1/N4 truyền thẳng vào cascade risk (2 hướng ngược nhau)

Đầu vào của rule 2 + rule 3 là **bảng Alert** → chất lượng cascade risk = chất lượng pipeline alert:

- **N1** (breach không persist → alert thật không nổ): proximity/thermal luôn 0 → cascade risk bị **đánh giá thấp giả tạo** — pin nguy hiểm thật không được nâng P1.
- **N4** (real mode: INA226 SOC=0 → LowSoc Critical spam mọi pin): mọi pin cùng site đều có alert Open → rule proximity +0.2/+0.4 **cho cả site** → score ≥ 0.7 hàng loạt → `BatteryCascadeRiskHighEvent` hàng loạt → **nâng P1 hàng loạt ticket** (safety-override sai trên diện rộng, audit trail đầy rác).

**Fix:** không fix ở đây — fix N1 (NS-07) + N4 (NS-08) là điều kiện tiên quyết để cascade risk có ý nghĩa trên hardware thật. Ghi rõ dependency này vào issue.

#### ⚪ R6 — Fallback ticket có thể nâng P1 NHẦM ticket không liên quan

**Bằng chứng:** `BatteryCascadeRiskHighConsumer:64-69` — không có `RelatedTicketId` → lấy **ticket active mới nhất bất kỳ** của pin. Pin đang có ticket bảo trì định kỳ (P3, không liên quan sự cố) → ticket đó bị nâng P1 oan, audit ghi "safety override" cho một việc thay dầu mỡ.

**Fix đề xuất:** fallback chỉ chọn ticket có `TicketCategory`/`TicketOrigin` liên quan sự cố (incident), hoặc bỏ fallback → đi đường R2-(a) tạo ticket mới.

#### ⚪ R7 — `Take(200)` không `OrderBy` → starvation khi scale

**Bằng chứng:** `CascadeRiskService:57` — `Distinct().Take(batchSize)` không sort → thứ tự tùy DB. Quá 200 asset có alert Open → tập được quét tùy ý, asset ngoài top có thể **không bao giờ** được tính qua nhiều tick. Scale capstone chưa sao — mìn cho tương lai. **Fix:** `OrderBy(a => a.CascadeRiskUpdatedAt)` (asset lâu chưa tính nhất lên đầu) hoặc bỏ Take, phân trang theo tick.

#### ⚪ R8 — Mức Medium hoàn toàn thụ động (ghi nhận, không hẳn bug)

Medium (0.5–<0.7) chỉ `LogInformation` — không event, không notify, không đánh dấu gì trên ticket. Manager chỉ thấy nếu chủ động mở heat map. Chấp nhận được cho scope capstone, nhưng nên ghi rõ trong docs FE: **màu vàng trên heat map = "hệ thống sẽ không nhắc bạn lần 2"**.

### 10.5 Đề xuất issue bổ sung (nối bảng §9.2)

| # | Task | Mức | Ước | Phụ thuộc |
|---|---|---|---|---|
| NS-12 | Fix R1 — tạo SlaTimer khi Assigned (dùng SlaCalculator) + recompute DueAt khi Priority đổi + tests | 🔴 | 1–1.5d | — (độc lập, giá trị lớn nhất: hồi sinh cả tầng SLA) |
| NS-13 | Fix R2 + R6 **(Q8=A)** — consumer auto-tạo ticket P1 (`TicketOrigin=System`) khi không có ticket active; fallback chỉ chọn ticket incident | 🔴 | 1d | NS-12 (ticket mới cần timer đúng) |
| NS-14 | Fix R3 — NotificationService consume BatteryCascadeRiskHighEvent (push + email Manager) | 🟡 | 0.5d | — |
| NS-15 | Fix R4 — recompute score cho asset hết alert Open (decay) + tests | 🟡 | 0.5d | — |
| NS-16 | R7 — OrderBy CascadeRiskUpdatedAt trong batch scan | ⚪ | 0.25d | Gộp vào NS-15 được |

> **Thứ tự khuyến nghị nếu demo cascade risk cho hội đồng:** NS-07/08 (vá đầu vào alert) → NS-12 (hồi sinh SLA) → NS-13 (không rơi event) → NS-14 (Manager được báo) → NS-15 (heat map đáng tin).

---

## 11. Phụ lục D — CÁCH LY PIN NGUY HIỂM: audit hiện trạng + 3 phương án + khuyến nghị

> Audit code 2026-07-09 trên cả 2 repo (backend + iot). Câu hỏi gốc: *"hệ thống đã có tính năng cách ly pin khi pin bị nguy hiểm chưa?"*
> **Kết luận: CHƯA — hệ thống hiện tại chỉ ĐỌC và BÁO, không có đường điều khiển nào can thiệp vật lý vào pin.** Nối tiếp Phụ lục C: chuỗi phản ứng dừng ở "nâng P1 cho con người xử lý".

### 11.1 Hiện trạng đã xác minh — vì sao kết luận CHƯA có

**a) Kênh lệnh downlink TỒN TẠI nhưng không có lệnh cách ly.**

Hạ tầng đã chạy: `POST /api/admin/iot-devices/{id}/command` (Admin) → MQTT `solar/{deviceCode}/cmd` → device ack về `solar/{deviceCode}/cmd/ack`. Backend tự nhận *"không validate sâu — chỉ relay JSON xuống MQTT"* (`AdminIotDevicesController` doc-comment). Firmware chỉ hiểu đúng **3 lệnh** (`iot/firmware-esp32/src/cmd/cmd_logic.h:22-27`):

| Lệnh | Tác dụng | Ghi chú |
|---|---|---|
| `set_interval` | Đổi chu kỳ đo 1–3600s | bounds check firmware |
| `trigger_ota` | Update firmware OTA | |
| `request_heartbeat` | Yêu cầu heartbeat ngay | |

Type lạ → ack `"unknown" / "unsupported command type"` (`command_handler.cpp:125-130`). Gửi lệnh `"isolate_battery"` hôm nay = device từ chối lịch sự.

**b) Không có phần cứng lẫn giao thức để cách ly.**
- Firmware **không có driver relay/contactor/breaker**, không GPIO nào cấu hình điều khiển nguồn (grep toàn src: "relay/mosfet/shutdown" chỉ khớp ngữ cảnh khác — OutboxRelay, WiFi, LWT).
- Kênh Modbus tới BMS **chỉ đọc**: `modbus_bms.cpp` chỉ gọi `readHoldingRegisters`/`readInputRegisters`, không một write nào → không thể ra lệnh BMS ngắt FET.
- Những thứ *trông giống* cách ly trong dữ liệu đều là **BMS tự bảo vệ rồi báo về**, phần mềm chỉ decode: `SWLK` (MOSFET lock), `OVP/OCD/SCD` protection flags (`decodeErrorCode`), `ChargingState.Bypass` (trạng thái, không phải lệnh).

**c) "Cách ly" mức logic hiện có chỉ là NHÃN.**
- `BatteryStatusEnum` = Active/Inactive/Decommissioned; Admin đổi qua PUT update asset (`UpdateBatteryAssetCommandHandler:111`). Đặt `Inactive` **không dừng ingest, không dừng anomaly scan** (cả 2 chỉ filter `!IsDeleted`) — không có hệ quả nghiệp vụ nào.
- IoT device có auto-Decommissioned (>50 outlier/h) + endpoint `revoke-key`, nhưng device Decommissioned **chỉ bị chặn heartbeat/provision (409)** — endpoint ingest vẫn nhận dữ liệu bình thường (`BatchIngestSensorReadingsCommandHandler:325-345` không có check chặn). Muốn cắt hẳn nguồn dữ liệu phải revoke API key (401).

**d) Spec cũng chưa định nghĩa.** `overall.md` không có mục "cách ly pin" — mọi match "isolat" là thứ khác (Isolation Forest AI, quarantine file, tenant isolation). Business flow hiện tại: nguy hiểm → P1 → **Staff đến site cô lập thủ công**, ngoài hệ thống, không ai track.

**Lớp cách ly vật lý DUY NHẤT đang tồn tại = BMS hardware tự ngắt** (quá dòng/ngắn mạch/quá nhiệt → FET off trong mili-giây) — nằm ngoài quyền kiểm soát của phần mềm.

### 11.2 Nguyên tắc thiết kế — đọc TRƯỚC khi chọn phương án

1. **Safety-critical:** ngắt pin từ xa = cắt điện khách hàng thật. Ngắt nhầm tệ hơn không ngắt. → Mọi phương án đều **semi-auto** (hệ thống đề xuất → con người xác nhận), KHÔNG full-auto trong scope capstone.
2. **Điều kiện tiên quyết — fix noise trước:** trigger cách ly ăn dữ liệu từ pipeline alert, mà pipeline đang vừa nén nhầm sự cố thật (N1) vừa spam Critical giả real mode (N4). Auto-trigger trên nền đó = ngắt điện oan hàng loạt. **NS-07/NS-08 phải xong trước.**
3. **Câu chuyện phân tầng (dùng khi hội đồng hỏi "pin cháy thì sao"):**
   - Lớp 1 — BMS tự ngắt bằng phần cứng, mili-giây (đã có, ngoài phần mềm).
   - Lớp 2 — hệ thống phát hiện sớm + điều phối con người, phút (đã có: alert → cascade → P1).
   - Lớp 2.5 — **quy trình cô lập có kiểm soát** (phần này đang thiếu → chính là feature đề xuất dưới đây).

### 11.3 Ba phương án theo bậc thang

#### ISO-A — "Isolation workflow" thuần phần mềm (quy trình cô lập có kiểm soát) ⭐ khuyến nghị

Không ngắt điện bằng phần mềm — biến hành động cô lập thủ công (đang tồn tại ngoài hệ thống, không ai track) thành **quy trình được quản lý, xác minh và audit**:

- **Trạng thái mới `Isolated`** cho BatteryAsset với ngữ nghĩa THẬT (khác `Inactive` chỉ là nhãn):
  - Loại khỏi dashboard vận hành bình thường, hiện cờ đỏ riêng.
  - Dừng auto-tạo ticket/alert mới cho pin đó (đã có người xử lý — tránh spam).
  - Ghi `IsolatedAt` / `IsolatedByUserId` / `IsolationReason` / `RelatedTicketId` vào audit.
- **Luồng nghiệp vụ:**
  ```
  Trigger (cascade High / Overheat Critical / Manager chủ động)
    → hệ thống ĐỀ XUẤT cách ly trên ticket P1 (khuyến nghị + checklist an toàn)
    → Staff đến site: ngắt breaker / tháo kết nối vật lý
    → xác nhận trên app (checklist + ảnh chụp hiện trường)
    → asset chuyển Isolated + audit đầy đủ
    → sửa xong → Manager phê duyệt tái kết nối → asset về Active
  ```
- **Điểm ăn tiền — xác minh bằng chính telemetry (không cần phần cứng nào):** sau khi Staff xác nhận cô lập, hệ thống theo dõi reading của pin đó — nếu vẫn nhận reading `|current| > 0` (pin vẫn đang nạp/xả) → cảnh báo **"cô lập chưa thành công"**. Ngược lại khi đã Isolated mà telemetry im hẳn → tick xanh "verified". Demo được 100% với simulator.

**Ưu:** thuần BE+FE (~1 sprint nhỏ), không phụ thuộc phần cứng, demo tốt với mock, đúng chuẩn ITIL (controlled change + audit trail), trả lời trọn câu hỏi quy trình. **Nhược:** con người vẫn là người ngắt điện — nhưng đó cũng chính là điểm an toàn.

#### ISO-B — Điều khiển FET của BMS qua Modbus/UART (cách ly bán tự động thật) — stretch goal

BMS pilot của dự án **có sẵn lệnh tắt FET sạc/xả từ xa**: JBD SP04S100A (UART protocol, lệnh MOSFET control 0xE1), Daly R32S (charge/discharge MOS switch). Nghĩa là **BOM = 0 đồng**, chỉ cần phần mềm:

- Firmware: thêm `CommandKind::IsolateBattery` + driver **ghi** register (hiện Modbus chỉ đọc) — verify write map từng model bằng Modbus scanner trước (quy trình có sẵn trong `docs/bms-register-map.md`).
- Backend: whitelist command type + luồng xác nhận 2 bước + trạng thái lệnh.
- Tận dụng nguyên kênh `solar/{deviceCode}/cmd` + ack đã chạy.

**Ưu:** cách ly thật, demo "bấm nút trên web → pin ngừng xả" rất ấn tượng. **Nhược:** (1) write register map KHÔNG chuẩn hóa giữa các model — phải verify datasheet + test bench từng con; (2) đòi hỏi hardware rig thật chạy (`USE_MOCK_BMS=0`) — hiện chưa có; (3) FET off ≠ cách ly galvanic tuyệt đối — BMS treo/chết thì lệnh vô nghĩa (vẫn cần lớp BMS hardware + con người làm backstop); (4) rủi ro an toàn cao nhất trong 3 phương án nếu làm ẩu.

#### ISO-C — Contactor/relay DC ngoài, ESP32 điều khiển GPIO — KHÔNG khuyến nghị (future work)

Cách ly vật lý galvanic đúng nghĩa, độc lập BMS. Nhưng: contactor DC chịu dòng pin + dập hồ quang là bài toán phần cứng nghiêm túc (chọn sai là **nguy hiểm thật**, không phải nguy hiểm giả lập); thêm chi phí mua + thiết kế mạch + thời gian; giá trị cộng thêm so với ISO-B không tương xứng scope capstone. → Ghi vào mục "future work" của báo cáo.

#### Bảng so sánh

| | Cách ly thật? | Phần cứng thêm | Effort | Demo với mock? | Rủi ro an toàn | Verdict |
|---|---|---|---|---|---|---|
| ISO-A workflow | ❌ (người ngắt, hệ thống xác minh) | Không | ~1 sprint nhỏ (BE+FE) | ✅ 100% | Thấp nhất | **Làm ngay** ⭐ |
| ISO-B BMS FET | ✅ (bán tự động) | Không (BMS có sẵn) | FW+BE+bench test | ❌ cần rig thật | Trung–cao | Stretch goal |
| ISO-C contactor | ✅✅ (galvanic) | Contactor DC + mạch | Lớn (HW+FW+BE) | ❌ | Cao (phần cứng) | Future work |

### 11.4 Khung an toàn BẮT BUỘC nếu làm ISO-B (điều khiển từ xa thật)

| # | Yêu cầu | Lý do |
|---|---|---|
| 1 | **Whitelist command type ở backend** — chấm dứt "relay JSON tự do" hiện tại của `SendCommand` | Endpoint hiện cho Admin gửi payload bất kỳ; lệnh nguy hiểm phải được backend hiểu và kiểm soát, không phải pass-through |
| 2 | **Xác nhận 2 bước** — Manager đề xuất → Admin phê duyệt (hoặc 2 người khác nhau, four-eyes) | Không một người / một click nào được ngắt điện khách hàng |
| 3 | **Trạng thái lệnh đầy đủ:** Pending → Sent → Acked → **Verified** (telemetry xác nhận dòng = 0) / Failed / Timeout | "Gửi rồi quên" là cấm kỵ — ack của device chỉ nói "đã nhận lệnh", chưa nói "pin đã ngắt" |
| 4 | **Idempotency:** dùng `cmdId` (đã có trong schema) — retry không tạo lệnh đôi | Kênh MQTT QoS + retry sẵn có |
| 5 | **Audit:** ai đề xuất, ai duyệt, lúc nào, score/alert nào là căn cứ — ghi AuditLog + TicketActivity | Cùng chuẩn safety-override của cascade risk (Phụ lục C) |
| 6 | **Fail-safe khi device offline:** lệnh không tới → hệ thống phải NÓI THẬT "chưa cách ly được, cần Staff đến site" — không im lặng | Đường offline luôn tồn tại; ISO-A là fallback của ISO-B |
| 7 | **Không auto-trigger** từ alert/cascade khi chưa fix N1/N4/N5 và chưa có thời gian vận hành đủ để tin false-positive rate | §11.2 nguyên tắc 2 |

### 11.5 Đề xuất issue (nối bảng §9.2 / §10.5)

> **📌 Q9=D (2026-07-14): CHƯA làm đợt này — NOTE LÀM SAU.** Toàn bộ NS-17..20 (cách ly pin) chuyển sang nhóm **Deferred** của Sprint Bonus. Giữ nguyên thiết kế ISO-A/B/C ở §11 để tái dùng khi mở lại. ISO-A (workflow phần mềm + telemetry verify) vẫn là hướng ưu tiên khi khởi động lại.

| # | Task | Mức | Ước | Phụ thuộc |
|---|---|---|---|---|
| NS-17 | ISO-A: trạng thái `Isolated` + luồng đề xuất→xác nhận→tái kết nối + audit + chặn auto-ticket khi Isolated | 🟢 feature | 2–3d BE | NS-12/13 (gắn vào ticket P1) |
| NS-18 | ISO-A: telemetry verification — theo dõi current sau xác nhận cô lập, cảnh báo "chưa thành công" / tick "verified" | 🟢 feature | 1d BE | NS-17; dùng chung filter primary (§1.4) |
| NS-19 | ISO-A: FE — checklist cô lập + upload ảnh + màn hình phê duyệt tái kết nối + cờ đỏ dashboard | 🟢 feature | 1.5–2d FE | NS-17 contract |
| NS-20 | (Stretch) ISO-B: verify write-register map JBD/Daly trên bench + firmware `CommandKind` mới + khung an toàn §11.4 | 🟡 stretch | 3–5d FW+BE | Hardware rig thật; NS-17 làm nền quy trình |

### 11.6 Câu trả lời chuẩn bị cho hội đồng (cách ly pin)

> *"Hệ thống phân tầng bảo vệ: (1) BMS tự ngắt bằng phần cứng trong mili-giây khi quá dòng/ngắn mạch — nhanh hơn mọi phần mềm; (2) hệ thống phát hiện sớm bất thường và rủi ro lan truyền, nâng ưu tiên P1 để con người phản ứng trong phút; (3) quy trình cô lập có kiểm soát đảm bảo hành động an toàn được đề xuất, xác nhận, **xác minh bằng chính dữ liệu telemetry** và audit lại được. Chúng em chủ động KHÔNG cho phần mềm tự ngắt điện — vì với hệ thống giám sát, ngắt nhầm điện của khách hàng nguy hiểm hơn cảnh báo trễ; quyền quyết định cuối thuộc về con người có mặt tại hiện trường."*

---

## 12. Phụ lục E — SỰ CỐ MÔI TRƯỜNG (Environmental Incident): audit định nghĩa + 4 dây nối còn thiếu

> Audit code 2026-07-09 trên cả 2 repo. Câu hỏi gốc: *"hệ thống đã có định nghĩa các loại sự cố môi trường chưa?"*
> **Kết luận: CÓ — định nghĩa đầy đủ, đồng bộ 2 đầu backend ↔ firmware, pipeline vận hành khá hoàn chỉnh (Sprint 5B #100/#102/#104). Phần thiếu nằm ở 4 DÂY NỐI: ambient detection chưa cắm, ticket chưa auto-tạo, người chưa report tay được, MQ2 hardcode Smoke.**

### 12.1 Những gì ĐÃ có (verified từng file)

**a) Enum định nghĩa loại sự cố — đồng bộ int value 2 đầu.**

`EnvironmentalIncidentTypeEnum` (`BatteryService.Domain/Enums/EnvironmentalIncidentEnums.cs`) và mirror firmware (`iot/firmware-esp32/src/sensor/environmental_incident.h:34-40` — comment ghi rõ gửi INT khớp backend):

| Giá trị | Loại | Nghĩa | Nguồn phát hiện TỰ ĐỘNG hiện tại |
|---|---|---|---|
| 1 | `Smoke` | Khói | ✅ MQ2 (`mq2.cpp:108` — Critical) |
| 2 | `FireDetected` | Cháy | ❌ chưa có nguồn |
| 3 | `GasLeak` | Rò khí | ❌ chưa có nguồn (dù MQ2 vốn là gas sensor — xem E4) |
| 4 | `Flood` | Ngập nước | ✅ water leak sensor (`water_leak.cpp:74` — Critical) |
| 5 | `OverheatHazard` | Nguy cơ quá nhiệt | ❌ chưa có nguồn |
| 99 | `Other` | Khác | ❌ (dành manual — nhưng xem E3) |

Lifecycle `EnvironmentalIncidentStatusEnum`: `Open=1 → Acknowledged=2 → Resolved=3 / FalseAlarm=4`.

**b) Entity `EnvironmentalIncident : AuditableEntity`** — site-level (`SiteId`), **cố ý tách khỏi Alert** battery-level (Alert link ngược qua `EnvironmentalIncidentId`). Track đầy đủ vòng đời: `ReportedBy/DetectedAt`, `AcknowledgedBy/At`, `ResolvedBy/At + ResolutionNote`, `FalseAlarmBy/At + FalseAlarmReason`, `Notes`.

**c) API `api/environmental-incidents`** (`EnvironmentalIncidentsController.cs`):

| Endpoint | Auth | Ghi chú |
|---|---|---|
| `POST /` | **ApiKey** scope `EnvironmentalIngest` | Report từ IoT — xem E3 |
| `POST {id}/acknowledge` | JWT Admin/Manager/Staff | |
| `POST {id}/resolve` | JWT Admin/Manager/Staff | ResolutionNote bắt buộc 5–2000 ký tự |
| `POST {id}/false-alarm` | JWT Admin/Manager | FalseAlarmReason bắt buộc 5–2000 ký tự |
| `GET /` + `GET {id}` | JWT mọi role | list + detail |
| `GET by-site/{siteId}/active` | JWT | incident đang mở của site |

**d) Firmware chống spam tử tế:** `IncidentTrigger` (`incident_trigger.h`) — edge-detection thuần (chỉ fire tại cạnh chuyển bình thường→bất thường; active giữ liên tục KHÔNG lặp; chattering trong cooldown bị suppress; nước có sẵn lúc boot = cạnh lên → fire). Cooldown 5 phút (`MQ2_REARM_COOLDOWN_MS` / `WATER_LEAK_REARM_COOLDOWN_MS = 300000`); MQ2 warm-up 30s mới đọc tin cậy. → Khói bay 10 phút liên tục chỉ report 1 lần.

**e) Luồng xử lý khi report** (`ReportEnvironmentalIncidentCommandHandler`):
```
POST report (validate: SiteId, IncidentType hợp lệ, DetectedAt không tương lai >5', Notes ≤1000)
  → tạo EnvironmentalIncident (status Open)
  → tự tạo Alert SITE-LEVEL: AnomalyType=EnvironmentalIncident, BatteryAssetId=null,
    SiteId set, dedup 1h, Unit="incident"
  → outbox EnvironmentalIncidentDetectedEvent
    (payload có sẵn CustomerId + SiteName — consumer khỏi gọi ngược BatteryService)
  → metric detection latency (Sprint 7 #118 — lag DetectedAt → ghi nhận)
```
- **Bypass noise suppression** — `AnomalyDetectionService:293`: EnvironmentalIncident fire ngay, không chờ đủ tần suất (đúng thiết kế an toàn §8.2).
- **Resolve** → đóng luôn mọi Alert liên quan (`incident.Alerts` → Resolved) + publish `EnvironmentalIncidentResolvedEvent` với cờ `WasFalseAlarm` riêng cho audit.
- Report/dashboard: `EnvironmentalIncidentsReportQueryHandler` + `GetBatteryDashboardStatsQueryHandler` đã đếm incident.

**f) Họ hàng gần nhưng KHÁC khái niệm (phân tầng đúng):** `HighAmbientTemp=9 / HighHumidity=10 / HighTempHumidityCombo=11` (AnomalyTypeEnum) + `AmbientThresholdConfig` per-site (warning/critical riêng từng metric + combo rule) = **"điều kiện môi trường vượt ngưỡng"** (dữ liệu SHT31 liên tục); còn `EnvironmentalIncident` = **"sự cố"** (sự kiện rời rạc từ sensor chuyên dụng). 2 tầng riêng — thiết kế tốt, nhưng tầng ambient đang đứt dây (E1).

### 12.2 — 4 DÂY NỐI còn thiếu (E1..E4, bug report chi tiết)

#### 🔴 E1 — `DetectAmbient` KHÔNG có caller trong production — ambient anomaly defined-but-not-wired

**Bằng chứng:** grep toàn BatteryService — `AnomalyRules.DetectAmbient` (`AnomalyRules.cs:121-176`) chỉ được gọi trong **tests** (`AnomalyRulesSprint5BTests`, `Sprint5BAmbientFlowIntegrationTests`, `AmbientHandlersTests`). `BatchIngestAmbientReadingsCommandHandler` chỉ insert reading, không detect; `AnomalyDetectionService` chỉ scan `sensor_readings`; không background service nào đọc `ambient_readings` cho mục đích anomaly (chỉ query/report/dashboard/weather-sync dedup).

**Hệ quả:** `AmbientThresholdConfig` có endpoint upsert hoạt động (`AmbientThresholdHandlers`) — Admin cấu hình ngưỡng nhiệt/ẩm xong **không ai dùng nó để phát hiện**. 3 anomaly type 9/10/11 không bao giờ được sinh ra ở production. Nhà kho 45°C + ẩm 95% (combo rule — điều kiện thoát nhiệt pin lithium) → hệ thống im lặng. Cùng họ defined-but-not-wired với N2/R3.

**Fix đề xuất:** thêm bước detect vào chính `BatchIngestAmbientReadingsCommandHandler` (sau insert, trước SaveChanges — load `AmbientThresholdConfig` của site, gọi `DetectAmbient`, tạo Alert site-level như khuôn environmental incident) — hoặc thêm scan ambient vào `AnomalyDetectionService` tick. Ưu tiên cách 1 (detect-at-ingest: đơn giản, latency thấp, ambient 1 phút/mẫu nên không nặng).

**Test:** ingest reading vượt `HighAmbientTempCritical` → assert Alert `HighAmbientTemp` Critical; combo temp+humidity cùng vượt → assert `HighTempHumidityCombo`; threshold `Enabled=false` → không alert.

#### 🔴 E2 — TicketService KHÔNG consume `EnvironmentalIncidentDetectedEvent` → cháy/ngập KHÔNG auto-tạo ticket

**Bằng chứng:**
- Doc-comment event (`SharedContracts/Events/EnvironmentalIncidentEvents.cs`) ghi rõ: *"Subscribers: NotificationService (notify Manager/Admin), **TicketService**"*.
- Consumers thực tế của TicketService: `AccountSync`, `BatteryAnomalyDetected`, `BatteryCascadeRiskHigh`, `CreateTicketFromAlert` (consume command nội bộ, không phải event này) — **không có** consumer environmental.
- Ticket entity ĐÃ có cột `EnvironmentalIncidentId` (thấy trong mọi migration Designer gần đây) — chỗ ngồi có sẵn, chưa ai ngồi.

**Hệ quả:** sự cố môi trường Critical (khói, ngập) chỉ dừng ở notification — **không có ticket, không có người được assign, không có SLA đếm giờ**. So sánh nghịch lý: pin hơi nóng quá ngưỡng (Overheat Critical) → auto-ticket đầy đủ; **nhà kho đang cháy → chỉ có notify**. Đây là loại sự cố P1-Critical rõ nhất theo Priority Matrix (safety, scope Site).

**Fix đề xuất:** thêm `EnvironmentalIncidentDetectedConsumer` bên TicketService — clone khuôn `BatteryAnomalyDetectedConsumer`, tạo ticket site-level gắn `EnvironmentalIncidentId`, Priority từ Severity (Critical → P1). Đóng vòng: consume thêm `EnvironmentalIncidentResolvedEvent` với `WasFalseAlarm=true` → auto-close ticket lý do false alarm.

**Test:** publish event Critical → assert ticket P1 + `EnvironmentalIncidentId` set; ResolvedEvent false-alarm → assert ticket đóng.

#### 🟡 E3 — Con người KHÔNG report thủ công được: POST chỉ nhận ApiKey IoT

**Bằng chứng:** `EnvironmentalIncidentsController:68-74` — `POST /` authorize bằng `ApiKey` scheme + scope `EnvironmentalIngest`, **không có biến thể JWT**. Staff đứng tại site nhìn thấy cháy → không có cách tạo incident trong hệ thống (field `ReportedBy` tồn tại nhưng chỉ IoT dùng được đường này). Các loại `FireDetected`/`OverheatHazard`/`Other` vốn không có sensor → thực tế **không ai tạo được**.

**Lưu ý khuôn có sẵn:** bài này giống hệt memory note "IoT devices auth split" — controller ApiKey vs controller JWT tách riêng. → Thêm endpoint JWT (`POST /manual`, roles Admin/Manager/Staff) hoặc controller riêng, `ReportedBy` lấy từ token thay vì body.

**Test:** JWT Staff POST manual → 201 + incident Open; Customer → 403.

#### 🟡 E4 — MQ2 là cảm biến GAS nhưng hardcode report `Smoke` — `GasLeak` định nghĩa xong bỏ không

**Bằng chứng:** `mq2.cpp:108` — mọi trigger đều `IncidentType::Smoke`. MQ2 về bản chất đo hỗn hợp khí cháy (LPG/propane/methane/khói); phân biệt khói vs rò khí bằng 1 con MQ2 đơn là không đáng tin — nhưng hiện tại thì `GasLeak=3` không bao giờ được dùng, và demo/hội đồng có thể hỏi "GasLeak khi nào fire?".

**Fix đề xuất (chọn 1, rẻ):** (a) đổi nhãn report của MQ2 thành `GasLeak` nếu muốn đúng bản chất sensor, giữ `Smoke` cho tương lai (sensor khói quang học riêng); (b) giữ nguyên `Smoke` + ghi rõ vào docs/glossary rằng MQ2 đại diện "khói/khí cháy", `GasLeak`/`FireDetected` là future hardware. Khuyến nghị **(b)** — không đụng firmware, chỉ docs; quan trọng là câu trả lời nhất quán khi bảo vệ.

### 12.3 Đề xuất issue (nối bảng §9.2 / §10.5 / §11.5)

| # | Task | Mức | Ước | Phụ thuộc |
|---|---|---|---|---|
| NS-21 | Fix E1 — wire `DetectAmbient` vào ambient ingest (detect-at-ingest + Alert site-level) + tests | 🔴 | 1d BE | — |
| NS-22 | Fix E2 — TicketService consume `EnvironmentalIncidentDetectedEvent` (auto-ticket P1, gắn `EnvironmentalIncidentId`) + consume ResolvedEvent false-alarm auto-close + tests | 🔴 | 1–1.5d BE | NS-12 (ticket mới cần SLA timer) |
| NS-23 | Fix E3 — endpoint JWT report thủ công (Admin/Manager/Staff, ReportedBy từ token) + FE form report | 🟡 | 0.5d BE + 0.5d FE | — |
| NS-24 | Fix E4 **(Q10=B)** — **đổi nhãn firmware MQ2 `Smoke`→`GasLeak`** (`iot/.../mq2.cpp`), giữ `Smoke` cho sensor khói quang học tương lai + cập nhật docs/glossary + câu trả lời hội đồng | ⚪→FW | 0.5d | Đụng repo `iot` |

> **Móc nối các phụ lục:** E2 phụ thuộc NS-12 (R1 — SLA timer) giống ISO-A; E1 dùng chung khuôn Alert site-level với environmental incident; incident `OverheatHazard` (chưa có nguồn) chính là ứng viên trigger tự nhiên cho đề xuất cách ly ISO-A (§11.3) — thermal runaway site-level → đề xuất cô lập pin trong site.

---

## 13. Phụ lục F — ANOMALY CLASSIFICATION: tầng rule-based ĐÃ xong · tầng AI (bảng riêng) CHƯA bắt đầu

> Audit code 2026-07-09. Câu hỏi gốc: *"backend đã làm phần anomaly classification — tên, ngưỡng — chưa? Có phải phải có 1 bảng riêng cho anomaly classification không?"*
> **Kết luận 2 vế:** (1) classification **rule-based** (tên + ngưỡng + luật severity) đã làm bài bản, có citation khoa học; (2) **bảng riêng thì bạn nhớ ĐÚNG** — spec `overall.md §30.3` (P0) định nghĩa 2 bảng `AnomalyClassification` + `SohPrediction` cho tầng AI, nhưng **0 dòng code nào tồn tại** — và plan ráp AI hiện tại (`aibeiotrealtime.md`) đang bỏ qua 2 bảng này (lệch spec, cần chốt).

### 13.1 Tầng 1 — Rule-based classification: ĐÃ CÓ đầy đủ

**a) Tên phân loại — `AnomalyTypeEnum` 15 loại** (`BatteryService.Domain/Enums/AnomalyTypeEnum.cs`, giá trị 1–15 là wire value cross-service, không đổi được):

| # | Tên | # | Tên |
|---|---|---|---|
| 1 | `Overheat` | 9 | `HighAmbientTemp` |
| 2 | `Overvoltage` | 10 | `HighHumidity` |
| 3 | `Undervoltage` | 11 | `HighTempHumidityCombo` |
| 4 | `LowSoc` | 12 | `HighInternalResistance` |
| 5 | `RapidDischarge` | 13 | `CellImbalance` |
| 6 | `AbnormalCharging` | 14 | `EnvironmentalIncident` |
| 7 | `DeviceOffline` | 15 | `SensorMismatch` |
| 8 | `SohDegradation` | | |

*(plan AI sẽ thêm 16 = `PredictedSohDegradation` — xem §13.3)*

Chiều phân loại thứ 2: `AlertSeverityEnum` Info=1 / Warning=2 / Critical=3.

**b) Cơ sở khoa học:** mỗi loại + ngưỡng có paper citation trong `.claude/docs/ai-research-references.md` **Phụ lục B2 §1** — bảng "Citation per anomaly type" (vd Overheat 60°C LiFePO4 / 55°C NMC cite Feng et al., *J. Power Sources* 2018, DOI 10.1016/j.jpowsour.2017.10.069 + IEEE Std 1625-2008). Sẵn sàng cho câu hỏi hội đồng "tại sao ngưỡng X".

**c) Ngưỡng — 3 tầng:**

*Tầng DB per BatteryType* — `ThresholdConfig` (CRUD `api/thresholds`, versioning `EffectiveFromUtc`/`IsActive`): Voltage Min/Max, Temperature Min/Max, SOC Warning/Critical, CurrentMaxCharge/Discharge (nullable), SOH Warning/Critical (nullable), InternalResistanceMax, CellVoltageDeltaMax + bộ noise suppression (§8.2). **Đã seed số thật** (`BatteryDataSeeder:243-245`):

| Loại pin | Voltage | Nhiệt độ | SOC warn/crit | SOH warn/crit |
|---|---|---|---|---|
| LiFePO4 | 10.5–14.6V | −10..60°C | 20% / 10% | 85% / 75% (EOL) |
| NMC | 42–54.6V | −10..55°C | 25% / 15% | 85% / 75% |
| NCA | 21–29.2V | −10..55°C | 25% / 15% | 85% / 75% |

*Tầng DB per Site* — `AmbientThresholdConfig`: HighAmbientTemp/HighHumidity Warning+Critical riêng + combo rule (nhưng chưa cắm — E1 §12.2).

*Tầng hằng số code* — `AnomalyRules`: `OverheatCriticalDeltaC = 5°C` (Warning vs Critical), SensorMismatch `0.5V`/`5°C`, outlier ingest bounds (0–1000V, ±1000A, −50..150°C).

**d) Luật phân loại (mapping rule → severity)** — `AnomalyRules.Detect` (pure static, không IO — dễ unit test):

| Loại | Rule | Severity |
|---|---|---|
| Overheat | > TempMax / > TempMax+5°C | Warning / **Critical** |
| Over/Undervoltage | ngoài Voltage Min/Max | **Critical** luôn (an toàn) |
| LowSoc | < SocWarning / < SocCritical | Warning / Critical |
| RapidDischarge | đang xả và \|I\| > CurrentMaxDischarge | **Critical** |
| AbnormalCharging | I > CurrentMaxCharge | **Critical** |
| SohDegradation | < SohWarning / < SohCritical | Warning / Critical |
| HighInternalResistance / CellImbalance | > ngưỡng Tier-2 | **Critical** |
| DeviceOffline | im lặng > OfflineThresholdMinutes (10') | Warning |
| SensorMismatch | ΔV > 0.5V hoặc ΔT > 5°C (BMS↔IoT) | Warning |
| EnvironmentalIncident | theo request (default Critical) | Info/Warning/Critical |
| Ambient 9/10/11 | theo AmbientThresholdConfig + combo | Warning/Critical (chưa wired — E1) |

**e) Gap của tầng rule-based (mới phát hiện thêm):**
- 🟡 **F1 — Thiếu rule Undertemp:** `ThresholdConfig.TemperatureMin` tồn tại (entity `:15`) và **được seed −10°C** cho cả 3 loại pin, nhưng `AnomalyRules.Detect` **không có check nào dùng nó** — không tồn tại anomaly "nhiệt độ quá thấp". Sạc pin lithium dưới 0°C gây lithium plating (nguy hiểm thật, có cơ sở trong chính citation B2). Field đang chết y hệt kiểu N2/`TemperatureMin`. → Thêm rule `Undertemp` (cần thêm enum value mới — lưu ý wire value cross-service).
- Nhắc lại 2 gap đã ghi ảnh hưởng tầng này: N4 (Detect không lọc source primary — §9), E1 (ambient chưa cắm — §12).

### 13.2 Tầng 2 — AI classification với BẢNG RIÊNG: spec CÓ, code CHƯA

**Spec `overall.md §30 "AI Module integration — P0"`** định nghĩa đầy đủ:

**§30.3 — 2 bảng mới cho BatteryService:**

`AnomalyClassification`:
| Field | Type | Ý nghĩa |
|---|---|---|
| `AlertId` | Guid? FK | link Alert nếu classify cho alert |
| `BatteryAssetId` | Guid | — |
| `Classification` | enum **Normal=1 / Degrading=2 / Failed=3** | output Isolation Forest + LSTM |
| `AnomalyScore` | decimal(8,6) | Isolation Forest decision score |
| `Confidence` | decimal(4,3) | — |
| `ModelVersion` | string(20) | "1.0" / "1.1" — khớp artifact versioning |
| `ClassifiedAt`, `LatencyMs` | | monitoring (SLA inference < 100ms) |
| `StaffFeedback` | enum? Correct=1/FalsePositive=2/FalseNegative=3 | **feedback loop** — Staff confirm sau resolve |
| `StaffFeedbackByUserId/At` | | audit người đánh giá |

`SohPrediction`: PredictedSohPercent, Confidence, ModelVersion, InputWindowStart/EndUtc, PredictedAt (indexed DESC), LatencyMs, RawResponse (jsonb debug).

**§30.2 — luồng hybrid threshold + AI:**
```
Threshold breached → AiInferenceClient.ClassifyAnomaly(30 readings cuối)
   ├── Normal    → log ứng viên false-positive (Staff review)
   ├── Degrading → Alert severity = Warning
   └── Failed    → Alert severity = Critical → publish event
   └──→ PredictSoh(window) → enrich Alert với SOH%
```

**§30.4 — interface `IAiInferenceClient`:** PredictSohAsync / ClassifyAnomalyAsync / HealthAsync (HTTP → `http://ai-module:8000`).

**Thực tế code (verified 2026-07-09):** grep toàn services — **0 file** chứa `AnomalyClassification`/`SohPrediction`; DbContext BatteryService không có DbSet tương ứng; không có `AiInferenceClient` (AI client duy nhất trong backend là chat AI DeepSeek/Gemini bên TicketService — không liên quan). Bảng anomaly-related thực tế chỉ có: `ThresholdConfigs`, `Alerts` (cột `AnomalyType` int), `NoiseBreachEvents`, `AmbientThresholdConfigs`, `EnvironmentalIncidents`.

### 13.3 — 🔴 F2 — Lệch spec §30 vs plan `aibeiotrealtime.md`: 2 tài liệu mô tả 2 mức độ khác nhau

| | `overall.md §30` (spec gốc, P0) | `aibeiotrealtime.md` (plan ráp AI hiện tại) |
|---|---|---|
| Kiến trúc | Hybrid: threshold breach → classify per-alert → severity từ AI | `SohPredictionBackgroundService` (clone WeatherSync) quét định kỳ → `/predict` |
| Kết quả AI lưu ở đâu | 2 bảng riêng `AnomalyClassification` + `SohPrediction` | **Đi thẳng vào `Alerts`** (AnomalyType mới `PredictedSohDegradation = 16`), không bảng riêng |
| Feedback loop | `StaffFeedback` Correct/FalsePositive/FalseNegative | **Không có** |
| Bằng chứng model chạy thật | score/confidence/latency/modelVersion persist | Mất — chỉ còn Alert |

**Hệ quả nếu đi theo plan tối giản mà không chốt lại:** không trả lời được câu hỏi hội đồng *"các em đánh giá model trên production thế nào?"* (không có ground-truth feedback, không có lịch sử score/confidence); không đối chiếu được classification giữa các `ModelVersion` khi retrain v1.0→v1.1.

**Khuyến nghị chốt:** khi làm AI integration, giữ **tối thiểu bảng `AnomalyClassification`** (kể cả bỏ `SohPrediction` cho gọn) — chi phí 1 entity + 1 migration + vài dòng insert trong background service, đổi lại: (1) bằng chứng model chạy thật (score/confidence/latency lưu được, chart lên dashboard); (2) feedback loop để nói chuyện precision/recall với hội đồng; (3) đối chiếu chất lượng giữa model version. Cập nhật `aibeiotrealtime.md` thêm task cho bảng này + endpoint Staff feedback.

### 13.4 Đề xuất issue (nối bảng NS)

| # | Task | Mức | Ước | Phụ thuộc |
|---|---|---|---|---|
| NS-25 | F1 **(Q11=A)** — thêm rule `Undertemp` + enum **`AnomalyTypeEnum.Undertemp = 16`** (wire value cross-service — đồng bộ FE + mọi service) + citation B2 + tests | 🟡 | 0.5d BE | Báo team về value 16 |
| NS-26 | F2 **(Q12=A — theo spec §30 ĐẦY ĐỦ)**: 2 bảng `AnomalyClassification` + `SohPrediction` + `StaffFeedback` loop + migration + insert flow AI + endpoint feedback | 🔴 (khi làm AI) | 1.5–2d BE | Sprint AI (`aibeiotrealtime.md`); KHÔNG dùng AnomalyType 16 cho PredictedSoh (đã cấp cho Undertemp) |
| NS-27 | F2 — đồng bộ tài liệu: cập nhật `aibeiotrealtime.md` khớp quyết định NS-26, đánh dấu §30.3 phần nào làm/không làm | ⚪ | 0.25d docs | NS-26 |

> **Móc nối:** bảng `AnomalyClassification` cũng là chỗ ghi kết quả khi ISO-A cần bằng chứng "pin này AI xác nhận Failed" trước khi đề xuất cách ly (§11.3); `LatencyMs` phục vụ benchmark SLA inference < 100ms của rules AI (`.claude/rules/tech/ai.md`).
