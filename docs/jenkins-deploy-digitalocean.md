# Hướng dẫn Deploy toàn bộ Stack lên DigitalOcean

Tài liệu này hướng dẫn từng bước setup **Jenkins CI/CD + toàn bộ backend + Prometheus + Grafana + DB**
trên DigitalOcean Kubernetes (DOKS) — từ tạo cluster cho đến lúc mọi UI đều truy cập được.

---

## Tổng quan kiến trúc sau khi deploy

```
Internet
   │
   ▼
DigitalOcean Load Balancer (tự tạo khi install Traefik)
   │
   ▼
Traefik (Ingress Controller)
   ├── jenkins.yourdomain.com     → Jenkins UI + BlueOcean
   ├── api.yourdomain.com         → ApiGateway → Swagger UI
   ├── grafana.yourdomain.com     → Grafana (dashboards + logs)
   └── prometheus.yourdomain.com  → Prometheus UI (internal)

Kubernetes Cluster (DOKS)
├── namespace: jenkins
│   └── Jenkins master + ephemeral build agents
├── namespace: solar-staging
│   ├── ApiGateway, AuthService, EmailService
│   ├── SmsService, FileStorageService, BatteryService
│   ├── Postgres (TimescaleDB), Redis, RabbitMQ, MinIO
│   ├── Prometheus, Grafana, Alertmanager
│   ├── Loki, Promtail (log aggregation)
│   └── postgres-exporter, redis-exporter
└── namespace: cert-manager
    └── cert-manager (tự động cấp TLS từ Let's Encrypt)
```

---

## Yêu cầu máy local (máy của bạn)

Cài sẵn các tool sau trước khi bắt đầu:

```bash
# Kiểm tra version hiện có
kubectl version --client
helm version
doctl version
git --version
```

Nếu chưa có:

```bash
# macOS
brew install kubectl helm doctl

# Xác thực doctl với DigitalOcean API token
doctl auth init
# Paste API token từ: https://cloud.digitalocean.com/account/api/tokens
```

---

## Phần 1 — Tạo Kubernetes Cluster trên DigitalOcean

### 1.1 Tạo cluster qua doctl

```bash
doctl kubernetes cluster create solar-cluster \
  --region sgp1 \
  --node-pool "name=solar-pool;size=s-4vcpu-8gb;count=3" \
  --wait
```

**Giải thích tham số:**
- `--region sgp1` — Singapore (gần VN nhất). Các option khác: `blr1` (Bangalore), `syd1` (Sydney)
- `--size s-4vcpu-8gb` — 4 vCPU / 8GB RAM mỗi node. Cần tối thiểu để chạy đủ stack
- `--count 3` — 3 node, đủ để distribute workload và Jenkins agents
- `--wait` — đợi cluster ready trước khi tiếp tục (mất khoảng 4-6 phút)

### 1.2 Lấy kubeconfig

```bash
doctl kubernetes cluster kubeconfig save solar-cluster

# Xác nhận kết nối được
kubectl get nodes
# Expected output:
# NAME                   STATUS   ROLES    AGE   VERSION
# solar-pool-xxxxx-01    Ready    <none>   2m    v1.32.x
# solar-pool-xxxxx-02    Ready    <none>   2m    v1.32.x
# solar-pool-xxxxx-03    Ready    <none>   2m    v1.32.x
```

### 1.3 Tạo namespaces

```bash
kubectl apply -f deploy/k8s/00-namespaces.yaml
# Output:
# namespace/solar-staging created
# namespace/jenkins created
# namespace/cert-manager created
```

---

## Phần 2 — Cài Traefik (Ingress Controller)

Traefik nhận traffic từ DigitalOcean Load Balancer và route về đúng service. Khi cài, DigitalOcean tự tạo một LoadBalancer tốn thêm ~$12/tháng.

