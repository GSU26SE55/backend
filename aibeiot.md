# Hướng ráp luồng IoT → Backend → AI Module

> Đọc xong cả 3 phía. Backend đã có sẵn 2 "khuôn" gần như đúc sẵn cho việc này: **`OpenMeteoClient`** (HttpClient gọi service ngoài, có Polly retry + timeout) và **`WeatherSyncBackgroundService`** (job nền quét theo chu kỳ). Ráp AI module về bản chất là **clone 2 khuôn đó và trỏ sang FastAPI**. Dưới đây là hướng làm đầy đủ.

---

## 0. Ba "ngôn ngữ" của 3 phía (chốt contract trước khi code)

| Phía | Nói gì | Đã có |
|------|--------|:----:|
| **IoT (ESP32)** | đẩy reading `{voltage, current, temperature, soc, ...}` mỗi 5s | ✅ |
| **Backend (BatteryService)** | lưu `SensorReading` (TimescaleDB), chạy threshold rule → Alert → Outbox → Ticket/Notification | ✅ |
| **AI (FastAPI :8000)** | `POST /predict` nhận 30 timestep × `[v,i,t,(i_load,v_load,time)]` → trả SOH%, classification, RUL, anomaly, `risk.priority` P1/P2/P3 | ✅ |

→ Việc cần làm: **bắc một cây cầu HTTP** từ BatteryService sang `:8000`, và **đổ kết quả AI vào đúng pipeline Alert có sẵn** để nó tự chảy tiếp ra Ticket + Notification.

---

## 1. Quyết định kiến trúc cốt lõi: AI cắm vào đâu?

**KHÔNG gọi AI trong handler ingest.** Ingest phải nhanh (5s/device, batch) — gọi AI 87ms+ network sẽ làm nghẽn. Thay vào đó:

> **Tạo một background service mới `SohPredictionBackgroundService`** (song song với `ThresholdCheckBackgroundService`), chạy theo chu kỳ riêng (vài phút/lần), gom 30 reading gần nhất của mỗi pin → gọi `/predict` → lưu kết quả + (nếu xấu) tạo Alert.

Sơ đồ tích hợp:

```
SensorReading (TimescaleDB)
        │
        ├──▶ ThresholdCheckBackgroundService (~60s)  ── rule cứng ──┐
        │                                                          │
        └──▶ SohPredictionBackgroundService (MỚI, ~5 phút)         │
                  │ gom 30 reading/pin                             │
                  ▼                                                ▼
            POST /predict (AI :8000) ───▶ lưu BatteryHealthSnapshot
                  │                                                │
            risk.priority = P1/P2 ?                                │
                  │ YES                                            │
                  ▼                                                ▼
            tạo Alert(AnomalyType.PredictedSohDegradation) ──▶ OUTBOX (chung)
                                                                   │
                                          ┌────────────────────────┤
                                          ▼                        ▼
                                   AlertTicket Saga          NotificationService
                                   → Ticket + SLA            → push Customer
```

**Điểm hay:** từ chỗ tạo Alert trở đi **không phải viết gì mới** — toàn bộ Outbox → Saga → Ticket → Notification đã chạy. AI chỉ là **một nguồn sinh Alert mới**, đứng ngang hàng với threshold rule.

---

## 2. Bốn khoảng cách (gap) phải xử lý — đọc kỹ phần này

Đây là chỗ dễ sai nhất khi ráp:

### Gap 1 — Feature: backend không có `current_load`/`voltage_load`

`SensorReading` chỉ có `Voltage, Current, Temperature` (không có 2 kênh load của NASA). Hai cột `*_load` là **artifact bàn thí nghiệm**, BMS field không trả → không nên mua sensor để đo.

> ⚠️ **Lưu ý quan trọng (đã verify trong code AI):** artifact production hiện `scaler.pkl` fit trên **6-feature** → hàm `_align_features` (`ai-module/src/services/inference.py`) sẽ **raise ValueError (422)** nếu nhận 3-feature, chỉ truncate khi nhận **nhiều hơn** chứ KHÔNG pad lên. Vậy "legacy 3-feature tự động align" chỉ đúng khi artifact được train trên 3-feature. **Phải chốt 1 trong 2 trước Phase 1:**
>
> - **(A) AI retrain trên 3-feature [v,i,t] (khuyến nghị)** → `scaler.pkl` mới `expected=3`, backend gửi đúng cái BMS có. Sạch, không phần cứng mới.
> - **(B) Backend bù 6 cột** → điền `current_load:=current`, `voltage_load:=voltage`, `time:=giây tương đối`. Đủ shape, không 422, hơi "giả" nhưng vô hại (2 kênh đó chỉ vào input projection thô, không ảnh hưởng anomaly; bộ 54-dim spectral chỉ dùng 3 channel đầu).

### Gap 2 — Cửa sổ 30 timestep nghĩa là gì?

