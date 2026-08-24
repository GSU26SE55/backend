# R4 Backend <-> R3 AI WireGuard and observability runbook

Runbook này chốt kết nối giữa Backend/observability trên R4 và AI/Jenkins trên
R3. Hai máy không còn phụ thuộc vào cùng một private VPC.
Không merge/deploy các gate mới trước khi hoàn tất mục 1-5 vì production
preflight cố ý fail-closed khi tunnel chưa sẵn sàng.

## 1. Contract cuối cùng

| Thành phần | R4 Backend/observability | R3 AI/Jenkins |
|---|---:|---:|
| WireGuard address | `10.20.0.1/32` | `10.20.0.2/32` |
| Stable public endpoint | `139.59.224.185` | `116.118.6.30` |
| WireGuard UDP | `51820` | `51820` |
| Loki bridge | `10.20.0.1:3100` | Alloy push tới bridge |
| AI application metrics | Prometheus scrape | `10.20.0.2:443/metrics/` với SNI `ai.solars.io.vn` |
| Host/container/agent metrics | Prometheus scrape | `10.20.0.2:{9100,8082,12345}` |

Luồng ứng dụng dùng cùng một origin `https://ai.solars.io.vn`:

1. BatteryService/TicketService ưu tiên gRPC HTTP/2 trên port `443`.
2. Khi lỗi gRPC thuộc nhóm cho phép fallback, client gọi HTTPS REST trên cùng origin.
3. Helm thêm host alias `ai.solars.io.vn -> 10.20.0.2` vào đúng hai deployment.
4. TLS vẫn kiểm certificate cho `ai.solars.io.vn`; không gọi HTTPS bằng IP.

Không public `3100`, `9100`, `8082`, `12345`, `8000` hoặc `50051`.

Hai Jenkins SSH account cố ý không có `CAP_NET_ADMIN` và không được cấp `sudo`
chỉ để đọc `wg show`. Production preflight xác nhận peer `/32` được route qua
`wg0`, sau đó dùng các probe HTTPS/gRPC có timeout tới peer để chứng minh tunnel,
TLS/SNI và ứng dụng đang hoạt động. Một request thành công cũng làm mới handshake
nếu tunnel hợp lệ nhưng trước đó nhàn rỗi.

## 2. Xác nhận endpoint trước khi cấu hình

R3 và R4 dùng public endpoint đã giới hạn nguồn cho WireGuard. Xác nhận mỗi IP
được gắn đúng máy trước khi sinh cấu hình:

```bash
# R4
ip -4 -brief address
curl --fail --silent --show-error https://api.ipify.org

# R3
ip -4 -brief address
curl --fail --silent --show-error https://api.ipify.org
```

R4 phải xác nhận `139.59.224.185`; R3 phải xác nhận `116.118.6.30`. Nếu nhà cung
cấp dùng NAT/Reserved IP, đối chiếu thêm metadata/provider console thay vì bỏ
qua kiểm tra. Không dùng private VPC address từ kiến trúc cũ làm endpoint R3.

Trên cả hai VPS:

```bash
sudo apt-get update
sudo apt-get install -y wireguard curl jq
```

Backend production preflight còn cần `grpcurl`. VPS đang dùng `amd64`, cài bản
release đã ghim và xác minh checksum trước khi cài package:

```bash
GRPCURL_VERSION=1.9.3
GRPCURL_DEB="grpcurl_${GRPCURL_VERSION}_linux_amd64.deb"
GRPCURL_BASE="https://github.com/fullstorydev/grpcurl/releases/download/v${GRPCURL_VERSION}"
work_dir="$(mktemp -d)"
cd "$work_dir"
curl --fail --location --remote-name "${GRPCURL_BASE}/${GRPCURL_DEB}"
curl --fail --location --remote-name \
  "${GRPCURL_BASE}/grpcurl_${GRPCURL_VERSION}_checksums.txt"
grep " ${GRPCURL_DEB}$" "grpcurl_${GRPCURL_VERSION}_checksums.txt" | sha256sum --check -
sudo apt-get install -y "./${GRPCURL_DEB}"
grpcurl -version
cd /
rm -rf "$work_dir"
```

