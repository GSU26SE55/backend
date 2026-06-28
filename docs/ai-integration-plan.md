# Kế hoạch tích hợp luồng IoT → Backend → AI Module

> **Document type:** Hướng dẫn tích hợp (integration guide) — nối `ai-module` (FastAPI + PyTorch) vào luồng IoT/BatteryService hiện có.
> **Scope:** Từ sensor reading (đã có trong TimescaleDB) → gọi AI `/predict` → đổ kết quả vào pipeline Alert → Ticket → Notification có sẵn → (optional) `/prescribe` điền nội dung ticket.
> **Liên quan:** `backend/iot.md`, `backend/iot/newiot.md`, `ai-module/docs/overall.md`, `.claude/rules/tech/be.md`, `.claude/rules/tech/ai.md`.
> **Trạng thái hiện tại:** AI module **CHƯA** được wire vào backend. Anomaly hiện tại là **rule-based threshold**, không phải ML. `SohPercent` đang lấy thẳng từ BMS, không qua AI.
> **Cập nhật:** 2026-06-26

---

## 0. TL;DR — đường đi ngắn nhất

```
Phase 0  Dựng ai-module + chốt contract /predict        (0.5 ngày)
Phase 1  Cây cầu HTTP IAiPredictionClient (copy OpenMeteoClient)   (1 ngày)
Phase 2  SohPredictionBackgroundService → sinh Alert     (1–2 ngày)   ← hết bước này là demo được end-to-end
Phase 3  Lưu + expose prediction cho FE/Mobile           (1 ngày)
Phase 4  /prescribe điền nội dung ticket (optional)       (1–2 ngày)
Phase 5  Hardening + load test + demo                    (1 ngày)
```

**Nguyên tắc xuyên suốt:** AI là **phụ thuộc mềm** — mọi lời gọi AI phải có feature-flag + timeout + try/catch, và **không bao giờ** nằm trong đường ingest hay làm chết threshold rule.

---

## 1. Ba "ngôn ngữ" của 3 phía (chốt contract trước)

| Phía | Nói gì | Trạng thái |
|------|--------|-----------|
| **IoT (ESP32 / iot-simulator)** | đẩy reading `{voltage, current, temperature, socPercent, sohPercent, ...}` mỗi ~5s (MQTT + HTTPS) | ✅ đã có |
| **Backend (BatteryService)** | lưu `SensorReading` (TimescaleDB) → threshold rule → `Alert` → Outbox → Saga → Ticket/Notification | ✅ đã có |
| **AI (FastAPI :8000)** | `POST /predict`: **30 timestep × [v,i,t,(i_load,v_load,time)]** → `{soh_percent, classification, rul_cycles_estimate, anomaly_score, risk.priority (P1/P2/P3), recommended_action, warnings}`. Latency <100ms. | ✅ có code, CHƯA nối |

**Việc cần làm:** bắc cầu HTTP từ BatteryService sang `:8000`, rồi đổ kết quả AI vào **đúng pipeline Alert có sẵn** để nó tự chảy tiếp ra Ticket + Notification.

### Endpoint AI dùng tới
- `GET  /health` — kiểm tra model loaded.
- `POST /predict` — SOH + classification + RUL + anomaly + risk. Đồng bộ, <100ms.
- `POST /prescribe` — (Sprint 3 AI) sinh kế hoạch bảo trì + text điền ticket. Async ~2–2.5s, cần `ANTHROPIC_API_KEY` + ChromaDB. Thiếu key → 503 (không ảnh hưởng `/predict`).

---

## 2. Quyết định kiến trúc: AI cắm vào đâu?

**KHÔNG gọi AI trong handler ingest** (`BatchIngestSensorReadingsCommandHandler`). Ingest phải nhanh (5s/device, batch); cộng 87ms+ network sẽ nghẽn.

> **Tạo một background service mới `SohPredictionBackgroundService`**, song song với `ThresholdCheckBackgroundService`, chạy theo chu kỳ riêng (vài phút/lần): gom 30 reading gần nhất của mỗi pin → gọi `/predict` → lưu kết quả + (nếu xấu) tạo `Alert`.

