# Kế hoạch 2 Sprint: BE-IoT (Realtime SSE) & BE-AI (Phân tích)

> Tách thành **2 sprint độc lập**:
> - **SPRINT BE-IoT** — lấy data từ IoT → lưu → **làm sạch (calibration + loại outlier)** → **stream realtime lên FE + Mobile** cho 1 hoặc nhiều pin của khách, dùng **SSE** (đồng bộ §34/§34.10 overall.md — KHÔNG SignalR).
> - **SPRINT BE-AI** — nối Backend → AI module để **phân tích** (SOH/RUL/anomaly/prescription), kèm danh sách **điểm hở/chưa kết nối + recommend**.
>
> Khuôn clone có sẵn: `OpenMeteoClient` (HttpClient + Polly) cho AI bridge · `WeatherSyncBackgroundService` (job nền) cho job gọi AI. **Realtime telemetry KHÔNG clone TicketCommentHub (SignalR)** — dùng **SSE theo §34** (transport doc đã chốt P0).
>
> **Cập nhật:** 2026-06-27 · Mọi contract đã verify trực tiếp từ code 3 repo. Realtime đã chốt **SSE** (xem overall.md §34.10 + Sprint BE-IoT-Realtime §17).

---

## 0. Ba "ngôn ngữ" của 3 phía (ngữ cảnh chung)

| Phía | Nói gì | Trạng thái (đã verify) |
|------|--------|----|
| **IoT (ESP32 + simulator)** | đẩy reading `{voltage, current, temperature, socPercent, sohPercent?, ...}` mỗi ~5s qua HTTPS `POST /api/sensor-readings/batch` + MQTT `solar/{dev}/{serial}/telemetry` | ✅ contract khớp 3 phía |
| **Backend (BatteryService)** | nhận → **làm sạch** → lưu `SensorReading` (TimescaleDB hypertable) → threshold rule → Alert → Outbox → Ticket/Notification | ✅ chạy |
| **AI (FastAPI :8000)** | `POST /predict` + `POST /prescribe/` (chỉ 2 endpoint này mounted) | ✅ chạy độc lập, ❌ **chưa nối BE** |
| **FE Web + Mobile** | đọc snapshot REST + (sẽ thêm) nhận realtime **SSE** | ⚙️ realtime chưa có |

## 1. Sơ đồ tổng thể — chỉ rõ 2 sprint cắm vào đâu

```
ESP32 ─MQTT/HTTP─▶ BatchIngestSensorReadingsCommandHandler
                     │  reject clock skew → calibration → loại outlier → INSERT → SaveChanges
                     │
                     │  ╔══════════ SPRINT BE-IoT (SSE) ════════╗
                     ├──╫─★ sau SaveChanges (data ĐÃ SẠCH) ─────╫─▶ Redis pub/sub
                     │  ║                                        ║      │
                     │  ║   SSE endpoint (BatteryService.Api)  ◀─╫──────┘
                     │  ╚════════════════════════════════════════╝      │
                     ▼                                          GET /api/sensor-readings/stream
              SensorReading (TimescaleDB)                       └─▶ FE Web + Mobile (1/nhiều pin của khách)
                     │
                     │  ╔══════════ SPRINT BE-AI ═══════════╗
                     └──╫─▶ SohPredictionBackgroundService ─╫─▶ POST /predict (AI)
                        ║      (~5 phút, gom 30 reading)     ║      │
                        ║   risk.priority P1/P2 → tạo Alert  ║──────┘
                        ╚════════════════════════════════════╝      │
                                                              Outbox → Saga → Ticket + Notification
```

---
---

# 🟦 SPRINT BE-IoT — Ingest → Làm sạch → Realtime SSE FE/Mobile

> Triển khai chính thức: **Sprint BE-IoT-Realtime** ở overall.md §17 (task `#BEIOT-RT-01..10`). Design source of truth: overall.md **§34.10**.

## I.0 Mục tiêu & phạm vi

**Mục tiêu:** dữ liệu pin từ IoT, sau khi đã **loại bỏ nhiễu/lỗi** (calibration + outlier), được **stream realtime lên FE Web (admin/manager) và Mobile (khách)** qua **SSE** để theo dõi trực tiếp **1 hoặc nhiều pin của một khách hàng**.

**Trong scope:** kênh SSE realtime + tap sau khi làm sạch + scope cho 1/nhiều pin + RBAC + mobile.
**Ngoài scope:** AI/phân tích (Sprint BE-AI), bật MQTT production (Sprint IoT-2 đã có).

## I.1 Hiện trạng — đã có gì (verified)

