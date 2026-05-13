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
- [GitHub CLI](https://cli.github.com/) — bắt buộc

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

### Bước 4 — Xác thực GitHub CLI

```bash
gh auth login
```

Chọn **GitHub.com** → **HTTPS** → xác thực qua browser.

### Bước 5 — Mở Claude Code

```bash
claude
```

Gõ `/kltn` để xem toàn bộ lệnh → sẵn sàng làm việc.

---

## Luồng làm việc mỗi issue

```
1. git pull origin main                              ← lấy code mới nhất
2. git checkout -b feature/GH-[number]-ten-ngan      ← tạo branch
3. /kltn-implement [number]                          ← đọc issue, lập plan
4. [review plan] → gõ "ok" để xác nhận
5. code...
6. /kltn-reviewcode                                  ← review trước khi test
7. /kltn-test [number]                               ← chạy test (dotnet test)
8. /kltn-ship [number]                               ← tạo PR + cập nhật issue
9. Đồng đội /kltn-reviewpr → approve
10. /kltn-complete [number]                          ← merge PR + close issue
```

---

## Quy tắc bắt buộc

- Không push thẳng lên `main` — luôn qua PR
- Không merge PR của chính mình — cần ≥ 1 người approve
- 1 issue = 1 branch: `feature/GH-[number]-ten-ngan`
- Commit format: `feat(#[number]): mô tả` / `fix` / `refactor` / `test`
- Không commit `.env` và `.claude/CLAUDE.local.md`

---

## Cần hỗ trợ

Liên hệ Leader: **Trần Minh Trí (SE183109)** — @Shu1237
