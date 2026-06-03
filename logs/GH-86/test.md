## TEST REPORT — GH-86 — 2026-05-30
### Scope: BE
### Môi trường: local

### TÓM TẮT
Toàn bộ 29 unit test PASS, coverage đạt 82.15% (target ≥ 80%). Các query handler hoạt động đúng theo filter, phân quyền, và phân trang.

| Test case | Input | Expected | Actual | Status |
|-----------|-------|----------|--------|--------|
| TicketGetList — soft delete filter | 1 ticket bình thường + 1 IsDeleted=true | 1 item trả về | 1 item | ✅ PASS |
| TicketGetList — filter by status | 3 tickets (Open/Assigned/InProgress), query Open | 1 item | 1 item | ✅ PASS |
| TicketGetList — filter by keyword | "Battery Overheating" + "Charging", keyword="overheat" | 1 item | 1 item | ✅ PASS |
| TicketGetList — filter by priority | 3 tickets (P1/P2/P3), query P1 | 1 item | 1 item | ✅ PASS |
| TicketGetList — filter by batteryAssetId | 2 tickets, 1 match targetId | 1 item | 1 item | ✅ PASS |
| TicketGetList — sort descending | 2 tickets (OLD/NEW), IsDescending=true | NEW trước | NEW trước | ✅ PASS |
| TicketGetList — pagination page 2 | 5 tickets, PageSize=3, PageNumber=2 | 2 items, total=5 | 2 items, total=5 | ✅ PASS |
| ManagerQueue — only Open tickets | 3 tickets (Open/Assigned/InProgress) | 1 item (Open) | 1 item (Open) | ✅ PASS |
| ManagerQueue — sort P1 first | 3 tickets (P3/P1/P2) same CreatedAt | P1→P2→P3 | P1→P2→P3 | ✅ PASS |
| ManagerQueue — filter by priority | P1 + P2, query P1 | 1 item | 1 item | ✅ PASS |
| ManagerQueue — filter by category | Overheat + Charging, query Overheat | 1 item | 1 item | ✅ PASS |
| MyTicketsAsCustomer — own tickets only | 2 mine + 1 other | 2 items | 2 items | ✅ PASS |
| MyTicketsAsCustomer — filter by status | 3 tickets (Open/Resolved/Closed), query Resolved | 1 item | 1 item | ✅ PASS |
| MyTicketsAsCustomer — pagination | 4 tickets, PageSize=3, PageNumber=2 | 1 item, total=4 | 1 item, total=4 | ✅ PASS |
| MyTicketsAsStaff — assigned tickets only | 2 mine + 1 other staff | 2 items | 2 items | ✅ PASS |
| MyTicketsAsStaff — filter by status | InProgress + Resolved, query InProgress | 1 item | 1 item | ✅ PASS |
| MyTicketsAsStaff — sort P1 first | P3 + P1 | P1 trước | P1 trước | ✅ PASS |
| TicketGetById — Admin đọc any ticket | role=Admin | 200 + data | 200 + data | ✅ PASS |
| TicketGetById — Manager đọc any ticket | role=Manager | 200 + data | 200 + data | ✅ PASS |
| TicketGetById — Customer đọc ticket của mình | role=Customer, customerId match | 200 + data | 200 + data | ✅ PASS |
| TicketGetById — Customer không đọc ticket người khác | role=Customer, customerId không match | 403 | 403 | ✅ PASS |
| TicketGetById — Staff đọc ticket được assign | role=Staff, assignedStaffId match | 200 + data | 200 + data | ✅ PASS |
| TicketGetById — Staff không đọc ticket chưa assign | role=Staff, assignedStaffId không match | 403 | 403 | ✅ PASS |
| TicketGetById — ticket not found | ticketId không tồn tại | 404 | 404 | ✅ PASS |
| TicketGetById — internal comments ẩn với Customer | 1 internal + 1 public comment | 1 comment (public) | 1 comment (public) | ✅ PASS |
| ActivityTimeline — Admin xem activities | role=Admin, 2 activities | 200 + 2 items | 200 + 2 items | ✅ PASS |
| ActivityTimeline — Customer xem ticket của mình | role=Customer, customerId match | 200 + data | 200 + data | ✅ PASS |
| ActivityTimeline — Customer không xem ticket người khác | role=Customer, customerId không match | 403 | 403 | ✅ PASS |
| ActivityTimeline — ticket not found | ticketId không tồn tại | 404 | 404 | ✅ PASS |

### Coverage
- Line coverage: **82.15%** (target ≥ 80%) ✅
- Branch coverage: 75%
- Method coverage: 78.26%

### Bugs tìm được
Không có bug. Code hoạt động đúng theo spec.

### RỦI RO & LƯU Ý
- Unit tests dùng MockQueryable.Moq — `.Include()` là no-op, navigation properties cần pre-populate trong test data. Đây là hành vi bình thường cho unit test, integration test sẽ verify Include thực sự.
- Coverage 75% branch chủ yếu do các nhánh null-check trong `MapToSlaTimerDTO` (SlaTimer=null) và logic `RemainingPercent` chưa có test với SlaTimer thực (domain của GH-86 là query handlers, không phải SLA logic).
- Chưa có integration test endpoint — cần bổ sung ở sprint sau khi DB infra sẵn sàng.

### KẾT LUẬN
**PASS** — Độ tự tin: **Cao**
