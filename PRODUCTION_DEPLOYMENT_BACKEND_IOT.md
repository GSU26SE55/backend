# Production runbook: Backend (K3s) + IoT (Docker) trên cùng VPS

Tài liệu này là nguồn hướng dẫn chính thức cho kiến trúc production hiện tại:

- VPS Jenkins chỉ chạy CI/CD.
- VPS Platform chạy toàn bộ backend và hạ tầng backend bằng K3s, đồng thời chạy Mosquitto của IoT bằng Docker Compose.
- VPS AI chạy `ai-module` độc lập; endpoint đã dùng là `https://ai.solars.io.vn` cho HTTPS và gRPC HTTP/2 trên cổng 443.
- Push hoặc merge vào `main` của từng repository sẽ chạy CI. Chỉ pipeline production trung tâm, được lưu cố định trong Jenkins, mới được phép ký image và SSH sang VPS production.

Không đưa mật khẩu, PAT, private key, kubeconfig hoặc file `.env` thật vào Git.

## 1. Thông tin người vận hành còn phải cung cấp

Trước khi setup VPS, phải có đủ:

1. Reserved/Public IPv4 của VPS Platform mới.
2. VPC/private IPv4 của chính VPS đó.
3. Email vận hành dùng cho Let's Encrypt ACME.
4. Public key SSH quản trị và một key SSH riêng cho Jenkins deploy.
5. Các secret liệt kê tại mục 7.
6. GitHub PAT đọc source và PAT đọc/ghi GHCR. Nếu organization bắt buộc SSO thì phải authorize PAT cho organization.

Không gửi private key hoặc secret vào chat/log. Nhập chúng trực tiếp vào VPS hoặc Jenkins Credentials.

## 2. Kiến trúc và số VPS

| VPS | Vai trò | Runtime | Cấu hình nên dùng |
|---|---|---|---|
| Jenkins | Build, test, scan, SBOM, ký image, điều phối deploy | Jenkins + Docker | 4 vCPU, 8 GB RAM, 120-160 GB SSD, 4 GB swap |
| Platform | 9 backend service, data infra, monitoring và MQTT | K3s + Docker | Khuyến nghị 8 vCPU, 16 GB RAM, 160 GB SSD; 4 vCPU/8 GB chỉ phù hợp demo/tải thấp và phải theo dõi pressure |
| AI | AI HTTP/gRPC và telemetry agent | Docker Compose | Máy hiện có, domain `ai.solars.io.vn` |

Một node K3s không phải HA: hỏng VPS là toàn bộ backend ngừng. Vì vậy backup ngoài node và snapshot Droplet là bắt buộc nếu đây là production thật.

## 3. Thành phần được triển khai

### Backend application trong K3s

1. ApiGateway
2. AuthService
3. EmailService
4. SmsService
5. FileStorageService
6. BatteryService, gồm HTTP và gRPC nội bộ
7. TicketService
8. NotificationService
9. AuditAggregatorService

Mỗi image production được build lại từ đúng commit `main`, scan bằng Trivy, tạo SBOM CycloneDX, push GHCR, ký Cosign và deploy bằng digest `sha256`, không deploy tag trôi nổi.

### Hạ tầng backend trong K3s

- PostgreSQL dùng chung, có 7 database logic và backup CronJob.
- Redis StatefulSet bật AOF.
- RabbitMQ với virtual host `/solar-prod`; AMQP và management chỉ là ClusterIP.
- MinIO private bucket, API public qua `files.solars.io.vn`; console không public.
- cert-manager + ClusterIssuer Let's Encrypt.
- Traefik ingress của K3s.
- PVC local-path cho database, object storage, firmware, Data Protection key, Tempo, Prometheus, Grafana và Loki.

### Monitoring trong K3s

