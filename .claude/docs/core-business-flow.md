# GSU26SE55 — Core Business Flow

**Quy trình nghiệp vụ cốt lõi** theo 4 role · Scope: SE-only · ITIL Ticket & SLA Management

---

## Vai trò trong hệ thống

| Role | Màu | Mô tả |
|------|-----|-------|
| Admin | 🔴 | Setup/Config — tạo account, cấu hình pin, SLA rules, quản lý Battery Asset |
| Customer | 🟣 | Tạo ticket, theo dõi tiến trình, rate service |
| Manager | 🔵 | Assign, approve, escalate |
| Staff | 🟢 | Xử lý ticket, ghi maintenance log |
| System | 🟡 | Auto alert / SLA timer |

---

## 1. Tổng quan 6 Phase

Quy trình tổng thể chia làm 6 phase. Mỗi phase có 1 role "owner" chính, role khác tham gia phụ.

| # | Phase | Mô tả | Owner |
|---|-------|-------|-------|
| ① | Setup & Configuration | Tạo account, cấu hình loại pin, set ngưỡng cảnh báo, định nghĩa SLA rules P1/P2/P3, quản lý Battery Asset (serial, warranty, owner) | ADMIN |
| ② | Monitoring & Detection | Customer theo dõi pin real-time. System auto-check ngưỡng, sinh cảnh báo khi bất thường | CUSTOMER + SYSTEM |
| ③ | Ticket Creation | Customer tạo ticket thủ công, HOẶC system auto-tạo từ alert critical | CUSTOMER / SYSTEM |
| ④ | Triage & Assignment | Manager review ticket, set priority, assign cho Staff phù hợp. SLA timer bắt đầu | MANAGER |
| ⑤ | Resolution | Staff xử lý: cập nhật status, ghi log, liên hệ customer, mark Resolved hoặc Request Escalation | STAFF |
| ⑥ | Verification & Closure | Manager approve, Customer đánh giá (rate/reopen). Ticket chính thức Closed | MANAGER + CUSTOMER |

---

## 2. Swimlane End-to-End

Mỗi lane là 1 role. Mũi tên cắt ngang lane = handoff giữa các role.

```mermaid
flowchart TB
    Start([🔔 Bắt đầu chu kỳ vận hành])

    subgraph ADMIN["🔴 ADMIN LANE — Setup 1 lần"]
        direction LR
        A1[Tạo accounts<br/>Manager/Staff/Customer]
        A2[Cấu hình loại pin<br/>+ ngưỡng cảnh báo]
        A3[Định nghĩa SLA rules<br/>L1/L2/L3]
        A4[Quản lý Battery Asset<br/>serial, warranty, owner]
        A1 --> A2 --> A3 --> A4
    end

    subgraph CUSTOMER["🟣 CUSTOMER LANE — Mobile App"]
        direction LR
        C1[Mở app<br/>xem pin real-time]
        C2{Phát hiện<br/>bất thường?}
        C3[Tạo ticket<br/>mô tả + ảnh]
        C4[Theo dõi tiến trình<br/>+ reply Staff]
        C5{Resolved OK?}
        C6[Rate & Close]
        C7[Reopen]
        C1 --> C2
        C2 -->|Có| C3
        C2 -->|Không| C1
        C4 --> C5
        C5 -->|OK| C6
        C5 -->|Chưa OK| C7
    end

    subgraph SYSTEM["🟡 SYSTEM LANE — Backend"]
        direction LR
        S1[Check ngưỡng<br/>từng chu kỳ]
        S2{Vượt<br/>ngưỡng?}
        S3[Auto-tạo alert<br/>+ push Customer]
        S7{Alert trùng<br/>trong cửa sổ thời gian?}
        S8[Merge vào ticket/alert cũ]
        S4[Start SLA timer<br/>khi ticket assigned]
        S9[Pause/Resume SLA<br/>khi chờ Customer/Parts]
        S5{SLA breach<br/>sắp xảy ra?}
        S6[Cảnh báo Manager<br/>+ Staff]
        S1 --> S2
        S2 -->|Có| S3 --> S7
        S7 -->|Có| S8
        S7 -->|Không| C2
        S2 -->|Không| S1
        S4 --> S9 --> S5
        S5 -->|Có| S6
    end

    subgraph MANAGER["🔵 MANAGER LANE — Web App"]
        direction LR
        M1[Nhận ticket<br/>vào queue]
        M2[Đánh giá priority<br/>P1/P2/P3]
        M3[Assign Staff theo<br/>capacity + skill matrix]
        M4{Staff mark<br/>Resolved?}
        M5[Review & Approve]
        M6[Reject<br/>gửi lại Staff]
        M7{Cần escalate?}
        M8[Reassign Senior<br/>hoặc Escalate L3]
        M9[Xem activity timeline<br/>và SLA history]
        M1 --> M2 --> M3
        M4 -->|Yes| M9 --> M5
        M4 -->|No sau SLA| M7
        M7 -->|Yes| M8
    end

    subgraph STAFF["🟢 STAFF LANE — Web App"]
        direction LR
        T1[Nhận notification<br/>ticket mới]
        T2[Update status<br/>In Progress]
        T3[Comment với Customer<br/>nếu cần thông tin]
        T8[Dùng Knowledge Base<br/>solution template]
        T4[Ghi Maintenance Log]
        T5{Xử lý được?}
        T6[Mark Resolved]
        T7[Request Escalation<br/>gửi Manager]
        T1 --> T2 --> T3 --> T8 --> T4 --> T5
        T5 -->|Được| T6
        T5 -->|Không| T7
    end

    Start --> A1
    A4 -.setup xong.-> C1
    A4 -.-> S1
    C3 --> M1
    M3 --> T1
    M3 --> S4
    T6 --> M4
    T7 --> M7
    M5 --> C4
    M6 --> T2
    C7 --> M1
    M8 --> T1
```