```
SensorReading (TimescaleDB)
        │
        ├──▶ ThresholdCheckBackgroundService (~60s) ── rule cứng ──┐
        │                                                          │
        └──▶ SohPredictionBackgroundService (MỚI, ~5 phút)         │
                 │ gom 30 reading/pin                              │
                 ▼                                                 ▼
            POST /predict (AI :8000) ───▶ lưu BatteryHealthSnapshot
                 │                                                 │
            risk.priority ∈ {P1,P2}  hoặc  classification ∈ {Failed,Degrading} ?
                 │ YES                                             │
                 ▼                                                 ▼
            tạo Alert(AnomalyType.PredictedSohDegradation) ──▶ OUTBOX (dùng chung)
                                                                  │
                                         ┌─────────────────────────┤
                                         ▼                         ▼
                                  AlertTicket Saga           NotificationService
                                  → Ticket + SLA             → push Customer
```

**Điểm mấu chốt:** từ chỗ tạo `Alert` trở đi **không phải viết gì mới** — Outbox → Saga → `CreateTicketFromAlertConsumer` → Notification đã chạy. AI chỉ là **một nguồn sinh Alert mới**, đứng ngang hàng với threshold rule.

---

## 3. Bốn khoảng cách (gap) phải xử lý — ĐỌC KỸ

### Gap 1 — Feature: backend không có `current_load` / `voltage_load`
`SensorReading` chỉ có `Voltage, Current, Temperature` (không có 2 kênh load). Hai cột `*_load` của NASA là **artifact bàn thí nghiệm** (đo tại đầu máy electronic load), **BMS field không trả** — không register Modbus/CAN nào có. **Không nên** mua sensor để đo (sai ngữ nghĩa + tốn + multi-drop mơ hồ).

### Gap 2 — ⚠️ Artifact production hiện fit trên 6-feature → gửi 3-feature sẽ **422**, KHÔNG tự align
Code thật `ai-module/src/services/inference.py::_align_features`:
```python
actual == expected → giữ nguyên
actual >  expected → cắt bớt (truncate)
actual <  expected → raise ValueError   # KHÔNG pad lên!
```
`scaler.pkl` production fit 6-feature → `expected = 6`. Tài liệu AI ghi "legacy 3-feature tự động align" là **chưa chính xác** — chỉ đúng nếu artifact được train trên 3-feature.

→ **Phải chọn 1 trong 2** (xem §3.1).

### Gap 3 — `*_load` gần như vô giá trị với model này
Bộ **54-dim spectral+kurtosis** (nuôi **cả FiLM của Mamba lẫn IsolationForest anomaly**) chỉ dùng **3 channel đầu**:
```python
raw_feat = extract_window_features(x_scaled[:, :3])   # voltage, current, temperature
```
→ `current_load`/`voltage_load` chỉ đi vào input projection thô `Linear(6→64)`, **không** ảnh hưởng anomaly, ảnh hưởng SOH rất gián tiếp. Model vẫn đạt MAE <2% nhờ v/i/t.

### Gap 4 — Cửa sổ 30 timestep + nhịp gọi
- Model NASA train trên **30 mẫu trong 1 discharge cycle** (~13s/mẫu). Live IoT là chuỗi 5s/mẫu trộn charge/discharge/rest.
- MVP: lấy **30 reading mới nhất** của pin (`OrderByDescending(Time).Take(30)` → đảo lại thứ tự thời gian). Chấp nhận xấp xỉ.
- Tinh hơn (sprint sau): lọc `ChargingState = Discharging` cho giống phân phối train.
- **Đừng gọi `/predict` mỗi reading (5s)** — model cycle-based, vô nghĩa + tốn. Gọi mỗi **N phút** (config, mặc định 5) cho pin có ≥30 reading mới.

### 3.1. Khuyến nghị xử lý feature (chọn 1)