```bash
# Thêm Helm repo
helm repo add traefik https://traefik.github.io/charts
helm repo update

# Cài Traefik vào namespace traefik
helm install traefik traefik/traefik \
  --namespace traefik \
  --create-namespace \
  --set service.type=LoadBalancer \
  --set ports.web.redirectTo.port=websecure \
  --wait
```

### 2.1 Lấy IP của Load Balancer

```bash
kubectl get svc traefik -n traefik -w
# Đợi đến khi cột EXTERNAL-IP hiện IP thực (không phải <pending>)
# Ví dụ: 143.198.xxx.xxx
```

**Ghi IP này lại — bạn sẽ cần trỏ DNS vào đây.**

### 2.2 Trỏ DNS

Vào DNS provider của bạn (DigitalOcean DNS, Cloudflare, GoDaddy...) và thêm các A records:

| Subdomain | Type | Value |
|-----------|------|-------|
| `jenkins.yourdomain.com` | A | `<IP Load Balancer>` |
| `api.yourdomain.com` | A | `<IP Load Balancer>` |
| `grafana.yourdomain.com` | A | `<IP Load Balancer>` |
| `prometheus.yourdomain.com` | A | `<IP Load Balancer>` |

> **Lưu ý:** DNS propagate mất 5-30 phút. Dùng `dig jenkins.yourdomain.com` để check.

---

## Phần 3 — Cài cert-manager (TLS tự động)

cert-manager tự động cấp và renew certificate từ Let's Encrypt.

```bash
# Cài cert-manager CRDs + controller
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.14.0/cert-manager.yaml

# Đợi cert-manager pods ready
kubectl wait --for=condition=ready pod \
  -l app.kubernetes.io/instance=cert-manager \
  -n cert-manager \
  --timeout=120s
```

### 3.1 Tạo ClusterIssuer

Sửa email trong file `deploy/k8s/01-cert-manager-issuer.yaml` trước (2 chỗ):

```yaml
# deploy/k8s/01-cert-manager-issuer.yaml — dòng 12 và dòng 29
email: your-real-email@gmail.com   # ← đổi email thực
```

```bash
kubectl apply -f deploy/k8s/01-cert-manager-issuer.yaml
# Output:
# clusterissuer.cert-manager.io/letsencrypt-staging created
# clusterissuer.cert-manager.io/letsencrypt-prod created
```

---

## Phần 4 — Sửa các placeholder trong config repo

**Bắt buộc sửa trước khi chạy bất kỳ helm install nào.**

### 4.1 Jenkinsfile

```groovy
// Jenkinsfile — dòng 31-36
environment {
  REGISTRY    = 'ghcr.io/GSU26SE55'          // ← tên org GitHub của team
  DEPLOY_HOST = 'yourdomain.com'              // ← domain thực (không có api.)
}
```

### 4.2 deploy/jenkins/values.yaml

```yaml
# dòng 49
hostName: jenkins.yourdomain.com              # ← đổi domain Jenkins
```

### 4.3 deploy/helm/solar-battery/values-staging.yaml

```yaml
# dòng 8
domain: yourdomain.com                        # ← đổi domain (không có api.)

# dòng 75
adminPassword: "mat-khau-manh-cua-ban"        # ← đổi Grafana password

# dòng 79
hosts:
- grafana.yourdomain.com                      # ← đổi domain Grafana
```

### 4.4 deploy/helm/solar-battery/values.yaml

```yaml
# dòng 12
imageRegistry: ghcr.io/GSU26SE55              # ← tên org GitHub của team

# dòng 28
domain: yourdomain.com                        # ← domain thực
```

---

## Phần 5 — Cài Jenkins lên K8s

### 5.1 Tạo Secret ghcr.io cho Kaniko (build agent)

Jenkins agent dùng Kaniko để push image lên GitHub Container Registry. Cần tạo PAT trước.

**Tạo GitHub PAT:**
1. Vào `github.com` → Settings → Developer settings → Personal access tokens → Tokens (classic)
2. Generate new token với scopes: `write:packages`, `read:packages`, `repo`
3. Copy token