| Thành phần | Trạng thái | Vị trí |
|---|---|---|
| Ingest endpoint + auth per-device | ✅ | `SensorReadingsController.cs:106` `POST /api/sensor-readings/batch` |
| **Làm sạch trong handler** (clock skew, calibration `raw*scale+offset`, outlier `IsOutlier`→continue) | ✅ | `BatchIngestSensorReadingsCommandHandler.cs` |
| Lưu TimescaleDB hypertable | ✅ | migration `...SensorHypertable.cs:49` `create_hypertable('sensor_readings','time')` |
| **Quyết định SSE (P0)** đã có trong doc | ✅ | overall.md §34 (SSE, không WebSocket) + §34.10 (telemetry) |
| `BatteryAsset.CustomerId` + `SiteId` (route scope) | ✅ | `BatteryAsset.cs:14,12` |
| Redis (cho pub/sub backplane) | ✅ | hạ tầng hiện hữu |

→ **Phần ingest + làm sạch ĐÃ XONG.** Sprint này xây **lớp SSE đẩy lên FE/Mobile**.

> ⚠️ **KHÔNG clone TicketCommentHub (SignalR).** TicketService dùng SignalR cho comment vì cần **2 chiều**; telemetry là **1 chiều** → SSE đúng hơn và khớp §34. SSE trong dự án mới được specced (§34) chứ chưa build → đây là lần triển khai đầu.

## I.2 Nguyên tắc cốt lõi — stream SAU khi đã làm sạch

Thứ tự trong `BatchIngestSensorReadingsCommandHandler` (đã verify):
```
1. reject clock skew (>5 phút → bỏ batch)
2. ApplyCalibration: value = raw*scale + offset   ← khử sai số sensor
3. IsOutlier? (V>1000 / T∉[-50,150] / SOC∉[0,100]...) → continue (KHÔNG insert)  ← loại nhiễu
4. INSERT vào hypertable
5. SaveChangesAsync
   ★ TAP Ở ĐÂY → publish lên Redis → SSE đẩy xuống FE
```
→ Tap **sau bước 5** ⇒ data lên FE/Mobile **đã calibrate + đã loại outlier**. Rác bị `continue` ở bước 3 **không bao giờ** lên stream.

> ⚠️ Anomaly/noise-suppression (~60s, ThresholdCheck) KHÔNG phải lọc reading — chỉ sinh Alert. Đừng đợi nó mới stream (sẽ trễ 60s).

## I.3 Thiết kế SSE — 1 endpoint, nhiều scope (1 pin → toàn hệ thống)

- **1 endpoint SSE** (kết nối HTTP giữ lâu, một chiều server→client):
  `GET /api/sensor-readings/stream?scope=...`
- **8 dạng scope** (định tuyến ai nhận gì) qua Redis pub/sub. Publisher fan-out mỗi reading lên: `telemetry:asset:{id}` + `:customer:{id}` + `:site:{id}` (hoặc `:site:none`) + `:type:{typeId}` + `:all`.

| Scope | Phục vụ | Ai mở |
|---|---|---|
| `asset:{id}` | chi tiết **1 pin** (event `reading`) | khách (pin mình) + **staff** (bất kỳ pin) + admin/manager |
| `assets:{id1,id2,…}` | **nhiều pin tùy ý** (≤50, event `summary`) | khách (pin mình) + **staff** (bất kỳ pin) + admin/manager |
| `customer:{id}` | **mọi pin của 1 khách** | chính khách đó + admin/manager |
| `site:{id}` | **mọi pin trong 1 site** | admin/manager |
| `sites:{id1,id2,…}` | **nhiều site cùng lúc** (≤50) | admin/manager |
| `type:{batteryTypeId}` | **theo loại pin** (LiFePO4/NMC…) | admin/manager |
| `all` | **TOÀN hệ thống** | admin/manager |
| `site:none` | **pin không thuộc site nào** (SiteId=null) | admin/manager |

- **2 cấp event** (chống flood khi nhiều pin):

| Event SSE | Scope đẩy | Nội dung | Nhịp |
|---|---|---|---|
| `reading` | **chỉ `asset:{1 id}`** | reading đầy đủ (vẽ chart) | mỗi reading ~5s |
| `summary` | mọi scope còn lại | gom giá trị mới nhất **mỗi pin** — **đầy đủ field y hệt `reading`** (KHÔNG rút gọn), coalescer ưu tiên source `primary` | throttle ~3–5s |

