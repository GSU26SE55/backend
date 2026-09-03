# Test E2E — luồng IoT → Backend → AI → Ticket → Gợi ý staff/KB

> Kiểm thử thủ công toàn tuyến: simulator đẩy số liệu pin xuống cấp → BatteryService gọi AI →
> AI kê đơn kèm tài liệu → saga tạo ticket → Manager xin gợi ý nhân viên → kỹ thuật viên xin
> gợi ý tài liệu KB.
>
> Cập nhật 2026-08-08 — sau khi thêm `ticket_ai_suggestions` + 2 RPC `SuggestStaff`/`SuggestKb`.

---

## 0. Bức tranh toàn cảnh

```
[iot_simulator]  --HTTP POST /api/sensor-readings/batch-->  [ApiGateway :4001]
                                                                  |
                                                            [BatteryService]
                                          SohPredictionBackgroundService (mỗi 5 phút)
                                                                  |
                                        gRPC Prescribe(enrich=true) --> [ai-module :50051]
                                                                  |     (Mamba SOH + IsolationForest
                                                                  |      + RAG ChromaDB + LLM)
                                                                  v
                                                    Alert (Critical) + Outbox
                                                                  |
                                          BatteryAnomalyDetectedV2Event (RabbitMQ)
                                                                  v
                                              [TicketService] AlertTicketSaga
                                                                  |
                                        Ticket (Open) + ticket_ai_suggestions
                                                                  |
                        +-----------------------------------------+
                        |                                         |
        Manager: GET /staff-suggestions            Staff: GET /kb-suggestions
                        |                                         |
              gRPC SuggestStaff                          gRPC SuggestKb
                        |                                         |
                 [ai-module chấm điểm]                  [ai-module chấm điểm]
```

**Nguyên tắc xuyên suốt:** AI chỉ **gợi ý + nêu lý do**. Manager quyết định phân công,
kỹ thuật viên quyết định đọc tài liệu nào. Không có bước nào tự động phân công hay tự gắn KB.

---

## 1. Điều kiện tiên quyết

### 1.1. Build lại image AI và TicketService ⚠ BẮT BUỘC

Code mới (2 RPC + bảng `ticket_ai_suggestions`) **chưa có trong image đang chạy**. Kiểm tra:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:4015/suggest/staff \
  -H "Content-Type: application/json" -d '{"category":1,"priority":3,"candidates":[]}'
```

- `404` → image cũ, **phải build lại**
- `200` → đã sẵn sàng

```bash
cd backend
docker compose build ai-module-http ai-module-grpc batteryservice ticketservice
docker compose up -d ai-module-http ai-module-grpc batteryservice ticketservice
```

### 1.2. Migration đã áp dụng

```bash
docker exec solar-postgres psql -U postgres -d ticket_db -t \
  -c "SELECT COALESCE(to_regclass('public.ticket_ai_suggestions')::text,'MISSING');"
```

Phải ra `ticket_ai_suggestions`. Nếu `MISSING`:

```bash
docker run --rm --network backend_solar-net -v "$PWD":/src -w /src \
  -v backend-nuget:/root/.nuget/packages -v backend-dotnettools:/root/.dotnet/tools \
  -e PATH="/root/.dotnet/tools:/usr/share/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
  -e "ConnectionStrings__TicketDb=Host=postgres;Port=5432;Database=ticket_db;Username=postgres;Password=Password12345@" \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet ef database update -p services/TicketService/src/TicketService.Infrastructure \
                            -s services/TicketService/src/TicketService.Api
```

> ⚠ **Không sửa `.env`** để đổi connection string — `EnvFileLoader` không ghi đè biến môi trường
> đã set, nên truyền qua `-e` là đủ và không đụng file của repo.

### 1.3. Cờ tính năng

Kiểm tra trong container `solar-batteryservice`:

```bash
docker inspect solar-batteryservice --format '{{range .Config.Env}}{{println .}}{{end}}' \
  | grep -E "^Ai__|ALERT_TICKET"
