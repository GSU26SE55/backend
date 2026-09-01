# BMS switch — thêm `target: "all"`

> Dành cho firmware. Mô tả đúng phần hợp đồng thay đổi khi backend bắt đầu gửi
> `target: "all"`, và những chỗ backend **không** khoan nhượng.
>
> Liên quan: `SetBmsSwitchCommandHandler.cs`, `MqttBridgeBackgroundService.cs`,
> `GetBmsSwitchStateQueryHandler.cs`, `IotDeviceOfflineDetectionService.cs`.

---

## 1. Có gì thay đổi

Chỉ **một giá trị mới** trong trường `params.target`:

| `target` | Firmware map | Ý nghĩa |
|----------|--------------|---------|
| `"charge"` | 1 | Chỉ MOSFET sạc |
| `"discharge"` | 2 | Chỉ MOSFET xả |
| **`"all"`** | **3** | **Cả hai MOSFET, cùng một giá trị `enable`** |

Cấu trúc payload, tên topic, định dạng ack — **không đổi gì cả**. Backend trước đây trả
`400 Target must be either 'charge' or 'discharge'` cho `"all"`; nay chấp nhận.

---

## 2. Downlink — lệnh nhận vào

**Topic:** `solar/{deviceCode}/cmd`  ·  deviceCode luôn **chữ thường**

```json
{
  "cmdId": "9f2c1b7a4e5d4f0e8c3a2b1d6e7f8a90",
  "type": "set_bms_switch",
  "params": {
    "serial": "BAT-2026-REAL-001",
    "target": "all",
    "enable": false
  },
  "issuedAt": "2026-08-30T15:04:05.123Z"
}
```

- `enable: false` → **tắt** cả hai MOSFET · `enable: true` → **bật** cả hai
- `serial` xác định pin nào trên gateway
- `cmdId` phải được trả nguyên văn trong ack

---

## 3. Uplink — ack phải trả về

**Topic:** `solar/{deviceCode}/cmd/ack`

```json
{
  "cmdId": "9f2c1b7a4e5d4f0e8c3a2b1d6e7f8a90",
  "status": "ok",
  "state": {
    "chargeEnabled": false,
    "dischargeEnabled": false
  },
  "error": null
}
```

### 3.1 `state` — bắt buộc có ĐỦ HAI trường

Đây là chỗ dễ hỏng nhất. Backend đọc `state` bằng `TryReadState`, và hàm này **đòi cả
`chargeEnabled` lẫn `dischargeEnabled`, cả hai phải là boolean thật** (`true`/`false`,
không phải `"true"`, không phải `1`, không phải `null`).

Thiếu một trường, sai kiểu, hoặc `state` không phải object → **toàn bộ `state` bị bỏ qua**,
`ResultJson` lưu `null`, và UI hiển thị *"No verified state"* với công tắc bị khoá. Lệnh vẫn
tính là `ok` nhưng người dùng không thấy trạng thái nào.

> **Không có định dạng riêng cho `all`.** Vẫn là hai trường như `charge`/`discharge`.

`state` là **trạng thái đọc lại từ BMS sau khi ghi**, không phải tiếng vọng của lệnh vừa nhận.
Backend tin `state` tuyệt đối — đó là thứ duy nhất hiển thị lên màn hình.

### 3.2 `status` — chỉ 4 giá trị

| Gửi | Backend hiểu | Khi nào dùng |
|-----|--------------|--------------|
| `"ok"` | `Ok` | Ghi thành công và đã đọc lại xác nhận |
| `"failed"` | `Failed` | Ghi thất bại, hoặc chỉ áp dụng được một phần |
| `"rejected"` | `Rejected` | BMS từ chối (khoá an toàn, sai chế độ…) |
| bất kỳ chuỗi nào khác | `Unknown` | — |

So sánh **không phân biệt hoa thường**, có `Trim()`. Nhưng `"success"`, `"OK!"`, `"done"`
đều rơi vào `Unknown` → UI báo *"firmware không nhận ra lệnh"*.

### 3.3 `error` bị backend ghi đè — hoàn toàn

Với lệnh `set_bms_switch`, backend **thay thế** `error` của firmware bằng một câu cố định
theo `status` (`NormalizeBmsSwitchAckError`):

| `status` | Chuỗi client thực sự nhận |
|----------|---------------------------|
| `ok` | `null` |
| `rejected` | `The BMS rejected the control command.` |
| `failed` | `The BMS control command failed.` |
| `unknown` | `The firmware did not recognize the BMS control command.` |

Nghĩa là **mọi chi tiết firmware viết trong `error` đều bị mất** trước khi tới client. Cứ gửi
để phục vụ log phía broker, nhưng đừng trông cậy nó hiện lên UI.

---

## 4. Một MOSFET được, một MOSFET hỏng

