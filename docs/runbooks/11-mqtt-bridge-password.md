# Runbook — mật khẩu `backend-bridge` của MQTT broker (production)

> IOT3-83 · Áp dụng cho môi trường **Production**. Môi trường dev cứ dùng
> `iot/infra/mqtt/mosquitto/bootstrap.sh` như cũ.

`backend-bridge` là tài khoản MQTT mà `MqttBridgeBackgroundService` (BatteryService) dùng để nối
vào broker. Nó có ACL `readwrite solar/#` — tức là **đọc và ghi được telemetry của mọi thiết bị**.
Lộ mật khẩu này nghiêm trọng hơn lộ credential của một thiết bị đơn lẻ nhiều lần.

---

## 1. Vì sao KHÔNG dùng `bootstrap.sh` ở production

`iot/infra/mqtt/mosquitto/bootstrap.sh` được viết cho bàn làm việc, và có hai điểm không hợp
với máy chủ thật:

| Việc script làm | Hệ quả ở production |
|---|---|
| `echo` mật khẩu ra **stdout** | Mật khẩu nằm lại trong lịch sử shell, log CI, và bộ đệm cuộn của terminal. Không thu hồi được. |
| `chmod 0644` file `passwd` | Mọi tiến trình trên máy đọc được hash của toàn bộ thiết bị. |

0644 ở bản dev là **có chủ ý** (backend chạy bằng root ghi vào, broker chạy uid 1883 đọc ra —
xem `bootstrap.sh`), nên đừng "sửa" nó ở dev. Production thì dựng file theo cách khác.

---

## 2. Sinh mật khẩu — làm ở MÁY CỦA BẠN, không phải trên server

```bash
# Máy cá nhân, KHÔNG phải server.
# `head -c 32` từ /dev/urandom → base64 → bỏ ký tự dễ nhầm khi đọc lại bằng mắt.
BRIDGE_PASS="$(LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 40)"

# Ghi thẳng vào trình quản lý bí mật của nhóm (1Password/Bitwarden/Vault…).
# KHÔNG `echo "$BRIDGE_PASS"`.
```

Sinh ngoài server để mật khẩu **không bao giờ xuất hiện** trong lịch sử shell của máy chủ, và
để nó có mặt ở trình quản lý bí mật *trước* khi được đưa vào dùng — không có bước đó thì lần
xoay tới không ai biết mật khẩu hiện tại là gì.

---

## 3. Nạp vào broker

Hash bằng chính image mosquitto để không phụ thuộc `mosquitto_passwd` cài trên máy:

```bash
# Sinh MỘT dòng passwd, đọc mật khẩu từ biến môi trường, không truyền qua tham số dòng lệnh
# (tham số dòng lệnh hiện trong `ps aux` của mọi người dùng trên máy).
printf '%s\n%s\n' "$BRIDGE_PASS" "$BRIDGE_PASS" | \
  docker run --rm -i eclipse-mosquitto:2.0 \
    sh -c 'mosquitto_passwd -c /tmp/pw backend-bridge >/dev/null 2>&1 && cat /tmp/pw'
```

Chép dòng kết quả (`backend-bridge:$7$...`) vào `/opt/solar/iot/infra/mqtt/mosquitto/passwd`,
**ở NGOÀI vùng có mốc**:

```
backend-bridge:$7$101$....$....

# >>> BatteryService managed devices (GH-784) — KHÔNG sửa tay
# <<< BatteryService managed devices
```

> ⚠️ Dòng `backend-bridge` **phải** nằm ngoài cặp mốc. `MosquittoPasswordFile.Compose()` dựng lại
> toàn bộ phần **trong** mốc mỗi lần đồng bộ thiết bị; để nhầm vào trong là lần cấp thiết bị kế
> tiếp sẽ xoá mất tài khoản cầu nối, và **toàn bộ telemetry MQTT chết** — không phải một thiết bị.
> Phần ngoài mốc được giữ nguyên từng ký tự, nên để đúng chỗ thì không cần lo gì thêm.

Quyền file:

```bash
sudo chown 1883:1883 /opt/solar/iot/infra/mqtt/mosquitto/passwd
sudo chmod 0640      /opt/solar/iot/infra/mqtt/mosquitto/passwd
```

