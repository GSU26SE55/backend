# GSU26SE55 — Backend (ASP.NET Core Web API)

**Dự án:** Solar Lithium-ion Battery Maintenance Management System  
**Nhóm:** GSU26SE55 — GVHD: Trương Long  
**Timeline:** 11/5/2026 → 6/9/2026

---

## Thành viên repo này

| Tên | MSSV | GitHub |
|-----|------|--------|
| Nguyễn Phúc Duy | SE184821 | @DuyNguyen-3006 |
| Bùi Phước Thắng | SE180445 | @Alexdev257 |
| Mai Hồng Thái | SE183923 | @relentless-spirit |

---

## Setup lần đầu (làm 1 lần duy nhất)

### Bước 1 — Yêu cầu

- [Claude Code](https://claude.ai/code) — bắt buộc
- Node.js 18+
- Git 2.30+

### Bước 2 — Clone repo

```bash
git clone https://github.com/GSU26SE55/backend.git
cd backend
```

### Bước 3 — Tạo file CLAUDE.local.md

Tạo file `.claude/CLAUDE.local.md` (file này **không được commit**):

```
---
Role: BE
Tên: [Tên của bạn]
MSSV: [MSSV của bạn]
---
```

### Bước 4 — Tạo file .env

Tạo file `.env` ở root folder (file này **không được commit**):

```
JIRA_BASE_URL=https://fpt-team-d7rg7yak.atlassian.net
JIRA_EMAIL=<email fpt của bạn>
JIRA_API_TOKEN=<token Jira của bạn>
```

**Lấy Jira API Token:**
1. Vào https://id.atlassian.com/manage-profile/security/api-tokens
2. Nhấn **Create API token** → đặt tên → Copy token

### Bước 5 — Setup Jira MCP

```bash
npx -y @rui.branco/jira-mcp setup "<email fpt>" "<API token>" "https://fpt-team-d7rg7yak.atlassian.net"
claude mcp add --transport stdio jira -- npx -y @rui.branco/jira-mcp
```

### Bước 6 — Mở Claude Code

```bash
claude
```

Gõ `/kltn` để xem toàn bộ lệnh → sẵn sàng làm việc.

---

## Luồng làm việc mỗi ticket

```
1. git pull origin main                          ← lấy code mới nhất
2. git checkout -b feature/KAN-XX-ten-ngan       ← tạo branch
3. /kltn-task KAN-XX                             ← đọc ticket, lập plan
4. [review plan] → gõ "ok" để xác nhận
5. code...
6. /kltn-reviewcode                              ← review trước khi test
7. /kltn-test KAN-XX                             ← chạy test
8. /kltn-ship KAN-XX                             ← tạo PR + cập nhật Jira
9. Đồng đội /kltn-reviewpr → approve
10. Leader merge PR vào main
```

---

## Quy tắc bắt buộc

- Không push thẳng lên `main` — luôn qua PR
- Không merge PR của chính mình — cần ≥ 1 người approve
- 1 ticket = 1 branch: `feature/KAN-XX-ten-ngan`
- Commit format: `feat(KAN-XX): mô tả` / `fix` / `refactor` / `test`
- Không commit `.env` và `.claude/CLAUDE.local.md`

---

## Cần hỗ trợ

Liên hệ Leader: **Trần Minh Trí (SE183109)** — @Shu1237