Model NASA train trên **30 mẫu trong 1 discharge cycle** (~13s/mẫu). Live IoT là chuỗi liên tục 5s/mẫu, trộn cả charge/discharge/rest.
→ MVP: lấy **30 reading mới nhất** của pin (`OrderByDescending(Time).Take(30)` rồi đảo lại thứ tự thời gian). Chấp nhận đây là xấp xỉ. Tinh hơn (Sprint sau): lọc `ChargingState = Discharging` để giống phân phối train.

### Gap 3 — Domain mismatch (rủi ro khoa học cần biết)

Model học từ pin 18650 NASA 2.0Ah. Pin thật của bạn (LiFePO4/NMC) khác hóa học, khác thang. SOH dự đoán có thể lệch.
→ Trong demo capstone vẫn chấp nhận được, nhưng **phải ghi rõ caveat** và **đừng để AI-SOH ghi đè SOH thật từ BMS** — lưu vào trường riêng (`PredictedSohPercent`), giữ `SohPercent` (BMS) nguyên.

### Gap 4 — Nhịp gọi (cadence)

Đừng gọi `/predict` mỗi reading (5s) — model là cycle-based, vô nghĩa và tốn.
→ Gọi mỗi **N phút** (config, mặc định 5 phút) cho mỗi pin có ≥30 reading mới. Hoặc theo "mỗi discharge cycle hoàn tất".

---

## 3. Kế hoạch theo Phase (đây là "hướng làm")

### Phase 0 — Dựng AI module + chốt contract (0.5 ngày)

- Chạy ai-module: `create_dummy_artifacts.py` → `uvicorn main:app --port 8000` → `GET /health` xanh.
- `curl POST /predict` với 30 row → xác nhận response shape (đặc biệt `risk.priority`, `classification`, `rul_cycles_estimate`).
- **Chốt phương án feature A/B (Gap 1).**
- Thêm ai-module vào `docker-compose.yml` cùng mạng với BatteryService:

```yaml
ai-module:
  build: ../ai-module        # hoặc image
  ports: ["8000:8000"]
  environment:
    - ANTHROPIC_API_KEY=${ANTHROPIC_API_KEY}   # chỉ cần cho /prescribe
```

- BatteryService env: `Ai__BaseUrl=http://ai-module:8000`.

### Phase 1 — Cây cầu HTTP `IAiPredictionClient` (1 ngày)

Clone y hệt pattern OpenMeteo. **3 file + 1 đăng ký DI:**

- `BatteryService.Application/Interfaces/IAiPredictionClient.cs`:

```csharp
Task<AiPredictionResult?> PredictAsync(
    string batteryId, IReadOnlyList<decimal[]> readings, CancellationToken ct);
```

- `BatteryService.Infrastructure/Implements/Services/AiPredictionClient.cs` — copy `OpenMeteoClient.cs`, đổi URL `predict`, serialize `{battery_id, readings}`, parse `soh_percent, classification, risk.priority, rul_cycles_estimate, anomaly_score, warnings`.
- DI (`ManageDependencyInjection.cs`, ngay cạnh dòng 100 OpenMeteo):

```csharp
services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
services.AddHttpClient<IAiPredictionClient, AiPredictionClient>((sp, http) => {
    var opt = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    http.BaseAddress = new Uri(opt.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(opt.TimeoutSeconds);   // 2s
}); // + Polly retry như OpenMeteo
```

- Config: `Ai:Enabled` (flag), `Ai:BaseUrl`, `Ai:TimeoutSeconds`, `Ai:MinReadings=30`, `Ai:IntervalMinutes=5`.
- Test: integration test gọi `/predict` thật (dummy artifacts) → assert parse đúng.

### Phase 2 — Background service sinh Alert từ AI (1–2 ngày)

- `BatteryService.Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs` — copy khung `WeatherSyncBackgroundService` (timer + `CreateScope`).
- Logic mỗi tick (xem mục 1):
  1. Lấy danh sách pin Active.
  2. Mỗi pin: `SensorReadings.GetAllAsync().Where(!IsDeleted && assetId).OrderByDescending(Time).Take(30)` → đảo chiều → `[[v,i,t]×30]`.
  3. `await _ai.PredictAsync(...)`.
  4. Lưu `BatteryHealthSnapshot` (Phase 3).
  5. Nếu `risk.priority ∈ {P1,P2}` hoặc `classification ∈ {Failed,Degrading}` → tạo Alert:
     - `AnomalyType = PredictedSohDegradation` (enum mới, đánh số tiếp theo)
     - `Severity` map từ priority (P1→Critical, P2→Warning)
     - `ActualValue = soh_percent`, `Unit="%"`
     - Dùng lại dedup (`FindActiveAlertToMergeAsync`) để không spam.
     - Critical → ghi Outbox event (như `AnomalyDetectionService` đang làm).