Backend **không có cách biểu diễn "một nửa"** — `IotDeviceCommandStatusEnum` không có
trạng thái partial. Vì vậy quy ước:

```json
{
  "cmdId": "...",
  "status": "failed",
  "state": {
    "chargeEnabled": false,
    "dischargeEnabled": true
  }
}
```

- `status: "failed"` — lệnh **không** trọn vẹn
- `state` — **sự thật hiện tại**, kể cả khi lệch nhau

Nguyên tắc: **`state` luôn là sự thật, `status` nói lệnh có trọn vẹn hay không.**

Đừng trả `ok` khi chỉ một MOSFET đổi — người vận hành sẽ tin cả hai đã tắt, trong khi pin
vẫn đang cấp điện cho tải. Đây là điều khiển điện lực, sai lệch kiểu này nguy hiểm.

Nếu firmware làm được rollback (đưa về trạng thái trước lệnh rồi báo `failed`) thì sạch hơn
về ngữ nghĩa, nhưng không bắt buộc.

---

## 5. Nếu firmware CHƯA hỗ trợ `all`

Trả về:

```json
{
  "cmdId": "...",
  "status": "rejected",
  "error": "unsupported target"
}
```

Người dùng sẽ thấy *"The BMS rejected the control command."* — đủ để hiểu lệnh không chạy.

### ⚠️ Vướng mắc đã biết ở phía backend

Mobile (`BmsSwitchCard.tsx`) có logic **ẩn hẳn control BMS** khi lệnh cuối là `Rejected` và
`error` chứa `unsupported` / `not support` / `verify`. Nhưng §3.3 cho thấy backend đã ghi đè
`error` thành `"The BMS rejected the control command."` **trước khi** client đọc được — chuỗi
đó không chứa từ khoá nào, nên **nhánh ẩn control này không bao giờ chạy**.

Hệ quả: thiết bị không hỗ trợ `all` vẫn hiện công tắc, người dùng bấm và nhận lỗi mỗi lần.

Đây là việc của backend, **không phải firmware** — firmware cứ gửi `rejected` + `error` mô tả
đúng. Cách sửa phía backend là giữ lại lý do gốc cho nhánh `Rejected` (hoặc thêm một trường
riêng như `reasonCode`) thay vì nuốt nó. Ghi ra đây để không ai đi tìm nhầm chỗ.

---

## 6. Timeout — 60 giây

Lệnh ở trạng thái `Pending` quá **60 giây** kể từ `CreatedAt` bị nền quét chuyển thành
`TimedOut` kèm *"Device did not acknowledge the command within 60 seconds."*

Nghĩa là ack phải về **trong vòng 60 giây**. Nếu thao tác ghi + đọc lại BMS có thể lâu hơn,
hãy ack sớm với trạng thái đọc được thay vì im lặng — một lệnh timeout khoá công tắc trên UI
cho tới lần thao tác sau.

---

## 7. Chống trùng lệnh — backend đã xử lý

Backend từ chối lệnh mới bằng `409` khi còn lệnh **chạm cùng MOSFET** đang chờ ack:

| Đang chờ | Lệnh mới | Kết quả |
|----------|----------|---------|
| `charge` | `charge` | 409 |
| `all` | `charge` | **409** |
| `charge` | `all` | **409** |
| `all` | `all` | 409 |
| `charge` | `discharge` | cho qua |

Trước khi có `"all"`, backend so `target` bằng chuỗi thuần — `all` đang chờ sẽ **không** bị coi
là xung đột với `charge` mới, và hai lệnh trái chiều cùng xuống thiết bị. Đã sửa bằng
`TargetsOverlap`: `all` giao với mọi target, theo cả hai chiều.

**Ngoại lệ có chủ ý:** lệnh ngắt xả **tự động** do sự cố (hệ thống phát, `target=discharge`,
`enable=false`) được phép chen ngang một lệnh bật xả đang chờ — an toàn ưu tiên hơn thứ tự.

Firmware không cần làm gì cho phần này, nhưng nên biết: nếu ack bị mất, mọi lệnh chạm cùng
MOSFET sẽ bị chặn cho tới khi timeout 60 giây kết thúc.

---

## 8. Checklist

- [ ] `params.target == "all"` → map `3`, ghi **cả hai** MOSFET theo `enable`
- [ ] Ack `state` luôn có **cả** `chargeEnabled` **và** `dischargeEnabled`, kiểu boolean
- [ ] `state` là giá trị **đọc lại từ BMS**, không phải tiếng vọng của lệnh
- [ ] `status` chỉ dùng `ok` / `failed` / `rejected`
- [ ] Áp dụng một phần → `status: "failed"` + `state` phản ánh thực tế
- [ ] Chưa hỗ trợ → `status: "rejected"` + `error` mô tả lý do (xem §5)
- [ ] Ack về trong **60 giây**
- [ ] `cmdId` trả nguyên văn