- Prometheus, Alertmanager, Grafana.
- node-exporter, kube-state-metrics, cAdvisor/Kubelet metrics.
- PostgreSQL exporter, Redis exporter, RabbitMQ metrics và MinIO health/metrics contract.
- Loki + Promtail.
- Tempo và OpenTelemetry.
- Blackbox exporter + Probe cho public endpoints.
- ServiceMonitor, PrometheusRule, dashboards và Discord alert relay.
- Loki được port-forward riêng lên địa chỉ WireGuard `10.20.0.1:3100` để Alloy trên VPS AI đẩy log tới; không public Loki ra Internet.

### IoT trong Docker

- Mosquitto TLS trên cổng 8883.
- Không publish cổng plaintext 1883.
- Password file, ACL, data và log được lưu ngoài container.
- TLS do cert-manager trong K3s cấp cho `mqtt.solars.io.vn`, rồi systemd timer đồng bộ sang Docker.
- Firmware CI build tất cả environment quan trọng và archive `.bin` cùng SHA256SUMS.

## 4. DNS và port public

Tạo bốn A record cùng trỏ đến Reserved IP của VPS Platform:

| Record | Mục đích |
|---|---|
| `api.solars.io.vn` | HTTPS API gateway |
| `files.solars.io.vn` | HTTPS MinIO/S3 file endpoint |
| `mqtt.solars.io.vn` | MQTT TLS 8883 |
| `grafana.solars.io.vn` | Grafana HTTPS |

Không tạo domain public cho PostgreSQL, Redis, RabbitMQ, RabbitMQ management, MinIO console, Prometheus, Alertmanager, Loki hoặc Tempo. Khi cần vận hành, dùng SSH + `kubectl port-forward`.

Firewall VPS Platform:

- TCP 22: chỉ IP quản trị và IP Jenkins; ít nhất dùng `ufw limit` và SSH key-only.
- TCP 80: public, dùng ACME HTTP-01 và redirect HTTPS.
- TCP 443: public, API/files/Grafana.
- TCP 8883: public, MQTT TLS.
- UDP 51820: chỉ IP peer WireGuard của VPS AI.
- Không mở 5432, 6379, 5672, 15672, 9000, 9001, 9090, 3100, 3200 hoặc 4317.

DigitalOcean Cloud Firewall và UFW phải cùng cho phép các port trên; thiếu một trong hai vẫn không truy cập được.

## 5. Chuẩn bị hệ điều hành VPS Platform

Thực hiện bằng tài khoản quản trị có `sudo`, không thực hiện qua Jenkins:

1. Cập nhật security packages và cài `ca-certificates`, `curl`, `git`, `jq`, `openssl`, `dnsutils`, `ufw`, `wireguard`, `tar`.
2. Cài Docker Engine, Compose plugin và Buildx từ repository chính thức; xác nhận `docker run --rm hello-world`.
3. Cài K3s với Traefik và local-path provisioner. Ghim một phiên bản K3s đã kiểm thử; không dùng `latest` ngầm trong production.
4. Cài Helm, `kubectl` và Cosign bản đã ghim/checksum.
5. Cài cert-manager bản đã ghim; chờ toàn bộ deployment trong namespace `cert-manager` Available.
6. Tạo swap 4 GB nếu VPS chỉ có 8-16 GB RAM; đặt `vm.swappiness` thấp, không xem swap là RAM thay thế.
7. Bật đồng bộ giờ và kiểm tra disk còn tối thiểu 30 GiB trước mỗi deploy.

Tạo runtime identity:

```bash
sudo groupadd --gid 10001 solar-runtime
sudo useradd --create-home --shell /bin/bash deploy
sudo usermod --append --groups docker,solar-runtime deploy
sudo passwd --lock deploy
```

Membership nhóm `docker` tương đương quyền root trên host. Chỉ cấp cho user deploy chuyên dụng, không dùng chung tài khoản cá nhân.

Tạo thư mục:

```bash
sudo install -d -o deploy -g solar-runtime -m 2770 \
  /opt/solar-platform \
  /opt/solar-platform/config \
  /opt/solar-platform/secrets \
  /opt/solar-platform/incoming \
  /opt/solar-platform/releases \
  /opt/solar-iot \
  /opt/solar-iot/config \
  /opt/solar-iot/secrets \
  /opt/solar-iot/incoming \
  /opt/solar-iot/releases \
  /opt/solar-iot/secrets/mosquitto/auth \
  /opt/solar-iot/secrets/mosquitto/tls \
  /opt/solar-iot/data/mosquitto/data \
  /opt/solar-iot/data/mosquitto/log
```

Tạo kubeconfig đọc được bởi `deploy`:

```bash
sudo install -d -o deploy -g deploy -m 0700 /home/deploy/.kube
sudo install -o deploy -g deploy -m 0600 \
  /etc/rancher/k3s/k3s.yaml /home/deploy/.kube/config
sudo -u deploy -H env KUBECONFIG=/home/deploy/.kube/config kubectl get node
```

Không sửa quyền của `/etc/rancher/k3s/k3s.yaml`; systemd root vẫn dùng file đó.

## 6. Bootstrap cluster một lần

Từ checkout backend tin cậy trên máy quản trị:

```bash
export KUBECONFIG=/home/deploy/.kube/config
kubectl apply -f deploy/k8s/00-namespaces.yaml

sed 's/__ACME_EMAIL__/EMAIL_VAN_HANH_THAT/g' \
  deploy/k8s/01-cert-manager-issuer.yaml | kubectl apply -f -

kubectl wait --for=condition=Ready \
  clusterissuer/letsencrypt-prod --timeout=2m
```

Tạo GHCR pull secret trong namespace production. PAT này chỉ cần `read:packages` và quyền đọc package private:

```bash
kubectl -n solar-prod create secret docker-registry ghcr-pull \
  --docker-server=ghcr.io \
  --docker-username='GITHUB_USERNAME' \
  --docker-password='GHCR_READ_PAT'
```

Đăng nhập GHCR cho user `deploy`, vì script IoT phải pull image Docker đã ký:

```bash
printf '%s' "$GHCR_READ_PAT" | sudo -u deploy -H docker login ghcr.io \
  --username "$GITHUB_USERNAME" --password-stdin
unset GHCR_READ_PAT GITHUB_USERNAME
```

## 7. File cấu hình và secret trên VPS

Backend templates:

- `deploy/production/host.env.example` -> `/opt/solar-platform/config/host.env`
- `deploy/production/backend.env.example` -> `/opt/solar-platform/secrets/backend.env`
- `deploy/production/monitoring.env.example` -> `/opt/solar-platform/secrets/monitoring.env`
- Cosign public key -> `/opt/solar-platform/config/cosign.pub`

Ba URL bắt buộc trong `/opt/solar-platform/config/host.env`:

```text
FRONTEND_PUBLIC_ORIGIN=https://solars.io.vn
AI_GRPC_ADDRESS=https://ai.solars.io.vn
AI_HTTP_BASE_URL=https://ai.solars.io.vn
```

Các giá trị trên là **origin**, không có dấu `/` cuối và không có path. Tuyệt đối không đặt
`AI_HTTP_BASE_URL=https://ai.solars.io.vn/docs`: `/docs` chỉ là Swagger UI. Backend gọi REST
fallback qua các path ứng dụng như `/ready`, `/predict/` và gọi gRPC HTTP/2 trên cùng origin 443.

IoT templates:

- `deploy/production/host.env.example` của repo IoT -> `/opt/solar-iot/config/host.env`
- `deploy/production/runtime.env.example` -> `/opt/solar-iot/secrets/runtime.env`

Quyền file:

```bash
sudo chown deploy:solar-runtime \
  /opt/solar-platform/config/host.env \
  /opt/solar-platform/config/cosign.pub \
  /opt/solar-platform/secrets/backend.env \
  /opt/solar-platform/secrets/monitoring.env \
  /opt/solar-iot/config/host.env \
  /opt/solar-iot/secrets/runtime.env
sudo chmod 0640 /opt/solar-platform/config/host.env \
  /opt/solar-platform/config/cosign.pub \
  /opt/solar-platform/secrets/backend.env \
  /opt/solar-platform/secrets/monitoring.env \
  /opt/solar-iot/config/host.env \
  /opt/solar-iot/secrets/runtime.env
```