- **Resilience bắt buộc:** bọc `try/catch`, AI lỗi/timeout → log + bỏ qua pin đó, **không** làm chết job, **không** đụng tới threshold flow. `Ai:Enabled=false` → service tự no-op.

→ Hết Phase 2 là đã có **luồng end-to-end**: IoT → reading → AI dự đoán → Alert → Ticket tự tạo (SLA) → push Customer.

### Phase 3 — Lưu & hiển thị prediction cho FE/Mobile (1 ngày)

- Entity mới `BatteryHealthSnapshot : AuditableEntity` (hoặc hypertable): `BatteryAssetId, Time, PredictedSohPercent, Classification, RulCyclesEstimate, AnomalyScore, Confidence`.
- Migration `AddBatteryHealthSnapshot` (theo §13 be.md).
- Thêm trường latest lên `BatteryAsset`: `PredictedSohPercent, HealthClassification, RulCyclesEstimate, LastPredictedAt` (cho dashboard nhanh).
- Query endpoint: `GET /api/batteries/{id}/health` (latest) + `.../health/history` (trend cho chart SOH trajectory + RUL).

### Phase 4 — Prescription điền nội dung ticket (1–2 ngày, optional)

Đúng như AI doc nói: *"BE gọi /predict trước để tạo alert, sau đó gọi /prescribe để điền nội dung ticket."*

- Khi Phase 2 tạo Alert P1/P2 → gọi tiếp `POST /prescribe` (async, ~2.5s, **không block**) → nhận `prescription.steps[], ticket_description, safety_warnings[]`.
- Mở rộng event để mang prescription sang TicketService: thêm field nullable vào `BatteryAnomalyDetectedV2Event` (vd `string? PrescriptionText`, `string? SuggestedSteps`) — record đã có sẵn, chỉ thêm property.
- `CreateTicketFromAlertConsumer` (TicketService) đổ `ticket_description`/steps vào ticket khi tạo.
- Feature-flag riêng `Ai:PrescriptionEnabled` + cần `ANTHROPIC_API_KEY` + đã chạy `build_knowledge_base.py`. Thiếu → `/prescribe` trả 503, BE bỏ qua, ticket vẫn tạo bằng text mặc định.

### Phase 5 — Hardening & demo (1 ngày)

- Circuit breaker (Polly) + metric `ai_predict_latency_ms`, `ai_predict_errors_total`.
- Kịch bản demo: bơm reading SOH thấp qua `iot-simulator --scenario soh_degradation` → AI trả Failed/P1 → Alert → Ticket P1 (SLA 4h) → Customer nhận push → (nếu Phase 4) ticket có sẵn các bước SOP.
- Khẳng định `/predict` <100ms; fallback: AI down thì threshold rule vẫn chạy bình thường.

---

## 4. Hai nguyên tắc giữ cho hệ thống không gãy

1. **AI và threshold rule cùng tồn tại, bổ trợ — không thay thế.**
   - Threshold = an toàn tức thì (overheat/overvoltage), nhanh, không phụ thuộc AI.
   - AI = dự báo suy giảm SOH/RUL, chậm hơn, "thông minh" hơn.
   - Cả hai đổ chung vào pipeline Alert. AI chết → threshold vẫn bảo vệ. Đây là lý do tách 2 background service riêng.
2. **AI là phụ thuộc mềm (soft dependency).** Mọi lời gọi AI đều phải: feature-flag + timeout + try/catch + không nằm trong đường ingest. Ingest và threshold **không bao giờ** được chờ AI.

---

## 5. Tóm tắt checklist file cần đụng (toàn bộ trong BatteryService + SharedContracts)

| Phase | File | Action |
|:----:|------|--------|
| 1 | `Application/Interfaces/IAiPredictionClient.cs` + DTO `AiPredictionResult` | create |
| 1 | `Infrastructure/.../Services/AiPredictionClient.cs` | create (copy `OpenMeteoClient`) |
| 1 | `Infrastructure/.../ManageDependencyInjection.cs` | thêm `AddHttpClient` |
| 1 | `Application/.../AiOptions.cs` + appsettings | create |
| 2 | `Infrastructure/BackgroundServices/SohPredictionBackgroundService.cs` | create (copy `WeatherSync`) |
| 2 | `Domain/Enums/AnomalyTypeEnum.cs` | thêm `PredictedSohDegradation` |
| 3 | `Domain/Entities/BatteryHealthSnapshot.cs` + Migration + query endpoint | create |
| 4 | `SharedContracts/Events/BatteryAnomalyDetectedV2Event.cs` | thêm field prescription |
| 4 | `Infrastructure/.../AiPrescriptionClient.cs` + TicketService consumer | create/modify |
| 0 | `docker-compose.yml` | thêm ai-module |

---

**Đường đi ngắn nhất tới "chạy được":** Phase 0 → 1 → 2. Hết Phase 2 bạn đã demo được luồng hoàn chỉnh IoT → AI → Ticket. Phase 3/4/5 là làm đẹp + prescription + hardening.
