# Battery Realtime Telemetry — Mô tả tích hợp cho FE / Mobile

> **Mục đích tài liệu:** mô tả **luồng chạy · scope · cách dùng · cách setup** của kênh realtime telemetry pin (Sprint BE-IoT-Realtime).
> Tài liệu **KHÔNG** quy định cách thiết kế UI/chart phía client — FE/Mobile tự quyết kiến trúc hiển thị. Đây chỉ là **hợp đồng (contract) + hành vi backend** để tích hợp.
> Nguồn chuẩn: `overall.md §34.10`. Issue: `#614`–`#623` (BEIOT-RT-01..10).

---

## 1. Tổng quan

Kênh **một chiều server → client** đẩy số đo cảm biến pin (voltage, current, temperature, SOC, …) **theo thời gian thực** (~5 giây/pin) lên FE Web và Mobile, cho **1 pin hoặc nhiều pin** cùng lúc.

- **Transport:** **SSE** (Server-Sent Events, `text/event-stream`) — không phải WebSocket/SignalR.
- **Dữ liệu đã được làm sạch** trước khi lên stream (calibration + loại nhiễu/outlier). Rác không bao giờ xuất hiện.
- Host trong **BatteryService**, fan-out giữa nhiều instance qua **Redis pub/sub**.
- Client kết nối **qua API Gateway** như mọi request khác.

---

## 2. Luồng chạy (data flow)

```
[IoT device / simulator]
      │  POST /api/sensor-readings/batch   (ApiKey auth — không phải JWT)
      ▼
[BatteryService — BatchIngest handler]
      │  1. reject clock skew (lệch > 5 phút)
      │  2. ApplyCalibration (raw * scale + offset)
      │  3. loại outlier (voltage/current/temperature ngoài ngưỡng → bỏ qua)
      │  4. INSERT TimescaleDB hypertable (sensor_readings)
      │  5. SaveChangesAsync
      │  6. ★ publish reading ĐÃ insert lên Redis  (sau commit · try/catch · cờ Realtime:Enabled)
      ▼
[Redis pub/sub]  fan-out channel theo asset / customer / site / type / all / site:none
      ▼
[BatteryService — SSE endpoint]  subscribe channel theo scope client yêu cầu
      │  event: reading | summary | ping
      ▼
[API Gateway]  passthrough stream (không buffer)
      ▼
[FE Web / Mobile]
```

**Điểm cốt lõi:**
- Reading lên stream **đã calibrate + đã loại outlier** (làm sạch ở bước 2–3 trước khi publish ở bước 6).
- Publish là **soft-dependency**: nằm sau `SaveChangesAsync`, bọc `try/catch`. Lỗi Redis/SSE **không** làm hỏng việc ghi dữ liệu (ingest vẫn chạy).
- Phát hiện bất thường (Alert) là pipeline **riêng** (~60s), **không** chặn/làm trễ stream.

---

## 3. Kết nối SSE

```
GET /api/sensor-readings/stream?scope=<scope>&access_token=<JWT>
Accept: text/event-stream
```

- **Đi qua API Gateway** (giống mọi API call khác). Local dev: gateway ở `http://localhost:4001`.
- **Xác thực bằng JWT.** Vì native `EventSource` (browser) **không set được header `Authorization`**, token truyền qua **query param** `?access_token=<JWT>`.
  - Nếu dùng SSE client dạng `fetch` (web: `fetch-event-source`; RN: `rn-eventsource`/fetch) thì có thể set header `Authorization: Bearer <JWT>` thay cho query param.