---

## 3. Sequence Diagram — Tương tác role theo thời gian

Góc nhìn khác: ai gọi ai, gọi thứ tự nào. Hữu ích khi thiết kế API contract.

```mermaid
sequenceDiagram
    autonumber
    actor Admin as 🔴 Admin
    actor Customer as 🟣 Customer
    participant Mobile as 📱 Mobile App
    participant Backend as ⚙️ Backend API
    participant DB as 🗄️ DB (TimescaleDB)
    actor Manager as 🔵 Manager
    actor Staff as 🟢 Staff
    participant Notif as 📨 Notification Service

    Note over Admin,Backend: Phase 1 — Setup (1 lần)
    Admin->>Backend: POST /users (tạo Manager/Staff/Customer)
    Admin->>Backend: POST /battery-types + /thresholds
    Admin->>Backend: POST /battery-assets (serial, customerId, warranty)
    Admin->>Backend: POST /sla-rules (P1/P2/P3 + escalation)

    Note over Customer,DB: Phase 2 — Monitoring & Alert
    Customer->>Mobile: Mở app
    Mobile->>Backend: GET /battery/realtime?assetId={id}
    Backend->>DB: query latest metrics
    DB-->>Backend: V, I, T, SOC
    Backend-->>Mobile: data
    Mobile-->>Customer: hiển thị chart

    Backend->>Backend: scheduled check ngưỡng
    Backend->>Backend: deduplicate/correlate alert
    Backend->>Notif: push alert nếu vượt ngưỡng
    Notif-->>Customer: 🔔 cảnh báo

    Note over Customer,Manager: Phase 3–4 — Create & Assign
    Customer->>Mobile: Tạo ticket
    Mobile->>Backend: POST /tickets (assetId, category, description, files)
    Backend->>DB: lưu ticket (status=OPEN)
    Backend->>DB: ghi Activity Timeline
    Backend->>Notif: notify Manager
    Notif-->>Manager: ticket mới

    Manager->>Backend: GET /tickets?status=OPEN
    Manager->>Backend: GET /staff/workload + /staff/skills
    Manager->>Backend: PUT /tickets/{id}/assign (staffId, priority)
    Backend->>DB: update + start SLA timer
    Backend->>DB: ghi Activity Timeline
    Backend->>Notif: notify Staff
    Notif-->>Staff: ticket được giao

    Note over Staff,Customer: Phase 5 — Resolution
    Staff->>Backend: PUT /tickets/{id}/status=IN_PROGRESS
    Staff->>Backend: GET /knowledge-base?category={category}
    Staff->>Backend: POST /tickets/{id}/comments
    Backend->>Notif: notify Customer
    Notif-->>Customer: reply từ Staff

    alt Chờ Customer hoặc chờ linh kiện
        Staff->>Backend: PUT /tickets/{id}/hold (WAITING_CUSTOMER / WAITING_PARTS)
        Backend->>DB: pause SLA timer + ghi lý do
    else Có thể xử lý tiếp
        Staff->>Backend: PUT /tickets/{id}/resume
        Backend->>DB: resume SLA timer
    end

    Staff->>Backend: POST /tickets/{id}/maintenance-log
    Staff->>Backend: PUT /tickets/{id}/status=RESOLVED

    Note over Manager,Customer: Phase 6 — Verify & Close
    Backend->>Notif: notify Manager approve
    Manager->>Backend: PUT /tickets/{id}/approve
    Backend->>Notif: notify Customer
    Customer->>Mobile: xem ticket resolved
    alt Customer đồng ý
        Customer->>Backend: POST /tickets/{id}/rate
        Backend->>DB: status=CLOSED + save CSAT
    else Customer chưa OK
        Customer->>Backend: PUT /tickets/{id}/reopen
        Backend->>DB: status=OPEN + increment reopenCount
        Backend->>Notif: notify Manager
    end
```