```

| Biến | Giá trị cần | Vai trò |
|---|---|---|
| `Ai__Enabled` | `true` | Bật job dự đoán SOH; `false` → job no-op |
| `Ai__PrescriptionEnabled` | `true` | Bật gọi `/prescribe` (RAG + LLM) |
| `Ai__IntervalMinutes` | `5` | Chu kỳ quét — quyết định thời gian chờ |
| `Ai__MinReadings` | `30` | **Số bản ghi tối thiểu**, xem §2.3 |
| `ALERT_TICKET_DISPATCH_ENABLED` | `true` | Cho saga tạo ticket |

### 1.4. Dữ liệu nền

- **Ít nhất 1 nhân viên `Role=Staff`**, `IsAvailable=true`, có `SkillCodes` khớp loại lỗi.
  Seed sẵn: `staff.tier1/2/3@solarbattery.local`.
- **Ít nhất 1 bài KB `Status=Published`** — không có thì `kb-suggestions` trả rỗng kèm note.

```bash
docker exec solar-postgres psql -U postgres -d ticket_db -c \
  "SELECT full_name, role, skill_tier, skill_codes, is_available FROM staff_accounts WHERE is_deleted=false;"
docker exec solar-postgres psql -U postgres -d ticket_db -c \
  "SELECT code, title, status, category FROM knowledge_base_articles WHERE is_deleted=false LIMIT 10;"
```

> ⚠ Cột `role` là **mới** (migration này). Hàng cũ mặc định `'Staff'`; Manager/Admin sẽ được
> ghi đè ở lần đồng bộ tài khoản kế tiếp. Nếu thấy Manager mang `role='Staff'`, sửa tay để
> họ không lọt vào danh sách gợi ý:
> ```sql
> UPDATE staff_accounts SET role='Manager' WHERE email='manager@solars.io.vn';
> ```

---

## 2. Bước 1 — IoT đẩy dữ liệu

### 2.1. Cấu hình simulator

`iot_simulator/config/seed.yaml`:

```yaml
backend:
  base_url: http://localhost:4001      # ApiGateway
  contract_version: current            # legacy contract — backend hôm nay accept

devices:
  - device_code: ESP32-S3-001
    api_key: <lấy từ Admin → IoT Devices>
    batteries:
      - serial: BAT-001
        battery_asset_id: <UUID pin, copy từ URL trang chi tiết pin>
        initial_soh: 82.0              # gần ngưỡng EOL 80% để nhanh ra alert
        scenario: soh_degradation      # SOH tụt dần → AI phân loại Degrading/Failed
```

### 2.2. Chạy

```bash
cd iot_simulator
make venv && source .venv/bin/activate && pip install -r requirements.txt

# Gửi liên tục (khuyến nghị — cần đủ 30 bản ghi)
python -m src.main --scenario soh_degradation

# Hoặc 1 batch rồi thoát, để kiểm tra kết nối
python -m src.main --once
```

### 2.3. ⚠ Cạm bẫy: cần ≥ 30 bản ghi

`Ai__MinReadings=30` và model dùng cửa sổ 30 timestep. Chưa đủ 30 bản ghi **trong khoảng thời
gian job quét** thì pin bị bỏ qua **im lặng** — không lỗi, không log cảnh báo rõ ràng.

Kiểm tra:

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c \
  "SELECT battery_asset_id, COUNT(*), MAX(\"timestamp\")
   FROM sensor_readings GROUP BY battery_asset_id ORDER BY 3 DESC LIMIT 5;"
```

### 2.4. Nghiệm thu bước 1

- [ ] Simulator log `200 OK` cho mỗi batch
- [ ] `sensor_readings` có ≥ 30 hàng cho pin đang test

---

## 3. Bước 2 — AI phân tích và kê đơn

`SohPredictionBackgroundService` chạy mỗi `Ai__IntervalMinutes` (mặc định 5 phút).
**Chờ tối đa 5 phút** hoặc restart để kích hoạt ngay:

```bash
docker restart solar-batteryservice
docker logs -f solar-batteryservice | grep -iE "SohPrediction|prescribe|alert"
```

Log mong đợi: `SohPrediction tick: predicted=N, alerts=M`

Phía AI:

```bash
docker logs solar-ai-module-grpc | grep -i prescribe | tail -5
```

Mong đợi: `prescribe battery_id=... enrich=True llm_provider=... rag_ms=... total_ms=...`

### 3.1. ⚠ Cạm bẫy: `llm_provider=none`

Không cấu hình API key LLM thì AI **vẫn chạy** nhưng theo đường rule-based:
`enriched=false`, `maintenance_docs` rỗng → `kb_doc_refs` rỗng → gợi ý KB mất tín hiệu mạnh nhất
(vẫn hoạt động, chỉ kém chính xác). Đây là **suy giảm có chủ ý**, không phải lỗi.

### 3.2. Nghiệm thu bước 2

```bash
docker exec solar-postgres psql -U postgres -d battery_db -c \
  "SELECT id, anomaly_type, severity, ai_prescription_id, detected_at
   FROM alerts ORDER BY created_at DESC LIMIT 3;"
```