- **Role được phép mở stream:** `Admin`, `Manager`, `Staff`, `Customer` (xem RBAC theo scope ở §6).
- **Heartbeat:** server gửi event `ping` mỗi **30 giây** để giữ kết nối sống.
- **Reconnect:** SSE tự reconnect. Server **có phát bù** qua `Last-Event-ID` — chi tiết ở [§3bis](#3bis-phát-bù-khi-reconnect--last-event-id).

---

## 3bis. Phát bù khi reconnect — `Last-Event-ID`

> Triển khai Sprint BE-IoT-Realtime `#614`. Trước bản này server **không** phát bù: rớt mạng là mất
> trắng mọi reading trong lúc rớt, và FE phải tự gọi `/history` để vá lỗ hổng.

### 3bis.1. FE phải làm gì

**Với `EventSource` chuẩn của trình duyệt: không phải làm gì cả.** Trình duyệt tự nhớ giá trị dòng
`id:` cuối cùng nhận được, và khi tự nối lại nó gửi kèm header `Last-Event-ID`. Server đọc header đó
để biết phát bù từ đâu.

**Với client tự cài (React Native, `fetch-event-source`, `rn-eventsource`, hoặc `curl`):** phải **tự
lưu** `id` của event cuối và **tự gửi** header `Last-Event-ID` ở lần kết nối lại. Không gửi = mất
phát bù (luồng trực tiếp vẫn chạy bình thường).

```
GET /api/sensor-readings/stream?scope=asset:{id}&access_token=<JWT>
Accept: text/event-stream
Last-Event-ID: 1785578400123-0
```

> Tên header **không phân biệt hoa-thường** — `Last-Event-ID` hay `Last-Event-Id` đều được.

### 3bis.2. Khung dữ liệu SSE có thêm dòng `id:`

```
id: 1785578400123-0
event: reading
data: {"batteryAssetId":"3fa85f64-...","time":"2026-08-01T07:20:00.123Z", ... }

```

- `id:` **luôn đứng trước** `event:` và `data:` trong cùng một khối. Đây là yêu cầu của đặc tả SSE —
  đặt sau thì trình duyệt bỏ qua.
- Định dạng id: `<mili-giây>-<số thứ tự>` (id do Redis Stream sinh). **Coi nó là chuỗi mờ** — đừng
  parse, đừng so sánh lớn-bé, đừng suy ra thời gian từ nó. Chỉ lưu lại và gửi trả nguyên văn.

### 3bis.3. ⚠️ CHỈ event `reading` ở scope 1 pin mới có `id:`

| Event | Có `id:`? | Vì sao |
|---|---|---|
| `reading` (scope `asset:{1 id}`) | ✅ **Có** | Từng bản ghi độc lập — bỏ lỡ là mất dữ liệu thật, nên phải phát bù được |
| `summary` (mọi scope gộp) | ❌ Không | Là **ảnh chụp định kỳ latest-per-asset**. Bỏ lỡ vài nhịp không mất gì: nhịp kế tiếp (≤ `SummaryIntervalSeconds`, mặc định 4s) đã mang trạng thái hiện tại của mọi pin |
| `stats` | ❌ Không | Giá trị rolling min/max — nhịp sau đã bao trùm nhịp trước |
| `ping` | ❌ Không | Heartbeat, không mang dữ liệu |

Hệ quả cho FE: mở stream ở scope gộp (`customer:`/`site:`/`all`/…) thì **`Last-Event-ID` không có
tác dụng**, và điều đó là đúng thiết kế — không cần vá gì thêm.

> **Vì sao không gắn id cho mọi event:** gửi `id:` rồi lờ đi khi client resume còn tệ hơn không gửi —
> client tưởng đã được bù trong khi thực tế không.

### 3bis.4. Cửa sổ phát bù — bù được bao nhiêu

Mỗi pin có một bộ đệm Redis Stream riêng, giới hạn bởi 2 tham số (`appsettings.json` → `Realtime`):

| Key | Mặc định | Ý nghĩa |
|---|---|---|
| `Realtime:ReplayMaxEvents` | `200` | Số reading gần nhất giữ lại **mỗi pin**. Cắt xấp xỉ (`MAXLEN ~`) nên số thực tế có thể nhỉnh hơn 200 một chút. `0` = **tắt phát bù** |
| `Realtime:ReplayTtlMinutes` | `5` | Hạn sống của bộ đệm, làm mới mỗi lần ghi. Pin ngừng gửi số liệu thì bộ đệm tự dọn. `0` = không đặt hạn |

Với nhịp ingest thường (~5s/reading), 200 bản ghi ≈ **vài phút** dữ liệu — đủ cứu các lần rớt mạng
vài chục giây của ứng dụng di động.

### 3bis.5. Các tình huống biên — FE phải lường trước

| Tình huống | Server làm gì | FE thấy gì |
|---|---|---|
| `Last-Event-ID` **hợp lệ, còn trong cửa sổ** | Phát bù các reading **sau** id đó, rồi nối luồng trực tiếp | Nhận một loạt `reading` cũ (theo đúng thứ tự thời gian) trước khi có dữ liệu mới |
| Id **quá cũ** (đã bị cắt khỏi bộ đệm) | Chỉ phát những gì còn giữ được | **Vẫn có lỗ hổng dữ liệu.** Muốn liền mạch tuyệt đối thì gọi `/history` (§7.2) để vá |
| Id **không tồn tại / sai định dạng** | Không phát bù, mở luồng trực tiếp bình thường | Không có gì bất thường, **không có lỗi** |
| Pin ngừng gửi > `ReplayTtlMinutes` | Bộ đệm đã bị dọn ⇒ không phát bù | Như trên |
| Redis lỗi lúc đọc bộ đệm | **Nuốt lỗi**, vẫn mở luồng trực tiếp | Mất phát bù, KHÔNG mất realtime. Đây là lựa chọn có chủ ý |
| `ReplayMaxEvents = 0` (tắt) | Không sinh `id:` nào | Không có dòng `id:` — client không có gì để nhớ |

**Không có sự kiện trùng.** Một reading có thể vừa nằm trong bộ đệm phát bù vừa được đẩy qua kênh
trực tiếp (server đăng ký kênh trực tiếp **trước** khi đọc bộ đệm, để không có sự kiện nào rơi vào
khe giữa hai bước). Server lọc trùng theo `id` trước khi gửi, nên FE **không cần** tự khử trùng.

Server cũng **không phát lại chính event ứng với `Last-Event-ID`** client gửi lên — chỉ những cái
sau nó.

### 3bis.6. Thử nhanh bằng curl

```bash
# 1) Mở stream, ghi lại id cuối cùng
curl -N -H "Accept: text/event-stream" \
  "http://localhost:4001/api/sensor-readings/stream?scope=asset:<assetId>&access_token=<JWT>"
# → id: 1785578400123-0
#   event: reading
#   data: {...}

# 2) Ngắt (Ctrl+C), chờ vài reading trôi qua, rồi nối lại kèm id đó
curl -N -H "Accept: text/event-stream" -H "Last-Event-ID: 1785578400123-0" \
  "http://localhost:4001/api/sensor-readings/stream?scope=asset:<assetId>&access_token=<JWT>"
# → nhận ngay loạt reading của quãng bị lỡ, rồi mới tới dữ liệu mới
```

---

## 4. Scope — ai xem gì

Tham số `scope` quyết định tập pin được nhận và loại event trả về:

| `scope` | Ý nghĩa | Event | Ghi chú id |
|---------|---------|-------|------------|
| `asset:{id}` | **1 pin** cụ thể | `reading` | đúng **1** GUID |
| `assets:{id1,id2,…}` | **nhiều pin** tùy ý | `summary` | nhiều GUID, **≤ 50** |
| `customer:{id}` | mọi pin của **1 khách hàng** | `summary` | 1 GUID |
| `site:{id}` | mọi pin trong **1 site** | `summary` | 1 GUID |
| `sites:{id1,id2,…}` | mọi pin trong **nhiều site** | `summary` | nhiều GUID, **≤ 50** |
| `type:{id}` | mọi pin theo **loại pin** (BatteryTypeId) | `summary` | 1 GUID |
| `all` | **toàn hệ thống** | `summary` | không có id |
| `site:none` | pin **không thuộc site nào** (SiteId = null) | `summary` | không có id |

**Quy tắc id:**
- `asset` / `customer` / `site` / `type`: nhận **đúng 1** id. Muốn nhiều id thì dùng dạng số nhiều `assets:` / `sites:`.
- `assets:` / `sites:`: nhiều id, cách nhau dấu phẩy, tối đa **50** id.
- `all` / `site:none`: **không** kèm id.
- Sai format / id không phải GUID / vượt 50 id / list rỗng → **400** (xem §8).

---

## 5. Sự kiện SSE (events)

Có **4 loại event**. Chỉ scope `asset:{1 id}` trả `reading`; mọi scope còn lại trả `summary`. Event `stats` (Sprint Bonus NS-01/03/04 — #646/#648/#649) đẩy trên **mọi scope**.

| Event | Khi nào | Nội dung | Nhịp | Có `id:` (phát bù) |
|-------|---------|----------|------|---|
| `reading` | scope `asset:{1 id}` | **1** reading đầy đủ của pin đó | mỗi reading (~5s) | ✅ **Có** |
| `summary` | mọi scope nhiều pin | `items[]` — **mỗi pin 1 phần tử, đầy đủ field y hệt `reading`** | throttle (~4s) gom latest/pin | ❌ Không |
| `stats` | mọi scope | rolling min/max dòng **nạp/xả** của 1 pin trong window `1h` / `today` | mỗi batch ingest (~5s), 2 event/batch (1 per window) | ❌ Không |
| `ping` | mọi scope | `{}` — heartbeat giữ kết nối | mỗi 30s | ❌ Không |

> Cột cuối: chỉ `reading` mang dòng `id:` để `Last-Event-ID` phát bù được — xem [§3bis](#3bis-phát-bù-khi-reconnect--last-event-id).

### 5.1. Payload `reading`

```
id: 1785578400123-0
event: reading
data: { ...các field bên dưới... }
```

- Dòng `id:` chỉ có ở event này. Lưu lại giá trị cuối cùng để gửi qua header `Last-Event-ID` khi
  nối lại — xem [§3bis](#3bis-phát-bù-khi-reconnect--last-event-id).

### 5.2. Payload `summary`

```
event: summary
data: { "scopeType": "customer", "items": [ { ...các field bên dưới... }, { ... } ] }
```

- `scopeType`: loại scope — **khớp đúng từ khóa `?scope=`** (`asset` | `customer` | `site` | `type` | `all` | `site:none`).
- `items`: mỗi pin **1 phần tử** — **cùng bộ field hệt `reading`** (parity, KHÔNG rút gọn). Một phần tử đủ thông số để hiển thị chi tiết pin đó mà không cần mở thêm stream `asset:`.

### 5.3. Bộ field của 1 reading (cả `reading` lẫn mỗi item của `summary`)

| Field (JSON, camelCase) | Kiểu | Nullable | Ý nghĩa |
|--------------------------|------|----------|---------|
| `batteryAssetId` | string (GUID) | không | Định danh pin — dùng để route đúng pin |
| `customerId` | string (GUID) | không | Khách sở hữu |
| `siteId` | string (GUID) | có | Site (null nếu pin không thuộc site) |
| `batteryTypeId` | string (GUID) | có | Loại pin |
| `time` | string (ISO 8601 UTC, `...Z`) | không | Thời điểm đo |
| `voltage` | number | không | Điện áp (V) |
| `current` | number | không | Dòng (A) — âm = xả, dương = sạc |
| `temperature` | number | không | Nhiệt độ (°C) |
| `socPercent` | number | không | State of Charge (%) |
| `sohPercent` | number | có | State of Health (%) |
| `cycleCount` | number (int) | có | Số chu kỳ sạc/xả |
| `chargingState` | number (int) | có | Trạng thái sạc |
| `internalResistanceMilliohm` | number | có | Nội trở (mΩ) |
| `cellVoltageDeltaMv` | number | có | Chênh áp cell (mV) |
| `bmsErrorCode` | string | có | Mã lỗi BMS |
| `sourceDeviceId` | string | có | Định danh thiết bị nguồn |
| `sourceType` | number (int) | không | Nguồn số đo: `1` = BMS, `2` = IoT Gateway, `3` = External |
| `sensorSourceCode` | string | có | `primary` \| `redundant` \| `external-temp` |

> ⚠️ **Field null bị LƯỢC khỏi JSON của SSE** (serialize bỏ field null). FE phải coi **field vắng mặt = null** (vd `chargingState`, `sohPercent`, `bmsErrorCode` có thể không xuất hiện).

### 5.3bis. Payload `stats` (Sprint Bonus — min/max nạp/xả streaming)

```
event: stats
data: {
  "batteryAssetId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerId": "9b2e...",
  "siteId": "c1d4...",
  "window": "1h",
  "windowStart": "2026-07-08T09:00:00Z",
  "maxChargeCurrent": 1.92,
  "minChargeCurrent": 0.31,
  "maxDischargeCurrent": 4.75,
  "minDischargeCurrent": 0.42,
  "chargeSampleCount": 210,
  "dischargeSampleCount": 385,
  "updatedAt": "2026-07-08T09:41:05Z"
}
```

| Field | Kiểu | Nullable | Ý nghĩa |
|-------|------|----------|---------|
| `batteryAssetId` | string (GUID) | không | Pin của thống kê này |
| `customerId` | string (GUID) | không | Khách sở hữu (route scope) |
| `siteId` | string (GUID) | có | Site (null nếu pin không thuộc site) |
| `window` | string | không | `1h` (bucket giờ hiện tại, UTC) \| `today` (từ 00:00 UTC) — **chốt chỉ 2 window** (Q3) |
| `windowStart` | string (ISO UTC) | không | Thời điểm bắt đầu window |
| `maxChargeCurrent` | number | có | Dòng nạp đỉnh trong window (A, **luôn dương**) |
| `minChargeCurrent` | number | có | Dòng nạp thấp nhất khi đang nạp (A, dương) |
| `maxDischargeCurrent` | number | có | Dòng xả đỉnh (A, **luôn dương** — `MAX(ABS(current))` với current < 0) |
| `minDischargeCurrent` | number | có | Dòng xả thấp nhất khi đang xả (A, dương) |
| `chargeSampleCount` | number (int) | không | Số mẫu chiều nạp đã tích lũy trong window |
| `dischargeSampleCount` | number (int) | không | Số mẫu chiều xả đã tích lũy trong window |
| `updatedAt` | string (ISO UTC) | không | Thời điểm cập nhật cuối |

**Hành vi:**
- Chỉ tính trên reading **`primary`** (null/empty coi như primary — cùng quy ước coalescer §5.4). Sample `current == 0` (idle) bỏ qua.
- Field min/max **null** khi window chưa có mẫu chiều đó. Field null bị lược khỏi JSON (như §5.3).
- State giữ trong Redis (TTL 2h cho `1h`, 26h cho `today`) — Redis flush → min/max window đang chạy reset, tự hồi sau vài mẫu; FE đối chiếu bằng REST `/aggregate` (§7.3).
- `Realtime:Enabled=false` → không có event `stats` (giống `reading`/`summary`).
- Client cũ không nghe `stats` → không ảnh hưởng (EventSource bỏ qua event lạ).
- Backfill khi mở màn: dùng REST `/aggregate` (đã có min/max per bucket) — `stats` chỉ đẩy incremental.

### 5.4. Đa nguồn cùng 1 pin

Mỗi pin có thể gửi nhiều reading cùng lúc từ các nguồn khác nhau, phân biệt bằng `sensorSourceCode`:
- `primary` — BMS, đầy đủ thông số (V/I/T/SOC/SOH/cycle/charging/bmsError).
- `redundant` — cảm biến dòng phụ (INA226).
- `external-temp` — cảm biến nhiệt phụ (DS18B20).

Trong event `summary`, backend **ưu tiên giữ source `primary`** mỗi pin (để tránh số liệu một phần của redundant/external-temp). FE **mặc định nên dùng `primary`**; `redundant`/`external-temp` để đối chiếu nếu cần.

---

## 6. RBAC — quyền theo role

| Role | Scope được phép |
|------|------------------|
| **Customer** | `asset:` / `assets:` (**chỉ pin mình sở hữu** — backend kiểm **mọi** id; 1 id lạ → 403 toàn bộ), `customer:{chính mình}` |
| **Staff** | `asset:` / `assets:` (**bất kỳ pin** — phục vụ xử lý ticket/bảo trì). **KHÔNG** được `customer` / `site` / `sites` / `type` / `all` / `site:none` |
| **Manager / Admin** | **tất cả** scope |

- Scope rộng/xuyên khách (`customer`, `site(s)`, `type`, `all`, `site:none`) → **chỉ Admin/Manager**.
- Backend **authorize tại thời điểm mở stream**. Customer A **không bao giờ** nhận data của Customer B.
- Sai quyền → **403** (xem §8).
- *(Ghi chú MVP: Staff hiện xem được bất kỳ pin; siết "chỉ pin của ticket được giao" sẽ làm sau.)*

---

## 7. REST bổ trợ — snapshot & lịch sử

SSE **chỉ đẩy data MỚI kể từ lúc kết nối**. Muốn hiển thị dữ liệu **trước thời điểm hiện tại** (vd seed chart ngay khi mở màn / sau reconnect), dùng các REST endpoint dưới (đều JWT, đi qua gateway). Cách dùng do FE tự quyết.

### 7.1. Reading mới nhất

```
GET /api/sensor-readings/{batteryAssetId}/latest
→ CommonResponse<SensorReadingDto>     (404 nếu pin chưa có reading nào)
```

### 7.2. Lịch sử (cursor pagination)

```
GET /api/sensor-readings/{batteryAssetId}/history?from=&to=&limit=&cursor=
```
- `from`, `to`: UTC, tùy chọn (lọc `time >= from`, `time <= to`).
- `limit`: mặc định 100, tối đa **1000**.
- `cursor`: timestamp record cuối trang trước (lấy trang kế).
- **Thứ tự trả về: DESC (mới nhất trước).** ⚠️ Nếu vẽ chart trái→phải theo thời gian, FE cần đảo lại cho tăng dần.
- Response `data`: `{ items: SensorReadingDto[], nextCursor: string|null, hasMore: boolean }`. Không trả `totalCount` (time-series). Dùng `hasMore` để biết còn trang.

`SensorReadingDto` (REST history/latest): `time`, `batteryAssetId`, `voltage`, `current`, `temperature`, `socPercent`, `cycleCount`, `sourceDeviceId`.

### 7.3. Aggregate (gộp bucket cho chart range lớn)

```
GET /api/sensor-readings/{batteryAssetId}/aggregate?from=&to=&interval=
```
- `interval`: `1m` | `5m` | `15m` | `1h` | `1d` (mặc định `1h`). Giá trị khác → **400**.
- Response `data`: `SensorReadingAggregateDto[]` — mỗi bucket: `time`, `avgVoltage`, `avgCurrent`, `avgTemperature`, `avgSocPercent`, `avgSohPercent` (null nếu bucket không có SOH), **tăng dần theo thời gian**.
- **Sprint Bonus NS-02 (#647):** mỗi bucket có thêm min/max tách chiều nạp/xả + V/T: `minVoltage`/`maxVoltage`, `minTemperature`/`maxTemperature`, `maxChargeCurrent`/`minChargeCurrent`/`avgChargeCurrent`, `maxDischargeCurrent`/`minDischargeCurrent`/`avgDischargeCurrent` (giá trị **luôn dương**, null nếu bucket không có mẫu chiều đó), `chargeSampleCount`/`dischargeSampleCount`. Aggregate chỉ tính trên source `primary`. Chi tiết: `docs/api-battery.md` §aggregate.
- Dùng khi range lớn (> 1 ngày) để tránh quá nhiều điểm thô, và để **backfill** card/chart min/max trước khi nghe SSE `stats`.

---

## 8. Contract lỗi (non-2xx)

Áp dụng cho cả 4 endpoint `/stream` · `/latest` · `/history` · `/aggregate`. Mọi response **non-2xx** đều là **`CommonResponse`**:

| Dạng lỗi | `statusCode` | `listErrors` | `message` |
|----------|--------------|--------------|-----------|
| **Field-level** (field sai/thiếu — vd `scope`, `limit`, `interval`) | **400** | `[{ field, detail }]` — nêu rõ field + mô tả | `"Dữ liệu không hợp lệ."` |
| **Cross-field** (vd `from` > `to`) | **422** | `[{ field, detail }]` | `"Dữ liệu không hợp lệ."` |
| **Lỗi khác** (auth / forbidden / not-found / nghiệp vụ) | 401 / 403 / 404 / … | **`null`** | mô tả ở `message` |

- Phân biệt phía client: **`listErrors !== null`** → có lỗi field-level (map xuống từng input); ngược lại đọc `message`.
- Ví dụ field-level: `400 { "isSuccess": false, "message": "Dữ liệu không hợp lệ.", "listErrors": [ { "field": "scope", "detail": "scope không hợp lệ. Dùng: asset:{id} | assets:{id1,id2} | ..." } ] }`.
- Ví dụ lỗi chung: `403 { "isSuccess": false, "message": "Không có quyền với scope này.", "listErrors": null }`.

**Riêng SSE `/stream`:**
- Lỗi **TRƯỚC khi mở stream** (scope sai / chưa auth / sai quyền) → trả **status 4xx thật** + body `CommonResponse` (như trên). **Không** dùng "200 + isSuccess=false" — client phân nhánh theo HTTP status.
- Sau khi stream đã mở (`200 text/event-stream`) → lỗi được báo bằng **đóng kết nối** (không còn body CommonResponse). `EventSource` chỉ báo `error` chung; muốn biết lý do thật, fetch lại cùng URL để đọc status.

---

## 9. Setup & chạy thử (local)

### 9.1. Chạy backend
```bash
cd capstone/backend
docker compose up -d            # gồm gateway, batteryservice, postgres/timescale, redis, rabbitmq, authservice...
```
- API Gateway: `http://localhost:4001` (FE/Mobile nối realtime + REST qua đây).
- **Redis bắt buộc** cho fan-out (đã có trong compose).

### 9.2. Cờ cấu hình (backend — `appsettings.json` → `Realtime`)
| Key | Mặc định | Ý nghĩa |
|-----|----------|---------|
| `Realtime:Enabled` | `true` | Bật/tắt toàn bộ kênh realtime. Tắt → ingest vẫn chạy, chỉ **không** stream |
| `Realtime:HeartbeatSeconds` | `30` | Nhịp event `ping` |
| `Realtime:SummaryIntervalSeconds` | `4` | Throttle gom `summary` (giây) |
| `Realtime:ReplayMaxEvents` | `200` | Số reading giữ lại **mỗi pin** cho phát bù `Last-Event-ID`. `0` = tắt phát bù (không sinh dòng `id:`) |
| `Realtime:ReplayTtlMinutes` | `5` | Hạn sống bộ đệm phát bù (phút), làm mới mỗi lần ghi. `0` = không đặt hạn |

> FE nên xử lý trường hợp `Realtime:Enabled=false`: stream mở được nhưng chỉ có `ping`, không có `reading`/`summary`.

### 9.3. Lấy JWT để test
```bash
curl -s -X POST http://localhost:4001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@yourdomain.com","password":"Admin123@"}'
# → token nằm ở data.tokens.accessToken
```

Tài khoản seed sẵn (dev):

| Role | Email | Password |
|------|-------|----------|
| Admin | `admin@yourdomain.com` | `Admin123@` |
| Manager | `manager.demo@solarbattery.local` | `Password123@` |
| Staff | `staff.tier1@solarbattery.local` | `Password123@` |
| Customer | `customer.demo@solarbattery.local` | `Password123@` |

### 9.4. Sinh dữ liệu realtime (để thấy stream nhảy)
Chạy IoT simulator để liên tục đẩy reading vào backend:
```bash
cd capstone/iot-simulator
make run            # liên tục (Ctrl+C để dừng); hoặc `make once` chạy 1 lần
```
Simulator trỏ về gateway `http://localhost:4001`, đẩy nhiều pin theo `config/seed.yaml`.

### 9.5. Thử nhanh stream bằng curl
```bash
TOK=$(curl -s -X POST http://localhost:4001/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@yourdomain.com","password":"Admin123@"}' \
  | python3 -c "import sys,json;print(json.load(sys.stdin)['data']['tokens']['accessToken'])")

# 1 pin → event reading
curl -N "http://localhost:4001/api/sensor-readings/stream?scope=asset:<assetId>&access_token=$TOK"

# toàn hệ thống → event summary (mỗi tick gom mọi pin)
curl -N "http://localhost:4001/api/sensor-readings/stream?scope=all&access_token=$TOK"
```

> Có sẵn trang test tay `backend/sse-telemetry-test.html` (mở trực tiếp bằng browser) để thử mọi scope + login 4 role — dùng tham khảo hành vi, **không** ràng buộc cách FE thiết kế.

---

## 10. Checklist tích hợp cho FE

- [ ] Nối SSE qua **gateway** (`/api/sensor-readings/stream`), token qua `?access_token=` (hoặc header nếu dùng fetch-based client).
- [ ] Chọn `scope` đúng theo role (ẩn scope ngoài quyền — xem §6).
- [ ] Lắng nghe **4 event**: `reading`, `summary`, `stats`, `ping`. Với `summary`, xử lý **toàn bộ** `items` (mỗi phần tử là 1 pin). Với `stats`, phân biệt theo `window` (`1h`/`today`) + `batteryAssetId`.
- [ ] Card "Nạp/Xả đỉnh": seed từ REST `/aggregate` (min/max per bucket) rồi cập nhật realtime bằng `stats`; vẽ đường ngưỡng từ `GET /api/thresholds` (`currentMaxCharge`/`currentMaxDischarge`).
- [ ] Field **null bị lược** khỏi SSE → coi field vắng = null.
- [ ] Mặc định dùng source `primary`.
- [ ] (Tùy chọn) seed dữ liệu lịch sử bằng REST `/history` (nhớ **DESC**) hoặc `/aggregate` trước khi để SSE đắp tiếp.
- [ ] Xử lý lỗi theo contract §8: `listErrors !== null` → field-level; ngược lại đọc `message`.
- [ ] Xử lý reconnect + trường hợp chỉ nhận `ping` (không có data / `Realtime` tắt).

---

## 11. Tham chiếu

| Nội dung | Nguồn |
|----------|-------|
| Spec đầy đủ SSE telemetry | `overall.md §34.10` (34.10.1 → 34.10.12) |
| Kế hoạch sprint + ráp IoT/AI | `aibeiotrealtime.md` (Sprint BE-IoT) |
| Issue | `#614`–`#623` (BEIOT-RT-01..10) |
| Endpoint ingest IoT | `POST /api/sensor-readings/batch` (ApiKey — IoT, không phải FE) |
| Trang test tay | `backend/sse-telemetry-test.html` |