---

## 4. Ticket State Machine

Vòng đời 1 ticket. Mỗi state có role được phép chuyển trạng thái.

```mermaid
stateDiagram-v2
    [*] --> NEW : Customer tạo / System auto-create

    NEW --> OPEN : Manager tiếp nhận<br/>(thêm vào queue)
    OPEN --> ASSIGNED : Manager assign Staff<br/>+ set priority<br/>→ start SLA timer

    ASSIGNED --> IN_PROGRESS : Staff nhận & bắt đầu
    IN_PROGRESS --> IN_PROGRESS : Staff ghi log,<br/>comment Customer

    IN_PROGRESS --> WAITING_CUSTOMER : Staff cần thêm thông tin<br/>→ pause SLA có kiểm soát
    WAITING_CUSTOMER --> IN_PROGRESS : Customer phản hồi<br/>→ resume SLA
    IN_PROGRESS --> WAITING_PARTS : Cần linh kiện/on-site<br/>→ pause SLA có lý do
    WAITING_PARTS --> IN_PROGRESS : Có linh kiện / tới lịch xử lý<br/>→ resume SLA

    IN_PROGRESS --> RESOLVED : Staff mark resolved
    IN_PROGRESS --> ESCALATED : Staff request escalate<br/>HOẶC SLA breach

    ESCALATED --> ASSIGNED : Manager reassign<br/>senior staff
    ESCALATED --> INCIDENT : Nhiều ticket liên quan<br/>hoặc rủi ro an toàn
    ESCALATED --> CLOSED_REJECTED : Manager reject ngoài scope

    INCIDENT --> ASSIGNED : Incident được điều phối<br/>về staff/lead staff

    RESOLVED --> CLOSED_PENDING_RATE : Manager approve<br/>chờ Customer rate
    CLOSED_PENDING_RATE --> CLOSED : Customer rate<br/>hoặc auto-close sau 7 ngày
    RESOLVED --> IN_PROGRESS : Manager reject<br/>gửi lại Staff
    CLOSED_PENDING_RATE --> OPEN : Customer reopen<br/>(trong 7 ngày)

    CLOSED --> [*]
    CLOSED_REJECTED --> [*]

    note right of ASSIGNED
        SLA Timer:
        P1 Critical: 4h
        P2 High: 24h
        P3 Normal: 72h
    end note

    note right of WAITING_CUSTOMER
        SLA pause phải có lý do,
        thời điểm pause/resume,
        và người thực hiện.
    end note

    note right of ESCALATED
        Trigger:
        (1) Staff không xử lý nổi
        (2) SLA sắp breach (80%)
        (3) SLA đã breach
        (4) Ticket reopen nhiều lần
    end note

    note right of INCIDENT
        Incident = 1 ticket "parent"
        điều phối nhiều ticket con
        liên quan cùng nguyên nhân.
        Manager link child tickets thủ công.
        Khi parent CLOSED → option
        auto-close toàn bộ child còn mở.
        Actor: Manager declare, Admin aware.
    end note
```

---

## 5. SLA Escalation Flow