## 3. Tạo key mà không lộ private key

Copy `deploy/scripts/configure-ai-wireguard.sh` từ release đã review tới cả hai
VPS. Chỉ public key in ra terminal; không copy hoặc gửi file
`/etc/wireguard/solar-private.key`.

```bash
# Backend VPS
sudo ./configure-ai-wireguard.sh init 10.20.0.1

# AI VPS
sudo ./configure-ai-wireguard.sh init 10.20.0.2
```

Lưu hai dòng `Public key (safe to share)` lần lượt thành
`BACKEND_WG_PUBLIC_KEY` và `AI_WG_PUBLIC_KEY`.

## 4. Firewall

Trong firewall của từng nhà cung cấp, thêm inbound UDP `51820`:

- R4 chỉ từ R3 `116.118.6.30/32`.
- R3 chỉ từ R4 `139.59.224.185/32`.

UFW trên Backend:

```bash
sudo ufw allow in from 116.118.6.30 to any port 51820 proto udp \
  comment 'WireGuard from R3 AI peer'
sudo ufw allow in on wg0 from 10.20.0.2 to 10.20.0.1 port 3100 proto tcp \
  comment 'AI Alloy to Loki bridge'
```

UFW trên AI:

```bash
sudo ufw allow in from 139.59.224.185 to any port 51820 proto udp \
  comment 'WireGuard from R4 Backend peer'
sudo ufw allow in on wg0 from 10.20.0.1 to 10.20.0.2 port 443 proto tcp \
  comment 'Backend to AI HTTPS and gRPC'
for port in 9100 8082 12345; do
  sudo ufw allow in on wg0 from 10.20.0.1 to 10.20.0.2 port "$port" proto tcp \
    comment 'Backend Prometheus to AI'
done
```

Không thêm rule public cho các port monitoring. Docker Compose bind chúng trực
tiếp vào `10.20.0.2`, và Kubernetes NetworkPolicy chỉ cho Prometheus egress tới
địa chỉ đó.

## 5. Ghép peer và kiểm handshake

Thay đúng public key, không để nguyên dấu `<...>`:

```bash
# Backend VPS
sudo ./configure-ai-wireguard.sh configure \
  10.20.0.1 "$AI_WG_PUBLIC_KEY" 116.118.6.30:51820

# AI VPS
sudo ./configure-ai-wireguard.sh configure \
  10.20.0.2 "$BACKEND_WG_PUBLIC_KEY" 139.59.224.185:51820
```

Kiểm tra hai chiều:

```bash
sudo wg show wg0
ip route get 10.20.0.1
ip route get 10.20.0.2
ping -c 3 10.20.0.1
ping -c 3 10.20.0.2
```

Mỗi host chỉ cần ping địa chỉ peer. `latest handshake` phải mới, route peer phải
đi qua `wg0`, transfer counters phải tăng. Unit phải tự khởi động lại sau reboot:

```bash
sudo systemctl is-enabled wg-quick@wg0.service
sudo systemctl is-active wg-quick@wg0.service
```

## 6. Bật Loki bridge trên Backend

Backend:

```bash
sudo install -o root -g root -m 0644 \
  deploy/systemd/solar-loki-wireguard.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now solar-loki-wireguard.service
curl --fail --silent --show-error http://10.20.0.1:3100/ready
sudo ss -lntp | grep '10.20.0.1:3100'
```

Service chỉ bind vào WireGuard address và forward đến Loki ClusterIP qua
`kubectl port-forward`. Không đổi thành `0.0.0.0:3100`.

## 7. Cập nhật env và deploy đúng thứ tự

AI `/opt/solar-ai/config/host.env` phải có:

```dotenv
AI_MONITORING_BIND_IP=10.20.0.2
PLATFORM_WIREGUARD_IPV4=10.20.0.1
LOKI_PUSH_URL=http://10.20.0.1:3100/loki/api/v1/push
```

Backend `/opt/solar-platform/config/host.env` phải có:

```dotenv
AI_GRPC_ADDRESS=https://ai.solars.io.vn
AI_HTTP_BASE_URL=https://ai.solars.io.vn
PLATFORM_WIREGUARD_IPV4=10.20.0.1
AI_WIREGUARD_IPV4=10.20.0.2
```