```bash
# Tạo secret trong namespace jenkins
kubectl create secret docker-registry ghcr-credentials \
  --namespace jenkins \
  --docker-server=ghcr.io \
  --docker-username=YOUR_GITHUB_USERNAME \
  --docker-password=YOUR_GITHUB_PAT
```

### 5.2 Cài Jenkins qua Helm

```bash
helm repo add jenkins https://charts.jenkins.io
helm repo update

helm install jenkins jenkins/jenkins \
  --namespace jenkins \
  --create-namespace \
  -f deploy/jenkins/values.yaml \
  --wait --timeout 10m
```

### 5.3 Apply RBAC cho Jenkins agent

```bash
kubectl apply -f deploy/jenkins/rbac.yaml
# Output:
# clusterrole.rbac.authorization.k8s.io/jenkins-deployer created
# clusterrolebinding.rbac.authorization.k8s.io/jenkins-deployer created
```

### 5.4 Lấy password Jenkins admin

```bash
kubectl get secret jenkins \
  -n jenkins \
  -o jsonpath='{.data.jenkins-admin-password}' | base64 --decode && echo
```

**Truy cập Jenkins UI:** `https://jenkins.yourdomain.com`
- Username: `admin`
- Password: output từ lệnh trên

---

## Phần 6 — Cấu hình Jenkins (làm 1 lần trên UI)

### 6.1 Thêm Credentials

Vào **Manage Jenkins → Credentials → System → Global credentials (unrestricted) → Add Credentials**

**Credential 1 — GitHub Token (cho checkout + webhook)**
- Kind: `Username with password`
- Username: `YOUR_GITHUB_USERNAME`
- Password: `YOUR_GITHUB_PAT` (scope: `repo`)
- ID: `github-token`
- Description: `GitHub PAT for repo access`

**Credential 2 — Discord Webhook (cho notification)**
- Kind: `Secret text`
- Secret: `https://discord.com/api/webhooks/xxx/yyy` (webhook URL từ Discord server)
- ID: `discord-webhook`
- Description: `Discord deploy notification`

**Credential 3 — ghcr.io (cho Kaniko push)**
- Kind: `Username with password`
- Username: `YOUR_GITHUB_USERNAME`
- Password: `YOUR_GITHUB_PAT`
- ID: `ghcr-credentials`
- Description: `GHCR push credentials`

### 6.2 Tạo Multibranch Pipeline

1. Từ Jenkins Dashboard → **New Item**
2. Tên: `solar-battery`
3. Chọn: **Multibranch Pipeline** → **OK**
4. Tab **Branch Sources** → **Add source** → **GitHub**
   - Credentials: chọn `github-token`
   - Repository HTTPS URL: `https://github.com/GSU26SE55/backend`
5. Tab **Build Configuration** → Mode: `by Jenkinsfile` (mặc định)
6. Tab **Scan Multibranch Pipeline Triggers** → tick **Periodically if not otherwise run** → 1 minute
7. **Save**

Jenkins sẽ scan ngay và tìm thấy `Jenkinsfile` ở root repo. Nó sẽ show list branches.

---

## Phần 7 — Setup GitHub Webhook (trigger tự động khi push/merge)

### 7.1 Thêm webhook vào GitHub repo

1. Vào GitHub repo → **Settings** → **Webhooks** → **Add webhook**
2. Điền:
   - **Payload URL:** `https://jenkins.yourdomain.com/github-webhook/`
   - **Content type:** `application/json`
   - **Secret:** để trống (hoặc thêm HMAC secret nếu muốn bảo mật hơn)
   - **Events:** chọn **Pushes** và **Pull requests**
3. **Add webhook**

### 7.2 Kiểm tra webhook hoạt động

GitHub hiện dấu tích xanh ✅ bên cạnh webhook nếu ping thành công. Nếu đỏ ❌ → kiểm tra lại Jenkins URL và network.

### 7.3 Branch nào trigger pipeline