3 track priority độc lập. Manager gán P1/P2/P3 khi triage — priority **không thay đổi** trong suốt vòng đời ticket. Breach SLA → escalate thêm *nhân lực*, không phải đổi deadline.

> **Lưu ý:** Mũi tên `E2 → P1` và `E3 → P2` thể hiện *luồng xử lý tiếp theo* được áp chuẩn SLA cao hơn — **không có nghĩa là hệ thống tự động đổi priority**. Ticket vẫn ở trạng thái **ESCALATED**; chính **Manager** mới là người quyết định reassign senior Staff, gia hạn SLA có lý do. System chỉ auto-notify và auto-change status → ESCALATED.

```mermaid
flowchart LR
    Start([Ticket ASSIGNED<br/>SLA timer start])

    Start --> P1{P1 Critical<br/>4 giờ}
    P1 -->|Staff xử lý xong| OK1[✅ RESOLVED]
    P1 -->|80% SLA| W1[⚠️ Warning<br/>notify Staff + Manager]
    W1 --> P1
    P1 -->|Breach 100%| E1[🔺 Auto-escalate<br/>Manager + Senior Staff]

    Start --> P2{P2 High<br/>24 giờ}
    P2 -->|Staff xử lý xong| OK2[✅ RESOLVED]
    P2 -->|80% SLA| W2[⚠️ Warning<br/>notify Staff]
    W2 --> P2
    P2 -->|Breach 100%| E2[🔺 Escalate<br/>Manager reassign senior]

    Start --> P3{P3 Normal<br/>72 giờ}
    P3 -->|Staff xử lý xong| OK3[✅ RESOLVED]
    P3 -->|80% SLA| W3[⚠️ Reminder<br/>notify Staff]
    W3 --> P3
    P3 -->|Breach 100%| E3[🔺 Manager review<br/>priority hoặc workload]

    E1 --> Critical[🚨 Incident flag<br/>notify Admin nếu rủi ro an toàn]
    E2 --> P1
    E3 --> P2
```

---

## 6. Admin — Chi tiết Flow

Admin chỉ hoạt động mạnh ở **Phase 1 (Setup)** và **giám sát định kỳ**. Không tham gia daily operation.

| | |
|---|---|
| **Entry** | Login Web App với tài khoản super-admin |
| **Task chính** | User CRUD · Battery config · Battery asset · SLA rules · Audit log |
| **Tần suất** | Setup 1 lần + cập nhật khi có yêu cầu |
| **Quyền đặc biệt** | Disable account · Reset password · Restore DB · Incident visibility |

```mermaid
flowchart TD
    Start([Admin mở Web App]) --> L1[Nhập email + password]
    L1 --> L2{Credential OK?}
    L2 -->|Sai| L3[Hiện lỗi<br/>+ đếm số lần sai] --> L1
    L2 -->|Đúng| L4{2FA enabled?}
    L4 -->|Có| L5[Nhập OTP]
    L4 -->|Không| Dashboard
    L5 --> L6{OTP OK?}
    L6 -->|Sai| L5
    L6 -->|Đúng| Dashboard[Admin Dashboard]

    Dashboard --> T1[👥 User Management]
    Dashboard --> T2[🔋 Battery Config]
    Dashboard --> T6[🧾 Battery Asset]
    Dashboard --> T3[📋 SLA Rules]
    Dashboard --> T4[📊 Audit Log]
    Dashboard --> T5[⚙️ System Settings]

    T1 --> U1{Hành động}
    U1 -->|Tạo mới| U2[Chọn role<br/>Manager/Staff/Customer]
    U2 --> U3[Điền info<br/>email, SĐT, tên]
    U3 --> U4{Validate email<br/>unique?}
    U4 -->|Trùng| U3
    U4 -->|OK| U5[Generate password<br/>+ gửi email invite]
    U5 --> U6[Log audit event]
    U1 -->|Edit| U7[Sửa info / role]
    U1 -->|Disable| U8[Confirm + revoke token]
    U1 -->|Reset PWD| U9[Gửi link reset]

    T2 --> B1[Thêm loại pin]
    B1 --> B2[Nhập specs<br/>capacity, voltage range]
    B2 --> B3[Set ngưỡng cảnh báo]
    B3 --> B4[V_min, V_max<br/>T_max, SOC_warning]
    B4 --> B5{Customer đang<br/>dùng loại pin này?}
    B5 -->|Có| B6[Warning:<br/>thay đổi sẽ affect N users]
    B6 --> B7[Confirm → save]
    B5 -->|Không| B7

    T6 --> BA1[Create asset<br/>serial + battery type]
    BA1 --> BA2[Link Customer<br/>owner/current user]
    BA2 --> BA3[Set install date<br/>location + warranty]
    BA3 --> BA4[View asset history<br/>alerts/tickets/logs]

    T3 --> SR1[Set response time<br/>per priority]
    SR1 --> SR2[P1: 4h<br/>P2: 24h<br/>P3: 72h]
    SR2 --> SR3[Set escalation rules<br/>auto-escalate khi breach %]
    SR3 --> SR4[Set priority mapping<br/>anomaly type → priority]

    T4 --> AL1[Filter by user/action/date]
    AL1 --> AL2[Xem chi tiết event]
    AL2 --> AL3[Export CSV nếu cần audit]

    T5 --> SY1[Email template]
    T5 --> SY2[Notification config]
    T5 --> SY3[Backup manual trigger]
    T5 --> SY4[Restore DB<br/>⚠️ destructive]
```