| Phương án | Việc làm | Đánh giá |
|-----------|----------|----------|
| **A. AI retrain trên 3-feature (NÊN)** | AI team rerun `preprocess.py` + `train.py` với cấu hình **3-feature [v,i,t]** → `scaler.pkl` mới `expected=3`. Backend gửi đúng cái BMS có. | ✅ Sạch nhất, không phần cứng mới, contract khớp thực tế. Effort nhỏ. |
| **B. Backend bù 6 cột (nhanh)** | Giữ artifact 6-feature; backend điền `current_load:=current`, `voltage_load:=voltage`, `time:=giây tương đối`. Đủ shape, không 422. | ⚠️ Chạy ngay được, vô hại (2 kênh ít quan trọng) nhưng hơi "giả". |
| ~~C. Mua sensor đo load~~ | Thêm INA226 phía tải | ❌ Tốn, sai ngữ nghĩa — không làm. |

> **Quyết định cần chốt với AI team trước Phase 1.** Mặc định khuyến nghị **A**. Nếu demo gấp dùng **B** rồi chuyển A sau.

### Gap 5 — Domain mismatch (rủi ro khoa học, ghi nhận)
Model học từ pin 18650 NASA 2.0Ah; pin thật (LiFePO4/NMC) khác hóa học/thang. SOH dự đoán có thể lệch. Demo capstone chấp nhận được, nhưng:
- **KHÔNG để AI-SOH ghi đè SOH thật từ BMS.** Lưu vào trường riêng `PredictedSohPercent`, giữ `SohPercent` (BMS) nguyên.
- Ghi rõ caveat trong báo cáo.

---

## 4. Kế hoạch theo Phase

### Phase 0 — Dựng AI module + chốt contract (0.5 ngày)
- Chạy ai-module: `python scripts/create_dummy_artifacts.py` → `uvicorn main:app --port 8000` → `GET /health` xanh.
- `curl POST /predict` với 30 row → xác nhận response shape (`risk.priority`, `classification`, `rul_cycles_estimate`, `anomaly_score`, `warnings`).
- **Chốt phương án feature A/B (§3.1).**
- Thêm `ai-module` vào `backend/docker-compose.yml`:
  ```yaml
  ai-module:
    build: ../ai-module          # hoặc image đã build
    ports: ["8000:8000"]
    environment:
      - ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY}   # chỉ cần cho /prescribe
    networks: [default]
  ```
  BatteryService env: `Ai__BaseUrl=http://ai-module:8000`.

### Phase 1 — Cây cầu HTTP `IAiPredictionClient` (1 ngày)
Clone pattern **OpenMeteo** (đã có sẵn, có Polly retry + timeout cấu hình ở DI). Template thật:
- `BatteryService.Infrastructure/Implements/Services/OpenMeteoClient.cs`
- DI tại `ManageDependencyInjection.cs` (quanh dòng 100, `AddHttpClient<IOpenMeteoClient, OpenMeteoClient>`).

**File cần tạo:**
- `BatteryService.Application/Interfaces/IAiPredictionClient.cs`
  ```csharp
  Task<AiPredictionResult?> PredictAsync(
      string batteryId, IReadOnlyList<decimal[]> readings, CancellationToken ct);
  ```
- `BatteryService.Application/DTOs/AiPredictionResult.cs` — `SohPercent, Classification, RulCyclesEstimate, AnomalyScore, Confidence, Priority (P1/P2/P3), RecommendedAction, Warnings`.
- `BatteryService.Infrastructure/Implements/Services/AiPredictionClient.cs` — copy `OpenMeteoClient`, đổi URL `predict`, serialize `{battery_id, readings}`, parse response (cả nested `risk.priority` lẫn flat fields).
- `BatteryService.Application/Options/AiOptions.cs` — `BaseUrl, Enabled, TimeoutSeconds=2, MinReadings=30, IntervalMinutes=5, PrescriptionEnabled=false`.
- DI (cạnh OpenMeteo):
  ```csharp
  services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
  services.AddHttpClient<IAiPredictionClient, AiPredictionClient>((sp, http) =>
  {
      var opt = sp.GetRequiredService<IOptions<AiOptions>>().Value;
      http.BaseAddress = new Uri(opt.BaseUrl);
      http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);
  }); // + Polly retry như OpenMeteo
  ```
- `appsettings`: section `Ai` + env `Ai__BaseUrl`, `Ai__Enabled`.
- **Test:** integration test gọi `/predict` thật (dummy artifacts) → assert parse đúng.