- [ ] Có alert `severity=Critical`
- [ ] `ai_prescription_id` khác NULL (nếu LLM bật)

---

## 4. Bước 3 — Saga tạo ticket + lưu structured data

```bash
docker logs solar-ticketservice | grep -iE "saga|CreateTicketFromAlert" | tail -10
```

### 4.1. Nghiệm thu — **điểm mới quan trọng nhất**

```bash
docker exec solar-postgres psql -U postgres -d ticket_db -c \
  "SELECT t.code, t.status, t.category, t.priority,
          s.llm_provider, s.enriched,
          jsonb_array_length(s.action_steps) AS steps,
          jsonb_array_length(s.kb_doc_refs)  AS kb_refs,
          jsonb_array_length(s.sop_references) AS sops
   FROM tickets t
   LEFT JOIN ticket_ai_suggestions s ON s.ticket_id = t.id
   WHERE t.origin_alert_id IS NOT NULL
   ORDER BY t.created_at DESC LIMIT 3;"
```

- [ ] Ticket tồn tại, `status=Open`, `origin_alert_id` khác NULL
- [ ] **Có hàng trong `ticket_ai_suggestions`** ← thứ trước đây bị mất
- [ ] `action_steps`, `sop_references` > 0
- [ ] `kb_doc_refs` > 0 (chỉ khi `enriched=true`)
- [ ] `Description` của ticket **vẫn** chứa `--- AI Prescription ---` (giữ có chủ ý, dùng cho AI dò trùng)

> Không có hàng `ticket_ai_suggestions` mà ticket vẫn tạo → **đúng thiết kế** với ticket từ
> threshold engine (không gọi AI). Việc ghi là best-effort, không bao giờ chặn tạo ticket.

---

## 5. Bước 4 — Gợi ý nhân viên (Manager)

### 5.1. Lấy token Manager

```bash
TOKEN=$(curl -s -X POST http://localhost:4001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"manager@solars.io.vn","password":"<mật khẩu>"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['accessToken'])")
```

### 5.2. Gọi

```bash
TICKET_ID=<guid ticket vừa tạo>
curl -s "http://localhost:4001/api/tickets/$TICKET_ID/staff-suggestions?topN=5" \
  -H "Authorization: Bearer $TOKEN" | python3 -m json.tool
```

Kết quả mong đợi:

```json
{
  "isSuccess": true,
  "data": {
    "items": [
      {
        "staffId": "...",
        "fullName": "Staff Tier2 Specialist",
        "skillTier": 2,
        "skillCodes": ["battery", "charging"],
        "activeTickets": 1,
        "maxConcurrentTickets": 8,
        "score": 0.81,
        "reason": "khớp kỹ năng chính 'battery'; Tier 2 đúng yêu cầu P2; đang xử lý 1/8 ticket"
      }
    ],
    "note": "",
    "aiAvailable": true
  }
}
```

### 5.3. Các trường hợp cần thử

| Tình huống | Cách tạo | Kết quả đúng |
|---|---|---|
| Bình thường | như trên | `items` có phần tử, mỗi phần tử có `reason` |
| Không ai đủ tier | ticket P1, chỉ có Tier 1/2 | `items=[]`, `note` nói rõ thiếu tier |
| Tất cả đầy tải | đặt `max_concurrent_tickets=1` rồi gán 1 ticket | `items=[]`, `note` nói đầy tải |
| **AI sập** | `docker stop solar-ai-module-grpc` | HTTP **200**, `aiAvailable=false`, `items=[]` |
| Staff gọi | dùng token Staff | **403** |

> ⚠ Trường hợp "AI sập" là quan trọng nhất: phải trả **200 kèm cờ**, KHÔNG được 500.
> Gợi ý là tính năng phụ trợ — Manager vẫn phải triage được như trước.
> Nhớ `docker start solar-ai-module-grpc` sau khi thử.

### 5.4. Đối chiếu với lệnh phân công thật

Danh sách gợi ý đã lọc theo **đúng** điều kiện mà `TicketAssignCommandHandler` kiểm tra.
Kiểm chứng: chọn người đầu danh sách rồi phân công thật — phải thành công, không nhận 403.

```bash
curl -s -X POST "http://localhost:4001/api/tickets/$TICKET_ID/assign" \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"primaryHandlerStaffId":"<staffId từ gợi ý>","supporterStaffIds":[]}' | python3 -m json.tool
```

- [ ] Phân công thành công → chứng minh bộ lọc của AI khớp với BE

---

## 6. Bước 5 — Gợi ý tài liệu KB (Staff)