---

## 7. Manager — Chi tiết Flow

Manager là "điều phối viên" — hoạt động liên tục trong Phase 4 (Triage) và Phase 6 (Verify). Quyết định priority, staff assignment, và escalation.

| | |
|---|---|
| **Entry** | Login Web App khi có ticket mới hoặc kiểm tra định kỳ |
| **Task chính** | Triage ticket · Assign Staff · Approve resolution · Handle escalation · Review SLA |
| **Tần suất** | Daily — check dashboard mỗi 2–4h |
| **Quyền đặc biệt** | Reassign · Approve/Reject · Escalate P2→P1 · Export report |

```mermaid
flowchart TD
    Start([Manager login]) --> Dash[Manager Dashboard]

    Dash --> KPI[Xem KPI<br/>Open: X · Overdue: Y<br/>SLA breach: Z%]

    KPI --> Action{Có ticket<br/>cần xử lý?}
    Action -->|Không| Idle[Xem report<br/>hoặc chờ notification]
    Action -->|Có| Queue[Mở Ticket Queue]

    Queue --> Filter[Filter<br/>status=OPEN ORDER BY<br/>priority, createdAt]
    Filter --> Pick[Pick ticket đầu tiên]

    Pick --> Review[Đọc mô tả Customer<br/>+ xem battery asset<br/>+ xem alert gốc]

    Review --> Priority{Đánh giá<br/>Priority}
    Priority -->|Critical/Safety| P1[Set P1 Critical<br/>SLA 4h]
    Priority -->|Degradation| P2[Set P2 High<br/>SLA 24h]
    Priority -->|Bất thường nhẹ| P3[Set P3 Normal<br/>SLA 72h]

    P1 --> Assign
    P2 --> Assign
    P3 --> Assign

    Assign[Chọn Staff assign] --> AC0[Xem skill matrix<br/>+ workload hiện tại]
    AC0 --> AC1{Staff còn capacity<br/>và match skill?}
    AC1 -->|Không| AC2[Xem workload<br/>tất cả Staff]
    AC2 --> AC3[Chọn Staff load thấp nhất<br/>hoặc senior phù hợp]
    AC1 -->|Có| AC4[Xem skill match<br/>với loại pin]
    AC3 --> AC5[Confirm assign]
    AC4 --> AC5
    AC5 --> Notify[System: start SLA timer<br/>+ push notify Staff]

    Notify --> Wait{Chờ Staff xử lý}

    Wait -->|Staff mark RESOLVED| Verify[Review Maintenance Log<br/>+ activity timeline]
    Wait -->|SLA warning 80%| Warn[Nhắc Staff<br/>qua comment]
    Wait -->|SLA breach| Escalate
    Wait -->|Reopen nhiều lần| Escalate

    Verify --> Check{Chất lượng xử lý OK?}
    Check -->|OK| Approve[Approve<br/>→ status CLOSED_PENDING_RATE]
    Check -->|Chưa ổn| Reject[Reject<br/>→ gửi lại Staff IN_PROGRESS<br/>kèm note]

    Warn --> Wait
    Reject --> Wait

    Escalate[🔺 Escalation Decision] --> ED1{Staff xử lý nổi<br/>nếu thêm time?}
    ED1 -->|Có| ED2[Gia hạn SLA có lý do<br/>+ warning note]
    ED1 -->|Không| ED3{Priority hiện tại}
    ED3 -->|P3| ED4[Escalate → P2<br/>Reassign Senior Staff]
    ED3 -->|P2| ED5[Escalate → P1<br/>Notify Admin + Lead Staff]
    ED3 -->|P1 breach| ED6[🚨 Critical incident<br/>Notify toàn team]
    ED4 --> Wait
    ED5 --> Wait

    Approve --> End{Customer rate?}
    End -->|Rate good| Closed[CLOSED]
    End -->|Reopen| Queue
    End -->|No action 7 ngày| Closed

    Dash --> Report[📊 Reports]
    Report --> R1[Staff Performance]
    Report --> R2[SLA Compliance]
    Report --> R3[Customer Satisfaction]
    Report --> R4[Export PDF/Excel]
```