### Phase 2 — Background service sinh Alert từ AI (1–2 ngày)
Copy khung **`WeatherSyncBackgroundService`** (timer + `IServiceScopeFactory.CreateScope`).
- `BatteryService.Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs`.
- Logic mỗi tick:
  1. Lấy pin Active.
  2. Mỗi pin: `SensorReadings.GetAllAsync().Where(x => !x.IsDeleted && x.BatteryAssetId == id).OrderByDescending(x => x.Time).Take(30)` → đảo chiều → `readings[30][3]` (hoặc [6] nếu chọn phương án B).
  3. `await _ai.PredictAsync(...)`.
  4. Lưu `BatteryHealthSnapshot` (Phase 3).
  5. **Nếu `Priority ∈ {P1,P2}` hoặc `Classification ∈ {Failed,Degrading}`** → tạo `Alert`:
     - `AnomalyType = PredictedSohDegradation` (enum mới, đánh số tiếp theo — xem `AnomalyTypeEnum`).
     - `Severity`: P1→Critical, P2→Warning.
     - `ActualValue = SohPercent`, `Unit = "%"`.
     - **Dùng lại dedup** `FindActiveAlertToMergeAsync()` để không spam.
     - Critical → ghi Outbox event (V1+V2) như `AnomalyDetectionService` đang làm.
- **Resilience bắt buộc:** bọc `try/catch` mỗi pin; AI lỗi/timeout → log + bỏ qua pin đó, **không** làm chết job, **không** đụng threshold flow. `Ai:Enabled=false` → service no-op.
- DI: `services.AddHostedService<SohPredictionBackgroundService>();`

> **Hết Phase 2 là có luồng end-to-end:** IoT → reading → AI dự đoán → Alert → Ticket tự tạo (SLA) → push Customer.

### Phase 3 — Lưu & expose prediction cho FE/Mobile (1 ngày)
- Entity `BatteryHealthSnapshot : AuditableEntity` (hoặc hypertable): `BatteryAssetId, Time, PredictedSohPercent, Classification, RulCyclesEstimate, AnomalyScore, Confidence`.
- Migration `AddBatteryHealthSnapshot` (theo §13 be.md — kiểm rollback).
- Trường latest trên `BatteryAsset`: `PredictedSohPercent, HealthClassification, RulCyclesEstimate, LastPredictedAt` (cho dashboard nhanh).
- Query endpoint:
  - `GET /api/batteries/{id}/health` (latest)
  - `GET /api/batteries/{id}/health/history` (trend SOH trajectory + RUL cho chart).

### Phase 4 — Prescription điền nội dung ticket (1–2 ngày, optional)
Đúng như AI doc: *"BE gọi /predict trước để tạo alert, sau đó gọi /prescribe để điền nội dung ticket."*
- Khi Phase 2 tạo Alert P1/P2 → gọi `POST /prescribe` (async, ~2.5s, **không block**) → nhận `prescription.steps[], ticket_description, safety_warnings[]`.
- **Mở rộng event** mang prescription sang TicketService: thêm field nullable vào `SharedContracts/Events/BatteryAnomalyDetectedV2Event.cs` (record — chỉ thêm property), vd `string? PrescriptionText`, `string? SuggestedSteps`.
- `CreateTicketFromAlertConsumer` (TicketService) đổ `ticket_description`/steps vào ticket khi tạo.
- **Feature-flag riêng** `Ai:PrescriptionEnabled` + cần `ANTHROPIC_API_KEY` + đã chạy `build_knowledge_base.py`. Thiếu → `/prescribe` 503 → BE bỏ qua, ticket vẫn tạo bằng text mặc định.

### Phase 5 — Hardening & demo (1 ngày)
- Circuit breaker (Polly) + metric `ai_predict_latency_ms`, `ai_predict_errors_total` (dùng `IIotMetricsRecorder` pattern).
- Kịch bản demo: `iot-simulator --scenario soh_degradation` → AI trả Failed/P1 → Alert → Ticket P1 (SLA 4h) → Customer nhận push → (Phase 4) ticket có sẵn các bước SOP.
- Khẳng định `/predict` <100ms; fallback: AI down thì threshold rule vẫn chạy bình thường.