> Chỉ **1 pin đơn** mới có `reading` chi tiết. Multi-asset / customer / site / type / all đều là `summary` (gom + throttle) để browser không ngợp. `LiveReadingDto` mang `BatteryTypeId` để route scope `type`. Multi-asset/site = subscribe **nhiều channel trên 1 kết nối**. RBAC: scope rộng/xuyên khách (customer/site/type/all/site:none) chỉ Admin/Manager; **Staff** xem được `asset`/`assets` (bất kỳ pin — phục vụ ticket/bảo trì, MVP; hardening sau = chỉ pin của ticket được giao); `assets:` của khách phải sở hữu **mọi** id.

**Đa nguồn mỗi pin (đã verify):** firmware gửi **3 reading/pin** — `primary` (BMS, sourceType=1) + `redundant` (INA226, =2) + `external-temp` (DS18B20, =2). Stream đẩy đủ cả 3 (có `sensorSourceCode`), **FE mặc định vẽ `primary`**; redundant/external-temp để đối chiếu.

## I.4 Contract event SSE (FE/Mobile ↔ BE chốt trước)

```
event: reading
data: { batteryAssetId, customerId, siteId, batteryTypeId, time, voltage, current,
        temperature, socPercent, sohPercent?, cycleCount?, chargingState?,
        internalResistanceMilliohm?, cellVoltageDeltaMv?, bmsErrorCode?,
        sourceDeviceId?, sourceType, sensorSourceCode }   // FE lọc — chart mặc định "primary"

event: summary
data: { scopeType, items:[{ batteryAssetId, customerId, siteId, batteryTypeId,
        time, voltage, current, temperature, socPercent, sohPercent?,
        cycleCount?, chargingState?, internalResistanceMilliohm?, cellVoltageDeltaMv?,
        bmsErrorCode?, sourceDeviceId?, sourceType, sensorSourceCode }] }
        // MỖI item = LiveReadingDto ĐẦY ĐỦ (parity reading) — coalescer ưu tiên primary/pin

event: ping
data: {}                                     // heartbeat 30s giữ kết nối
```
**Quy tắc bất biến:** mọi message mang `batteryAssetId` (+ `customerId`/`siteId`) để route đúng pin.

**Client render (single vs fleet):** `reading` (1 pin) → chart **đa-metric** (nhiều đường = nhiều metric của pin đó). `summary` (nhiều pin) → **fleet view: mỗi pin 1 đường**, chọn 1 metric (dropdown). Vì mỗi item đã đầy đủ field, client PHẢI push **TẤT CẢ** `items` vào chart (mỗi pin 1 chuỗi ~60 điểm) — **không** chỉ vẽ `items[0]` (bug điển hình → chỉ pin đầu nhảy). KHÔNG vẽ N pin × 8 metric cùng lúc (rối). Mẫu: `sse-telemetry-test.html`. Chi tiết: overall.md §34.10.11.

**Backfill chart (cả single lẫn fleet):** khi mở stream, nạp `/history` để chart hiện NGAY thay vì chờ vài tick SSE. 1 pin → backfill pin đó; nhiều pin → backfill **mỗi pin trong scope** (song song, **cap số pin** để khỏi flood, log phần bị cắt). `/history` trả **DESC** → client lấy `N` mới nhất rồi **đảo về tăng dần** (cũ→mới) khớp hướng SSE append, ghép điểm live đã tới (dedup `time`). Pin của scope `customer`/`site`/`type`/`all`/`site:none` resolve từ danh sách asset đã tải; `assets:`/`sites:` có id sẵn. Chi tiết: overall.md §34.10.9.

**Contract lỗi (non-2xx) — `/stream` `/latest` `/history` `/aggregate`:** đều dùng `CommonResponse`. **Field-level** (scope/Limit/Interval sai) → `400` + `listErrors:[{field, detail}]` + `message` tổng quát; **cross-field** (`from`>`to`) → `422` + `listErrors`; **lỗi khác** (auth/not-found) → `message` + `listErrors:null` (converter null-hoá list rỗng). SSE: lỗi **trước khi mở stream** mới có body CommonResponse, status 4xx **thật** (không 200+isSuccess=false). Helper: `CommonResponseWriter` (stream) / `IValidatable`+`ValidationBehavior` (query). Đã verify 6 case. Chi tiết: overall.md §34.10.12.

## I.5 Thành phần phải xây (SSE — KHÔNG SignalR)