---

## 8. Staff — Chi tiết Flow

Staff là người trực tiếp xử lý ticket — hoạt động chính ở Phase 5 (Resolution). Cần document kỹ mỗi bước vì sẽ được Manager review.

| | |
|---|---|
| **Entry** | Login Web App · nhận push notification ticket mới |
| **Task chính** | Diagnose · Liên hệ Customer · Apply solution · Ghi log · Request pause/escalation |
| **Tần suất** | Continuous — xử lý multiple tickets song song |
| **Giới hạn** | Không đổi priority · Không approve · Phải escalate khi vượt skill |

```mermaid
flowchart TD
    Start([Staff login]) --> Dash[Staff Dashboard]
    Dash --> MyT[My Tickets]

    MyT --> N1{Có ticket mới?}
    N1 -->|Không| N2[Xem ticket đang làm]
    N1 -->|Có| Accept[Mở ticket detail]

    Accept --> Read[Đọc mô tả +<br/>xem ảnh từ Customer]
    Read --> Ctx[Xem context<br/>battery asset + history 7d<br/>+ alert gốc]
    Ctx --> Start2[Update status<br/>IN_PROGRESS]

    Start2 --> KB[Tra Knowledge Base<br/>theo issue category]
    KB --> Diag{Đủ info<br/>để chẩn đoán?}

    Diag -->|Thiếu info| Ask[Comment hỏi Customer<br/>ví dụ: thời điểm, hiện tượng cụ thể]
    Ask --> Pause1[Set WAITING_CUSTOMER<br/>+ pause SLA có lý do]
    Pause1 --> WaitR{Customer reply?}
    WaitR -->|Timeout 24h| Remind[Nhắc Customer lần 2]
    WaitR -->|Reply| Resume1[Resume SLA<br/>+ back IN_PROGRESS]
    Resume1 --> Read
    Remind --> WaitR

    Diag -->|Đủ info| Hypo[Đưa giả thuyết nguyên nhân]
    Hypo --> Solve{Có thể xử lý<br/>remote không?}

    Solve -->|Remote OK| Remote[Hướng dẫn Customer<br/>qua comment/video]
    Remote --> Check1{Customer confirm<br/>đã OK?}
    Check1 -->|Có| LogRemote[Ghi Maintenance Log<br/>loại: Remote Support]
    Check1 -->|Không| Hypo

    Solve -->|Cần on-site| OnSite[Lên lịch on-site<br/>+ thông báo Customer]
    OnSite --> Pause2[Set WAITING_ONSITE_SCHEDULE<br/>+ pause SLA nếu hợp lệ]
    Pause2 --> Visit[Tới hiện trường]
    Visit --> Resume2[Resume SLA<br/>khi bắt đầu xử lý]
    Resume2 --> Exec[Thực hiện bảo trì<br/>thay linh kiện nếu cần]
    Exec --> LogOnSite[Ghi Maintenance Log<br/>loại: On-site<br/>+ upload ảnh before/after]

    Solve -->|Không xử lý nổi| Escalate[Request Escalation]
    Escalate --> EscReason[Ghi lý do<br/>- Thiếu skill<br/>- Cần lead support<br/>- Ngoài scope]
    EscReason --> EscSend[Submit → chuyển Manager]
    EscSend --> WaitMgr{Manager reassign?}
    WaitMgr -->|Ticket chuyển đi| BackDash[Back to Dashboard]

    LogRemote --> Resolve
    LogOnSite --> Resolve

    Resolve[Mark RESOLVED<br/>+ summary xử lý] --> WaitApprove{Manager approve?}
    WaitApprove -->|Approve| Done[✅ Ticket CLOSED/PENDING RATE]
    WaitApprove -->|Reject| Read

    Start2 --> SLA[Check SLA countdown<br/>ở banner]
    SLA --> SLAWarn{80% SLA?}
    SLAWarn -->|Có| Urgent[⚠️ Ưu tiên xử lý<br/>notify Manager]
    Urgent --> Diag
    SLAWarn -->|Chưa| Diag
```