Sau khi ticket đã được phân công ở §5.4:

```bash
STAFF_TOKEN=$(curl -s -X POST http://localhost:4001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"staff2@solars.io.vn","password":"<mật khẩu>"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['accessToken'])")

curl -s "http://localhost:4001/api/tickets/$TICKET_ID/kb-suggestions?topN=5" \
  -H "Authorization: Bearer $STAFF_TOKEN" | python3 -m json.tool
```

Mong đợi:

```json
{
  "isSuccess": true,
  "data": {
    "items": [
      {
        "kbArticleId": "...",
        "code": "KB-2026-0001",
        "title": "Xử lý pin quá nhiệt",
        "score": 0.72,
        "reason": "đúng loại lỗi; khớp thẻ nhiet; AI đã tham chiếu tài liệu này khi phân tích"
      }
    ],
    "note": "",
    "aiAvailable": true
  }
}
```

### 6.1. Các trường hợp cần thử

| Tình huống | Kết quả đúng |
|---|---|
| Staff được phân công (PrimaryHandler) | 200 + danh sách |
| **Staff là Supporter** | 200 + danh sách (cố ý nới — họ cũng đang sửa chữa) |
| Staff KHÔNG được phân công | **403** |
| Staff đã bị chuyển giao (PreviousPrimaryHandler) | **403** |
| Manager/Admin | 200 (xem mọi ticket) |
| Customer | **403** |
| Chưa có bài KB `Published` | 200, `items=[]`, `note` giải thích |
| AI sập | 200, `aiAvailable=false` |

> **Xem ≠ Gắn.** Supporter xem được gợi ý nhưng **không** gắn được tài liệu —
> `POST /api/knowledge-base/references` vẫn chỉ cho PrimaryHandler. Đây là chủ ý.

### 6.2. Kiểm chứng bonus "AI đã tham chiếu"

Bài KB mà AI thực sự truy hồi qua RAG phải xếp trên, kể cả khác category:

```bash
docker exec solar-postgres psql -U postgres -d ticket_db -c \
  "SELECT kb_doc_refs, sop_references FROM ticket_ai_suggestions
   WHERE ticket_id='$TICKET_ID';"
```

Đối chiếu `kb_doc_refs` (vd `maintenance/bms_warning_codes.md`) với `reason` của kết quả đầu
danh sách — phải thấy chuỗi *"AI đã tham chiếu tài liệu này khi phân tích"*.

> ⚠ **Giới hạn đã biết:** `kb_doc_refs` là **đường dẫn file** trong kho tài liệu của ai-module,
> KHÔNG phải `KnowledgeBaseArticle.Code`. Hai kho tách rời nhau nên chỉ so khớp **mềm** theo
> token tên file ↔ tiêu đề bài viết. Tên file và tiêu đề không chung từ khoá thì bonus không
> kích hoạt — đúng thiết kế hiện tại, không phải lỗi.

---

## 7. Bước 6 — FE và Mobile

### 7.1. Trạng thái hiện tại ⚠

**Chưa có UI cho hai endpoint mới.** Phase 2 chỉ làm backend. Cụ thể:

| Thành phần | Trạng thái |
|---|---|
| `frontend` — `endpoints.ts` | ❌ chưa khai `staff-suggestions` / `kb-suggestions` |
| `frontend` — màn Manager triage | ❌ chưa có panel gợi ý nhân viên |
| `frontend` — màn Staff xử lý ticket | ❌ chưa có panel gợi ý tài liệu |
| `mobile` | ❌ chưa có (và không nằm trong luồng này — Customer không xem gợi ý nội bộ) |

Vì vậy bước 4–5 hiện **chỉ test bằng curl/Postman/Swagger**.

### 7.2. Phần FE/Mobile *có thể* test ngay

Những thứ luồng này đi qua và FE đã hỗ trợ:

| Màn hình | Kiểm chứng |
|---|---|
| Manager → Alerts | Alert mới xuất hiện, đúng severity |
| Manager → Tickets | Ticket `[Auto] ...` xuất hiện, status `Open` |
| Chi tiết ticket | `Description` chứa đoạn `--- AI Prescription ---` |
| Manager → phân công | Chọn nhân viên (thủ công) → ticket sang `Assigned` |
| Staff → ticket của tôi | Thấy ticket vừa được giao |
| Mobile (Customer) | Nhận push khi có alert; xem chi tiết pin + biểu đồ SOH |

### 7.3. Việc cần làm để đóng vòng FE