| Việc | Ghi chú |
|---|---|
| `Api/Controllers/.../stream` endpoint (`IAsyncEnumerable<SseEvent>` + `Content-Type: text/event-stream`, `ping` 30s, `Last-Event-ID` resume) | ASP.NET Core SSE — xem §34.6/§34.10.4 |
| `Application/Interfaces/ITelemetryPublisher.cs` + `DTOs/LiveReadingDto.cs` | publish reading lên Redis channel `telemetry:{scope}` |
| `Infrastructure/Realtime/RedisTelemetryPublisher.cs` + `RedisTelemetrySubscriber` | pub/sub backplane — fan-out N instance (như §34.6) |
| `Application/Interfaces/IBatteryRealtimeAuthorizationService.cs` + impl (khách chỉ pin/customerId mình; admin/manager theo site) | authorize lúc mở stream |
| `Infrastructure/BackgroundServices/TelemetrySummaryBroadcastBackgroundService.cs` (coalesce latest/pin → publish `summary` mỗi 3–5s) | chống flood |
| Cắm vào `BatchIngestSensorReadingsCommandHandler`: gom `LiveReadingDto` của reading **đã insert** → `ITelemetryPublisher.Publish` **sau** `SaveChangesAsync`, bọc try/catch | soft-dependency |
| `BatteryService.Api/Program.cs`: map stream endpoint + CORS; **token qua query param** (EventSource không set header) | xem §34.10.7 |
| ApiGateway: **SSE passthrough** `/api/sensor-readings/stream` — **YARP 2.3 stream mặc định, đã verify OK qua `4001`** (chỉ cần không bật compression/output-cache) | §34.10 / BEIOT-RT-08 |
| FE Web hooks: `useBatteryTelemetry(assetId)` + `useSiteTelemetry(siteId)` (fetch-based SSE: `fetch-event-source`) | backfill REST khi reconnect |
| **FE Mobile** (RN/Expo): `useBatteryTelemetry` + `useCustomerTelemetry(customerId)` (`rn-eventsource`/fetch), token từ **`expo-secure-store`** | cùng endpoint |

## I.6 Tasks theo phase (map sang `#BEIOT-RT` ở §17)

| Task | Nội dung | `#BEIOT-RT` |
|---|---|---|
| **R1** — SSE 1 pin | endpoint stream + Redis pub/sub + tap ingest + scope `asset` + authz + FE Web/Mobile `useBatteryTelemetry` | RT-01/02/03/04/06/07 |
| **R2** — nhiều pin của khách | scope `customer:{id}` + `site:{id}` + `TelemetrySummaryBroadcast` (throttle) + FE `useCustomerTelemetry`/`useSiteTelemetry` | RT-05/06/07 |
| **R3** — hardening | ApiGateway SSE passthrough + reconnect Last-Event-ID + metric + flag `Realtime:Enabled` + load test | RT-08/09 |
| **R4** — realtime alert (optional) | publish `AlertRaised` ở **MỌI nơi tạo Alert** (`AnomalyDetectionService` threshold + sau này AI) qua cùng SSE | (mở rộng) |

→ Đường ngắn nhất: **R1 → R2**.

## I.7 Điều kiện deploy (cấu hình, không phải code — "confirm sau")

| Việc | Ghi chú |
|---|---|
| Fill `firmware-esp32/include/config.h` | placeholder: `API_KEY`, `BACKEND_URL`, `MQTT_BROKER_HOST/PASSWORD`; port `:7200` ≠ gateway `:4001`/BatteryService `:4006` |
| Provision sẵn device cho simulator/firmware | auth per-device theo `IotDevice.ApiKeyHash` — key phải khớp 1 device đã tạo |
| (nếu dùng MQTT) bật bridge | `Mqtt__Enabled=false` hiện tại — đã track ở Sprint IoT-2 |

## I.8 Bẫy BE-IoT (SSE)

1. **Soft dependency** — publish Redis sau commit, try/catch, không bao giờ ném lỗi vào handler. Ingest > realtime.
2. **Chỉ stream reading ĐÃ insert** — outlier đã `continue`, đừng push lại data thô.
3. **Auth SSE qua query param** — `EventSource` native KHÔNG set được header `Authorization` → dùng `?access_token=` hoặc fetch-based SSE client.
4. **Flood khi nhiều pin** — bắt buộc throttle/coalesce cho `customer`/`site` scope.
5. **Redis pub/sub bắt buộc khi >1 instance** — thiếu thì client nối instance A không nhận reading ingest ở instance B.
6. **REST vẫn cần** — SSE chỉ delta; snapshot + backfill lúc reconnect là `/latest` `/history` `/aggregate`.
7. **ApiGateway passthrough SSE** — YARP 2.3 **stream SSE mặc định**, KHÔNG buffer (đã verify: qua gateway `4001` event `reading` tới realtime đúng 5s). Chỉ cần KHÔNG bật response compression / output caching trên gateway. → HTML test dùng thẳng gateway `4001` cho mọi request.
8. **Đa nguồn/pin** — FE mặc định vẽ `primary`; mọi message có `sensorSourceCode`.
9. **Mobile dùng CÙNG endpoint** — đừng làm cơ chế realtime riêng cho mobile.

---
---