Thứ tự bắt buộc:

1. Tunnel hai chiều và Loki bridge xanh.
2. Deploy AI. Bản này bind node-exporter, cAdvisor và Alloy vào `10.20.0.2`, đồng
   thời chặn `/metrics` từ Internet.
3. Chạy `/opt/solar-ai/current/deploy/scripts/verify-observability.sh` trên AI.
4. Deploy backend. Helm tạo bốn `ScrapeConfig`; deploy gate đợi đủ `4/4` target UP.
5. Chạy các acceptance checks dưới đây.

## 8. Acceptance checks

### HTTPS và gRPC thật sự qua WireGuard

Trên Backend, ép socket tới peer nhưng giữ hostname TLS:

```bash
curl --fail --silent --show-error --connect-timeout 5 --max-time 10 \
  --resolve ai.solars.io.vn:443:10.20.0.2 \
  https://ai.solars.io.vn/ready | jq -e '.ready == true'

grpcurl -authority ai.solars.io.vn \
  -import-path /opt/solar-platform/current/deploy/contracts \
  -proto grpc_health_v1.proto \
  -d '{"service":"aimodule.v1.AiService"}' \
  10.20.0.2:443 grpc.health.v1.Health/Check

grpcurl -authority ai.solars.io.vn \
  -import-path /opt/solar-platform/current/deploy/contracts \
  -proto ai_health_v1.proto -d '{}' \
  10.20.0.2:443 aimodule.v1.AiService/Health
```

Standard gRPC phải trả `SERVING`; application health phải trả `status=ok` cùng
các model/scaler production đã load. Kiểm tra host alias của hai workload:

```bash
KUBECONFIG=/home/deploy/.kube/config
sudo -u deploy -H env KUBECONFIG="$KUBECONFIG" \
  kubectl -n solar-prod get deployment batteryservice ticketservice \
  -o jsonpath='{range .items[*]}{.metadata.name}{" => "}{.spec.template.spec.hostAliases}{"\n"}{end}'
```

Cả hai phải có `10.20.0.2` và `ai.solars.io.vn`.

### Metrics và logs tập trung

Backend:

```bash
KUBECONFIG=/home/deploy/.kube/config
sudo -u deploy -H env KUBECONFIG="$KUBECONFIG" \
  kubectl -n solar-prod get scrapeconfig \
  -l app.kubernetes.io/source=ai-vps

for port in 9100 8082 12345; do
  curl --fail --silent --show-error "http://10.20.0.2:${port}/metrics" >/dev/null
done

curl --fail --silent --show-error \
  --resolve ai.solars.io.vn:443:10.20.0.2 \
  https://ai.solars.io.vn/metrics/ >/dev/null
```

AI:

```bash
/opt/solar-ai/current/deploy/scripts/verify-observability.sh
```

Helper gửi một marker qua host Caddy, đợi Alloy đẩy access log của AI container
và query lại từ Loki trên Backend. Thành công chứng minh đường log end-to-end,
không chỉ chứng minh port Loki đang mở.

Từ Internet, `https://ai.solars.io.vn/metrics/` phải trả `403`; `/live` và
`/ready` vẫn trả `200`.

## 9. Observability còn lại sau milestone này

Milestone này hoàn thành metrics tập trung, log tập trung, dashboard datasource
và alert nền cho bốn AI targets, gRPC error rate và HTTP 5xx. Các phần sau là
hardening tiếp theo, không phải blocker của kết nối gRPC/HTTPS:

1. OpenTelemetry tracing từ backend và AI qua OTLP tới Tempo, kèm propagation
   `traceparent` xuyên gRPC/HTTP.
2. Structured log có `trace_id`, `request_id`, `ticket_id` để nhảy từ Grafana
   metric/trace sang Loki log.
3. Dashboard AI riêng: latency p50/p95/p99, gRPC/HTTP error, model/RAG/cache,
   CPU/RAM/disk và restart/OOM.
4. Alert routing + test notification thật, runbook link và owner cho từng alert.
5. SLO/error budget, retention/capacity, backup Grafana configuration và diễn
   tập mất tunnel/reboot hai VPS.