Hiện tại `Jenkinsfile` chỉ chạy khi push vào branch `staging` (dòng 61). Nếu muốn thêm:

```groovy
// Jenkinsfile — dòng 61
// Thêm dev vào danh sách trigger
if (!['staging', 'dev'].contains(env.BRANCH_NAME)) {
  currentBuild.result = 'NOT_BUILT'
  error("Pipeline chỉ chạy cho branch staging/dev")
}
```

---

## Phần 8 — Tạo Secrets cho Application

Application cần một số secret nhạy cảm (DB password, JWT key, API keys...). Tạo trước khi deploy lần đầu.

### 8.1 Tạo file secret tạm (KHÔNG commit file này vào git)

Tạo file `/tmp/solar-secrets.env` trên máy local:

```bash
cat > /tmp/solar-secrets.env << 'EOF'
# Database
POSTGRES_PASSWORD=MatKhauManhChoPostgres123@
POSTGRES_USER=postgres
POSTGRES_DB=postgres
AUTH_DB_NAME=auth_db
FILE_STORAGE_DB_NAME=file_storage_db
BATTERY_DB_NAME=battery_db

# JWT
JWT_SECRET=jwt-secret-key-rat-dai-va-manh-it-nhat-32-chars

# MailJet
MAILJET_API_KEY=your-mailjet-api-key
MAILJET_SECRET_KEY=your-mailjet-secret-key

# RabbitMQ
RABBITMQ_USERNAME=guest
RABBITMQ_PASSWORD=guest

# MinIO
MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin

# Discord (cho alertmanager)
DISCORD_WEBHOOK=https://discord.com/api/webhooks/xxx/yyy

# Battery API Key
BATTERY_SENSOR_INGEST_API_KEY=dev-battery-ingest-key-32chars
EOF
```

### 8.2 Tạo K8s Secret từ file

```bash
kubectl create secret generic solar-secrets \
  --namespace solar-staging \
  --from-env-file=/tmp/solar-secrets.env

# Xoá file tạm
rm /tmp/solar-secrets.env
```

### 8.3 Tạo ghcr pull secret (để K8s pull image)

```bash
kubectl create secret docker-registry ghcr-pull \
  --namespace solar-staging \
  --docker-server=ghcr.io \
  --docker-username=YOUR_GITHUB_USERNAME \
  --docker-password=YOUR_GITHUB_PAT
```

---

## Phần 9 — Deploy lần đầu (toàn bộ stack)

### 9.1 Update Helm chart dependencies

```bash
# Tải subchart: kube-prometheus-stack, loki-stack, postgres-exporter, redis-exporter
helm dependency update deploy/helm/solar-battery
# Tạo ra file deploy/helm/solar-battery/Chart.lock và thư mục charts/
```

### 9.2 Deploy toàn bộ stack

```bash
helm upgrade --install solar deploy/helm/solar-battery \
  --namespace solar-staging \
  --create-namespace \
  -f deploy/helm/solar-battery/values.yaml \
  -f deploy/helm/solar-battery/values-staging.yaml \
  --set global.imageTag=latest \
  --atomic \
  --wait \
  --timeout 15m
```

**Lần đầu mất 8-15 phút** vì cần pull image Postgres, RabbitMQ, Prometheus, Grafana...

### 9.3 Kiểm tra tất cả pods đang chạy

```bash
kubectl get pods -n solar-staging
# Expected: tất cả STATUS = Running, READY = x/x

# Xem chi tiết nếu có pod không start được
kubectl describe pod <tên-pod> -n solar-staging
kubectl logs <tên-pod> -n solar-staging
```

---

## Phần 10 — Truy cập tất cả UI