`backend.env` bắt buộc có:

- PostgreSQL password.
- RabbitMQ password.
- JWT current key >= 32 ký tự, issuer, audience; previous key chỉ điền khi rotation.
- MailJet API key/secret và sender email đã verify trên MailJet.
- MinIO access/secret key mạnh.
- Google OAuth client ID/secret.
- DeepSeek key cho chat/text AI trực tiếp của TicketService.
- Gemini key cho voice transcription trực tiếp của TicketService.
- Hai API key ngẫu nhiên >= 32 ký tự cho sensor ingest và environmental ingest.
- Admin seed password.
- Discord webhook.
- Unsubscribe HMAC secret.
- MQTT password.

Trong Google Cloud Console, OAuth client của production phải có chính xác:

- Authorized JavaScript origin: `https://solars.io.vn` (nếu client-side flow sử dụng origin này).
- Authorized redirect URI: `https://api.solars.io.vn/api/auth/google/callback`.

Không thêm `/auth-service` vào redirect URI; ApiGateway public route không có prefix đó.

Giá trị `Mqtt__Password` trong backend và IoT phải giống tuyệt đối; username production phải là `backend-bridge`. Có thể sinh secret bằng `openssl rand -base64 48`, nhưng tránh ký tự newline.

Không dùng `minioadmin`, `guest`, khóa dev, `CHANGE_ME`, mật khẩu dưới 32 ký tự cho các API key/JWT, hoặc sender MailJet chưa verify. Script preflight sẽ chặn những trường hợp này.

### Bắt buộc xoay secret đã từng xuất hiện trong Git

File legacy `env.prod.example` trước đây từng chứa giá trị trông như secret thật. Việc thay bằng
placeholder ở commit hiện tại không xóa chúng khỏi lịch sử Git. Trước khi production chạy, phải
thu hồi và tạo lại toàn bộ PostgreSQL/admin/Grafana password, JWT signing key, MailJet API key,
Google OAuth client secret, Discord webhook và sensor ingest key từng dùng trong file đó. Không
tái sử dụng giá trị cũ; cập nhật giá trị mới trực tiếp trong Jenkins/VPS secret store.

Firmware production generic đã ghim hai endpoint không bí mật:

```text
BACKEND_URL=https://api.solars.io.vn
MQTT_BROKER_HOST=mqtt.solars.io.vn
```

Mỗi thiết bị vẫn phải được provision `deviceCode`, API key, MQTT credential, Wi-Fi và mật khẩu
AP/portal riêng qua NVS/khâu lắp đặt trước khi giao khách. Không dùng các placeholder compile-time
trong `config.example.h` làm credential vận hành.

## 8. TLS MQTT và WireGuard Backend <-> AI

Backend Helm tạo Certificate `mqtt-public-tls`. Sau lần backend deploy đầu tiên, cài script đồng bộ và timer bằng root:

```bash
sudo install -o root -g root -m 0755 \
  deploy/scripts/sync-mqtt-tls.sh /usr/local/sbin/solar-sync-mqtt-tls
sudo install -o root -g root -m 0644 \
  deploy/systemd/solar-mqtt-tls-sync.service \
  deploy/systemd/solar-mqtt-tls-sync.timer \
  /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now solar-mqtt-tls-sync.timer
sudo systemctl start solar-mqtt-tls-sync.service
```

Runbook đầy đủ, gồm thứ tự không gây deadlock giữa hai pipeline, nằm tại
`docs/runbooks/ai-wireguard-observability.md`. Contract bắt buộc:

- Platform: `10.20.0.1/32`.
- AI: `10.20.0.2/32`.
- Chỉ route hai địa chỉ `/32`, không mở toàn bộ private network.
- Nếu hai Droplet cùng DigitalOcean VPC, dùng private VPC IPv4 làm WireGuard
  endpoint. Chỉ fallback sang primary public IPv4 sau khi xác nhận VPC route không dùng được.
- DigitalOcean Cloud Firewall và UFW chỉ cho UDP `51820` từ đúng endpoint peer.
- Backend gọi cả gRPC primary và HTTPS fallback tới hostname
  `ai.solars.io.vn`; pod resolve hostname này thành `10.20.0.2`, nhờ đó vẫn giữ
  đúng TLS SNI/certificate nhưng traffic không đi qua Internet.
- Prometheus scrape application, node-exporter, cAdvisor và Alloy qua tunnel.
- Alloy đẩy log sang Loki bridge `10.20.0.1:3100`; Loki không được public.

Sau khi `wg show` và ping hai chiều thành công, cài Loki bridge trên Platform:

```bash
sudo install -o root -g root -m 0644 \
  deploy/systemd/solar-loki-wireguard.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now solar-loki-wireguard.service
```

AI host env dùng:

```text
AI_MONITORING_BIND_IP=10.20.0.2
PLATFORM_WIREGUARD_IPV4=10.20.0.1
LOKI_PUSH_URL=http://10.20.0.1:3100/loki/api/v1/push
```

Sau khi các gate mới được merge, WireGuard không còn là optional: cả backend và
AI production preflight đều dừng trước khi đổi release nếu tunnel/handshake hoặc
Loki bridge không đạt. Cấu hình tunnel trước, deploy AI trước, rồi deploy backend.
Không đổi Loki hoặc exporter thành public để né bước này.

## 9. Jenkins executor requirements

Jenkins VPS phải có và user `jenkins` phải chạy được:

- Git, Bash, OpenSSH, SCP.
- Docker Engine, Compose, Buildx.
- .NET SDK 9 trở lên để đọc `.slnx`; application vẫn target .NET 8 runtime.
- Helm và kubectl.
- ShellCheck, Trivy, Syft, Cosign.
- Python 3.11 + pip.
- PlatformIO CLI (`pio`) cho firmware IoT.
- Ít nhất 4 GB swap và đủ disk cho nhiều image build.

Plugins tối thiểu: Pipeline, Git, Credentials Binding, SSH Agent, Lockable Resources và workflow dependencies của chúng. Cập nhật plugin/core theo đợt bảo trì có backup; không gỡ plugin đang phục vụ pipeline.

Tạo label `docker-linux` cho built-in node hoặc agent có toàn bộ tool trên. Backend và IoT `Jenkinsfile` đều yêu cầu label này.

## 10. Jenkins credentials

Tạo trong `Manage Jenkins -> Credentials -> System -> Global`:

| ID | Kind | Nội dung |
|---|---|---|
| `backend-github-read` | Username with password | GitHub username + PAT đọc repo backend |
| `backend-registry-write` | Username with password | GitHub username + PAT `read:packages`, `write:packages` |
| `iot-github-read` | Username with password | GitHub username + PAT đọc repo IoT |
| `iot-registry-write` | Username with password | GitHub username + PAT `read:packages`, `write:packages` |
| `ai-cosign-private-key` | Secret file | Cùng `cosign.key` đã dùng cho AI |
| `ai-cosign-public-key` | Secret file | Cùng `cosign.pub` đã cài trên VPS |
| `ai-cosign-password` | Secret text | Passphrase của private key Cosign |
| `platform-vps-target` | Secret text | `deploy@PUBLIC_OR_RESERVED_IP` |
| `platform-vps-ssh` | SSH Username with private key | Username `deploy`, private key Jenkins riêng |
| `platform-vps-known-hosts` | Secret file | known_hosts đã xác minh fingerprint của VPS Platform |

Không dùng `StrictHostKeyChecking=no`, không tạo known_hosts bằng cách tin mù kết quả `ssh-keyscan`. So sánh fingerprint với `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` trên VPS qua phiên quản trị đã tin cậy.