---

## 5. Hai nguyên tắc giữ hệ thống không gãy

1. **AI và threshold rule cùng tồn tại, bổ trợ — không thay thế.**
   - Threshold = an toàn tức thì (overheat/overvoltage), nhanh, không phụ thuộc AI.
   - AI = dự báo suy giảm SOH/RUL, chậm hơn, "thông minh" hơn.
   - Cả hai đổ chung vào pipeline Alert. AI chết → threshold vẫn bảo vệ → lý do tách 2 background service riêng.

2. **AI là soft dependency.** Mọi lời gọi AI: feature-flag + timeout + try/catch + ngoài đường ingest. Ingest và threshold **không bao giờ** chờ AI.

---

## 6. Checklist file cần đụng (toàn bộ trong BatteryService + SharedContracts)

| Phase | File | Action |
|-------|------|--------|
| 0 | `backend/docker-compose.yml` | thêm service `ai-module` |
| 1 | `Application/Interfaces/IAiPredictionClient.cs` + `DTOs/AiPredictionResult.cs` | create |
| 1 | `Infrastructure/Implements/Services/AiPredictionClient.cs` | create (copy `OpenMeteoClient`) |
| 1 | `Application/Options/AiOptions.cs` + `appsettings*.json` | create |
| 1 | `Infrastructure/DependencyInjection/ManageDependencyInjection.cs` | thêm `AddHttpClient` |
| 2 | `Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs` | create (copy `WeatherSyncBackgroundService`) |
| 2 | `Domain/Enums/AnomalyTypeEnum.cs` | thêm `PredictedSohDegradation` |
| 2 | `Infrastructure/DependencyInjection/ManageDependencyInjection.cs` | `AddHostedService` |
| 3 | `Domain/Entities/BatteryHealthSnapshot.cs` + Migration + query endpoint | create |
| 3 | `Domain/Entities/BatteryAsset.cs` | thêm trường latest prediction |
| 4 | `shared/src/SharedContracts/Events/BatteryAnomalyDetectedV2Event.cs` | thêm field prescription |
| 4 | `Infrastructure/Implements/Services/AiPrescriptionClient.cs` | create |
| 4 | `TicketService/.../Consumers/CreateTicketFromAlertConsumer.cs` | đổ prescription vào ticket |

---

## 7. Caveat / rủi ro cần nói trong báo cáo

- **Domain mismatch:** model train NASA 18650 ≠ pin LiFePO4/NMC thật → SOH có thể lệch. Giữ SOH-BMS riêng, không để AI ghi đè.
- **Window semantics:** 30 reading live ≠ 30 mẫu/discharge-cycle NASA → là xấp xỉ. Cải thiện bằng lọc `ChargingState`.
- **Feature contract:** phải chốt A (retrain 3-feature) hoặc B (backend bù cột) — nếu không sẽ 422.
- **`/prescribe` phụ thuộc Anthropic API + ChromaDB:** phải feature-flag, không để chặn luồng ticket cơ bản.
- **Latency:** mạng giữa BatteryService ↔ ai-module cộng vào 87ms inference; đặt timeout ~2s, đừng kỳ vọng <100ms khi qua HTTP nội bộ.

---

## 8. Lệnh chạy end-to-end (sau khi xong Phase 0–2)

```bash
# 1. Hạ tầng: TimescaleDB + Redis + RabbitMQ + Mosquitto
cd iot/infra && docker compose -f docker-compose.dev.yml up -d

# 2. AI module
cd ../../ai-module && python scripts/create_dummy_artifacts.py && uvicorn main:app --port 8000

# 3. Backend (Ai__Enabled=true, Ai__BaseUrl=http://localhost:8000)
cd ../backend && dotnet run

# 4. Giả lập thiết bị gửi data degrade dần
cd ../iot-simulator && make run   # hoặc: python -m src.main --scenario soh_degradation
```

Kỳ vọng: simulator gửi reading → `SohPredictionBackgroundService` gọi `/predict` → Alert `PredictedSohDegradation` → Ticket P1/P2 tự tạo + Customer nhận push.