| Service | URL | Username | Password |
|---------|-----|----------|----------|
| Jenkins UI | `https://jenkins.yourdomain.com` | `admin` | lấy từ Secret (xem 5.4) |
| Jenkins BlueOcean | `https://jenkins.yourdomain.com/blue` | `admin` | như trên |
| API Gateway (Swagger) | `https://api.yourdomain.com/swagger` | — | — |
| Grafana | `https://grafana.yourdomain.com` | `admin` | password đặt ở 4.3 |
| Prometheus | `https://prometheus.yourdomain.com` | — | — |
| RabbitMQ Management | nội bộ (port-forward) | `guest` | `guest` |
| MinIO Console | nội bộ (port-forward) | `minioadmin` | `minioadmin` |

### Port-forward cho các service nội bộ

```bash
# RabbitMQ Management UI
kubectl port-forward svc/rabbitmq 15673:15672 -n solar-staging
# → http://localhost:15673

# MinIO Console
kubectl port-forward svc/minio 9091:9001 -n solar-staging
# → http://localhost:9091

# Prometheus (nếu không muốn expose qua Ingress)
kubectl port-forward svc/monitoring-prometheus 9094:9090 -n solar-staging
# → http://localhost:9094
```

---

## Phần 11 — Luồng CI/CD tự động sau khi setup

```
Developer tạo PR → merge vào branch staging
         ↓
GitHub gửi webhook đến https://jenkins.yourdomain.com/github-webhook/
         ↓
Jenkins nhận webhook → trigger pipeline solar-battery/staging
         ↓
Jenkins spawn ephemeral pod trong namespace jenkins
  Pod có 6 containers:
  - jnlp      (Jenkins agent core)
  - dotnet    (build + test)
  - kaniko    (build Docker image không cần Docker daemon)
  - trivy     (security scan)
  - kubectl   (helm deploy)
  - curl      (smoke test)
         ↓
Stage 0a: Checkout code (depth 50 để detect-changes.sh hoạt động)
Stage 0b: Xác nhận đúng branch staging
Stage 1:  Detect service nào thay đổi (skip build service không đổi)
Stage 2:  dotnet format --verify-no-changes (FAIL nếu code không format đúng)
Stage 2.5: Trivy scan CVE trong dependencies (CRITICAL → FAIL, HIGH → warn)
Stage 3:  dotnet restore + build Release
Stage 4:  dotnet test --filter FullyQualifiedName!~IntegrationTests
Stage 5:  Kaniko build + push image → ghcr.io/GSU26SE55/<service>:<SHA>
Stage 6:  Trivy scan image vừa build
Stage 7:  helm upgrade --install (deploy tất cả: app + DB + monitoring)
Stage 8:  Smoke test: curl https://api.yourdomain.com/metrics (retry 6×15s)
         ↓
✅ Success → Discord: "Deploy STAGING success — version abc1234"
❌ Failure → helm rollback solar -n solar-staging
           → Discord: "Deploy STAGING FAILED — build #N — đã rollback"
```

### Theo dõi build realtime trên BlueOcean

```
https://jenkins.yourdomain.com/blue/organizations/jenkins/solar-battery/activity
```

BlueOcean hiển thị:
- Từng stage dưới dạng pipeline visual (màu xanh/đỏ/vàng)
- Log của từng step trong stage
- Thời gian chạy từng stage
- Lịch sử build + filter theo branch

---

## Phần 12 — Grafana: theo dõi sau deploy

### 12.1 Dashboards có sẵn

Sau khi deploy, Grafana tự load các dashboard từ `deploy/helm/solar-battery/dashboards/`:

| Dashboard | Nội dung |
|-----------|----------|
| Solar Battery Services | Request rate, error rate, latency của 6 services |
| Node Exporter | CPU, RAM, disk của từng node K8s |
| PostgreSQL | Connection pool, query latency, cache hit rate |
| Redis | Memory, hit rate, command latency |
| Kubernetes | Pod status, restart count, resource usage |
| Logs (Loki) | Log aggregation từ tất cả containers |

### 12.2 Xem logs realtime

1. Vào Grafana → **Explore** (biểu tượng la bàn bên trái)
2. Chọn datasource: **Loki**
3. Nhập query: `{namespace="solar-staging", container="batteryservice"}`
4. Bấm **Run query**