---

## 9. Customer — Chi tiết Flow

Customer sử dụng Mobile App — hành vi chia làm 3 loại: **monitoring daily**, **react-to-alert**, và **ticket management**.

| | |
|---|---|
| **Entry** | Mobile App (iOS/Android) · Push notification |
| **Task chính** | Xem pin · Nhận cảnh báo · Tạo/theo dõi ticket · Rate service |
| **Tần suất** | Daily check + khi có alert |
| **Self-service** | Xem history · Export data · Cài đặt notification · Quản lý thiết bị pin |

```mermaid
flowchart TD
    Start([Mở Mobile App]) --> First{Lần đầu?}

    First -->|Có| Reg[Đăng ký<br/>email/SĐT]
    Reg --> OTP[Nhập OTP]
    OTP --> Link[Link thiết bị pin<br/>scan QR / serial]
    Link --> Home

    First -->|Không| Login[Login<br/>email + password]
    Login --> LC{OK?}
    LC -->|Sai| Login
    LC -->|Đúng| Home[Home Dashboard]

    Home --> Asset[Chọn Battery Asset<br/>nếu có nhiều thiết bị]
    Asset --> Mon[📊 Daily Monitoring]
    Mon --> View[Xem real-time<br/>V · I · T · SOC %]
    View --> Health{Trạng thái?}
    Health -->|Normal 🟢| Idle[Idle, đóng app]
    Health -->|Warning 🟡| CheckChart[Xem chart<br/>7d/30d]
    Health -->|Critical 🔴| AlertFlow

    CheckChart --> Decide{Có cần báo?}
    Decide -->|Không| Idle
    Decide -->|Có| CreateT

    Home --> Alert[🔔 Push notification]
    Alert --> AlertFlow[Mở ticket alert<br/>xem lý do + ngưỡng vượt]
    AlertFlow --> AS{Hành động}
    AS -->|Đóng alert| Idle
    AS -->|Tạo ticket| CreateT
    AS -->|Đã xử lý tự| Dismiss[Mark as resolved<br/>by self]

    CreateT[Tạo Ticket] --> CT0[Confirm battery asset<br/>đúng serial/thiết bị]
    CT0 --> CT1[Chọn loại sự cố<br/>Charging · Overheat · No power ...]
    CT1 --> CT2[Mô tả chi tiết]
    CT2 --> CT3[Upload ảnh/video<br/>tối đa 5 files]
    CT3 --> CT4[Xem priority gợi ý<br/>do system tính]
    CT4 --> CT5[Submit]
    CT5 --> CT6[Nhận ticket ID<br/>+ est. response time]

    CT6 --> Track

    Home --> MyT[📋 My Tickets]
    MyT --> Track[Xem tiến trình<br/>OPEN → IN_PROGRESS → RESOLVED]

    Track --> TS{Status hiện tại?}
    TS -->|Staff hỏi| Reply[Trả lời comment<br/>+ ảnh bổ sung]
    TS -->|WAITING_CUSTOMER| Reply
    TS -->|IN_PROGRESS lâu| ChaseCheck{Quá SLA?}
    TS -->|RESOLVED/PENDING RATE| ReviewRes

    Home --> WaitPush[🔔 Push: Staff cần thông tin<br/>từ bạn · ticket đang chờ]
    WaitPush --> OpenTicket[Mở ticket từ notification<br/>xem comment Staff]
    OpenTicket --> Reply

    ChaseCheck -->|Có| Chase[Nhắc qua comment<br/>hoặc liên hệ hotline]
    ChaseCheck -->|Chưa| Track

    Reply --> Track
    Chase --> Track

    ReviewRes[Review kết quả xử lý<br/>đọc maintenance log] --> Verdict{Hài lòng?}
    Verdict -->|Có| Rate[Rate 1-5 sao<br/>+ comment]
    Rate --> Closed[✅ Ticket CLOSED]
    Verdict -->|Không| ReopenCheck{Trong 7 ngày?}
    ReopenCheck -->|Có| Reopen[Reopen<br/>mô tả vấn đề còn]
    ReopenCheck -->|Không| NewTicket[Tạo ticket mới]
    Reopen --> Track
    NewTicket --> CreateT
    Verdict -->|Không action 7d| Auto[System auto-close]

    Home --> Profile[👤 Profile]
    Profile --> P1[Sửa thông tin]
    Profile --> P2[Đổi mật khẩu]
    Profile --> P3[Notification settings<br/>- Push on/off<br/>- Email digest<br/>- Alert threshold cá nhân]
    Profile --> P4[My Battery Assets<br/>serial, warranty, history]
```