1. Thêm vào `frontend/src/shared/utils/endpoints.ts`:
   ```ts
   STAFF_SUGGESTIONS: (id: string) => `/api/tickets/${id}/staff-suggestions`,
   KB_SUGGESTIONS:    (id: string) => `/api/tickets/${id}/kb-suggestions`,
   ```
2. Hook TanStack Query — `staleTime: 0` (tình trạng rảnh/bận đổi liên tục).
3. Panel gợi ý nhân viên ở màn triage của Manager: hiển thị `score`, **`reason`**, `activeTickets/max`.
4. Panel gợi ý tài liệu ở màn Staff xử lý ticket, kèm nút "áp dụng" gọi
   `POST /api/knowledge-base/references`.
5. **Bắt buộc:** hiển thị `note` và xử lý `aiAvailable=false` — người dùng cần phân biệt
   "không có ai phù hợp" với "hệ thống gợi ý đang hỏng".

---

## 8. Bảng nghiệm thu tổng

| # | Hạng mục | Cách kiểm | Đạt |
|---|---|---|---|
| 1 | IoT gửi được số liệu | simulator log 200 | ☐ |
| 2 | Có ≥ 30 bản ghi | truy vấn `sensor_readings` | ☐ |
| 3 | AI chạy prescribe | log `solar-ai-module-grpc` | ☐ |
| 4 | Alert Critical được tạo | truy vấn `alerts` | ☐ |
| 5 | Ticket auto được tạo | truy vấn `tickets` | ☐ |
| 6 | **`ticket_ai_suggestions` có dữ liệu** | truy vấn JOIN §4.1 | ☐ |
| 7 | Description vẫn có đoạn AI | xem chi tiết ticket | ☐ |
| 8 | Gợi ý nhân viên trả kèm lý do | `GET /staff-suggestions` | ☐ |
| 9 | Người được gợi ý phân công được | `POST /assign` thành công | ☐ |
| 10 | Manager/Admin không lọt vào gợi ý | kiểm `items` | ☐ |
| 11 | Gợi ý KB trả kèm lý do | `GET /kb-suggestions` | ☐ |
| 12 | Supporter xem được, người ngoài 403 | đổi token | ☐ |
| 13 | **AI sập vẫn trả 200 + cờ** | `docker stop ai-module-grpc` | ☐ |
| 14 | FE hiện alert + ticket | mở web Manager | ☐ |

---

## 9. Xử lý sự cố

| Triệu chứng | Nguyên nhân thường gặp | Cách xử lý |
|---|---|---|
| `/suggest/staff` trả 404 | image AI cũ | build lại (§1.1) |
| Không có alert nào | chưa đủ 30 bản ghi, hoặc `Ai__Enabled=false` | §2.3, §1.3 |
| Có alert nhưng không có ticket | `ALERT_TICKET_DISPATCH_ENABLED=false` | bật lại, restart ticketservice |
| Ticket có nhưng `ticket_ai_suggestions` rỗng | `Ai__PrescriptionEnabled=false`, hoặc ticket từ threshold engine | kiểm cờ; nếu từ threshold engine thì **đúng** |
| `kb_doc_refs` rỗng | không có API key LLM → đường rule-based | đúng thiết kế; đặt key nếu muốn đủ tính năng |
| `staff-suggestions` trả rỗng | không ai đủ tier / đầy tải / sai `role` | đọc `note`; kiểm cột `role` (§1.4) |
| Manager xuất hiện trong gợi ý | cột `role` chưa đồng bộ | `UPDATE staff_accounts SET role='Manager' ...` |
| 500 thay vì 200 khi AI sập | fail-safe hỏng — **lỗi thật** | báo ngay, đây là hồi quy |
| Migration báo "already up to date" nhưng bảng không có | EF đọc `.env` trỏ `localhost` | truyền `-e ConnectionStrings__TicketDb=...` (§1.2) |

---

## 10. Ghi chú cho người kiểm thử

**Thời gian chờ.** Job quét mỗi 5 phút; saga thêm vài giây. Từ lúc IoT gửi đủ dữ liệu tới lúc
ticket xuất hiện thường **1–6 phút**. Muốn nhanh thì `docker restart solar-batteryservice`.

**Idempotency.** Cùng một pin + cùng loại lỗi trong lúc alert còn mở sẽ **tái dùng** ticket cũ,
không tạo mới. Muốn ticket mới thì đóng ticket cũ hoặc đổi sang pin khác.

**Không có bước nào tự động phân công.** Nếu thấy ticket tự chuyển sang `Assigned` mà không ai
bấm, đó là **lỗi** — luồng này chỉ gợi ý.
