# AI.md — Hướng dẫn xây dựng AI Module (GSU26SE55)

> File này tổng hợp **toàn bộ kiến thức và quy trình** để xây dựng AI Module cho dự án **Solar Lithium-ion Battery Maintenance Management System**.
> Đối tượng đọc: AI dev (chính), BE dev (tham khảo API contract), Leader (planning).
>
> **File liên quan:**
> - `.claude/rules/tech/ai.md` — coding convention bắt buộc (architecture, hyperparameters)
> - `overall.ai.md` — công việc backend phải làm để phục vụ AI
> - `overall.md §30, §48, §53.7, §57` — spec tích hợp đầy đủ

---

## Mục lục

- [Phần I — Bối cảnh & định vị](#phần-i--bối-cảnh--định-vị)
  - [1. AI Module trong dự án này không phải agent kiểu LLM](#1-ai-module-trong-dự-án-này-không-phải-agent-kiểu-llm)
  - [2. Hai nhiệm vụ cốt lõi](#2-hai-nhiệm-vụ-cốt-lõi)
  - [3. Vị trí AI Module: 1 repo độc lập](#3-vị-trí-ai-module-1-repo-độc-lập)
  - [4. Pattern kiến trúc: Hybrid threshold + AI](#4-pattern-kiến-trúc-hybrid-threshold--ai)
- [Phần II — Stack công nghệ](#phần-ii--stack-công-nghệ)
  - [5. Bảng tổng stack](#5-bảng-tổng-stack)
  - [6. Chi tiết từng tech & lý do chọn](#6-chi-tiết-từng-tech--lý-do-chọn)
- [Phần III — Cấu trúc repo](#phần-iii--cấu-trúc-repo)
  - [7. Cây thư mục `ai-module/`](#7-cây-thư-mục-ai-module)
  - [8. Naming conventions](#8-naming-conventions)
- [Phần IV — Dataset & Preprocessing](#phần-iv--dataset--preprocessing)
  - [9. NASA Ames dataset](#9-nasa-ames-dataset)
  - [10. Train/Val/Test split](#10-trainvaltest-split)
  - [11. Preprocessing pipeline](#11-preprocessing-pipeline)
  - [12. Labeling problem — nhãn đến từ đâu?](#12-labeling-problem--nhãn-đến-từ-đâu)
- [Phần V — Model A: LSTM / CNN-LSTM](#phần-v--model-a-lstm--cnn-lstm)
  - [13. Bài toán & lý thuyết](#13-bài-toán--lý-thuyết)
  - [14. RNN — ông tổ](#14-rnn--ông-tổ)
  - [15. LSTM — giải pháp vanishing gradient](#15-lstm--giải-pháp-vanishing-gradient)
  - [16. CNN — bắt pattern cục bộ](#16-cnn--bắt-pattern-cục-bộ)
  - [17. CNN-LSTM — kết hợp](#17-cnn-lstm--kết-hợp)
  - [18. Tại sao chọn CNN-LSTM](#18-tại-sao-chọn-cnn-lstm)
  - [19. Training config bắt buộc](#19-training-config-bắt-buộc)
- [Phần VI — Model B: Isolation Forest](#phần-vi--model-b-isolation-forest)
  - [20. Ý tưởng thuật toán](#20-ý-tưởng-thuật-toán)
  - [21. Hyperparameters](#21-hyperparameters)
  - [22. Tại sao chọn Isolation Forest](#22-tại-sao-chọn-isolation-forest)
- [Phần VII — Artifacts & versioning](#phần-vii--artifacts--versioning)
  - [23. 3 file artifact bắt buộc](#23-3-file-artifact-bắt-buộc)
  - [24. Versioning strategy](#24-versioning-strategy)
  - [25. Metadata bắt buộc](#25-metadata-bắt-buộc)
  - [26. Load artifacts khi FastAPI startup](#26-load-artifacts-khi-fastapi-startup)
- [Phần VIII — Serving (FastAPI)](#phần-viii--serving-fastapi)
  - [27. Cấu trúc FastAPI app](#27-cấu-trúc-fastapi-app)
  - [28. API contract](#28-api-contract)
  - [29. Inference pipeline](#29-inference-pipeline)
  - [30. Latency SLA](#30-latency-sla)
- [Phần IX — Vận hành](#phần-ix--vận-hành)
  - [31. Docker & docker-compose](#31-docker--docker-compose)
  - [32. Monitoring (Prometheus)](#32-monitoring-prometheus)
  - [33. Logging (structlog)](#33-logging-structlog)
  - [34. Testing](#34-testing)
- [Phần X — Advanced (Sprint 7+)](#phần-x--advanced-sprint-7)
  - [35. CI/CD model deployment](#35-cicd-model-deployment)
  - [36. Retraining trigger criteria](#36-retraining-trigger-criteria)
  - [37. Inference batching](#37-inference-batching)
  - [38. Multi-replica scaling (K8s HPA)](#38-multi-replica-scaling-k8s-hpa)
  - [39. Drift detection](#39-drift-detection)
  - [40. A/B testing](#40-ab-testing)
- [Phần XI — Quy trình xây dựng](#phần-xi--quy-trình-xây-dựng)
  - [41. 3 giai đoạn xây dựng](#41-3-giai-đoạn-xây-dựng)
  - [42. Roadmap theo Sprint](#42-roadmap-theo-sprint)
  - [43. Team responsibilities](#43-team-responsibilities)
- [Phần XII — Điều KHÔNG làm](#phần-xii--điều-không-làm)
- [Phần XIII — Tài liệu tham khảo](#phần-xiii--tài-liệu-tham-khảo)

---

# Phần I — Bối cảnh & định vị

## 1. AI Module trong dự án này không phải agent kiểu LLM

Đây là điểm hay nhầm lẫn nhất, cần làm rõ trước:

| Khái niệm | Bản chất | Có phải dự án bạn? |
|-----------|---------|-------------------|
| **AI agent (agentic AI)** | Hệ thống dùng LLM (GPT/Claude) tự lập kế hoạch, gọi tool, ra quyết định | ❌ Không |
| **ML inference service** | Model học từ data quá khứ → input → trả prediction theo công thức đã học | ✅ Chính là cái bạn xây |

→ Tên đúng: **ML model serving service**. Sau đây gọi gọn là **AI Module**.

## 2. Hai nhiệm vụ cốt lõi

Lấy từ `.claude/rules/tech/ai.md` và `overall.md §30.1`:

| Model | Bài toán | Output | Thuật toán | Kiểu học |
|-------|---------|--------|-----------|---------|
| **Model A** | Dự đoán SOH (State of Health) | `soh_percent` ∈ [0, 100] + confidence | LSTM / CNN-LSTM | Supervised |
| **Model B** | Phân loại bất thường | `Normal / Degrading / Failed` + anomaly_score | Isolation Forest | Unsupervised |

**Target metric bắt buộc:**
- SOH regression: **MAE < 2%**, **RMSE < 3%**
- Anomaly classification: **F1-score > 0.80**
- Inference latency: **< 100ms** cho P1 ticket SLA

**Tại sao tách 2 model:**
- SOH là số liên tục (regression), anomaly là class rời rạc (classification) — 2 bài toán khác nhau
- Tách → debug riêng, retrain riêng, fail riêng (1 model down không kéo cái còn lại sập)

## 3. Vị trí AI Module: 1 repo độc lập

```
┌─────────────────────────────┐         ┌──────────────────────────────┐
│   ai-module/ (repo riêng)   │         │   backend/ (repo này)        │
│   Python · AI team own      │   HTTP  │   .NET · BE team own         │
│                             │◄────────┤                              │
│   - Training scripts        │  REST   │   BatteryService             │
│   - FastAPI serving         │  :8000  │     └─ IAiInferenceClient   │
│   - models/weights/*.pth    │         │        (HttpClient + Polly)  │
│   - models/weights/*.pkl    │         │                              │
└─────────────────────────────┘         └──────────────────────────────┘
```

**Đặc điểm bắt buộc:**
- 2 repo độc lập, 2 team own riêng
- Giao tiếp **DUY NHẤT qua HTTP REST** — không share DB, không share code
- Backend không biết PyTorch là gì, AI module không biết .NET là gì
- Đây là microservice boundary chuẩn (`overall.md §30.9`)

## 4. Pattern kiến trúc: Hybrid threshold + AI

Đây là điểm quan trọng nhất — **AI không làm hết mọi việc** (`overall.md §30.2`):

```
SensorReading ingest (BE nhận từ pin)
    │
    ▼
ThresholdAnomalyDetector  ←─ rule-based, nhanh, free
    │                       (vd rule: V < 3.0V, T > 60°C)
    ├──[Normal]──→ skip, KHÔNG gọi AI (tiết kiệm cost)
    │
    └──[Threshold breached]──→ chỉ khi đó mới gọi AI
            │
            ▼
    AI ClassifyAnomaly(30 readings)
            │
            ├── Normal     → false-positive candidate (Staff review)
            ├── Degrading  → Alert severity = Warning
            └── Failed     → Alert severity = Critical
            │
            └──→ AI PredictSoh(window) → enrich Alert với SOH%
```

**Tác dụng:**

| Lợi ích | Giải thích |
|---------|-----------|
| Tiết kiệm AI compute | Rule xử lý 99% case rẻ tiền; AI chỉ chạy khi rule trigger |
| Giảm false-positive rule | Rule báo bất thường nhưng AI nói Normal → không alert ầm ĩ |
| Resilience | AI down → rule vẫn hoạt động → hệ thống không sập |
| Latency tổng | Request bình thường không bị đợi AI |

---

# Phần II — Stack công nghệ

## 5. Bảng tổng stack

```
┌─────────────────────────────────────────────────────────────────────┐
│ NGÔN NGỮ NỀN                                                        │
│   Python 3.11                                                       │
├─────────────────────────────────────────────────────────────────────┤
│ XỬ LÝ DỮ LIỆU                                                       │
│   NumPy        — mảng số nhanh (C bên dưới)                         │
│   pandas       — bảng dữ liệu                                       │
│   scipy.io     — đọc NASA .mat                                      │
├─────────────────────────────────────────────────────────────────────┤
│ TRAINING                                                            │
│   PyTorch          — LSTM/CNN-LSTM (deep learning)                  │
│   scikit-learn     — Isolation Forest + MinMaxScaler                │
│   joblib           — lưu/load .pkl                                  │
│   matplotlib, tqdm — visualize loss + progress bar                  │
├─────────────────────────────────────────────────────────────────────┤
│ SERVING                                                             │
│   FastAPI    — web framework REST                                   │
│   Uvicorn    — ASGI server chạy FastAPI                             │
│   Pydantic   — validate JSON request/response                       │
├─────────────────────────────────────────────────────────────────────┤
│ VẬN HÀNH                                                            │
│   Docker             — đóng gói container                           │
│   prometheus-client  — expose metrics /metrics                      │
│   structlog          — logging JSON có cấu trúc                     │
│   pytest             — test framework                               │
│   ruff               — linter + formatter                           │
└─────────────────────────────────────────────────────────────────────┘
```

## 6. Chi tiết từng tech & lý do chọn

### 6.1. Ngôn ngữ: Python 3.11

| Tác dụng | Lý do chọn |
|---------|-----------|
| Ngôn ngữ chính cho cả training và serving | 95% thế giới ML dùng Python. Mọi thư viện ML (PyTorch, scikit-learn) đều viết cho Python trước. Không có lựa chọn thực tế nào khác cho ML 2025 |

### 6.2. Xử lý dữ liệu

| Tech | Tác dụng | Lý do chọn |
|------|---------|-----------|
| **NumPy** | Mảng số đa chiều, phép toán vectorized | Python thuần xử lý mảng cực chậm. NumPy dùng C bên dưới → nhanh gấp 100 lần. Mọi lib ML đều build trên NumPy |
| **pandas** | Đọc/lọc/group dữ liệu dạng bảng | Như Excel/SQL trong code. Đọc CSV, lọc theo battery ID, group theo timestep — 1 dòng code thay vì 50 |
| **scipy.io** | Đọc file `.mat` của NASA | NASA Ames dataset xuất Matlab format. scipy có sẵn `loadmat()` |
| **scikit-learn MinMaxScaler** | Chuẩn hóa số về [0, 1] | Voltage = 3.7V, Current = 1.5A, Temp = 25°C — 3 thang đo khác nhau. Model sẽ thiên vị số to. Scale về [0,1] để 3 feature ngang nhau |

### 6.3. Training

| Tech | Tác dụng | Lý do chọn |
|------|---------|-----------|
| **PyTorch** | Framework deep learning, build LSTM/CNN-LSTM | 2 lựa chọn: PyTorch vs TensorFlow. PyTorch dễ debug (code như Python thường), thống trị academic, paper mới ra PyTorch trước. TensorFlow mạnh hơn cho mobile deploy nhưng dự án bạn deploy server → PyTorch hợp |
| **scikit-learn** | Lib ML cổ điển (Isolation Forest, RandomForest, SVM) | Cho thuật toán không cần deep learning. Nhẹ, nhanh, không cần GPU. Isolation Forest train trong 5 giây trên CPU |
| **joblib** | Lưu/load file `.pkl` | Nhanh hơn pickle thường, được scikit-learn khuyến nghị chính thức |
| **matplotlib** | Vẽ loss curve, confusion matrix | Cần nhìn model học có ổn không — loss giảm đều, không overfit |
| **tqdm** | Progress bar khi train | Train 50 epoch mất 30 phút trên CPU. Không có progress bar thì không biết còn bao lâu |

### 6.4. Serving

| Tech | Tác dụng | Lý do chọn |
|------|---------|-----------|
| **FastAPI** | Web framework REST | 3 lựa chọn: Flask (cổ, đơn giản), Django (nặng, có ORM), FastAPI (mới, nhanh, async, auto Swagger). FastAPI là chuẩn de-facto cho ML serving 2025 — Netflix, Uber, Microsoft đều dùng |
| **Uvicorn** | ASGI server chạy FastAPI | FastAPI cần ASGI server để nhận HTTP. Uvicorn nhanh nhất (dùng uvloop). Tương tự `dotnet run` của .NET |
| **Pydantic** | Validate JSON với schema | Đi kèm FastAPI. Định nghĩa `class PredictRequest(BaseModel)` — request sai format tự reject 422. Tương đương `[FromBody]` + DataAnnotations trong .NET |

### 6.5. Vận hành

| Tech | Tác dụng | Lý do chọn |
|------|---------|-----------|
| **Docker** | Đóng gói AI module thành container | BE dev không cần cài Python/PyTorch — chỉ `docker compose up`. Deploy production cũng 1 image |
| **prometheus-client** | Expose `/metrics` endpoint | BE đã có Prometheus stack → AI module phải tương thích để Grafana vẽ chung dashboard |
| **structlog** | Log JSON có cấu trúc | Log dạng `{"event": "predict", "asset_id": "...", "latency_ms": 45}` — query được bằng Loki/ELK |
| **pytest** | Test framework | Như xUnit trong .NET. Test preprocess, predict, benchmark latency < 100ms |
| **ruff** | Linter + formatter | Như ESLint+Prettier cho Python. Dự án có hook `.claude/hooks/ai/check-ruff.sh` chạy sau mỗi edit `.py` |

---

# Phần III — Cấu trúc repo

## 7. Cây thư mục `ai-module/`

```
ai-module/
├── src/
│   ├── train/                          ← scripts chỉ chạy local/Colab khi train
│   │   ├── __init__.py
│   │   ├── preprocess.py               ← NASA .mat → npy arrays + scaler.pkl
│   │   ├── dataset.py                  ← PyTorch Dataset class
│   │   ├── train_lstm.py               ← train CNN-LSTM → .pth
│   │   ├── train_isoforest.py          ← train Isolation Forest → .pkl
│   │   └── eval.py                     ← MAE/RMSE/F1 evaluation
│   ├── inference/                      ← runtime serving (production)
│   │   ├── __init__.py
│   │   ├── model_loader.py             ← load .pth + .pkl khi startup
│   │   ├── pipeline.py                 ← preprocess → predict → format
│   │   ├── labeling.py                 ← rule map (score, soh) → class
│   │   └── schemas.py                  ← Pydantic models cho request/response
│   ├── api/                            ← FastAPI app
│   │   ├── __init__.py
│   │   ├── main.py                     ← FastAPI() + lifespan load models
│   │   ├── deps.py                     ← shared dependencies
│   │   └── routes/
│   │       ├── __init__.py
│   │       ├── predict.py              ← POST /predict/soh + /batch
│   │       ├── classify.py             ← POST /classify/anomaly
│   │       └── health.py               ← GET /health
│   ├── core/                           ← infrastructure cross-cutting
│   │   ├── __init__.py
│   │   ├── config.py                   ← env vars (MODEL_VERSION, PATHS)
│   │   ├── logging.py                  ← structlog setup
│   │   └── metrics.py                  ← prometheus counters/histograms
│   └── models/                         ← model architecture definitions
│       ├── __init__.py
│       └── soh_predictor.py            ← class SOHPredictor(nn.Module)
├── models/
│   ├── weights/                        ← ARTIFACTS — commit Git
│   │   ├── current → v1.0/             ← symlink (sprint 7+)
│   │   └── v1.0/
│   │       ├── scaler.pkl
│   │       ├── soh_lstm.pth
│   │       └── isolation_forest.pkl
│   └── metadata/
│       └── versions.json               ← registry: metrics, training data, hash
├── tests/
│   ├── conftest.py                     ← pytest fixtures
│   ├── test_preprocess.py
│   ├── test_inference_pipeline.py
│   ├── test_api_predict.py             ← TestClient FastAPI
│   ├── test_api_classify.py
│   ├── test_labeling.py
│   └── test_latency_benchmark.py       ← 100 runs, assert P95 < 100ms
├── notebooks/                          ← exploratory (không deploy)
│   ├── 01_explore_nasa.ipynb
│   ├── 02_train_lstm_colab.ipynb
│   └── 03_evaluate_models.ipynb
├── scripts/
│   ├── download_nasa.sh                ← tải dataset
│   ├── train_all.sh                    ← preprocess + train cả 2 model
│   └── bench_latency.sh                ← chạy benchmark trước commit
├── .github/
│   └── workflows/
│       ├── ci.yml                      ← ruff + pytest trên PR
│       └── deploy-model.yml            ← CI/CD deploy artifact (Sprint 7+)
├── docs/
│   ├── api-contract.md                 ← schema request/response (ký với BE)
│   ├── training-guide.md
│   └── deployment.md
├── Dockerfile
├── docker-compose.yml                  ← chỉ AI module standalone (dev)
├── requirements.txt                    ← pin version chính xác
├── requirements-dev.txt                ← thêm pytest, ruff, ipykernel
├── pyproject.toml                      ← ruff config + project metadata
├── .gitignore                          ← exclude __pycache__, .ipynb_checkpoints
├── .gitattributes                      ← Git LFS nếu .pth > 100MB
└── README.md
```

## 8. Naming conventions

| Loại | Pattern | Ví dụ |
|------|---------|-------|
| Module file | `snake_case.py` | `model_loader.py` |
| Class | `PascalCase` | `SOHPredictor`, `PredictRequest` |
| Function | `snake_case` | `compute_soh()`, `load_scaler()` |
| Constant | `UPPER_SNAKE_CASE` | `MODEL_VERSION`, `WINDOW_SIZE` |
| Pydantic schema | `{Verb}{Noun}` | `PredictSohRequest`, `PredictSohResponse` |
| Test file | `test_{module}.py` | `test_preprocess.py` |
| Artifact | `{model}_v{ver}.{ext}` | `soh_lstm_v1.0.pth`, `isolation_forest_v1.0.pkl` |

---

# Phần IV — Dataset & Preprocessing

## 9. NASA Ames dataset

| Item | Giá trị |
|------|---------|
| Nguồn | NASA Ames Prognostics Center of Excellence |
| Link tải | xem `.claude/docs/ai-datasets.md` |
| Format | `.mat` (Matlab) |
| Battery IDs dùng | B0005, B0006, B0007, B0018 |
| Capacity nominal | 2.0 Ah |
| Operations | charge / discharge / impedance cycles |
| Features | voltage (V), current (A), temperature (°C), time |

**Backup dataset:** CALCE (University of Maryland) — chỉ dùng nếu NASA không đủ.

## 10. Train/Val/Test split

**Quy tắc bắt buộc** (`.claude/rules/tech/ai.md`):

| Split | Battery IDs | % data | Ghi chú |
|-------|-------------|--------|---------|
| Train | B0005, B0006, B0007 | ~70% | Fit scaler ở đây |
| Val | B0018 (70% đầu timestep) | ~15% | Early stopping |
| Test | B0018 (30% cuối timestep) | ~15% | Verify metric |

**Quy tắc tuyệt đối:**
- Chia theo **battery ID trước**, sau đó chia timestep cho B0018
- **KHÔNG xáo trộn ngẫu nhiên** (sẽ leak data từ test sang train)
- Random seed = **42** mọi nơi (reproducibility)

## 11. Preprocessing pipeline

**Mục tiêu:** Biến NASA `.mat` thô → mảng số chuẩn hóa model "ăn" được.

```
NASA .mat file
    ↓ scipy.io.loadmat()
Dict Python (raw)
    ↓ extract cycles (pandas)
DataFrame: [cycle, time, V, I, T, capacity]
    ↓ compute target SOH = capacity / 2.0 × 100
DataFrame: [..., SOH%]
    ↓ sliding window size 30
Tensors: X (N, 30, 3), y (N,)
    ↓ MinMaxScaler.fit(X_train).transform(X_*)
Tensors normalized [0, 1]
    ↓ save scaler.pkl (commit Git)
    ↓ save X_train.npy, y_train.npy, ...
Ready for training
```

**Input features bắt buộc (3):**
1. Voltage (V)
2. Current (A)
3. Temperature (°C)

**Window size:** 30 timestep (cố định, không đổi giữa train và inference).

## 12. Labeling problem — nhãn đến từ đâu?

Đây là phần dễ hiểu nhầm nhất. **NASA dataset chỉ có nhãn SOH%, KHÔNG có nhãn "Normal/Degrading/Failed"**.

### 12.1. Model A (LSTM) — Supervised, nhãn từ NASA

Công thức: `SOH% = (capacity_hiện_tại / 2.0) × 100`

Ví dụ pin B0005:
| Cycle | Capacity (Ah) | SOH% (nhãn target) |
|-------|---------------|-------------------|
| 1 | 1.98 | 99.0% |
| 50 | 1.85 | 92.5% |
| 100 | 1.72 | 86.0% |
| 150 | 1.60 | 80.0% |
| 168 | 1.40 | 70.0% (EOL) |

→ Nhãn là **số liên tục lấy trực tiếp từ phép đo NASA**, không cần đánh tay.

### 12.2. Model B (Isolation Forest) — Unsupervised, KHÔNG cần nhãn

Isolation Forest **không học từ label** — nó học "thế nào là bình thường":
1. Show toàn bộ data train (30-timestep windows)
2. Tự xây 100 cây quyết định ngẫu nhiên → học density
3. `contamination=0.1` = "bảo nó: 10% là bất thường, tự tìm"
4. Output: **anomaly score** (số liên tục), KHÔNG phải class

### 12.3. 3 class "Normal/Degrading/Failed" — Rule mapping (con người viết)

3 class được **suy ra** từ score + SOH bằng rule (`.claude/rules/tech/ai.md`):

```python
def classify_anomaly(score: float, soh: float) -> str:
    """
    score: Isolation Forest decision_function (âm hơn = bất thường hơn)
    soh:   SOH% từ LSTM
    """
    if score > -0.1:
        return "Normal"
    elif score > -0.3 or soh >= 80:
        return "Degrading"
    else:
        return "Failed"
```

**Logic dựa trên 2 nguồn:**

| Nguồn | Cung cấp |
|-------|---------|
| **Industry SOH threshold** | ≥ 80% = Normal · 60-80% = Degrading · < 60% = Failed (chuẩn EV Tesla/BYD) |
| **Isolation Forest score** | Pattern bất thường đột ngột (vd: voltage spike) chưa phản ánh trong SOH |

### 12.4. Tương lai: Semi-supervised với Staff feedback

Theo `overall.md §30.12 + §48.1`:
- Staff resolve ticket → confirm AI đúng/sai → lưu `StaffFeedback`
- Sau vài tháng có **ground-truth label** → AI team retrain supervised
- Đây là **feedback loop** chính — `overall.md §48.3` export Parquet hàng tháng

---

# Phần V — Model A: LSTM / CNN-LSTM

## 13. Bài toán & lý thuyết

Cho 30 phép đo liên tiếp → đoán SOH%:
```
t1   t2   t3   ... t30        → SOH%
V:3.7 V:3.7 V:3.6 ... V:3.5   → 87.3
I:1.5 I:1.4 I:1.5 ... I:1.3
T:25  T:26  T:26  ... T:28
```

Đây là **time-series regression** — thứ tự quan trọng, output liên tục.

### Neural network cơ bản
Chuỗi phép nhân-cộng: `input × weights + bias → activation → output`. Training = đoán sai → điều chỉnh weights bằng backpropagation → lặp 50 epoch → weights hội tụ.

**Vấn đề:** Network thường (feedforward) **không có khái niệm thời gian** — coi 30 input như 30 số riêng lẻ → không phù hợp time-series.

## 14. RNN — ông tổ

**Ý tưởng:** Cho network vòng lặp, mỗi bước nhận input + memory bước trước:
```
input(t1) → [RNN] → output(t1)
                ↓ (memory)
input(t2) → [RNN] → output(t2)
                ↓
...
input(t30) → [RNN] → SOH%
```

**Vấn đề chí mạng — vanishing gradient:** Chuỗi > 20 bước → thông tin đầu chuỗi loãng dần qua từng phép nhân → cuối chuỗi quên sạch. RNN gần như không ai dùng nữa.

## 15. LSTM — giải pháp vanishing gradient

LSTM = RNN nâng cấp với **3 cổng** trong mỗi cell:

```
                ┌──────────────────────────────┐
                │   LSTM Cell                  │
input(t) ─────→ │                              │
                │ ① Forget Gate — quên gì?    │
prev_memory ──→ │ ② Input Gate  — nhận gì?    │ ──→ new_memory
                │ ③ Output Gate — xuất gì?    │ ──→ output(t)
                └──────────────────────────────┘
```

**Analogy thư ký ghi chép:**
- **Forget gate:** "Thông tin cũ còn cần không?" → spike 10 phút trước hết quan trọng → xóa
- **Input gate:** "Thông tin mới đáng ghi không?" → temp tăng 5°C đột ngột → ghi
- **Output gate:** "Báo cáo gì lúc này?" → tổng hợp memory + input

**3 cổng học được trong train** — model tự phát hiện feature nào quan trọng nhớ lâu.

**Kết quả với pin:**
- Nhớ xu hướng dài hạn (voltage tụt dần 30 timestep → xuống cấp)
- Nhớ sự kiện đầu chuỗi (t3 spike → t30 vẫn nhớ)
- Không bị vanishing gradient nhờ memory cell chạy thẳng qua các bước

## 16. CNN — bắt pattern cục bộ

CNN gốc cho ảnh, nhưng dùng được cho time-series.

**Ý tưởng — sliding window:**
```
Voltage: 3.7  3.7  3.6  3.5  3.6  3.7  3.7  3.5  3.4  3.3 ...
         ╰─ kernel size 3 ─╯
              ╰─ trượt ─╯
                   ╰─ trượt ─╯
```

"Kính lúp" (kernel) trượt qua chuỗi, mỗi vị trí phát hiện 1 pattern cục bộ:
- Kernel A: "sụt áp đột ngột 3 timestep" → kích hoạt khi gặp `3.7 → 3.5 → 3.3`
- Kernel B: "voltage ổn định"
- Kernel C: "voltage tăng"

CNN có nhiều kernel song song (32 kernel) → output 32 feature map.

**Điểm mạnh:** nhanh hơn LSTM, bắt local pattern cực tốt.
**Điểm yếu:** không có khái niệm thứ tự dài hạn — kernel chỉ nhìn 3-5 timestep cùng lúc.

## 17. CNN-LSTM — kết hợp

Kiến trúc bạn dùng (theo `.claude/rules/tech/ai.md`):

```
Input: (batch, 30, 3)
   ↓
┌─────────────────────────────────────────────┐
│ CNN Block — bắt pattern cục bộ              │
│   Conv1D(in=3, out=32, kernel=3, padding=1) │
│   ReLU activation                            │
│   MaxPool(kernel=2)                          │
│   → Output: (batch, 32, 15)                  │
└─────────────────────────────────────────────┘
   ↓
┌─────────────────────────────────────────────┐
│ LSTM Block — học xu hướng dài hạn          │
│   LSTM(input=32, hidden=64, layers=2,        │
│        dropout=0.2)                          │
│   → Hidden state cuối: (batch, 64)          │
└─────────────────────────────────────────────┘
   ↓
┌─────────────────────────────────────────────┐
│ Fully Connected — quy về 1 số              │
│   Linear(64 → 32) → ReLU → Dropout(0.2)     │
│   Linear(32 → 1)                             │
│   → Output: SOH% (scalar)                   │
└─────────────────────────────────────────────┘
```

**Logic 2 tầng:**
1. **CNN nhìn trước:** "30 timestep có pattern cục bộ nào đáng chú ý?" → trích 32 đặc trưng
2. **LSTM nhìn sau:** "Cho chuỗi 32-feature, xu hướng dài hạn là gì?"

**Analogy bác sĩ chẩn đoán:**
- CNN = y tá ghi nhận triệu chứng cụ thể (huyết áp 8h, sốt 10h)
- LSTM = bác sĩ tổng hợp diễn biến theo thời gian (sốt + huyết áp tăng 3 ngày → chẩn đoán)

### Code skeleton (theo `.claude/rules/tech/ai.md`)

```python
import torch.nn as nn

class SOHPredictor(nn.Module):
    def __init__(self):
        super().__init__()
        # CNN block
        self.conv1 = nn.Conv1d(in_channels=3, out_channels=32, kernel_size=3, padding=1)
        self.relu  = nn.ReLU()
        self.pool  = nn.MaxPool1d(kernel_size=2)
        # LSTM block
        self.lstm  = nn.LSTM(input_size=32, hidden_size=64, num_layers=2,
                             batch_first=True, dropout=0.2)
        # FC head
        self.fc1   = nn.Linear(64, 32)
        self.fc2   = nn.Linear(32, 1)
        self.dropout = nn.Dropout(0.2)

    def forward(self, x):
        x = x.permute(0, 2, 1)
        x = self.pool(self.relu(self.conv1(x)))
        x = x.permute(0, 2, 1)
        _, (h_n, _) = self.lstm(x)
        x = h_n[-1]
        x = self.dropout(self.relu(self.fc1(x)))
        return self.fc2(x).squeeze(-1)
```

## 18. Tại sao chọn CNN-LSTM

| Kiến trúc | Ưu | Nhược | Phù hợp? |
|-----------|-----|-------|---------|
| Thuần RNN | Đơn giản | Vanishing gradient | ❌ Lạc hậu |
| Thuần LSTM | Nhớ dài hạn tốt | Chậm, kém local pattern | ⚠️ Yếu spike đột ngột |
| Thuần CNN 1D | Nhanh, local pattern | Mù xu hướng dài hạn | ⚠️ Yếu degradation từ từ |
| **CNN-LSTM** ✅ | Cả 2 ưu điểm | Phức tạp hơn 1 chút | ✅ Đúng cho SOH |
| Transformer | Mạnh nhất hiện tại | Cần GPU lớn, dataset to | ❌ Overkill capstone |

## 19. Training config bắt buộc

Theo `.claude/rules/tech/ai.md`:

| Tham số | Giá trị | Tác dụng |
|---------|---------|---------|
| Window size | 30 timestep | Cửa sổ input |
| Input features | 3 (V, I, T) | — |
| Normalization | MinMaxScaler [0, 1] | Fit train set, save `scaler.pkl` |
| Optimizer | **Adam** | Default tốt nhất 90% trường hợp — tự điều chỉnh learning rate |
| Learning rate | **1e-3** | Bước nhảy weight mỗi update |
| Loss | **MSELoss** | Phạt sai số lớn nặng hơn |
| Epochs | **50** | Số lần duyệt full dataset |
| Patience | **10** | Early stopping nếu val loss không giảm 10 epoch |
| Batch size | **32** | Số sample / lần update weight |
| Random seed | **42** | Reproducible |

**Môi trường train:**
- Local CPU: 2-3 giờ
- Google Colab miễn phí (GPU T4): ~10 phút → **khuyến nghị**

---

# Phần VI — Model B: Isolation Forest

## 20. Ý tưởng thuật toán

Build nhiều cây quyết định **ngẫu nhiên**. Tại mỗi cây:
- Chọn ngẫu nhiên 1 feature
- Chọn ngẫu nhiên 1 ngưỡng split
- Tách dữ liệu thành 2 nhánh
- Lặp đến khi mỗi sample 1 lá

**Insight:** Sample bất thường **bị tách sớm** (độ sâu nhỏ) vì khác đa số. Sample bình thường nằm vùng đông đúc → cần nhiều split mới tách được → độ sâu lớn.

→ Score = trung bình độ sâu qua nhiều cây. **Score thấp = bất thường.**

## 21. Hyperparameters

| Param | Giá trị | Tác dụng |
|-------|---------|---------|
| `contamination` | **0.1** | Ước tính 10% data là bất thường (NASA) |
| `n_estimators` | **100** | Số cây trong forest |
| `random_state` | **42** | Reproducible |

```python
from sklearn.ensemble import IsolationForest
iso_forest = IsolationForest(
    contamination=0.1,
    n_estimators=100,
    random_state=42,
)
iso_forest.fit(X_train_features)
```

**Feature input:** statistics extracted từ 30-timestep window — mean, std, min, max của V/I/T → 12 features per window.

## 22. Tại sao chọn Isolation Forest

| Lựa chọn | Đánh giá |
|---------|---------|
| **Isolation Forest** ✅ | Train 5s trên CPU, không cần GPU, tốt cho anomaly |
| One-Class SVM | Chậm hơn 100x với dataset lớn |
| Autoencoder | Cần deep learning, overkill |
| LSTM Autoencoder | Quá phức tạp cho capstone |

---

# Phần VII — Artifacts & versioning

## 23. 3 file artifact bắt buộc

Sau khi train, có **3 file đều phải commit Git**:

```
ai-module/models/weights/v1.0/
├── scaler.pkl                    ← MinMaxScaler đã fit trên train set
├── soh_lstm.pth                  ← Trọng số LSTM/CNN-LSTM (file "trí khôn")
└── isolation_forest.pkl          ← Trọng số Isolation Forest
```

**Tác dụng:**
- `.pth` = trọng số neural network sau khi học
- `.pkl` = trọng số Isolation Forest + scaler
- 3 file **luôn đi cùng nhau** — bất kỳ file nào sai version → predict sai mà không báo lỗi

## 24. Versioning strategy

Theo `overall.md §57.4`:

```
ai-module/models/
├── weights/
│   ├── current → symlink to v1.2/    ← rollback nhanh (đổi symlink)
│   ├── v1.0/
│   │   ├── scaler.pkl
│   │   ├── soh_lstm.pth
│   │   └── isolation_forest.pkl
│   ├── v1.1/
│   └── v1.2/
└── metadata/
    └── versions.json                  ← registry: metrics, training data, hash
```

**Quy tắc:**
- `v1.0 → v1.1`: retrain cùng architecture, khác data/hyperparameter
- `v1.x → v2.0`: thay đổi architecture (vd: thêm attention layer)
- Cả 3 artifact phải commit cùng 1 git commit
- Symlink `current` → rollback chỉ đổi symlink, không cần rebuild image

**Git LFS** nếu `.pth > 100MB`:
```bash
git lfs install
git lfs track "models/weights/*.pth"
git lfs track "models/weights/*.pkl"
git add .gitattributes
```

> Với LSTM nhỏ (< 50MB) và Isolation Forest (< 5MB): commit trực tiếp, không cần LFS cho scope capstone.

## 25. Metadata bắt buộc

**Lưu artifact kèm metadata (.claude/rules/tech/ai.md):**

```python
import torch, joblib

# Scaler
joblib.dump({
    "scaler": scaler,
    "version": "1.0",
    "trained_on": ["B0005", "B0006", "B0007"],
    "features": ["voltage", "current", "temperature"],
}, "models/weights/v1.0/scaler.pkl")

# LSTM
torch.save({
    "model_state_dict": soh_model.state_dict(),
    "version": "1.0",
    "window_size": 30,
    "input_features": 3,
    "architecture": "CNN-LSTM",
    "metrics": {"mae": 1.8, "rmse": 2.5},
}, "models/weights/v1.0/soh_lstm.pth")

# Isolation Forest
joblib.dump({
    "model": iso_forest,
    "version": "1.0",
    "contamination": 0.1,
    "n_estimators": 100,
}, "models/weights/v1.0/isolation_forest.pkl")
```

`versions.json` registry:
```json
{
  "versions": [
    {
      "version": "1.0",
      "released_at": "2026-07-05",
      "metrics": {"mae": 1.8, "rmse": 2.5, "f1": 0.83},
      "training_data": "NASA_Ames_B0005-B0007",
      "artifact_hashes": {
        "scaler.pkl": "sha256:...",
        "soh_lstm.pth": "sha256:...",
        "isolation_forest.pkl": "sha256:..."
      }
    }
  ],
  "current": "1.0"
}
```

## 26. Load artifacts khi FastAPI startup

**Bắt buộc verify file tồn tại + version match TRƯỚC khi serve** (`.claude/rules/tech/ai.md`):

```python
# src/inference/model_loader.py
import os, joblib, torch
from src.core.config import settings

SCALER_PATH = f"models/weights/v{settings.MODEL_VERSION}/scaler.pkl"
LSTM_PATH = f"models/weights/v{settings.MODEL_VERSION}/soh_lstm.pth"
ISO_PATH = f"models/weights/v{settings.MODEL_VERSION}/isolation_forest.pkl"

def load_all():
    for path, label in [(SCALER_PATH, "Scaler"), (LSTM_PATH, "LSTM"), (ISO_PATH, "IsolationForest")]:
        assert os.path.exists(path), (
            f"[STARTUP] {label} not found at '{path}'. "
            "Run training script and commit artifacts before starting."
        )

    scaler_art = joblib.load(SCALER_PATH)
    assert scaler_art["version"] == settings.MODEL_VERSION, \
        f"Scaler version mismatch: expected {settings.MODEL_VERSION}, got {scaler_art['version']}"

    ckpt = torch.load(LSTM_PATH, map_location="cpu")
    assert ckpt["version"] == settings.MODEL_VERSION

    soh_model = SOHPredictor()
    soh_model.load_state_dict(ckpt["model_state_dict"])
    soh_model.eval()  # tắt dropout, BatchNorm in eval mode

    iso_art = joblib.load(ISO_PATH)

    return scaler_art["scaler"], soh_model, iso_art["model"]
```

**Tại sao quan trọng:** Nếu scaler v1.1 nhưng model v1.0 → inference âm thầm predict sai. Assert bắt lỗi ngay startup → fail-fast.

---

# Phần VIII — Serving (FastAPI)

## 27. Cấu trúc FastAPI app

```python
# src/api/main.py
from contextlib import asynccontextmanager
from fastapi import FastAPI
from src.inference.model_loader import load_all
from src.api.routes import predict, classify, health

@asynccontextmanager
async def lifespan(app: FastAPI):
    # STARTUP — load model 1 lần
    scaler, lstm, iso = load_all()
    app.state.scaler = scaler
    app.state.lstm = lstm
    app.state.iso = iso
    yield
    # SHUTDOWN — cleanup nếu cần

app = FastAPI(
    title="Solar Battery AI",
    version="1.0.0",
    lifespan=lifespan,
)

app.include_router(predict.router, prefix="/predict", tags=["predict"])
app.include_router(classify.router, prefix="/classify", tags=["classify"])
app.include_router(health.router, prefix="/health", tags=["health"])
```

**Chạy production:**
```bash
uvicorn src.api.main:app --host 0.0.0.0 --port 8000 --workers 1
```

> `workers=1` vì model load vào memory mỗi worker (tốn RAM). Scale bằng K8s replica, không bằng worker.

## 28. API contract

Đây là **biên giới** giữa AI và BE — **ký kết sớm, không đổi**. Lưu tại `ai-module/docs/api-contract.md`.

### 28.1. POST /predict/soh

**Request:**
```json
{
  "asset_id": "550e8400-e29b-41d4-a716-446655440000",
  "readings": [
    {
      "time": "2026-07-05T10:00:00Z",
      "voltage": 3.72,
      "current": 1.50,
      "temperature": 25.3
    },
    // ... 30 readings total
  ]
}
```

**Response 200:**
```json
{
  "soh_percent": 87.3,
  "confidence": 0.92,
  "model_version": "1.0",
  "latency_ms": 45,
  "input_window_start_utc": "2026-07-05T10:00:00Z",
  "input_window_end_utc": "2026-07-05T10:29:00Z"
}
```

**Response 422 (invalid input):**
```json
{
  "detail": [
    {"loc": ["body", "readings"], "msg": "Expected 30 readings, got 25"}
  ]
}
```

### 28.2. POST /classify/anomaly

**Request:** giống `/predict/soh`.

**Response 200:**
```json
{
  "classification": "Degrading",
  "anomaly_score": -0.22,
  "soh_percent": 78.5,
  "confidence": 0.87,
  "model_version": "1.0",
  "latency_ms": 52
}
```

### 28.3. POST /predict/soh/batch (Sprint 7+, `overall.md §57.3`)

**Request:**
```json
{
  "items": [
    {"asset_id": "...", "readings": [...]},
    // up to 32 items
  ]
}
```

**Response 200:**
```json
{
  "results": [
    {"asset_id": "...", "soh_percent": 87.3, "confidence": 0.92},
    ...
  ],
  "batch_size": 32,
  "total_latency_ms": 80
}
```

**Tác dụng:** 32 items × 1 forward pass thay vì 32 forward passes → throughput cao 32×.

### 28.4. GET /health

```json
{
  "status": "ok",
  "model_version": "1.0",
  "scaler_loaded": true,
  "lstm_loaded": true,
  "isolation_forest_loaded": true,
  "uptime_seconds": 3600
}
```

## 29. Inference pipeline

```
HTTP POST /predict/soh
    ↓
FastAPI nhận body
    ↓
Pydantic validate (30 readings, types đúng)
    ↓
Extract V/I/T arrays → np.array shape (30, 3)
    ↓
scaler.transform(arr) → normalized
    ↓
torch.tensor(...).unsqueeze(0) → shape (1, 30, 3)
    ↓
with torch.no_grad(): model(tensor) → scalar
    ↓
Format response (round 2 decimal, add confidence)
    ↓
Update Prometheus metrics (latency, success counter)
    ↓
Return JSON
```

**Phải xong < 100ms** cho P1 ticket.

```python
# src/inference/pipeline.py — pseudocode
def predict_soh(readings: list[Reading], scaler, model) -> SohResult:
    start = time.perf_counter()

    arr = np.array([[r.voltage, r.current, r.temperature] for r in readings])
    arr_scaled = scaler.transform(arr)
    tensor = torch.tensor(arr_scaled, dtype=torch.float32).unsqueeze(0)

    with torch.no_grad():
        soh = model(tensor).item()

    latency_ms = int((time.perf_counter() - start) * 1000)

    return SohResult(
        soh_percent=round(soh, 2),
        confidence=compute_confidence(soh),
        model_version=settings.MODEL_VERSION,
        latency_ms=latency_ms,
    )
```

## 30. Latency SLA

Theo `.claude/rules/tech/ai.md`:

| Priority | Yêu cầu | Lý do |
|----------|---------|-------|
| P1 Critical (4h SLA) | < 100ms | Alert real-time |
| P2/P3 batch | < 500ms | Acceptable batch |

**Benchmark bắt buộc trước commit model:**
```python
# tests/test_latency_benchmark.py
def test_inference_latency_under_100ms(client, sample_input):
    latencies = []
    for _ in range(100):
        start = time.perf_counter()
        resp = client.post("/predict/soh", json=sample_input)
        latencies.append((time.perf_counter() - start) * 1000)
    avg = sum(latencies) / len(latencies)
    p95 = sorted(latencies)[95]
    assert avg < 100, f"Avg latency {avg:.1f}ms > 100ms"
    assert p95 < 150, f"P95 latency {p95:.1f}ms > 150ms"
```

---

# Phần IX — Vận hành

## 31. Docker & docker-compose

### 31.1. Dockerfile

```dockerfile
FROM python:3.11-slim

WORKDIR /app

# System deps cho scipy, torch
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential curl && \
    rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY src/ ./src/
COPY models/ ./models/

ENV PYTHONUNBUFFERED=1 \
    MODEL_VERSION=1.0 \
    PORT=8000

EXPOSE 8000

HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
    CMD curl -f http://localhost:${PORT}/health || exit 1

CMD ["uvicorn", "src.api.main:app", "--host", "0.0.0.0", "--port", "8000", "--workers", "1"]
```

### 31.2. docker-compose (chung với BE, `overall.md §30.9`)

```yaml
services:
  ai-module:
    build:
      context: ./ai-module
      dockerfile: Dockerfile
    container_name: solar-ai
    environment:
      MODEL_VERSION: "1.0"
      SCALER_PATH: /app/models/weights/v1.0/scaler.pkl
      LSTM_PATH: /app/models/weights/v1.0/soh_lstm.pth
      ISO_FOREST_PATH: /app/models/weights/v1.0/isolation_forest.pkl
    ports: ["8000:8000"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8000/health"]
      interval: 10s
      retries: 5
    networks: [solar-net]

  battery-service:
    environment:
      AI_BASE_URL: http://ai-module:8000
    depends_on:
      ai-module:
        condition: service_healthy
    networks: [solar-net]

networks:
  solar-net:
```

## 32. Monitoring (Prometheus)

Expose `/metrics` (theo `overall.md §30.10`):

```python
# src/core/metrics.py
from prometheus_client import Counter, Histogram, Gauge

inference_latency = Histogram(
    "ai_inference_latency_milliseconds",
    "AI inference latency",
    labelnames=["endpoint"],
    buckets=(10, 25, 50, 75, 100, 150, 200, 500, 1000),
)

inference_total = Counter(
    "ai_inference_total",
    "Total inference calls",
    labelnames=["endpoint", "status"],  # status=success|timeout|error
)

model_version_info = Gauge(
    "ai_model_version_info",
    "Current model version",
    labelnames=["version"],
)

inference_queue_depth = Gauge(
    "ai_inference_queue_depth",
    "Pending inference requests (for HPA)",
)
```

**Alert rule (AlertManager):**
- `ai_inference_latency_p95 > 100ms` for 5 phút → notify team
- `rate(ai_inference_total{status="error"}[5m]) > 0.05` → notify

## 33. Logging (structlog)

```python
# src/core/logging.py
import structlog

structlog.configure(
    processors=[
        structlog.processors.add_log_level,
        structlog.processors.TimeStamper(fmt="iso"),
        structlog.processors.JSONRenderer(),
    ]
)

log = structlog.get_logger()

# Usage trong handler
log.info("predict_soh",
    asset_id=str(asset_id),
    soh_percent=result.soh_percent,
    latency_ms=result.latency_ms)
```

Output:
```json
{"level":"info","timestamp":"2026-07-05T10:00:00.123Z","event":"predict_soh","asset_id":"...","soh_percent":87.3,"latency_ms":45}
```

→ Query được bằng Loki/ELK.

## 34. Testing

### 34.1. Unit tests bắt buộc

```
tests/
├── test_preprocess.py
│   - test_loadmat_returns_dict
│   - test_compute_soh_correct
│   - test_sliding_window_size_30
│   - test_minmax_scaler_fits_train_only
├── test_inference_pipeline.py
│   - test_predict_soh_with_valid_input
│   - test_classify_anomaly_returns_3_classes
│   - test_labeling_rule_correctly
├── test_api_predict.py (TestClient)
│   - test_predict_soh_returns_200
│   - test_predict_soh_invalid_returns_422
│   - test_predict_soh_missing_readings_returns_422
├── test_api_classify.py
│   - test_classify_anomaly_returns_correct_schema
├── test_labeling.py
│   - test_score_above_neg_01_returns_normal
│   - test_score_below_neg_03_and_low_soh_returns_failed
│   - test_score_neg_02_with_high_soh_returns_degrading
└── test_latency_benchmark.py
    - test_avg_inference_under_100ms (100 runs)
    - test_p95_under_150ms
```

### 34.2. Coverage

Target: **≥ 85%** (`.claude/rules/workflow.md` § Quality Gates).

```bash
pytest tests/ -v --cov=src --cov-report=term --cov-fail-under=85
```

### 34.3. Performance test trước commit

```bash
# scripts/bench_latency.sh
#!/bin/bash
set -e
uvicorn src.api.main:app --port 8001 &
PID=$!
sleep 5

pytest tests/test_latency_benchmark.py -v

kill $PID
```

---

# Phần X — Advanced (Sprint 7+)

## 35. CI/CD model deployment

`ai-module/.github/workflows/deploy-model.yml` (theo `overall.md §57.1`):

```yaml
name: Deploy Model
on:
  push:
    tags: ['models/v*']
  workflow_dispatch:

jobs:
  validate:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Run validation tests
        run: |
          pytest tests/test_inference_pipeline.py -v
          python -c "from src.train.eval import validate_metrics; validate_metrics(mae_threshold=2.0, f1_threshold=0.80)"

  build:
    needs: validate
    runs-on: ubuntu-latest
    steps:
      - uses: docker/build-push-action@v5
        with:
          tags: ai-module:${{ github.ref_name }}
          push: true

  canary:
    needs: build
    steps:
      - name: Deploy to staging (5% traffic)
        run: ./scripts/canary-deploy.sh ${{ github.ref_name }}
      - name: Monitor 24h
        run: sleep 86400
      - name: Auto-rollback if error rate > 5%
        run: ./scripts/check-canary.sh

  promote:
    needs: canary
    if: success()
    steps:
      - name: Promote to prod
        run: ./scripts/promote.sh ${{ github.ref_name }}
```

**Rollback:** giữ previous 2 versions, manual rollback bằng đổi symlink `current`.

## 36. Retraining trigger criteria

Auto-trigger retrain (`overall.md §57.2`):

| Trigger | Threshold | Action |
|---------|-----------|--------|
| Drift detected | KL divergence > 0.2 week-over-week | Notify AI team + create retrain job |
| Accuracy degradation | True positive rate < 75% over 100 samples | Notify AI team |
| Schedule | Every 3 months | Auto-schedule retrain |
| Manual | Admin trigger | `POST /api/v1/admin/ai/retrain-trigger` |

## 37. Inference batching

```python
# src/api/routes/predict.py
@router.post("/soh/batch", response_model=BatchPredictResponse)
async def predict_batch(req: BatchPredictRequest, request: Request):
    """
    Batch up to 32 items → single GPU forward pass.
    Latency tăng nhẹ nhưng throughput 32× cao hơn.
    """
    if len(req.items) > 32:
        raise HTTPException(400, "Max batch size is 32")

    # Stack all readings → shape (batch, 30, 3)
    arrays = [extract_readings(item) for item in req.items]
    tensor = torch.tensor(np.stack(arrays), dtype=torch.float32)

    with torch.no_grad():
        soh_batch = request.app.state.lstm(tensor)  # (batch,)

    return BatchPredictResponse(results=[
        SohResult(asset_id=item.asset_id, soh_percent=soh_batch[i].item(), ...)
        for i, item in enumerate(req.items)
    ])
```

→ BE side phải collect batch trong 100ms window (`overall.md §57.3`).

## 38. Multi-replica scaling (K8s HPA)

`deploy/helm/ai-module/templates/hpa.yaml` (theo `overall.md §57.5`):

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: ai-module
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: ai-module
  minReplicas: 1
  maxReplicas: 5
  metrics:
    - type: Pods
      pods:
        metric:
          name: ai_inference_queue_depth
        target:
          type: AverageValue
          averageValue: "10"
```

> Mỗi replica load model in-memory read-only, không share state → scale horizontal an toàn.

## 39. Drift detection

Background job hoặc cron (`overall.md §48.5 + §57.6`):

```python
def detect_drift():
    """
    Compare prediction distribution last 7 days vs previous 7 days.
    KL divergence > 0.2 → publish AiModelDriftDetectedEvent.
    """
    current = get_predictions(days=7)
    previous = get_predictions(days=7, offset=7)

    kl_div = compute_kl_divergence(current, previous)
    if kl_div > 0.2:
        publish_event("AiModelDriftDetectedEvent", {"kl_divergence": kl_div})
```

> Có thể chia: AI team viết drift logic, BE schedule + publish event. Hoặc BE làm hết (như `overall.md §48.5` gán cho BE). Quyết định trong sprint planning.

## 40. A/B testing

Feature flag `AI_MODEL_VERSION_VARIANT` (`overall.md §57.7`):
- 90% traffic → v1.1 (control)
- 10% traffic → v1.2 (variant)
- Compare metrics 2 tuần
- Promote winner

→ Logic này thường nằm ở BE (route traffic) chứ không phải AI side.

---

# Phần XI — Quy trình xây dựng

## 41. 3 giai đoạn xây dựng

```
┌─────────────────────────┐  ┌─────────────────────┐  ┌──────────────────┐
│ 1. PREPROCESS (1 lần)   │→ │ 2. TRAIN (vài lần)  │→ │ 3. SERVE (24/7)  │
│ NASA .mat → arrays      │  │ ra .pth + .pkl      │  │ FastAPI HTTP     │
└─────────────────────────┘  └─────────────────────┘  └──────────────────┘
```

### Giai đoạn 1 — Preprocess

1. `scripts/download_nasa.sh` → tải `.mat` files
2. `python -m src.train.preprocess` → output `X_*.npy, y_*.npy, scaler.pkl`
3. Commit `scaler.pkl` vào Git

### Giai đoạn 2 — Train

**LSTM:**
1. `python -m src.train.train_lstm` → loop 50 epoch
2. Validate sau mỗi epoch, early stop nếu val loss không giảm 10 epoch
3. Test trên test set → verify MAE < 2%, RMSE < 3%
4. Save → `models/weights/v1.0/soh_lstm.pth`

**Isolation Forest:**
1. `python -m src.train.train_isoforest` → fit (5 giây)
2. Test → verify F1 > 0.80
3. Save → `models/weights/v1.0/isolation_forest.pkl`

**Train trên Colab GPU T4** (free) → 10 phút thay vì 2-3h CPU.

### Giai đoạn 3 — Serve

1. Local dev: `uvicorn src.api.main:app --reload`
2. Docker: `docker compose up ai-module`
3. Bench: `./scripts/bench_latency.sh` → assert P95 < 150ms

## 42. Roadmap theo Sprint

Theo `overall.md §17`:

| Sprint | Việc AI Module | Output |
|--------|---------------|--------|
| **Sprint 1-3** | AI dev chuẩn bị dataset NASA, prototype trên Colab, refine paper references (B2 task) | Dataset ready, notebook PoC |
| **Sprint 4** | Train baseline v1.0 NASA → đạt MAE < 2%, F1 > 0.80. Build FastAPI cơ bản. Benchmark latency. **Build Docker image** | 3 artifact + Docker image |
| **Sprint 5** | Tích hợp với BatteryService (BE side làm). AI dev support API contract refinement | API contract finalized |
| **Sprint 5B/6** | Tuning v1.1 nếu cần. Hỗ trợ feedback loop UI | v1.1 (optional) |
| **Sprint 7** | CI/CD `deploy-model.yml`. Inference batching endpoint. Drift detection job | Batch endpoint + CI/CD |
| **Sprint 8** | Demo prep, A/B test framework (nice-to-have), Q&A material cho hội đồng | Demo ready |

## 43. Team responsibilities

| Người | Repo | Việc cụ thể |
|-------|------|------------|
| **AI dev** | `ai-module/` | Train model, FastAPI, latency < 100ms, ship Docker image, version artifacts, CI/CD `deploy-model.yml` |
| **BE dev** | `backend/` | (Xem `overall.ai.md` — toàn bộ tích hợp BE side) |
| **Cả 2 cùng làm sớm** | Cross-repo | **API contract** JSON schema — ký kết trước Sprint 5, không đổi sau đó |

**Lưu ý:**
- BE dev **không cần biết PyTorch** — chỉ cần hiểu JSON schema
- AI dev **không cần biết .NET** — chỉ cần serve FastAPI đúng contract
- Contract là biên giới — 2 bên độc lập phát triển

---

# Phần XII — Điều KHÔNG làm

- ❌ Không build "AI agent" kiểu LLM/chatbot — sai bản chất dự án
- ❌ Không thêm Transformer / attention layer trong scope capstone
- ❌ Không thêm ML framework mới ngoài PyTorch + scikit-learn
- ❌ Không fit scaler lại trên production data — chỉ fit train set 1 lần
- ❌ Không bỏ rule-based threshold ở BE — hybrid mới là design đúng
- ❌ Không gộp AI vào repo backend — phải tách 2 repo
- ❌ Không deploy AI bằng cách khác Docker — chuẩn microservice
- ❌ Không skip metadata version trong artifact — sẽ silent fail
- ❌ Không hardcode model version trong serving code — dùng `MODEL_VERSION` env var
- ❌ Không xáo trộn ngẫu nhiên train/val/test — chia theo battery ID
- ❌ Không thêm hyperparameter mới ngoài spec `.claude/rules/tech/ai.md` mà chưa thông qua Leader

---

# Phần XIII — Tài liệu tham khảo

## Trong repo

- `.claude/rules/tech/ai.md` — spec model bắt buộc (window=30, epochs=50, seed=42, kiến trúc CNN-LSTM)
- `.claude/docs/ai-datasets.md` — link download NASA + CALCE + convention
- `.claude/docs/ai-research-references.md` — paper tham khảo (B2 task)
- `overall.md §30` — AI Module integration core
- `overall.md §48` — Feedback loop & analytics
- `overall.md §53.7` — SOH integration với cycle log
- `overall.md §57` — AI advanced (deployment, retrain, batching, scaling, drift, A/B)
- `overall.ai.md` — backend tasks để phục vụ AI

## Học liệu ngoài

- **Christopher Olah** — "Understanding LSTM Networks" (colah.github.io) — best tutorial về LSTM
- **PyTorch official tutorial** — "Sequence Models and LSTM Networks"
- **FastAPI official docs** — fastapi.tiangolo.com/tutorial
- **scikit-learn IsolationForest** — user guide
- **NASA Ames PCoE** — battery dataset documentation

## Glossary

| Thuật ngữ | Nghĩa |
|-----------|-------|
| SOH | State of Health — sức khỏe pin (%) so với pin mới |
| EOL | End of Life — pin coi như hết tuổi thọ (chuẩn EV: SOH < 60%) |
| MAE | Mean Absolute Error |
| RMSE | Root Mean Squared Error |
| F1-score | Harmonic mean của Precision + Recall |
| KL divergence | Đo độ khác nhau giữa 2 distribution |
| Inference | Quá trình model nhận input → trả output (sau khi đã train) |
| Forward pass | 1 lần data đi qua network (chiều tính prediction) |
| Backward pass | 1 lần gradient đi ngược (training) |
| Epoch | 1 lần duyệt full dataset trong training |
| Batch | Nhóm samples xử lý cùng 1 forward pass |
| Dropout | Tắt random neuron khi train → tránh overfit |
| Early stopping | Dừng train khi val loss không cải thiện N epoch |
