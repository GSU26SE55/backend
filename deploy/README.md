# Deploy Guide — Jenkins + K8s lên VPS

## Tổng quan

Stack có **3 con đường** chạy độc lập, không xung đột:

| Path | Để làm gì | Trigger |
|---|---|---|
| `docker-compose.yml` (root) | Local dev trên máy | `docker compose up -d` |
| `.github/workflows/*` | CI quality gate trên GitHub | Push main/dev hoặc PR |
| **`Jenkinsfile` + `deploy/`** | **Deploy thật lên VPS K8s** | **Push branch `staging`** |

## Branching Model

```
main ────────────────────────────────────────────────────────►
  │
  ├── feature/xxx ─→ PR vào main ─→ GitHub Actions validate
  │
  └── staging ←─ merge từ main khi muốn deploy
                  ↓
            Jenkins trigger ─→ K8s VPS
```

**Rule**:
- KHÔNG bao giờ push trực tiếp `staging`. Luôn `git merge main` rồi push.
- GitHub Actions chạy trên PR và push `main`/`dev` (validate).
- Jenkins CHỈ chạy trên push `staging` (deploy).
- Branch protection trên `staging`: require PR từ `main`, no direct push.

## Pre-flight Checklist (LÀM 1 LẦN trước deploy lần đầu)

### A. Chuẩn bị

- [ ] **VPS** Vietnix Ubuntu 22.04, ≥ 8GB RAM, public IP
- [ ] **Domain** trỏ wildcard `*.your-domain.com` về IP VPS
- [ ] **GitHub PAT** với scope `repo` + `write:packages` + `read:packages`
- [ ] **Discord webhook** cho channel #alerts

### B. Đổi placeholder trong source

Search & replace:

| Tìm | Đổi thành |
|---|---|
| `your-org` | GitHub username/org của bạn |
| `example.com` | Domain thật |
| `staging.example.com` | `staging.your-domain.com` |
| `admin@example.com` | Email Let's Encrypt |
| `changeme-strong` (Grafana password) | Password mạnh |

Files cần đổi:
- `Jenkinsfile` (REGISTRY, DEPLOY_HOST)
- `deploy/helm/solar-battery/values-staging.yaml` (global.domain, kube-prometheus-stack.grafana)
- `deploy/jenkins/values.yaml` (hostName)
- `deploy/k8s/01-cert-manager-issuer.yaml` (email × 2)

### C. Cài cluster

```bash
# 1. SSH vào VPS, cài k3s
ssh root@your-vps
curl -sfL https://get.k3s.io | sh -s -

# 2. Copy kubeconfig về máy local
scp root@your-vps:/etc/rancher/k3s/k3s.yaml ~/.kube/k3s-staging.yaml
sed -i '' "s/127.0.0.1/your-vps-ip/g" ~/.kube/k3s-staging.yaml
export KUBECONFIG=~/.kube/k3s-staging.yaml

# 3. Cài cert-manager
kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/v1.14.0/cert-manager.yaml
kubectl wait --for=condition=Available -n cert-manager deployment --all --timeout=2m

# 4. Apply namespaces + cluster issuer
kubectl apply -f deploy/k8s/00-namespaces.yaml
kubectl apply -f deploy/k8s/01-cert-manager-issuer.yaml
```

### D. Tạo secrets (manual — KHÔNG commit value)

```bash
# 1. Secret cho app stack
kubectl create secret generic solar-secrets \
  --namespace solar-staging \
  --from-literal=POSTGRES_PASSWORD='your-strong-password' \
  --from-literal=RabbitMQ__Password='your-rmq-password' \
  --from-literal=JwtSettings__SecretKey='at-least-32-character-secret-key-here' \
  --from-literal=JwtSettings__Issuer='https://api.staging.your-domain.com' \
  --from-literal=JwtSettings__Audience='https://api.staging.your-domain.com' \
  --from-literal=MailJet__ApiKey='xxx' \
  --from-literal=MailJet__ApiSecret='xxx' \
  --from-literal=ObjectStorage__AccessKey='minioadmin' \
  --from-literal=ObjectStorage__SecretKey='minio-strong-password' \
  --from-literal=GoogleOAuth__ClientId='xxx.apps.googleusercontent.com' \
  --from-literal=GoogleOAuth__ClientSecret='GOCSPX-xxx' \
  --from-literal=ADMIN_PASSWORD='Admin123@strong' \
  --from-literal=DISCORD_WEBHOOK='https://discord.com/api/webhooks/xxx'

# 2. Secret cho ghcr.io pull image trong namespace solar-staging
kubectl create secret docker-registry ghcr-pull \
  --namespace solar-staging \
  --docker-server=ghcr.io \
  --docker-username=YOUR_GITHUB_USER \
  --docker-password=YOUR_GITHUB_PAT

# 3. Secret cho ghcr.io trong namespace jenkins (cho kaniko push)
kubectl create secret docker-registry ghcr-credentials \
  --namespace jenkins \
  --docker-server=ghcr.io \
  --docker-username=YOUR_GITHUB_USER \
  --docker-password=YOUR_GITHUB_PAT
```