# 🟨 SPRINT BE-AI — Nối Backend → AI để phân tích

## II.0 Mục tiêu & phạm vi

**Mục tiêu:** Backend gửi chuỗi sensor reading sang AI module để lấy **SOH dự đoán + classification + RUL + anomaly + risk.priority**, đổ kết quả vào pipeline Alert có sẵn (→ Ticket + Notification), và (optional) lấy **prescription** điền nội dung ticket.

**Trong scope:** cây cầu HTTP BE→AI, job gọi `/predict`, sinh Alert, lưu/expose prediction, prescription.
**Ngoài scope:** realtime telemetry (Sprint BE-IoT).

## II.1 Hiện trạng — CHƯA kết nối (verified)

- Backend **không có** HttpClient/`IAiPredictionClient`/config `Ai__BaseUrl`/background service/controller gọi AI (`ManageDependencyInjection.cs:100` chỉ có `OpenMeteoClient`).
- `SohPercent` hiện lấy thẳng từ BMS qua ingest, **không** qua ML. Anomaly hiện là **threshold rule** (`AnomalyDetectionService`).
- AI module chạy độc lập, **chưa vào docker-compose nào**.

## II.2 Contract AI THẬT (đã verify từ code — dùng đúng cái này)

**`POST /predict`** — `src/schemas/predict.py`:
```
Request:  { battery_id: str, readings: [[v, i, t, (i_load, v_load, time)] × 30] }
          (3-feature legacy [v,i,t] CHỈ chạy nếu scaler fit 3-feature — xem Gap 1)
Response: nested { prediction{soh_percent, rul_cycles_estimate, degradation_rate_per_cycle,
                   soh_trend, cycles_to_maintenance, soh_trajectory[], health_stage},
                   anomaly{anomaly_score, anomaly_status, anomaly_confidence},
                   risk{risk_level, priority("P1"|"P2"|"P3"|"None"), action_code, reasons[]},
                   evidence{warnings[], feature_summary},
                   metadata{model_version, window_size:30, input_features:6, inference_ms} }
          + flat backward-compat: { soh_percent, classification, confidence, rul_cycles_estimate,
                   anomaly_score, recommended_action, warnings[], ... }
```

**`POST /prescribe/`** (CHÚ Ý dấu `/` cuối) — `src/schemas/prescribe.py`, **kế thừa PredictRequest**:
```
Request:  { battery_id, readings:[[...]×30],          ← GIỐNG /predict, KHÔNG phải "prediction object"
            age_cycles?, last_maintenance_date?, ticket_history:[], enrich:false }
            enrich=false → rule-based, <100ms, KHÔNG gọi LLM/mạng
            enrich=true  → RAG (ChromaDB) + LLM (Claude); LLM lỗi → tự fallback rule-based
Response: { soh_percent, risk_level, priority, action_code,
            prescription: str, action_steps: [str], escalation_conditions: [str],
            ppe_required: [str], sop_references: [str],
            enriched: bool, maintenance_docs[], safety_docs[],
            human_verification_required (P1 luôn true), safety_warnings[],
            inference_ms, rag_ms, llm_ms }
```
> **Insight:** `/prescribe/` là **superset của `/predict`** — nó tự chạy prediction từ `readings` rồi trả cả context (soh/risk/priority) lẫn prescription. Backend gọi `/predict` cho job nền và `/prescribe/` (enrich=true) khi tạo ticket.

## II.3 Bốn gap phải xử lý

**Gap 1 — Feature 3 vs 6 (⚠️ chốt TRƯỚC khi code):** `config.py:17 INPUT_FEATURES=6`, `_align_features` **raise (422)** nếu nhận ít feature hơn scaler (chỉ truncate khi dư, KHÔNG pad). BMS không có `current_load`/`voltage_load` (`SensorReading` chỉ V/I/T). → **chốt (A) AI retrain trên 3-feature [v,i,t] (khuyến nghị)** hoặc **(B) backend bù cột** `current_load:=current, voltage_load:=voltage, time:=giây`. Load channel ít quan trọng (54-dim spectral chỉ dùng 3 channel đầu).

**Gap 2 — Cửa sổ 30 timestep:** model train trên 30 mẫu/discharge-cycle; live là 5s/mẫu trộn sạc-xả. MVP lấy 30 reading mới nhất (xấp xỉ); sau lọc `ChargingState=Discharging`.

**Gap 3 — Domain mismatch:** model học pin NASA 18650 2.0Ah ≠ pin thật LiFePO4/NMC → SOH có thể lệch. **KHÔNG để AI-SOH ghi đè SOH-BMS** — lưu trường riêng `PredictedSohPercent`.