---

## Phần 13 — Cấu hình Prometheus Ingress (tùy chọn)

Prometheus mặc định không expose ra ngoài. Nếu muốn truy cập `https://prometheus.yourdomain.com`, thêm vào `values-staging.yaml`:

```yaml
# Thêm vào cuối values-staging.yaml
kube-prometheus-stack:
  prometheus:
    ingress:
      enabled: true
      ingressClassName: traefik
      hosts:
        - prometheus.yourdomain.com
      paths:
        - /
      tls:
        - secretName: prometheus-tls
          hosts:
            - prometheus.yourdomain.com
      annotations:
        cert-manager.io/cluster-issuer: letsencrypt-prod
```

Sau đó `helm upgrade solar deploy/helm/solar-battery -n solar-staging -f values.yaml -f values-staging.yaml`.

---

## Phần 14 — Troubleshooting thường gặp

### Pod không start — ImagePullBackOff

```bash
kubectl describe pod <tên-pod> -n solar-staging | grep -A 10 "Events:"
# Nếu thấy: unauthorized to read from ghcr.io
```

Kiểm tra ghcr-pull secret:
```bash
kubectl get secret ghcr-pull -n solar-staging
# Nếu không có → chạy lại lệnh ở Phần 8.3
```

### Pod CrashLoopBackOff

```bash
# Xem log container
kubectl logs <tên-pod> -n solar-staging --previous

# Xem event
kubectl describe pod <tên-pod> -n solar-staging
```

Nguyên nhân phổ biến:
- Secret `solar-secrets` thiếu key → `kubectl get secret solar-secrets -n solar-staging -o yaml`
- Database chưa sẵn sàng → kiểm tra postgres pod trước
- Migration fail → xem log của pod đầu tiên khởi động

### Jenkins agent không spawn được

```bash
kubectl get pods -n jenkins
kubectl describe pod <jenkins-agent-pod> -n jenkins
```

Kiểm tra:
- Secret `ghcr-credentials` đã tạo trong namespace `jenkins` chưa (Phần 5.1)
- RBAC đã apply chưa (Phần 5.3)
- Xem Jenkins System Log: `https://jenkins.yourdomain.com/log/all`

### cert-manager không cấp TLS

```bash
kubectl get certificate -n solar-staging
kubectl describe certificate <tên> -n solar-staging

kubectl get certificaterequest -n solar-staging
kubectl describe certificaterequest <tên> -n solar-staging
```

Nguyên nhân phổ biến:
- DNS chưa propagate → `dig api.yourdomain.com` phải trả về IP Load Balancer
- Dùng `letsencrypt-staging` → browser báo cert không tin (bình thường). Đổi sang `letsencrypt-prod` khi ổn định

### Helm deploy fail — timeout

```bash
# Xem events trong namespace
kubectl get events -n solar-staging --sort-by='.lastTimestamp' | tail -30

# Xem tất cả pods
kubectl get pods -n solar-staging

# Rollback thủ công
helm rollback solar -n solar-staging
```

### Smoke test fail sau deploy

```bash
# Kiểm tra ApiGateway pod
kubectl logs deployment/apigateway -n solar-staging

# Kiểm tra Ingress
kubectl describe ingress -n solar-staging

# Test thủ công
curl -v https://api.yourdomain.com/metrics
```

---

## Phần 15 — Tổng hợp những chỗ cần thay đổi

Tất cả chỗ cần đổi trước khi bắt đầu (không bỏ sót chỗ nào):