### E. Cài Jenkins

```bash
helm repo add jenkins https://charts.jenkins.io
helm repo update

helm install jenkins jenkins/jenkins \
  --namespace jenkins \
  -f deploy/jenkins/values.yaml

# Apply RBAC cho jenkins-agent (cho phép helm deploy)
kubectl apply -f deploy/jenkins/rbac.yaml

# Lấy admin password
kubectl exec --namespace jenkins -it svc/jenkins -c jenkins -- \
  cat /run/secrets/additional/chart-admin-password
```

Truy cập `https://jenkins.your-domain.com` → login `admin` + password vừa lấy.

### F. Configure Jenkins

Trong Jenkins UI:

1. **Manage Jenkins → Credentials → Global**:
   - `github-pat` (Username + Password): GitHub PAT
   - `discord-webhook` (Secret text): Discord webhook URL

2. **New Item → Multibranch Pipeline `solar-battery`**:
   - Branch source: GitHub
   - Owner: `your-org`
   - Repository: `SolarBatteryMaintainance`
   - Behaviors → Filter by name with regex: `^staging$`
   - Build Configuration: by Jenkinsfile

3. **GitHub repo → Settings → Webhooks → Add**:
   - Payload URL: `https://jenkins.your-domain.com/github-webhook/`
   - Content type: `application/json`
   - Events: Just push event

### G. Deploy lần đầu

```bash
# Pull subchart dependencies (1 lần)
cd deploy/helm/solar-battery
helm dependency update
cd -

# Tạo branch staging từ main
git checkout main
git pull
git checkout -b staging
git push origin staging
```

→ Jenkins trigger → 8-12 phút sau pod up → `https://api.staging.your-domain.com` live.

## Workflow hàng ngày (sau setup)

```bash
# Dev feature trên branch riêng
git checkout -b feature/voucher-system
# ...code...
git push origin feature/voucher-system

# Tạo PR vào main → GitHub Actions validate
# Merge PR → main

# Deploy lên staging
git checkout staging
git merge main
git push origin staging   # ← Jenkins trigger
```

## Cấu trúc thư mục

```
deploy/
├── helm/solar-battery/        # Helm chart — toàn stack chạy K8s
│   ├── Chart.yaml             # Metadata + 4 subchart dependency
│   ├── values.yaml            # Default config (dev)
│   ├── values-staging.yaml    # Override staging
│   ├── dashboards/            # 4 Grafana dashboard JSON
│   └── templates/
│       ├── _helpers.tpl
│       ├── shared/            # ConfigMap + Secret + NetworkPolicy + ResourceQuota
│       ├── services/          # 6 ASP.NET service (deployment + service + servicemonitor + hpa + pdb)
│       ├── infra/             # Postgres + Redis + RabbitMQ + Minio + backup CronJob
│       └── monitoring/        # Alert rules + Dashboards CM + AM-Discord relay
├── k8s/                       # Cluster-level setup (apply 1 lần)
│   ├── 00-namespaces.yaml
│   └── 01-cert-manager-issuer.yaml
└── jenkins/                   # Jenkins setup
    ├── values.yaml            # Helm values
    ├── agent-pod.yaml         # Build agent template (5 container)
    └── rbac.yaml              # ClusterRole jenkins-agent có quyền helm deploy

ci/scripts/
├── detect-changes.sh          # Skip service không đổi
└── smoke-test.sh              # Verify deploy success

Jenkinsfile                    # Pipeline 8 stage
```

