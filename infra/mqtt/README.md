# `infra/mqtt` — Mosquitto cho stack tích hợp (Sprint IoT-1 `#253`)

Broker MQTT cho `docker-compose.yml` ở repo backend. `batteryservice` nối tới đây qua
`Mqtt__Host=mosquitto` trên network `solar-net`.

## Vì sao có bản sao ở cả hai repo

| Nơi | Dùng khi nào | Ai sửa |
|---|---|---|
| `backend/infra/mqtt/` (thư mục này) | **Stack tích hợp** — chạy cùng Postgres/Redis/RabbitMQ/các service | BE |
| `iot/infra/mqtt/` | **Bench độc lập** — firmware dev đo latency/ACL mà không cần dựng cả hệ thống | IoT/firmware |

`acl.conf` phải khớp `MqttTopicMap` trong `BatteryService.Infrastructure/Mqtt/MqttTopicMap.cs`.
Đây là chỗ dễ trôi nhất, nên có test chặn: `BatteryService.IntegrationTests/Mqtt/` dựng broker thật
bằng Testcontainers với **chính file `acl.conf` này** rồi kiểm quyền publish. Sửa `MqttTopicMap` mà
quên sửa `acl.conf` (hoặc ngược lại) là test đỏ ngay.

## Chạy

```bash
# 1. Sinh passwd cho user backend-bridge (1 lần). In ra plaintext — paste vào .env.Docker.
./infra/mqtt/mosquitto/bootstrap.sh

# 2. (tuỳ chọn) Bật TLS 8883 — sinh CA + server cert VÀ conf.d/tls.conf cùng lúc.
./infra/mqtt/scripts/gen-certs.sh mosquitto

# 3. Bật broker — nằm sau compose profile `mqtt` nên KHÔNG chạy mặc định.
docker compose --profile mqtt up -d mosquitto

# 4. Bật bridge phía backend
#    .env.Docker: Mqtt__Enabled=true, Mqtt__Password=<plaintext ở bước 1>
docker compose up -d batteryservice
```

## TLS bật/tắt thế nào

`mosquitto.conf` **không** khai `listener 8883`. Listener TLS nằm ở `config/conf.d/tls.conf`, do
`gen-certs.sh` sinh ra **cùng lúc với cert**:

- Chưa chạy `gen-certs.sh` → không có `conf.d/*.conf` → broker chạy bình thường với 1883.
- Đã chạy → có cert + có conf → `docker compose restart mosquitto` là có 8883.

Thiết kế này thay cho cách cũ "uncomment tay block listener 8883": cách cũ vừa dễ quên (TLS không
bao giờ bật, cổng 8883 mở nhưng không ai nghe), vừa dễ làm **broker fail to start** nếu bỏ comment
khi chưa có cert.

⚠️ **Đã kiểm chứng bằng broker thật 31/07/2026:** trong Mosquitto 2.0, `password_file` và `acl_file`
là option **toàn cục** — khai lại trong `conf.d` sẽ làm broker chết với
`Error: Duplicate password_file value in configuration.` Ngược lại `allow_anonymous` là
**per-listener**, nên `tls.conf` bắt buộc phải khai nó, nếu không cổng 8883 cho vào tự do trong khi
1883 vẫn siết.

## Không commit

`passwd`, `certs/*` và `config/conf.d/*.conf` đều nằm trong `.gitignore` — chúng là secret/artifact
cục bộ, sinh lại bằng 2 script ở trên.
