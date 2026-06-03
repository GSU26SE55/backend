# ADR 0005: Customer Scope (B2B) and ITIL Stance

Status: Accepted.

## Context

Văn bản tổng hợp ngày 2026-05-16 nêu lưu ý quan trọng:

> ITIL là quy trình quản lý dịch vụ IT nội bộ tổ chức. Hệ thống này có customer bên ngoài → ITIL chỉ tham khảo, không áp dụng nguyên bản.

`overall.md` cũ §26 References ban đầu cite ITIL 4 Incident Management / Problem Management mà không phân biệt phiên bản internal-IT vs B2B. Điều này gây hiểu nhầm và không bảo vệ được trước Hội đồng KLTN khi hỏi "framework SLA của em là gì?".

## Decision

**Customer scope chính thức:** B2B (doanh nghiệp vận hành solar farm). Có thể có một số B2C end-user trong scope phụ qua Mobile App, nhưng quyết định kiến trúc, SLA, và workflow phải lấy B2B làm chuẩn.

**ITIL stance:** Áp dụng **ITIL 4 Service Value System (SVS)** với góc nhìn Service Provider → External Customer. Cụ thể:

1. **Incident Management Practice (ITIL 4 Foundation §5.3)** — basis của Ticket lifecycle và state machine (`overall.md §2.4`).
2. **Incident Prioritization (Impact × Urgency)** — basis của Priority Matrix (`overall.md §2.4bis`).
3. **Problem Management Practice** — basis của Incident flag và parent-child ticket relations (§32).

**KHÔNG** áp dụng:
- ITIL 4 cho internal-IT operations (Service Desk cho employee tickets).
- ITIL v3 (đã legacy, không cite trong báo cáo).
- PRINCE2/PMI risk matrices — quá generic, không có concept "asset scope".

**SLA timing 4h/24h/72h** giữ nguyên — industry common cho B2B managed services (MSPAlliance Cloud Verify Tier 1, AWS Premium Support, Atlassian Jira Service Management defaults).

## Consequences

**Positive:**
- Báo cáo KLTN có cơ sở framework rõ ràng để defend trước Hội đồng.
- SLA design + Priority Matrix có cite chính thức (ITIL 4 SVS + ISO/IEC 20000-1:2018).
- Phân biệt rõ với ITIL nội bộ IT — không bị câu hỏi "tại sao dùng ITIL cho external customer".

**Negative / accepted trade-offs:**
- ITIL 4 SVS license tài liệu chính thức là pay-walled (AXELOS) — phải mua hoặc dựa vào tóm tắt công khai (Wikipedia, AXELOS preview, vendor blogs).
- Service credits / contractual SLA enforcement nằm ngoài scope capstone — chỉ implement operational SLA (timer + breach event + escalation), không có legal layer.

**Ripple effects:**
- `overall.md §26 References` đã update (xem commit B5/B11) — cite ITIL 4 SVS rõ ràng + thêm ISO/IEC 20000-1.
- `.claude/docs/ai-research-references.md` Phụ lục B5 cite chi tiết các nguồn ITIL 4 SVS + B2B SaaS SLA best practices.
- Public Knowledge Base (§43) khả thi vì customer-facing service.
- Status Page (§64) cần thiết vì B2B customer phải biết khi nào dịch vụ bị gián đoạn.

## Date

2026-05-16