**Gap 4 — Cadence:** đừng gọi `/predict` mỗi reading (5s). Gọi mỗi N phút (mặc định 5) cho pin có ≥30 reading mới.

## II.4 Tasks theo phase

> ### ⚠️ CẬP NHẬT NS-27 (#667) — chốt 2026-07-16 theo Sprint Bonus F2 (Q12=A)
>
> Kế hoạch **BE-AI-P2/P3 + G5 dưới đây ĐÃ LỖI THỜI** ở chỗ lưu kết quả AI. Quyết định chốt (spec §30.3):
>
> 1. **Kết quả AI đi vào bảng RIÊNG, KHÔNG đi thẳng vào `Alerts`.** NS-26 (#666) đã tạo sẵn 2 entity
>    `AnomalyClassification` (Classification Normal/Degrading/Failed + AnomalyScore + Confidence +
>    ModelVersion + LatencyMs + **StaffFeedback** loop) và `SohPrediction` (PredictedSohPercent +
>    InputWindow + PredictedAt + RawResponse) + migration + DbSet + repo.
> 2. **KHÔNG thêm `AnomalyType.PredictedSohDegradation = 16`** — giá trị 16 đã cấp cho
>    **`AnomalyTypeEnum.Undertemp`** (NS-25 #665). `SohPredictionBackgroundService` khi chạy phải
>    **insert `AnomalyClassification` + `SohPrediction`** (dùng repo `_uow.AnomalyClassifications` /
>    `_uow.SohPredictions` đã có), KHÔNG tạo Alert với AnomalyType mới. Nếu vẫn cần raise Alert cho
>    Failed, tái dùng type hiện có (vd `SohDegradation=8`) — đừng mint type mới.
> 3. **Feedback loop ĐÃ có endpoint:** `POST /api/v1/anomaly-classifications/{id}/feedback`
>    (JWT Admin/Manager/Staff) ghi `StaffFeedback` (Correct/FalsePositive/FalseNegative) — phục vụ
>    đánh giá precision/recall + xuất retrain (§30.12).
> 4. **§30.3 phần đã làm ở NS-26:** persistence (2 bảng + migration + repo) + feedback endpoint.
>    **Phần còn lại của Sprint AI:** `IAiPredictionClient` + background service **insert vào 2 bảng
>    này** (thay vì vào Alerts) + endpoint đọc history/soh (§30.7) + `/api/v1/ai/feedback-stats`.
>
> → Đọc các dòng có ~~gạch ngang~~ bên dưới là bản CŨ; thực thi theo callout này.

| Task | Nội dung |
|---|---|
| **BE-AI-P0** — chốt + deploy | Chốt feature A/B (Gap 1) · chạy ai-module local (`create_dummy_artifacts.py` → `uvicorn main:app --port 8000` → `/health`) · thêm ai-module vào `docker-compose.yml` + env `Ai__BaseUrl=http://ai-module:8000` |
| **BE-AI-P1** — cây cầu HTTP | `IAiPredictionClient` (clone `OpenMeteoClient`: `AddHttpClient` + Polly + timeout 2s) · `AiPredictionResult` DTO (parse `risk.priority`, `classification`, `soh_percent`, `rul_cycles_estimate`, `anomaly_score`) · `AiOptions` (`Ai:Enabled/BaseUrl/TimeoutSeconds/MinReadings=30/IntervalMinutes=5`) |
| **BE-AI-P2** — job sinh classification ~~+ Alert~~ | `SohPredictionBackgroundService` (clone `WeatherSyncBackgroundService`): mỗi pin Active gom 30 reading mới nhất → `/predict` → **[NS-27] insert `AnomalyClassification` + `SohPrediction`** (score/confidence/latency/modelVersion) qua `_uow.AnomalyClassifications`/`_uow.SohPredictions`. Nếu `classification ∈ {Failed,Degrading}` cần raise Alert → tái dùng `SohDegradation=8` (⚠️ ~~`PredictedSohDegradation=16`~~ — 16 nay là `Undertemp`, NS-25) + dedup `FindActiveAlertToMergeAsync`. **try/catch, `Ai:Enabled=false`→no-op.** |
| **BE-AI-P3** — lưu & expose | ~~Entity `BatteryHealthSnapshot`~~ → **[NS-27] dùng `AnomalyClassification` + `SohPrediction` đã tạo ở NS-26** (không tạo entity mới) · trường latest trên `BatteryAsset` (tùy chọn) · endpoint `GET /api/battery-assets/{id}/anomaly-classifications` + `/soh-history` (§30.7) · feedback endpoint `POST /api/v1/anomaly-classifications/{id}/feedback` **ĐÃ có (NS-26)** |
| **BE-AI-P4** — prescription (CONTRACT ĐÃ SỬA) | Khi Alert P1/P2 tạo → gọi `POST /prescribe/` với **`{battery_id, readings[30], enrich:true}`** (async, không block) → nhận **`prescription, action_steps[], ppe_required[], sop_references[], safety_warnings[]`** → đổ vào ticket. Mở rộng `BatteryAnomalyDetectedV2Event` thêm field nullable (`Prescription`, `ActionSteps`) → `CreateTicketFromAlertConsumer` điền ticket. Feature-flag `Ai:PrescriptionEnabled`; thiếu `ANTHROPIC_API_KEY` → `/prescribe` tự fallback rule-based (`enriched=false`) vẫn có `action_steps`. |
| **BE-AI-P5** — hardening | Circuit breaker (Polly) + metric `ai_predict_latency_ms`/`errors_total` · demo `iot-simulator --scenario soh_degradation` → Alert P1 → Ticket SLA 4h → push · fallback: AI down → threshold rule vẫn chạy |

→ Đường ngắn nhất: **P0 → P1 → P2**.

## II.5 ★ Điểm hở / CHƯA kết nối BE↔AI + RECOMMEND (đã verify)

| # | Điểm hở | Mức | Recommend xử lý |
|---|---|---|---|
| **G1** | **ai-module KHÔNG có Dockerfile** → `docker compose build ../ai-module` fail | P0 cho deploy | Viết Dockerfile (python:3.11-slim + `pip install -r requirements.txt` + `uvicorn main:app --port 8000`). *(User đang để defer — ghi nhận để không quên.)* |
| **G2** | Contract `/prescribe` từng bị hiểu sai (nhận `prediction`/trả `ticket_description`) | P1 | **ĐÃ sửa** ở Task P4 (dùng `readings`+`enrich`, trả `action_steps`). Khi code bám đúng II.2. |
| **G3** | `SensorReading` thiếu `current_load`/`voltage_load`; scaler 6-feature → 3-feature **422** | P0 | Chốt Gap 1: **(A) AI retrain 3-feature** *(khuyến nghị)* hoặc **(B) backend bù cột**. Verify `scaler.n_features_in_` trước. |
| **G4** | AI chỉ mount `/health` `/predict` `/prescribe/`. `/predict-long`, `/predict-rul`, `/predict-forecast` **chưa có router** + thiếu weights (`soh_mamba_long_v2.0.pth`, `scaler_long.pkl`, forecast) | P2 (không chặn) | **`/predict` đã trả `rul_cycles_estimate`** → đủ cho RUL cơ bản. Chỉ làm long/rul/forecast nếu cần độ chính xác cao: AI commit weights + viết router. |
| **G5** ~~(cũ)~~ | `AnomalyType.SohDegradation=8` đã tồn tại (threshold) | nhỏ | **[NS-27 override]** ~~Thêm `PredictedSohDegradation=16`~~ — **KHÔNG** (16 nay là `Undertemp`, NS-25). Kết quả AI đi vào **`AnomalyClassification.Classification`** (bảng riêng, NS-26), KHÔNG vào `Alerts.AnomalyType`. Phân biệt nguồn AI vs threshold bằng bảng riêng, không bằng anomaly type mới. |
| **G6** | Version `SCALER=1.1` vs `MODEL=1.2` | không phải bug | **Cố ý** — `load_models()` assert scaler=1.1. Đừng "sửa". |
| **G7** | ai-module chưa vào `docker-compose.yml`, backend chưa có env `Ai__*` | P0 | Làm ở Task P0 (đã nằm trong plan). |

> **Không liên quan BE-AI nhưng ghi để khỏi nhầm:** `/prescribe` RAG **ĐÃ sẵn sàng** (knowledge/ + chroma.sqlite3 + anthropic/chromadb/sentence-transformers trong requirements) — tài liệu tổng quan ghi "Sprint 3 chưa làm" là cũ.

## II.6 Bẫy BE-AI

1. **Soft dependency** — gọi AI ngoài đường ingest, try/catch + timeout + feature-flag. Ingest/threshold không bao giờ chờ AI.
2. **AI + threshold cùng tồn tại, bổ trợ** — AI chết thì threshold rule vẫn bảo vệ. Tách 2 background service riêng.
3. **Không ghi đè SOH-BMS** bằng SOH-AI (Gap 3).
4. **Latency thực > 100ms** khi qua HTTP nội bộ — đặt timeout ~2s, đừng kỳ vọng 100ms end-to-end.
5. **`/prescribe/` có dấu `/` cuối** — sai path → 404/307.

---
---

## ★ Nguyên tắc chung (cả 2 sprint)

1. **Soft dependency** — mọi publish SSE (BE-IoT) và mọi gọi AI (BE-AI) đều: feature-flag + timeout + try/catch + **sau commit, ngoài đường ingest**. Ingest **không bao giờ** chờ SSE hay AI; lỗi → log + bỏ qua, data vẫn lưu đúng.
2. **Stream/phân tích cái đã sạch** — cả 2 sprint đều dùng reading sau bước calibration + loại outlier (đã làm trong handler trước khi persist).

## ★ Checklist file

### SPRINT BE-IoT (SSE)
| Task | File | Action |
|---|---|---|
| R1 | `Application/Interfaces/ITelemetryPublisher.cs` + `DTOs/LiveReadingDto.cs` | create |
| R1 | `Application/Interfaces/IBatteryRealtimeAuthorizationService.cs` + impl | create |
| R1 | `Infrastructure/Realtime/RedisTelemetryPublisher.cs` + `RedisTelemetrySubscriber.cs` | create |
| R1 | `Api/Controllers/...` SSE endpoint `GET /api/sensor-readings/stream` (`IAsyncEnumerable<SseEvent>`) | create |
| R1 | `…Handler/SensorReading/BatchIngestSensorReadingsCommandHandler.cs` | inject publisher + publish sau `SaveChangesAsync` |
| R1 | `Infrastructure/DependencyInjection/ManageDependencyInjection.cs` | đăng ký Redis pub/sub + DI |
| R1 | `BatteryService.Api/Program.cs` | map stream + token query param + CORS |
| R1 | `RealtimeOptions.cs` (`Realtime:Enabled`) + appsettings | create |
| R1 | FE Web `hooks/useBatteryTelemetry.ts` (fetch-SSE) · FE Mobile `useBatteryTelemetry.ts` (RN, `expo-secure-store`) | create |
| R2 | `Infrastructure/BackgroundServices/TelemetrySummaryBroadcastBackgroundService.cs` | create |
| R2 | FE Web `useSiteTelemetry.ts` · FE Mobile `useCustomerTelemetry.ts` | create |
| R3 | ApiGateway SSE passthrough `/api/sensor-readings/stream` (disable buffering) + metric | modify |
| R4 | publish `AlertRaised` ở `AnomalyDetectionService` qua SSE | modify |

### SPRINT BE-AI
| Task | File | Action |
|---|---|---|
| P0 | `docker-compose.yml` thêm ai-module + `Ai__BaseUrl` · (ai-module) **Dockerfile** [G1] | create |
| P1 | `Application/Interfaces/IAiPredictionClient.cs` + `DTOs/AiPredictionResult.cs` | create |
| P1 | `Infrastructure/Implements/Services/AiPredictionClient.cs` | create (copy `OpenMeteoClient`) |
| P1 | `Application/Options/AiOptions.cs` + appsettings · DI `AddHttpClient` | create/modify |
| P2 | `Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs` — insert `AnomalyClassification`+`SohPrediction` [NS-27] | create (copy `WeatherSync`) |
| ~~P2~~ | ~~`AnomalyTypeEnum.cs` thêm `PredictedSohDegradation=16`~~ — **BỎ** (16=`Undertemp` NS-25; kết quả AI vào bảng riêng NS-26) | ~~modify~~ |
| ~~P3~~ | ~~`BatteryHealthSnapshot.cs`~~ — **BỎ**, dùng `AnomalyClassification`+`SohPrediction` (NS-26 đã tạo entity+migration+repo+feedback endpoint) | dùng lại NS-26 |
| P4 | `Infrastructure/.../AiPrescriptionClient.cs` (`POST /prescribe/`, `readings`+`enrich`) | create |
| P4 | `SharedContracts/Events/BatteryAnomalyDetectedV2Event.cs` thêm `Prescription`/`ActionSteps` nullable · `CreateTicketFromAlertConsumer` điền ticket | modify |
| (Gap 1) | **Chốt A/B** — AI retrain 3-feature *hoặc* `SensorReading` + handler bù cột [G3] | quyết định |

---

**Tóm tắt:**
- **BE-IoT:** ingest + làm sạch **đã xong** → xây realtime **SSE** (R1 1 pin → R2 nhiều pin của khách → R3 hardening). Stream là data **đã loại outlier**, lên Web (admin) + Mobile (khách). Triển khai = Sprint BE-IoT-Realtime §17, design = overall.md §34.10.
- **BE-AI:** **chưa nối** → P0 deploy → P1 cầu HTTP → P2 sinh Alert → P3 lưu/expose → P4 prescription. Điểm hở cần chốt sớm: **G3 (feature A/B → 422)** và **G1 (Dockerfile)**.