0640 ở production được, khác dev: `docker-compose.prod.yml` chạy broker bằng `user: "1883:1883"`
và BatteryService ghi qua mount `rw` cùng uid.

---

## 4. Nạp vào backend

**Máy đơn (docker compose):** `/opt/solar/.env.prod`, mode 600.

```bash
sudo touch /opt/solar/.env.prod
sudo chmod 600 /opt/solar/.env.prod
sudo chown root:root /opt/solar/.env.prod
# Mở bằng editor và dán vào — KHÔNG `echo >>`, tránh lưu vào lịch sử shell:
#   Mqtt__Username=backend-bridge
#   Mqtt__Password=<mật khẩu vừa sinh>
sudo -e /opt/solar/.env.prod
```

**Kubernetes:** thêm vào Secret `solar-secrets`, đọc từ stdin để không lọt vào lịch sử:

```bash
kubectl create secret generic solar-secrets \
  --from-literal=Mqtt__Username=backend-bridge \
  --from-file=Mqtt__Password=/dev/stdin \
  --dry-run=client -o yaml | kubectl apply -f -
# (dán mật khẩu rồi Ctrl-D)
```

---

## 5. Xoay định kỳ — 90 ngày

Broker chỉ đọc lại `passwd` khi nhận **SIGHUP**; vòng `passwd-watch` trong
`iot/infra/docker-compose.prod.yml` tự bắn tín hiệu đó trong vòng 5 giây sau khi file đổi.

Thứ tự có ý nghĩa — làm ngược lại là cầu nối tự khoá mình ra ngoài:

1. Sinh mật khẩu mới (mục 2).
2. Cập nhật `/opt/solar/.env.prod` **hoặc** Secret.
3. Khởi động lại BatteryService: `docker compose -f docker-compose.prod.yml up -d batteryservice`.
   Nó sẽ nối bằng mật khẩu MỚI và **thất bại** — đúng như dự kiến, đây là cửa sổ vài giây.
4. Cập nhật dòng `backend-bridge` trong `passwd`.
5. Trong ≤ 5 giây, `passwd-watch` gửi SIGHUP, broker nạp lại, cầu nối tự nối được.

Đổi ngược thứ tự (passwd trước, backend sau) cũng ra một cửa sổ tương tự — nhưng khi đó
BatteryService còn đang chạy với mật khẩu cũ, sẽ quay vòng reconnect và đổ log lỗi trong suốt
thời gian bạn còn đang sửa file `.env.prod`. Cách trên gói cửa sổ lỗi vào đúng bước 3–5.

**Kiểm tra sau khi xoay:**

```bash
docker logs solar-batteryservice --since 2m | grep -i "mqtt"
# Mong đợi: "MQTT bridge connected". Nếu thấy "Not authorized" thì bước 4 chưa tới broker.

docker exec solar-mosquitto \
  mosquitto_pub -h 127.0.0.1 -p 1883 -u backend-bridge -P "$BRIDGE_PASS" \
  -t solar/healthcheck -m ok -q 1 && echo "✔ credential mới dùng được"
```

---

## 6. Khi nghi bị lộ

1. Xoay ngay theo mục 5 — **không** chờ tới kỳ 90 ngày.
2. `docker logs solar-mosquitto` tìm client id lạ đã đăng nhập bằng `backend-bridge`.
3. Vì ACL của tài khoản này là `readwrite solar/#`, phải coi như **mọi lệnh downlink** trong
   khoảng thời gian nghi ngờ đều có thể do người khác gửi: đối chiếu `solar/+/cmd` trong log
   broker với `IotDeviceCommand` trong DB, chênh lệch nào cũng phải điều tra.

---

## Liên quan

- `iot/infra/docker-compose.prod.yml` — IOT3-81, cấu hình broker production
- `iot/infra/mqtt/scripts/gen-certs.sh` — IOT3-82, cert TLS + CA nhúng trong firmware
- `services/BatteryService/src/BatteryService.Application/Mqtt/MosquittoPasswordFile.cs` — vùng có mốc
- `services/BatteryService/src/BatteryService.Infrastructure/Mqtt/MqttPasswordFileSyncService.cs` — đồng bộ