Tạo lockable resource tên `solar-platform-prod`; cả backend và IoT dùng chung lock để không ghi đồng thời lên cùng VPS.

## 11. Jenkins jobs

### Backend Multibranch Pipeline

- Job: `solar-backend` hoặc tên tùy chọn.
- Repository: backend.
- Script path: `Jenkinsfile`.
- Scan/build `main`, `dev`, PR và feature branch theo chính sách nhóm.
- Webhook push event từ GitHub tới Jenkins.

Trên branch thường: chạy CI, scan và build nhưng không deploy. Trên `main`: sau khi toàn bộ gate xanh, giải phóng executor rồi gọi job `solar-backend-production` với `GIT_SHA` chính xác.

### Backend production Pipeline

- Job bắt buộc có tên `solar-backend-production`.
- Definition: Pipeline script nhập trực tiếp trong Jenkins UI.
- Nội dung lấy từ `deploy/jenkins/production.Jenkinsfile.example` sau khi review.
- Không dùng “Pipeline script from SCM” cho job production; nếu dùng source từ chính commit chưa tin cậy thì người sửa repo có thể đổi logic ký/deploy.

Job production xác minh SHA thuộc `origin/main`, rebuild 9 image từ SHA đó, scan, SBOM, mirror alert relay, push digest, ký Cosign, đóng gói Helm dependencies rồi SSH deploy.

### IoT Multibranch Pipeline

- Job: `solar-iot`.
- Repository: IoT.
- Script path: `Jenkinsfile`.
- Webhook push event.

Trên `main`, CI xanh sẽ gọi `solar-iot-production` sau khi giải phóng executor.

### IoT production Pipeline

- Job bắt buộc có tên `solar-iot-production`.
- Definition: Pipeline script nhập trực tiếp trong Jenkins UI.
- Nội dung lấy từ `deploy/jenkins/production.Jenkinsfile.example` của repo IoT.
- Job xác minh SHA main, rebuild firmware, mirror image Mosquitto vào GHCR, scan/SBOM, ký image rồi SSH deploy Docker.

Production job cố ý tách khỏi Multibranch để tránh deadlock executor và để code chưa tin cậy không trực tiếp sở hữu private signing key/deploy key.

## 12. Thứ tự deploy lần đầu

1. Xác nhận 4 DNS A record đã cùng trả Reserved IP Platform từ authoritative DNS và resolver công cộng.
2. Hoàn tất OS, Docker, K3s, Helm, cert-manager, Cosign, deploy user, kubeconfig và firewall.
3. Tạo thư mục, host env, backend env, monitoring env, IoT runtime env, Cosign public key và GHCR login/pull secret.
4. Cấu hình Jenkins credentials, labels, lock và bốn jobs.
5. Cấu hình WireGuard và Loki bridge theo
    `docs/runbooks/ai-wireguard-observability.md`.
6. Deploy AI để exporter/Alloy bind lên `10.20.0.2`.
7. Merge backend vào `main`.
8. Chờ backend Multibranch xanh và `solar-backend-production` xanh; lần deploy
   này tạo bốn Prometheus target và ép kết nối gRPC/HTTPS qua tunnel.
9. Xác nhận certificate cho API/files/Grafana/MQTT Ready.
10. Cài và chạy MQTT TLS sync timer; xác nhận `/opt/solar-iot/secrets/mosquitto/tls/tls.crt` và `tls.key` tồn tại, khớp nhau.
11. Merge IoT vào `main`.
12. Chờ IoT Multibranch và `solar-iot-production` xanh.
13. Chạy toàn bộ smoke check ở mục 13 và acceptance matrix trong runbook WireGuard.

Không deploy IoT trước khi cert MQTT được cấp và đồng bộ; preflight sẽ chặn.

## 13. Kiểm tra sau deploy

Trên VPS Platform:

```bash
sudo -u deploy -H env KUBECONFIG=/home/deploy/.kube/config \
  kubectl -n solar-prod get pod,svc,ingress,certificate,pvc

sudo -u deploy -H env KUBECONFIG=/home/deploy/.kube/config \
  helm -n solar-prod status solar

sudo -u deploy -H docker compose \
  --project-name solar-iot \
  --env-file /opt/solar-iot/config/host.env \
  --env-file /opt/solar-iot/secrets/runtime.env \
  --env-file /opt/solar-iot/current/deploy/production/image-lock.env \
  -f /opt/solar-iot/current/infra/docker-compose.prod.yml ps
```

Từ máy bên ngoài:

```bash
dig +short A api.solars.io.vn
dig +short A files.solars.io.vn
dig +short A mqtt.solars.io.vn
dig +short A grafana.solars.io.vn

curl --fail --show-error https://api.solars.io.vn/health
curl --fail --show-error https://files.solars.io.vn/minio/health/live
curl --fail --show-error https://grafana.solars.io.vn/api/health

openssl s_client \
  -connect mqtt.solars.io.vn:8883 \
  -servername mqtt.solars.io.vn \
  -verify_hostname mqtt.solars.io.vn \
  -verify_return_error </dev/null
```

Kiểm tra backend -> AI bằng log/metric và gọi gRPC health từ máy có proto. Endpoint phải là `ai.solars.io.vn:443` với TLS, không dùng IP hay cổng 50051 public.

Kiểm tra port không bị lộ:

```bash
sudo ss -lntup
sudo ufw status verbose
```

Chỉ 22, 80, 443, 8883 và WireGuard đã giới hạn được phép public.

## 14. Backup, rollback và khôi phục

- Trước mỗi Helm upgrade, pipeline tạo một Job từ `postgres-backup` hiện hữu và dừng deploy nếu backup thất bại.
- CronJob backup dump đủ 7 database, kiểm tra archive rồi upload MinIO.
- MinIO nằm cùng VPS, vì vậy cần thêm DigitalOcean snapshot/volume backup hoặc đồng bộ backup tới object storage ngoài VPS. Chỉ backup vào MinIO cùng node không bảo vệ khỏi mất Droplet/disk.
- Helm deploy dùng `--atomic --cleanup-on-fail`; lỗi rollout tự trở về revision trước.
- Xem lịch sử: `helm -n solar-prod history solar`.
- Rollback thủ công: `helm -n solar-prod rollback solar REVISION --wait --timeout 25m`.
- IoT giữ symlink `/opt/solar-iot/previous`; script tự chạy Compose của release trước nếu deploy mới thất bại.
- Backend release manifests nằm ở `/opt/solar-platform/releases`, `current` và `previous` để audit; image luôn tham chiếu digest.

Thử restore PostgreSQL định kỳ trên môi trường cô lập. Backup chưa từng restore thử không được coi là backup đã xác minh.

## 15. Các điều kiện pipeline cố ý chặn

Pipeline/preflight sẽ fail nếu:

- SHA không đủ 40 ký tự lowercase hoặc không thuộc `main`.
- Image không có digest hợp lệ hay chữ ký Cosign đúng public key.
- Trivy phát hiện vulnerability vượt gate.
- Docker image app không chạy UID/GID 10001.
- Helm lint/template lỗi hoặc chart thiếu dependency.
- Secret thiếu, dùng placeholder, JWT/API key ngắn hoặc MailJet sender sai định dạng.
- DNS không trỏ đúng Reserved IP.
- Private IP khai báo không gắn trên VPS.
- K3s node/ClusterIssuer chưa Ready, `ghcr-pull` thiếu.
- Disk còn dưới 30 GiB hay RAM available dưới 2 GiB.
- MQTT TLS thiếu, sắp hết hạn, hostname sai hoặc key không khớp certificate.
- Mosquitto không healthy hoặc public TLS smoke check thất bại.

Không bỏ các gate này để “deploy cho qua”; sửa nguyên nhân và chạy lại đúng commit.
