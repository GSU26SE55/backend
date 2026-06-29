# ClamAV Virus Scan Setup Guide

Hướng dẫn deploy ClamAV REST cho tính năng **#COMMENT-14 — VirusScanWorker**.

---

## Tổng quan

`VirusScanWorker` background service poll `TicketAttachment` với `VirusScanStatus=Pending` mỗi 30 giây, download file từ FileStorageService, và gửi lên ClamAV REST để scan. Kết quả cập nhật vào `VirusScanStatus` (Clean / Infected / Failed).

Tính năng **mặc định disabled** (`Chat:Features:EnableVirusScan=false`) — không cần ClamAV chạy để các service khác hoạt động bình thường.

---

## Deploy ClamAV REST (Docker)

Dùng image `clamav/clamav` + wrapper REST:

```yaml
# docker-compose.override.yml
services:
  clamav:
    image: clamav/clamav:stable
    ports:
      - "3310:3310"
    volumes:
      - clamav_data:/var/lib/clamav
    environment:
      - CLAMAV_NO_FRESHCLAMD=false

  clamav-rest:
    image: benzino77/clamrest:latest
    ports:
      - "3000:3000"
    environment:
      - PORT=3000
      - CLAMD_IP=clamav
      - CLAMD_PORT=3310
    depends_on:
      - clamav

volumes:
  clamav_data:
```

Chạy:
```bash
docker compose -f docker-compose.yml -f docker-compose.override.yml up -d clamav clamav-rest
```

---

## Cấu hình TicketService

Thêm vào `appsettings.Development.json` (không commit key thật):

```json
{
  "Chat": {
    "Features": {
      "EnableVirusScan": true
    },
    "VirusScan": {
      "Endpoint": "http://localhost:3000",
      "TimeoutSeconds": 60,
      "BatchSize": 20,
      "IntervalSeconds": 30,
      "FileStorageBaseUrl": "http://localhost:5002"
    }
  }
}
```

**Production** — set qua environment variables:
```
Chat__Features__EnableVirusScan=true
Chat__VirusScan__Endpoint=http://clamav-rest:3000
Chat__VirusScan__FileStorageBaseUrl=http://file-storage-service:80
```

---

## API ClamAV REST

`VirusScanWorker` gọi `POST /scan` với multipart form-data:

```
POST http://clamav-rest:3000/scan
Content-Type: multipart/form-data

file: <binary content>
```

**Response:**
| Body | Ý nghĩa | VirusScanStatus |
|------|---------|-----------------|
| `"Everything OK"` / chứa `"OK"` | File sạch | `Clean` |
| Chứa `"FOUND"` | Phát hiện virus | `Infected` |
| Lỗi / timeout | Scan thất bại | `Failed` |

---

## Download Endpoint

`GET /api/tickets/{ticketId}/chats/{chatId}/attachments/{attachmentId}/download`

| VirusScanStatus | HTTP | Hành động |
|-----------------|------|-----------|
| `Clean` | 200 | Trả download URL từ FileStorageService |
| `Pending` / `Failed` | 202 | "Scan in progress, retry shortly" |
| `Infected` | 451 | "File is infected and cannot be downloaded" |

---

## Troubleshooting

**Worker không scan?**
- Kiểm tra `Chat:Features:EnableVirusScan=true` trong config
- Xem logs: `VirusScanWorker starting. EnableVirusScan=False` → chưa enable

**File luôn trả `Failed`?**
- ClamAV chưa khởi động xong (freshclam cần 5–10 phút lần đầu để tải signature DB)
- Kiểm tra: `docker logs clamav` — chờ `ClamAV daemons started`

**Test thủ công:**
```bash
# Upload file lên ClamAV REST
curl -F "file=@test.pdf" http://localhost:3000/scan

# Test với EICAR test virus
echo 'X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*' > eicar.txt
curl -F "file=@eicar.txt" http://localhost:3000/scan
# Expected: chứa "FOUND"
```
