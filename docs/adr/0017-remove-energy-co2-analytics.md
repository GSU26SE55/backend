# ADR 0017: Loại bỏ Energy/CO2 Analytics khỏi BatteryService

Status: Accepted (Sprint 5B — 2026-07-20).

## Context

Văn bản `overall.md` v4.x §53.1–§53.3 chốt lại scope của BatteryService chỉ phục vụ **battery health** (Voltage, Current, SOC, SOH, CycleCount, NominalCapacity, Temperature, IR, CellDelta). Các backlog cũ từng đề cập:

- Entity `EnergySession`, `BatteryCycleLog`, `EnergyDailySummary`, `SiteEnergySummary`
- Entity `ElectricityRate`, `CarbonEmissionFactor`
- Field `Site.CapacityKw` (nominal solar capacity của site)
- Report/dashboard "Energy Reduction", "CO2 Saved", "kWh saved"

Đây là phạm vi **Energy/CO2 analytics** — gắn với mô hình BEMS (Battery Energy Management System) hoặc EMS (Energy Management System) chuyên biệt, không phải solar battery maintenance management mà hệ thống KLTN này nhắm tới.

## Decision

**Loại bỏ vĩnh viễn** toàn bộ Energy/CO2 analytics khỏi BatteryService:

1. **Không tạo** các entity sau (đã có trong backlog cũ nhưng chưa implement):
   - `EnergySession`
   - `BatteryCycleLog`
   - `EnergyDailySummary`
   - `SiteEnergySummary`
   - `ElectricityRate`
   - `CarbonEmissionFactor`

2. **Xóa field** `Site.CapacityKw` (kW công suất nominal site) — xem migration `RemoveSiteCapacityKw` (#234).

3. **Loại khỏi API contract**: không export property `capacityKw` trong bất cứ DTO/response/request nào.

4. **Loại khỏi report/dashboard/demo script**: không có "energy reduction", "kWh saved", "CO2 avoided", "carbon footprint" trong scope demo Sprint 8.

5. **Giữ lại** các trường battery-health-relevant:
   - `Voltage`, `Current`, `SOC`, `SOH`, `Temperature`
   - `CycleCount` (đếm chu kỳ sạc-xả phục vụ lifespan, KHÔNG phải energy throughput)
   - `BatteryAsset.NominalCapacityAh` (Ah, dung lượng định mức của pin — phục vụ tính SOH%)
   - `InternalResistanceMilliohm`, `CellVoltageDeltaMv` (Tier 2 — Sprint 5B `#101`)

6. **CI scope-guard**: pre-commit hook + GitHub Action grep `Energy|CO2|kWh|CapacityKw` trên active source — xem `overall.md` §53.2bis + §53.2ter.

## Consequences

**Positive:**
- Scope BatteryService gọn — focus battery maintenance, không lấn sang EMS domain.
- Báo cáo KLTN dễ defend: "hệ thống chuyên cho battery maintenance theo ITIL incident workflow, không phải energy reporting platform".
- Giảm complexity: bớt 6 entity + 4 migration + 8+ API endpoint không cần thiết cho mục tiêu KLTN.
- Demo Sprint 8 focus đúng vào Alert → Ticket → Maintenance flow.

**Negative / accepted trade-offs:**
- Nếu sau này muốn pivot sang B2B EMS, phải design lại layer riêng (acceptable — không phải scope capstone).
- Mất khả năng tính "ROI per site" theo kWh saved (acceptable — KLTN tập trung maintenance compliance, không tập trung commercial reporting).

## Implementation

- Migration `RemoveSiteCapacityKw` — Sprint 5B `#234` (BatteryService).
- CI scope-guard rule — Sprint 5B `#233`/`#235` (xem `.github/workflows/ci.yml` + `.pre-commit-config.yaml`).
- ADR cross-ref: ADR-018 (Saga orchestration) chia sẻ release gate Sprint 5B; ADR-005 (B2B ITIL stance) là cơ sở "không phải B2B EMS".

## References

- `overall.md` §53.1–§53.3, §53.2bis, §53.2ter
- ADR-005 (B2B ITIL stance) — `docs/adr/0005-b2b-itil-stance.md`
- ADR-018 (Alert–Ticket Saga orchestration) — sẽ merge cùng `#239`