---

## 10. Business Rules

| Rule | Mô tả |
|------|-------|
| BR-01 | Ticket phải gắn với Battery Asset (assetId bắt buộc) |
| BR-02 | Alert critical có thể auto-create ticket nếu chưa có ticket đang mở cho cùng asset |
| BR-03 | Dedup alert: cùng asset + anomaly type + time window → append vào ticket hiện tại, không tạo mới |
| BR-04 | SLA pause phải có lý do hợp lệ (WAITING_CUSTOMER / WAITING_PARTS / WAITING_ONSITE_SCHEDULE) và ghi timeline |
| BR-05 | Staff chỉ mark RESOLVED; Manager mới approve → CLOSED_PENDING_RATE |
| BR-06 | Customer chỉ được reopen trong 7 ngày sau resolved/closed pending |
| BR-07 | Reopen ≥ 2 lần → System auto-cảnh báo Manager để review hoặc escalate senior |
| BR-08 | Mọi thay đổi quan trọng (assign, status, pause SLA, approve, reject, reopen) phải lưu actor + timestamp + reason |

### Priority & SLA

| Priority | SLA | Trigger | Breach action |
|----------|-----|---------|---------------|
| P1 Critical | 4h | Rủi ro an toàn, mất điện, nguy cơ hư hại | Manager reassign Senior + notify Admin → Critical Incident nếu vẫn fail |
| P2 High | 24h | Suy giảm hiệu năng rõ rệt, lỗi sạc/xả | Manager reassign Senior nếu cần (priority giữ P2) |
| P3 Normal | 72h | Bất thường nhẹ, câu hỏi vận hành | Manager review + bàn giao nếu cần (ticket không vào ESCALATED) |

> **Priority do Manager gán 1 lần duy nhất** khi triage (OPEN → ASSIGNED). Sau đó **không thay đổi** trong toàn bộ vòng đời ticket. Breach SLA → escalate thêm *nhân lực/cấp bậc*, không phải đổi deadline hay priority. Giữ priority cố định đảm bảo audit trail chính xác và SLA report không bị skew.

### Entity bổ sung nên có trong SRS

| Entity | Mục đích | Trường dữ liệu chính |
|--------|----------|----------------------|
| BatteryAsset | Đại diện một bộ pin cụ thể của Customer | assetId, serialNumber, batteryTypeId, customerId, installDate, warrantyStatus, location |
| Alert | Lưu cảnh báo sinh ra từ dữ liệu pin | alertId, assetId, anomalyType, severity, thresholdValue, actualValue, detectedAt, status |
| TicketActivity | Lưu timeline thao tác trên ticket | activityId, ticketId, actorId, actionType, oldValue, newValue, reason, createdAt |
| SlaTimer | Theo dõi deadline, pause/resume và breach | ticketId, priority, startedAt, dueAt, pausedAt, totalPausedMinutes, breachAt, status |
| KnowledgeBaseArticle | Hỗ trợ Staff xử lý lỗi lặp lại | articleId, category, symptoms, diagnosisSteps, solutionSteps, createdBy, updatedAt |
| CustomerFeedback | Lưu rating và đánh giá sau xử lý | feedbackId, ticketId, rating, comment, createdAt |

---

*Project GSU26SE55 · Core Business Flow · SRS-ready · SE-only scope*