## Lỗi thường gặp + cách fix

### Pod ImagePullBackOff
```bash
kubectl describe pod <name> -n solar-staging
# Nếu thấy "unauthorized": ghcr-pull secret chưa đúng
# Fix: kiểm tra secret + GitHub PAT có scope read:packages
```

### Pod CrashLoopBackOff
```bash
kubectl logs <pod> -n solar-staging --previous
# Đọc error gần nhất
```

### Helm install fail "no matches for kind ServiceMonitor"
- Subchart `kube-prometheus-stack` chưa cài trước → CRD chưa có.
- Fix: Set `monitoring.prometheusRules.enabled: false` trong values, install subchart trước, rồi enable lại.

### Cert chưa lấy được
```bash
kubectl get certificate -n solar-staging
kubectl describe certificate apigateway-tls -n solar-staging
# Thường do DNS chưa propagate hoặc rate limit
```

### Rollback thủ công
```bash
helm history solar -n solar-staging
helm rollback solar <REVISION> -n solar-staging
```

### Database connection exhausted
```bash
# Vào Postgres pod
kubectl exec -it postgres-0 -n solar-staging -- \
  psql -U postgres -c "SELECT count(*) FROM pg_stat_activity;"
# Nếu > 250: tăng postgres.maxConnections trong values
```

### Backup không chạy
```bash
kubectl get cronjob postgres-backup -n solar-staging
kubectl get jobs -n solar-staging | grep postgres-backup
# Logs:
kubectl logs job/postgres-backup-xxxxx -n solar-staging
```

## Disaster Recovery

### Restore Postgres từ backup
```bash
# 1. Download backup từ Minio
kubectl exec -it minio-0 -n solar-staging -- \
  mc cp minio/postgres-backups/auth_db_YYYYMMDD-HHMMSS.sql.gz /tmp/

kubectl cp solar-staging/minio-0:/tmp/auth_db_xxx.sql.gz ./backup.sql.gz

# 2. Restore vào Postgres
kubectl exec -i postgres-0 -n solar-staging -- \
  bash -c 'gunzip | psql -U postgres auth_db' < backup.sql.gz
```

### Toàn cluster crash, mất data
```bash
# Backup luôn ship lên Minio. Restore:
# 1. Recreate VPS + k3s (Phase C)
# 2. Apply secrets (Phase D)
# 3. Helm install lại
# 4. Restore Postgres từ Minio backup file mới nhất
```

## Kiểm soát lỗi đã thực hiện

12 lỗi đã được fix trong Helm chart + Jenkinsfile:

| # | Lỗi | Cách fix |
|---|---|---|
| 1 | Helm Secret xung đột manual | `lookup` skip nếu Secret đã exist |
| 2 | Subchart CRD timing | 2-phase install: CRD trước, full chart sau |
| 3 | Jenkins shallow clone | depth=50 + fetch origin/main |
| 4 | NetworkPolicy chặn Prometheus | Allow rule prometheus + kubelet + DNS egress |
| 5 | DB migration race | Database-init Job + service initContainers đợi DB riêng |
| 6 | Smoke test endpoint sai | Align /metrics |
| 7 | Postgres backup image bloat | postgres:16-alpine + scope retention |
| 8 | PrometheusRule label | release: monitoring match subchart |
| 9 | App start trước khi DB riêng tồn tại | postgres client initContainer kiểm tra database existence |
| 10 | imagePullPolicy default | Always cho floating tag |
| 11 | Resource OOM cluster | ResourceQuota cap |
| 12 | DB connection exhausted | max_connections 300 + pool 25/pod |

## Biến môi trường ghi chú

ASP.NET dùng `__` thay `:` trong env var (chuẩn Microsoft):
- `Logging__Console__FormatterName` ↔ `Logging:Console:FormatterName`
- `ConnectionStrings__AuthDb` ↔ `ConnectionStrings:AuthDb`
- `ConnectionStrings__FileStorageDb` ↔ `ConnectionStrings:FileStorageDb`
- `ConnectionStrings__BatteryDb` ↔ `ConnectionStrings:BatteryDb`
- `JwtSettings__SecretKey` ↔ `JwtSettings:SecretKey`
