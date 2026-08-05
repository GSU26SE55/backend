# Từ điển nghiệp vụ

> **Tầng T0.** Chỉ ghi những từ mà **đoán sai được**.

| Từ nghiệp vụ | Trong code | Nghĩa chính xác | Dễ nhầm với |
|---|---|---|---|
| Lấy danh sách | `GetAllAsync()` | Hàm **ĐỒNG BỘ**, trả `IQueryable` để nối `.Where()` | Hàm bất đồng bộ cần `await` |
| Xoá | `DeleteAsync(entity)` | **Xoá mềm**, trả **void**, interceptor set `IsDeleted=true` | Xoá vật lý; hàm async |
| Nguồn đo chính | `SensorSourceCode = "primary"` | Số đo từ BMS chính (đối lập `redundant`, `external-temp`) | Khoá chính / primary key |
| Nâng cấp xử lý | `Escalate` | Thêm nhân lực / nâng cấp bậc kỹ thuật viên | Gia hạn deadline SLA |
| Cụm pin | `BatteryAsset` | Tài sản pin lắp tại site, có chủ sở hữu Customer | `Battery` (kiểu/model pin) |
| Ghi chú nội bộ | `IsInternal = true` | Chỉ Admin/Manager/Staff thấy | Ghi chú riêng tư của 1 người |
| P1 / P2 / P3 | `TicketPriorityEnum` | Mức SLA của ticket: 4h / 24h / 72h | Nhãn `priority:` của GitHub issue |
| Sức khoẻ pin | `SOH` | `capacity_current / capacity_nominal × 100` (%) | `SOC` (mức sạc hiện tại) |
| Thời điểm đo | `SensorReading.Time` | Cột thời gian của hypertable | `Timestamp` (KHÔNG tồn tại) |

## Viết tắt

| Viết tắt | Đầy đủ | Ghi chú |
|---|---|---|
| SOH | State of Health | % sức khoẻ pin; ngưỡng EOL = 80% |
| SOC | State of Charge | % mức sạc hiện tại |
| BMS | Battery Management System | Nguồn `primary` |
| LWT | Last Will and Testament | Bản tin MQTT báo thiết bị mất kết nối |
| OTA | Over-The-Air | Cập nhật firmware từ xa |
| SLA | Service Level Agreement | P1 4h · P2 24h · P3 72h |
| DLQ | Dead Letter Queue | Hàng đợi message xử lý thất bại |

## Trạng thái và vòng đời

**Ticket**

```
NEW → OPEN → ASSIGNED → IN_PROGRESS → RESOLVED → CLOSED_PENDING_RATE → CLOSED
                ↘ ESCALATED (P1/P2 breach, hoặc Staff xin)
                ↘ CLOSED_REJECTED (Manager từ chối vì ngoài scope)
```

- **Không thể đảo ngược:** `CLOSED`. Mở lại phải tạo ticket mới (đếm vào chỉ số reopen).
- **Cần quyền đặc biệt:** `OPEN → ASSIGNED` chỉ Manager (đây là lúc triage, gán
  `ImpactScope` + `UrgencyLevel` → hệ thống tính ra `Priority`). `CLOSED_REJECTED` chỉ Manager.
- **Sinh tác dụng phụ ra ngoài:** `ASSIGNED` khởi động `SlaTimer` + gửi notification;
  `RESOLVED` gửi yêu cầu đánh giá; `ESCALATED` báo Admin. Mọi chuyển trạng thái đều ghi
  `TicketActivity`.

**Priority** gán **một lần** lúc triage rồi **cố định** cả vòng đời — kể cả khi escalate.

**IotDevice**

```
provisioned → active ⇄ offline → revoked/disabled
```

- Thiết bị `revoked`/`disabled` phải bị chặn ở **mọi** đường vào: HTTP ingest, MQTT ingress,
  heartbeat. Chặn một đường là chưa đủ.