| File | Dòng | Placeholder | Đổi thành |
|------|------|-------------|-----------|
| `Jenkinsfile` | 31 | `ghcr.io/your-org` | `ghcr.io/GSU26SE55` |
| `Jenkinsfile` | 36 | `staging.example.com` | domain VPS của bạn |
| `deploy/jenkins/values.yaml` | 49 | `jenkins.example.com` | `jenkins.yourdomain.com` |
| `deploy/helm/solar-battery/values.yaml` | 12 | `ghcr.io/your-org` | `ghcr.io/GSU26SE55` |
| `deploy/helm/solar-battery/values.yaml` | 28 | `dev.example.com` | domain của bạn |
| `deploy/helm/solar-battery/values-staging.yaml` | 8 | `staging.example.com` | domain của bạn |
| `deploy/helm/solar-battery/values-staging.yaml` | 75 | `changeme-strong` | Grafana password mạnh |
| `deploy/helm/solar-battery/values-staging.yaml` | 79 | `grafana.staging.example.com` | `grafana.yourdomain.com` |
| `deploy/k8s/01-cert-manager-issuer.yaml` | 12, 29 | `admin@example.com` | email thực của bạn |

---

## Phần 16 — Checklist thực hiện theo thứ tự

```
[ ] 1.  Cài kubectl, helm, doctl trên máy local
[ ] 2.  doctl auth init (paste DigitalOcean API token)
[ ] 3.  Tạo DOKS cluster (Phần 1.1)
[ ] 4.  kubectl get nodes → 3 nodes Ready
[ ] 5.  kubectl apply -f deploy/k8s/00-namespaces.yaml
[ ] 6.  Cài Traefik (Phần 2)
[ ] 7.  Ghi IP Load Balancer
[ ] 8.  Trỏ 4 A records DNS vào IP (jenkins, api, grafana, prometheus)
[ ] 9.  Cài cert-manager (Phần 3)
[ ] 10. Sửa tất cả placeholder trong code (Phần 4 + bảng Phần 15)
[ ] 11. Commit và push các thay đổi config lên repo
[ ] 12. Tạo secret ghcr-credentials trong namespace jenkins (Phần 5.1)
[ ] 13. helm install jenkins (Phần 5.2)
[ ] 14. kubectl apply -f deploy/jenkins/rbac.yaml (Phần 5.3)
[ ] 15. Lấy Jenkins admin password (Phần 5.4)
[ ] 16. Truy cập https://jenkins.yourdomain.com → đăng nhập
[ ] 17. Thêm 3 credentials trong Jenkins UI (Phần 6.1)
[ ] 18. Tạo Multibranch Pipeline (Phần 6.2)
[ ] 19. Thêm GitHub Webhook (Phần 7.1)
[ ] 20. Tạo solar-secrets (Phần 8.2)
[ ] 21. Tạo ghcr-pull secret trong namespace solar-staging (Phần 8.3)
[ ] 22. helm dependency update deploy/helm/solar-battery (Phần 9.1)
[ ] 23. helm upgrade --install solar ... (Phần 9.2) — DEPLOY LẦN ĐẦU
[ ] 24. kubectl get pods -n solar-staging → tất cả Running
[ ] 25. Truy cập https://api.yourdomain.com/swagger → Swagger UI load
[ ] 26. Truy cập https://grafana.yourdomain.com → Grafana load
[ ] 27. Push thử vào branch staging → xem build chạy trên BlueOcean
[ ] 28. Kiểm tra Discord nhận notification build
```

---

## Phần 17 — Chi phí DigitalOcean ước tính

| Resource | Specs | Giá/tháng |
|----------|-------|-----------|
| 3× Droplet (K8s nodes) | 4 vCPU / 8GB | ~$144 ($48/node) |
| 1× Load Balancer | Traefik tạo tự động | ~$12 |
| Block Storage (PVC) | ~50GB tổng (Postgres 10G + RabbitMQ 5G + MinIO 20G + Jenkins 10G + Grafana/Loki 5G) | ~$5 |
| **Tổng** | | **~$161/tháng** |

> Tiết kiệm hơn: dùng 2 node `s-4vcpu-8gb` (~$96) + 1 node `s-2vcpu-4gb` ($24) cho Jenkins = ~$132/tháng. Jenkins agent là ephemeral nên node nhỏ hơn cho Jenkins master là ổn.
