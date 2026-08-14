# Báo cáo kiểm thử E2E toàn hệ thống — Solar Battery Maintenance

**Thời điểm kiểm thử:** 2026-08-01–2026-08-02 (Asia/Ho_Chi_Minh)  
**Backend branch/commit:** `dev` / `1050d20`  
**Repo issue:** `GSU26SE55/backend`  
**Milestone:** [E2E](https://github.com/GSU26SE55/backend/milestone/20)  
**Kết quả cuối sau vòng kiểm thử thứ 27:** 276 defect unique đã được xác nhận và tạo issue trong các dải [#722](https://github.com/GSU26SE55/backend/issues/722)–[#865](https://github.com/GSU26SE55/backend/issues/865), [#867](https://github.com/GSU26SE55/backend/issues/867)–[#893](https://github.com/GSU26SE55/backend/issues/893), [#895](https://github.com/GSU26SE55/backend/issues/895), [#897](https://github.com/GSU26SE55/backend/issues/897)–[#952](https://github.com/GSU26SE55/backend/issues/952), [#954](https://github.com/GSU26SE55/backend/issues/954), [#956](https://github.com/GSU26SE55/backend/issues/956)–[#997](https://github.com/GSU26SE55/backend/issues/997) và [#999](https://github.com/GSU26SE55/backend/issues/999)–[#1003](https://github.com/GSU26SE55/backend/issues/1003). #866 là issue feature ngoài audit; #894/#896/#953 không tồn tại; #955 là pull request, không phải defect; #998 là duplicate của #809, đã đóng `not_planned` và gỡ milestone nên không được tính. Vòng 27 là vòng full-audit tạo **0 issue mới**, đáp ứng tiêu chí hội tụ quan sát được trong phạm vi và giới hạn §6.

## 1. Kết luận điều hành

- Backend solution build Release thành công. Vòng năm tiếp tục chạy tuần tự `make ci-test` có **2.688/2.688 unit pass** và `make ci-integration` có **292/292 integration pass**.
- AI module có **538/539 full test pass**, coverage `src` **92%**; vòng năm chạy thêm **187/187 AI targeted**, **17/17 Battery AI bridge/outbox** và **25/25 Ticket saga/restart** pass. REST health/predict/prescribe và đủ 4 gRPC RPC hoạt động với model thật; compatibility/cancellation failure paths được kiểm tra riêng ở #852–#853.
- IoT có **118/118 native tests pass**. Vòng năm chạy **94/94 Battery IoT/MQTT/Ambient/Environmental unit**, **3/3 Mosquitto broker E2E** và **2/2 firmware mock/real BMS build**; tiếp tục kiểm tra failure path MQTT, OTA compatibility, command persistence và simulator queue.
- Chín image .NET build thành công và 9 service backend chạy được cùng PostgreSQL, Redis, RabbitMQ, MinIO và monitoring. Toàn bộ **373 OpenAPI operations** của 7 API surface được phục vụ trực tiếp và qua API Gateway.
- Full `docker compose up -d --build` vẫn không thành công vì AI module thiếu Dockerfile. Đây là lỗi deployment P1 [#734](https://github.com/GSU26SE55/backend/issues/734).
- Phân bố issue cuối: **84 P1 Critical, 153 P2 High, 39 P3 Standard**.
- Vòng 27 full terminal audit tạo **0 issue mới**: Battery **418/418**, Auth targeted **3/3**, Compose config pass; AI **539 pass + 1 known #759**, coverage **92%**, bridges **16/16 + 25/25**; IoT **118/118**, 3 firmware builds, 2 HTTP integrations và 3 real-MQTT integrations pass. Đây là hội tụ theo bằng chứng quan sát được, không thay thế các giới hạn HIL/external-provider/security scan ở §6.
- Các rủi ro cao nhất còn gồm raw idempotency key làm rò response chéo service/user, production mở public các internal port, NetworkPolicy mất tác dụng, apparent live webhook trong Git history, Grafana dùng password biết trước, MQTT bỏ qua device scope/status, AI bỏ cảnh báo safety do chemistry typo và ticket làm rơi AI safety/SOP.

## 2. Phạm vi đã đọc và kiểm toán

### 2.1 Backend repository

- 2.800 tracked files; 2.378 file C#.
- 145 tracked Markdown files, tổng 69.388 dòng.
- Đã đọc và lập bản đồ đầy đủ root `overall.md`: **17.459 dòng**, bao gồm Auth, Battery, Ticket/SLA, Notification, File, Audit, realtime, AI, IoT, SMS, security và các sprint bổ sung.
- Đã kiểm tra 9 service: ApiGateway, AuditAggregatorService, AuthService, BatteryService, EmailService, FileStorageService, NotificationService, SmsService, TicketService; cùng `shared`, migrations, seeders, Docker/monitoring/MQTT config và test projects.
- Các file context mà root `AGENTS.md` dẫn tới (`.codex/AGENTS.md`, `.codex/context/project-context.md`, `.codex/context/business-flow.md`, `.codex/project-context-full.md`, `.codex/reference/INDEX.md`) không tồn tại trong checkout. Đây là limitation tài liệu, không được báo thành runtime bug.

### 2.2 AI và IoT sibling repositories

- AI module: 421 tracked files, 211 Markdown files; kiểm toán router/schema/service/model/proto/scripts, README, `docs/overall.md`, KB/model artifacts và test suite.
- IoT: 232 tracked files, 105 Markdown files; kiểm toán firmware ESP32, network/MQTT/OTA/sensor safety, simulator, mock backend, PlatformIO, infra và integration scripts.
- Không đọc hoặc in `.env`/`.env.Docker`. Credential dùng trong test chỉ được giữ trong process shell và không ghi vào báo cáo/log đầu ra.

## 3. Công cụ và phương pháp

| Nhóm | Công cụ / kỹ thuật | Kết quả |
|---|---|---|
| Build/test .NET | `dotnet restore`, Release build, `make ci-test`, targeted `dotnet test` | Pass; 0 build error |
| Runtime E2E | Docker Compose, `curl`, OpenAPI runtime, JWT demo accounts, MinIO create/read/delete | Pass ngoại trừ defect đã ghi |
| Event bus | RabbitMQ queue/consumer inspection | 60/60 queue có consumer, backlog 0 |
| MQTT | Mosquitto authenticated publish/subscribe, bridge uplink/downlink | Pass; hai healthcheck variant sai theo #744/#786 |
| AI | Python 3.11 clean venv, pytest+coverage, REST, gRPC unary/stream, real artifact benchmark | 538 pass, 1 fail; 92%; performance fail SLA |
| AI quality/security | `pip check`, Ruff, Bandit | pip consistency pass; Ruff fail; Bandit 9 Medium/6 Low |
| IoT | PlatformIO native/unit, 3 firmware builds, integration scripts, compile/syntax/compose checks | Pass; các behavior/security/integration defect xem §5 |
| Dependencies | NuGet vulnerable transitive scan | Npgsql, Packaging, OTel, MessagePack, AngleSharp findings |
| GitHub | GitHub connector được thử trước; private repo không visible nên fallback `gh` bằng credential đã lưu | Xác nhận milestone và 276 issue unique qua #1003; Round27 zero-new |

Browser-use skill đã được áp dụng để xác định quy trình browser testing, nhưng runtime Node REPL/Playwright của plugin không được expose trong phiên này. Repository không có web UI; realtime backend là SSE/SignalR/MQTT. Postman/Newman, k6, ZAP và Trivy không được cài trong môi trường; OpenAPI + curl + integration tests được dùng thay thế. Các plugin Figma/Slack/Drive/Calendar… không liên quan tới code/runtime E2E nên không được cài hoặc gọi.

## 4. Kết quả test chi tiết

### 4.1 Backend automated tests

| Suite | Kết quả |
|---|---:|
| Unit tests qua CI target | 2.688 pass |
| Integration tests qua CI target vòng ba | 291 pass, 1 test-race fail; rerun Email 19/19 pass |
| Tổng CI vòng ba | 2.979 pass, 1 test-race fail |
| Audit targeted độc lập | 2.987 pass (2.686 unit + 301 integration) |
| Solution Release build | Pass, 0 error, 32 warning |
| 9 Docker image .NET | Pass |

Vòng kiểm tra lại không có `[Fact]`/`[Theory]` bị `Skip`. Inventory tĩnh xác nhận **73 controller / 373 operation**; chỉ Auth và Ticket có route-level `WebApplicationFactory` coverage đáng kể, còn Battery, FileStorage và Notification thiếu HTTP route test toàn diện. Đây là khoảng trống coverage, không được tự động tính thành defect nếu chưa có behavior sai.

Vòng ba chạy lại toàn bộ CI unit/integration. Test concurrent Email tại `ConcurrentPublishTests.cs:43-49` chỉ chờ render rồi đếm Mailjet trong khi send chưa chắc hoàn tất; test mixed ngay bên dưới đã chờ đúng `WaitForMailjetCallAsync`. Vì isolated test pass 3/3 và assembly pass 19/19, kết quả được ghi chính xác là test flake [#801](https://github.com/GSU26SE55/backend/issues/801), không che giấu thành “292 pass”.

AuditAggregator performance assembly có 1 test fail khi chạy dưới contention nhưng pass khi chạy isolated và chính source đánh `Category=Performance`, loại khỏi CI. Không đủ bằng chứng xem đây là product defect.

Vòng bốn chạy lại hai CI target bằng đúng thứ tự sản xuất: **2.688/2.688 unit** và **292/292 integration** pass. Một test MassTransit Shared từng fail khi hai target nặng bị chạy đồng thời; chạy isolated bốn lượt đều pass. Kết quả này được giữ như quan sát về giới hạn tài nguyên test host, không tạo issue sản phẩm. Audit source đa replica phát hiện SLA warning thiếu Staff, duplicate SLA/outbox và duplicate Battery alert/escalation (#824–#827); kiểm tra cấu hình email phát hiện invite production rơi về localhost (#828).

Vòng năm tiếp tục có **2.688/2.688 unit** và **292/292 integration** pass. Targeted bổ sung gồm Battery ambient **17/17** và Ticket rating/client **5/5**. Audit failure/multi-replica/contract phát hiện Helm thiếu toàn bộ gRPC topology (#847), secret workflow deploy placeholder hoặc bỏ qua external secret name (#848), AutoClose/RatingRequest race giữa replica (#849), ambient nhận dữ liệu tương lai/vô hiệu (#850), và Battery→Ticket biến SOH unknown thành 0% (#851). Phần publish-before-commit của AutoClose/Rating không tạo lặp vì đã thuộc #727.

### 4.2 Runtime services và OpenAPI

| Service | Direct health/smoke | OpenAPI paths | Operations | Gateway Swagger |
|---|---:|---:|---:|---:|
| AuthService | `/health`, `/live`, `/ready` = 200 | 73 | 82 | 200 |
| FileStorageService | upload/metadata/presigned/download/delete | 9 | 9 | 200 |
| BatteryService | `/api/battery/health` = 200 | 81 | 91 | 200 |
| TicketService | `/health`, `/api/ticket/health` = 200 | 131 | 153 | 200 |
| NotificationService | authenticated list = 200 | 14 | 20 | 200 |
| SmsService | `/` = 200 | 6 | 7 | 200 |
| AuditAggregatorService | `/live`, `/ready`, `/health` = 200 | 11 | 11 | 200 |
| EmailService | `/` = 200; consumers active | N/A | consumer service | N/A |
| ApiGateway | `/health` = 200 | aggregate | aggregate | 200 |

Các smoke flow runtime:

- Demo Customer login trực tiếp AuthService và qua Gateway: 200, access token hợp lệ; invalid login: 400.
- Auth `me`, Battery `battery-assets/me` và `alerts`, Ticket `customer/tickets/me`, Notification list: đều 200 trực tiếp/gateway.
- Customer truy cập admin audit: 403 đúng mong đợi.
- FileStorage: upload PNG trả 201; metadata/presigned/download 200; delete hoàn tất. Không để binary test mới trong source tree.
- RabbitMQ: 60 queue, 60 queue có consumer, 0 pending message sau smoke.
- MQTT authenticated publish/subscribe trả đúng payload `e2e-ok`.
- IoT ingest với asset không tồn tại trả 201, `inserted=0`, `skipped=1` mà không tạo telemetry. Retry một timestamp thật đã có tái hiện 500/unique violation, tạo #763.
- Battery SOH background thực tế bị AI từ chối vì một outlier 52,4V trên asset 12V, tạo #762.
- Không có unhandled runtime error khác ngoài request duplicate được chủ động dùng để tái hiện #763.

Vòng hai chạy thêm:

- Gọi đủ **373 operation không token** cả direct và Gateway: **746 request**, 0 timeout, 0 response 5xx.
- Chạy authenticated GET role matrix qua Gateway cho Manager, Staff và Customer: **165 route/role, 495 request**, 0 timeout, 0 response 5xx. Admin runtime không chạy được vì credential seed được document không còn hợp lệ trong persistent DB; Admin vẫn được bao phủ bằng integration/static authorization audit.
- Runtime xác nhận Staff và Customer đều đọc được dashboard global `totalAssets=10`, tạo [#774](https://github.com/GSU26SE55/backend/issues/774).
- Runtime xác nhận `/api/auth/introspect` nhận 12/12 request anonymous liên tiếp, đều 200/active và không 401/429, tạo [#776](https://github.com/GSU26SE55/backend/issues/776).
- Bật MQTT bridge tạm thời: BatteryService kết nối broker và subscribe 4 topic. Publish QoS1 đúng device/site/asset được insert; SSE direct và qua Gateway cùng nhận **1 reading + 2 stats**, 1.177 byte. Sau test BatteryService đã được recreate về config Compose mặc định và health=200.

Vòng ba chạy lại **746 anonymous/invalid-auth request** cho đủ 373 operation qua direct + Gateway và **495 authenticated GET request** cho Manager/Staff/Customer qua Gateway: 0 timeout, 0 response 5xx. RabbitMQ tiếp tục có 60 queue, không queue nào thiếu consumer, backlog 0; Redis PING và persistence status đều pass. Kiểm tra migration runtime phát hiện migration KB không được EF discover và obsolete unique index vẫn chặn dữ liệu hợp lệ [#799](https://github.com/GSU26SE55/backend/issues/799). SSE truyền dữ liệu thành công nhưng client disconnect bị Gateway đếm thành 502 [#800](https://github.com/GSU26SE55/backend/issues/800).

Audit kết nối production phát hiện Gateway chạy môi trường Production nhưng chỉ có container destinations trong `appsettings.Docker.json`, khiến mọi cluster dùng localhost [#787](https://github.com/GSU26SE55/backend/issues/787). MinIO production cũng để credential mặc định, public ports và anonymous bucket [#788](https://github.com/GSU26SE55/backend/issues/788). Các luồng outbox/inbox, notification multi-replica, virus scan FileStorage và Ticket parent/authorization/pagination được kiểm toán lại; mọi behavior sai đã được tách thành issue #789–#798.

Vòng bốn gọi lại đủ **746 request** cho toàn bộ 373 OpenAPI operation trực tiếp và qua Gateway: **0 timeout, 0 response 5xx**; phân bố 200=13, 302=2, 400=26, 401=579, 404=4, 415=4, 422=2, 429=116. Các 429 xuất hiện đúng sau khi Gateway throttle ma trận request. Hai runtime repro mới xác nhận: 12 địa chỉ X-Forwarded-For khác nhau vẫn dùng chung Auth limiter (#812), và cùng một Idempotency-Key làm Auth trả nguyên response lỗi của Notification (#817). Helm render/lint và production compose audit phát hiện internal ports public, NetworkPolicy selector quá rộng, Audit bị ngắt kết nối và HTTPS termination loop (#813–#816); SignalR Notification và lifecycle event consumers được kiểm tra xuyên Gateway/broker bằng contract/source (#819–#821). Secret scan chỉ báo một apparent live Discord webhook trong `env.prod.example`; giá trị tuyệt đối không được chép vào log/báo cáo (#822). Grafana staging hard-code credential được tách riêng ở #823.

Vòng năm chạy lại **746 anonymous/invalid-auth request**: 0 timeout, 0 response 5xx. Authenticated GET matrix có **165 route × 3 role = 495 request** với pacing để Gateway limiter không che lỗi; endpoint hourly aggregate là 5xx duy nhất và tái hiện cho Manager, Staff, Customer. Direct repro xác nhận không filter=500, chỉ `from`=500, cả `from+to`=200; PostgreSQL log `42P08` (#864). Sáu body challenge token khác nhau trả 422 năm lần rồi 429 lần sáu, xác nhận 2FA limiter bỏ qua token trong JSON (#865). Helm/container audit phát hiện app chạy root (#861), floating image tags (#862) và readiness giả dựa `/metrics`/static response (#863).

### 4.3 AI module

- Clean Python 3.11.15 venv; exact `requirements.txt` + dev requirements; `pip check` pass.
- Production scaler, feature scaler, Mamba v1.6 và IsolationForest load thành công trong khoảng 6,58 giây.
- `pytest tests --cov=src`: **538 pass, 1 fail, 16 warnings, 92% coverage**, 171,01 giây.
- REST `/health`, `/predict/`, `/prescribe/` và feedback surface hoạt động.
- gRPC Health, Predict, PredictStream (giữ đúng B0005/B0006/B0007 order) và Prescribe pass.
- Benchmark model thật lặp lại không đạt SLA <100ms: direct ~620ms, unary ~596–708ms, stream ~508–719ms; p95 tới ~1.038ms.
- Clean offline không có SentenceTransformer cache làm 15 RAG test fail; sau tải encoder các test đó pass. Đây là deployment/graceful-degradation defect #755.
- Bandit: 0 High, 9 Medium, 6 Low; runtime `torch.load(weights_only=False)` được tách thành #754.
- Remote Python CVE audit không được policy cho phép; trạng thái là **NOT RUN/BLOCKED**, không được diễn giải là không có CVE.
- Hai ChromaDB binary bị test mutate đã được khôi phục chính xác từ `HEAD`; giữ nguyên file người dùng `AI_SYSTEM_ANALYSIS.md`.

Vòng hai kiểm tra riêng kết nối Battery ↔ AI:

- 16/16 targeted fallback/client/worker test pass; live REST/gRPC health, Predict, Prescribe và stream tiếp tục pass với artifact production.
- Proto hai bên wire-compatible; Battery proto chỉ thiếu field response `cached=27`, protobuf có thể bỏ unknown field nên chưa coi là runtime defect.
- Payload sai 29/31 rows bị AI reject đúng contract; chính điều này chứng minh config `Ai:MinReadings != 30` làm worker ngừng prediction [#780](https://github.com/GSU26SE55/backend/issues/780).
- Model thật với cùng seed/input cho SOH 67,33 khi Battery gửi 4 cột và 40,46 khi gửi đủ cycle/SOC, lệch 26,87 điểm [#777](https://github.com/GSU26SE55/backend/issues/777).
- Hai store Chroma tách biệt tái hiện chính xác: ID tạo ở store gRPC không update được từ store REST, tạo [#779](https://github.com/GSU26SE55/backend/issues/779).

Vòng ba chạy **88/88 AI regression mục tiêu pass**, xác nhận artifact v1.6, scaler, Mamba và Isolation Forest load/inference; Battery gọi live gRPC Predict khoảng 0,8–1,3 giây và Prescribe đều HTTP/2 200. Kiểm tra đồng thời/semantic phát hiện cache stampede [#802](https://github.com/GSU26SE55/backend/issues/802), retry cùng cycle làm bẩn causal history [#803](https://github.com/GSU26SE55/backend/issues/803), timestamp đảo vẫn được accept [#804](https://github.com/GSU26SE55/backend/issues/804), và Battery bỏ qua AI risk P1/P2 khi classification vẫn Normal [#805](https://github.com/GSU26SE55/backend/issues/805).

Vòng bốn chạy **229/229 AI targeted**, **9/9 Battery AI bridge** và **23/23 Ticket alert-saga** pass. Live/controlled parity tests phát hiện 3-column contract gây REST 500/gRPC INTERNAL (#837), gRPC Prescribe thiếu named readings (#838), Infinity tạo semantic trái ngược (#839), typo chemistry làm mất critical warning (#840), cache REST/gRPC tách process (#841), cancellation không dừng inference (#842), lazy singleton race (#843), và RAG rỗng vẫn cho LLM output `enriched=true` (#844). Hai kết nối downstream được tái hiện: Battery nhận `{}` HTTP 200 thành SOH 0/Normal (#845), và Alert→Ticket làm rơi safety warnings/SOP/action steps (#846).

Vòng năm chạy **187/187 AI targeted**, **17/17 Battery AI bridge/outbox** và **25/25 Ticket saga/restart**. Mixed-artifact repro xác nhận startup vẫn ready với IsolationForest 3 feature rồi request đầu crash trên vector 57 feature (#852). Cancellation repro xác nhận Battery nhận gRPC Cancelled vẫn gọi HTTP và nuốt `OperationCanceledException` thành null (#853). Artifact production hiện tại vẫn tương thích 4/57/57, window 30; lỗi #852 là fail-fast deployment, không được mô tả sai thành artifact hiện tại hỏng.

### 4.4 IoT

- `pio test -e native`: **118/118 pass**.
- `pio run`: `esp32-s3-devkitc-1`, `esp32-s3-real`, `example-blink` đều pass.
- Sprint 1/Sprint 3 integration scripts, Python compile, shell syntax và Compose validation pass.
- MQTT là realtime channel; repo IoT không có browser/WebSocket UI.
- Không tạo issue cho `include_dir` chỉ xuất hiện trong dirty/uncommitted state của sibling IoT checkout tại thời điểm audit; không quy lỗi đó cho committed HEAD.
- Không có real ESP32 hardware/HIL, gas/water rig hoặc TLS MITM lab. Các lỗi hardware behavior được xác nhận bằng control-flow/source, native tests và simulator/stub repro; giới hạn này được ghi rõ thay vì tuyên bố HIL pass.

Vòng hai kiểm tra riêng kết nối IoT ↔ Battery:

- Device tạm được create/provision; heartbeat và firmware-check 200. Thiếu/sai key 401 và DeviceCode mismatch bị chặn.
- HTTP ingest mới 201/inserted=1; replay cùng Idempotency-Key 200 không insert lại; partial valid+unknown trả inserted=1/skipped=1.
- MQTT QoS1 qua Mosquitto → bridge → Battery insert → SSE reading pass. Downlink API 202 và subscriber nhận đúng topic/payload command.
- Credential MQTT do create-device trả về không authenticate được broker và thiếu broker host/port [#784](https://github.com/GSU26SE55/backend/issues/784).
- Device scope mặc định 11 làm ambient/environmental trả 401; đổi scope 15 thì ambient 201 [#785](https://github.com/GSU26SE55/backend/issues/785).
- Device E2E tạm đã decommission; giữ reading lịch sử cho forensic theo thiết kế, không tạo incident/ticket thật.

Vòng ba kiểm tra thêm site binding, lifecycle, provisioning contract, heartbeat queue, MQTT LWT và OTA state machine. Runtime xác nhận API key environmental của thiết bị Site A ghi ambient cho Site B được 201 [#806](https://github.com/GSU26SE55/backend/issues/806); provision rỗng và heartbeat có số âm/quá giới hạn vẫn 200/persist [#807](https://github.com/GSU26SE55/backend/issues/807). Source/contract tests xác nhận firmware bỏ battery mappings/sensors [#808](https://github.com/GSU26SE55/backend/issues/808), luôn báo queue depth 0 [#809](https://github.com/GSU26SE55/backend/issues/809), backend parse mọi chuỗi chứa `offline` thành LWT offline [#810](https://github.com/GSU26SE55/backend/issues/810), và OTA log không có validation/transition guard [#811](https://github.com/GSU26SE55/backend/issues/811). Device test đã revoke/decommission; unknown-site write được rollback, không tạo incident/ticket thật.

Vòng bốn chạy lại **118/118 native**, **59/59 Battery IoT/MQTT unit** và **3/3 Mosquitto Testcontainers E2E**. Audit kết nối xác nhận MQTT bỏ qua scope/status (#829), device cùng site được quyền ghi mọi battery (#830), topic serial không bind payload (#831), OTA rollback state sai thứ tự (#832), NTP provision không được áp dụng (#833), QoS1 command redelivery lặp side effect (#834), queue mất idempotency sidecar (#835), và topic contract trong `overall.md` lệch implementation/ACL (#836).

Vòng năm chạy **94/94 Battery IoT/MQTT/Ambient/Environmental**, **3/3 real-broker E2E**, **118/118 firmware native** và **2/2 firmware build**. Probe không ghi dữ liệu xác nhận legacy SensorIngest key vượt EnvironmentalIngest scope (#854). Failure-path audit phát hiện bridge subscribe QoS0/ACK lỗi (#855), OTA không check hardware/model (#856), command offline queue volatile/unbounded/no TTL (#857), set_interval không persist (#858), backend quảng bá command firmware không hỗ trợ (#859), và JSONL hỏng làm simulator crash/compaction mất tính atomic (#860).

## 5. Danh sách 276 lỗi unique đã tạo issue

### 5.1 Backend, shared và infrastructure

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#722](https://github.com/GSU26SE55/backend/issues/722) | P1 | BatteryService/Security | Customer đọc và ACK tài nguyên ngoài tenant |
| [#723](https://github.com/GSU26SE55/backend/issues/723) | P1 | FileStorage/Security | User bất kỳ tải attachment/photo riêng tư |
| [#724](https://github.com/GSU26SE55/backend/issues/724) | P1 | Battery/IoT/Security | API key thiết bị lưu và trả lại plaintext |
| [#725](https://github.com/GSU26SE55/backend/issues/725) | P2 | BatteryService | Outbox thiếu event type và có poison-batch risk |
| [#726](https://github.com/GSU26SE55/backend/issues/726) | P2 | TicketService | Outbox bỏ Ticket/Chat/Participant/SLA event |
| [#727](https://github.com/GSU26SE55/backend/issues/727) | P2 | TicketService | Auto-close/rating publish trước DB commit |
| [#728](https://github.com/GSU26SE55/backend/issues/728) | P2 | AuditAggregator | Replay trả 202 nhưng không replay |
| [#729](https://github.com/GSU26SE55/backend/issues/729) | P3 | AuditAggregator | Retention timer có thể không bao giờ chạy |
| [#730](https://github.com/GSU26SE55/backend/issues/730) | P1 | DB/Security | Npgsql 8.0.2, CVE-2024-32655 |
| [#731](https://github.com/GSU26SE55/backend/issues/731) | P1 | Shared/Security | System.IO.Packaging 6.0.0, hai advisory High |
| [#732](https://github.com/GSU26SE55/backend/issues/732) | P3 | Shared/Security | OpenTelemetry 1.9.0 advisories Moderate |
| [#733](https://github.com/GSU26SE55/backend/issues/733) | P2 | Ticket/Security | MessagePack và AngleSharp vulnerable transitive deps |
| [#734](https://github.com/GSU26SE55/backend/issues/734) | P1 | Infrastructure/AI | Compose build fail do AI Dockerfile không tồn tại |
| [#762](https://github.com/GSU26SE55/backend/issues/762) | P2 | Battery/AI | Một outlier làm hỏng toàn SOH prediction window |
| [#763](https://github.com/GSU26SE55/backend/issues/763) | P2 | Battery/IoT | Duplicate telemetry trả 500 thay vì idempotent skip |

### 5.2 IoT

| Issue | Mức | Tóm tắt |
|---|---|---|
| [#735](https://github.com/GSU26SE55/backend/issues/735) | P1 | HTTPS/OTA tắt xác minh TLS bằng `setInsecure()` |
| [#736](https://github.com/GSU26SE55/backend/issues/736) | P1 | Gas/water sensors ngừng sample khi Wi-Fi/NTP outage |
| [#737](https://github.com/GSU26SE55/backend/issues/737) | P2 | Offline queue không thu BMS samples khi mất Wi-Fi |
| [#738](https://github.com/GSU26SE55/backend/issues/738) | P2 | Simulator coi HTTP 201 là failure và queue mãi |
| [#739](https://github.com/GSU26SE55/backend/issues/739) | P3 | Simulator/mock backend lệch JSON casing |
| [#740](https://github.com/GSU26SE55/backend/issues/740) | P2 | Partial MQTT + full HTTPS fallback tạo duplicate |
| [#741](https://github.com/GSU26SE55/backend/issues/741) | P2 | Environmental 4xx bị tight retry vô hạn |
| [#742](https://github.com/GSU26SE55/backend/issues/742) | P2 | DS18B20 tạo SensorMismatch giả ở backend |
| [#743](https://github.com/GSU26SE55/backend/issues/743) | P2 | Mock BMS 12V gắn vào asset seed 48V |
| [#744](https://github.com/GSU26SE55/backend/issues/744) | P3 | Mosquitto healthcheck luôn xanh dù auth fail |
| [#745](https://github.com/GSU26SE55/backend/issues/745) | P2 | `trigger_ota` ACK success nhưng không trigger OTA |
| [#746](https://github.com/GSU26SE55/backend/issues/746) | P3 | Telemetry ghi QoS1 nhưng thực tế QoS0 |
| [#747](https://github.com/GSU26SE55/backend/issues/747) | P3 | Hot-change identity phá credential/ACL/subscription |
| [#748](https://github.com/GSU26SE55/backend/issues/748) | P3 | Firmware bỏ qua partial-ingest errors trong 2xx |
| [#749](https://github.com/GSU26SE55/backend/issues/749) | P3 | Identity quá dài persist NVS trước khi truncate RAM |

### 5.3 AI module

| Issue | Mức | Tóm tắt |
|---|---|---|
| [#750](https://github.com/GSU26SE55/backend/issues/750) | P2 | Predict model production vi phạm SLA <100ms |
| [#751](https://github.com/GSU26SE55/backend/issues/751) | P2 | Prescribe làm rơi chemistry/capacity và lệch Predict |
| [#752](https://github.com/GSU26SE55/backend/issues/752) | P2 | Cache key bỏ pack config, trả stale result |
| [#753](https://github.com/GSU26SE55/backend/issues/753) | P2 | Agentic query-gen bypass budget/concurrency guard |
| [#754](https://github.com/GSU26SE55/backend/issues/754) | P2 | Unsafe PyTorch pickle deserialization |
| [#755](https://github.com/GSU26SE55/backend/issues/755) | P3 | Offline deployment thiếu RAG encoder/degrade im lặng |
| [#756](https://github.com/GSU26SE55/backend/issues/756) | P3 | `/predict-long` được document nhưng không expose |
| [#757](https://github.com/GSU26SE55/backend/issues/757) | P3 | Inconsistent cycle_count được accept và bỏ qua |
| [#758](https://github.com/GSU26SE55/backend/issues/758) | P3 | Blank battery_id được accept |
| [#759](https://github.com/GSU26SE55/backend/issues/759) | P3 | KB manifest hash phụ thuộc CRLF |
| [#760](https://github.com/GSU26SE55/backend/issues/760) | P2 | README schema cũ làm client gửi sai semantic |
| [#761](https://github.com/GSU26SE55/backend/issues/761) | P3 | Ruff: 33 lint errors, 60 file sai format |

### 5.4 Backend/shared — lỗi mới từ vòng kiểm tra lại

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#764](https://github.com/GSU26SE55/backend/issues/764) | P1 | Shared/Reliability | Inbox claim message trước side effect làm retry mất email/SMS/event |
| [#765](https://github.com/GSU26SE55/backend/issues/765) | P1 | Notification/Reliability | Debounce key ghi trước notification DB commit làm mất notification khi retry |
| [#766](https://github.com/GSU26SE55/backend/issues/766) | P1 | Auth/Integration | Không đường đổi status nào publish `AccountStatusChangedEvent` |
| [#767](https://github.com/GSU26SE55/backend/issues/767) | P1 | Ticket/Integration | AccountStatus enum lệch một đơn vị và bị cast trực tiếp |
| [#768](https://github.com/GSU26SE55/backend/issues/768) | P1 | Auth/Email | Cross-device 2FA publish email event nhưng không có consumer |
| [#769](https://github.com/GSU26SE55/backend/issues/769) | P1 | Auth/Integration | Đổi role không đồng bộ Battery/Ticket/Notification read model |
| [#770](https://github.com/GSU26SE55/backend/issues/770) | P2 | Auth/Ticket | Add/delete staff skill không publish event cho Ticket |
| [#771](https://github.com/GSU26SE55/backend/issues/771) | P1 | Auth/Authorization | Sửa role permissions không invalidate cache quyền |
| [#772](https://github.com/GSU26SE55/backend/issues/772) | P1 | Ticket/Integration | Account đã delete vẫn Active do thiếu consumer |
| [#773](https://github.com/GSU26SE55/backend/issues/773) | P2 | Battery/Integration | Customer profile update không tới Battery read model |
| [#774](https://github.com/GSU26SE55/backend/issues/774) | P1 | Battery/Security | Customer và Staff đọc được dashboard toàn hệ thống |
| [#775](https://github.com/GSU26SE55/backend/issues/775) | P1 | AuditAggregator | Nuốt mọi `DbUpdateException` như duplicate và làm mất audit |
| [#776](https://github.com/GSU26SE55/backend/issues/776) | P2 | Auth/Security | OAuth introspection mở anonymous và không throttle |

### 5.5 AI module/bridge — lỗi mới từ vòng kiểm tra lại

| Issue | Mức | Tóm tắt |
|---|---|---|
| [#777](https://github.com/GSU26SE55/backend/issues/777) | P1 | Battery SOH worker bỏ cycle/SOC, lệch 26,87 điểm trên repro model thật |
| [#778](https://github.com/GSU26SE55/backend/issues/778) | P2 | Battery làm rơi prescription ID và không có feedback bridge |
| [#779](https://github.com/GSU26SE55/backend/issues/779) | P2 | gRPC/HTTP container dùng history riêng nên feedback ID trả 404 |
| [#780](https://github.com/GSU26SE55/backend/issues/780) | P2 | `Ai:MinReadings` khác 30 vi phạm exact-window contract |
| [#781](https://github.com/GSU26SE55/backend/issues/781) | P2 | Compose override TEMPORARY tự route khỏi AI containers |
| [#782](https://github.com/GSU26SE55/backend/issues/782) | P1 | Production compose/env bỏ toàn bộ AI deployment/config |
| [#783](https://github.com/GSU26SE55/backend/issues/783) | P1 | Prescribe chạy trước dedup; open SOH alert/ticket lặp mỗi giờ |

### 5.6 IoT/infrastructure — lỗi mới từ vòng kiểm tra lại

| Issue | Mức | Tóm tắt |
|---|---|---|
| [#784](https://github.com/GSU26SE55/backend/issues/784) | P1 | Credential MQTT do API cấp không dùng được; broker endpoint null và ACL casing lệch |
| [#785](https://github.com/GSU26SE55/backend/issues/785) | P1 | Scope device mặc định chặn ambient/gas/water safety reporters |
| [#786](https://github.com/GSU26SE55/backend/issues/786) | P3 | Root Compose healthcheck dùng `/dev/tcp` unsupported nên broker false-unhealthy |

### 5.7 Lỗi mới từ vòng kiểm tra chéo thứ ba

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#787](https://github.com/GSU26SE55/backend/issues/787) | P1 | ApiGateway/Production | Gateway production dùng destination localhost cho mọi cluster |
| [#788](https://github.com/GSU26SE55/backend/issues/788) | P1 | Infrastructure/FileStorage | MinIO production public, credential mặc định và anonymous bucket |
| [#789](https://github.com/GSU26SE55/backend/issues/789) | P1 | Shared/Outbox/Inbox | Event ID bị sinh lại sau deserialize, retry vượt inbox dedupe |
| [#790](https://github.com/GSU26SE55/backend/issues/790) | P2 | Ticket/FileStorage | VirusScanWorker không authenticate, attachment kẹt vĩnh viễn |
| [#791](https://github.com/GSU26SE55/backend/issues/791) | P1 | Ticket/Security | Staff đọc/xóa KB reference của ticket không được phân công |
| [#792](https://github.com/GSU26SE55/backend/issues/792) | P2 | Notification | External delivery xảy ra trước commit trạng thái Sent |
| [#793](https://github.com/GSU26SE55/backend/issues/793) | P2 | Notification | Leader election không atomic và Pending row không được claim |
| [#794](https://github.com/GSU26SE55/backend/issues/794) | P2 | Auth/SMS/Outbox | Hai replica relay có thể publish cùng outbox row |
| [#795](https://github.com/GSU26SE55/backend/issues/795) | P3 | Notification | Template test-send publish email trước audit commit |
| [#796](https://github.com/GSU26SE55/backend/issues/796) | P3 | Ticket | PATCH maintenance log bỏ qua route ticketId |
| [#797](https://github.com/GSU26SE55/backend/issues/797) | P3 | Ticket | Chat/saga pagination nhận số âm và page size không giới hạn |
| [#798](https://github.com/GSU26SE55/backend/issues/798) | P3 | Ticket/Migration | Runtime EF model lệch migration snapshot |
| [#799](https://github.com/GSU26SE55/backend/issues/799) | P2 | Ticket/Database | Migration KB không discover, obsolete unique index vẫn active |
| [#800](https://github.com/GSU26SE55/backend/issues/800) | P3 | ApiGateway/SSE | Client disconnect bình thường bị metric/log thành 502 |
| [#801](https://github.com/GSU26SE55/backend/issues/801) | P3 | Email/Tests | Concurrent test race giữa render và Mailjet assertion |
| [#802](https://github.com/GSU26SE55/backend/issues/802) | P2 | AI/Concurrency | Prescription cache stampede chạy inference/LLM trùng |
| [#803](https://github.com/GSU26SE55/backend/issues/803) | P2 | AI/History | Retry cùng cycle làm bẩn causal degradation history |
| [#804](https://github.com/GSU26SE55/backend/issues/804) | P2 | AI/Preprocessing | Timestamp đảo/reset được accept và làm sai SOC |
| [#805](https://github.com/GSU26SE55/backend/issues/805) | P2 | Battery/AI | Battery bỏ qua risk P1/P2 khi classification Normal |
| [#806](https://github.com/GSU26SE55/backend/issues/806) | P1 | Battery/IoT/Security | Environmental API key ghi ambient/incident cho site khác |
| [#807](https://github.com/GSU26SE55/backend/issues/807) | P2 | Battery/IoT | Provision/heartbeat persist giá trị operational vô hiệu |
| [#808](https://github.com/GSU26SE55/backend/issues/808) | P2 | IoT/Battery/Provision | Firmware bỏ mappings/supported sensors và không refresh config |
| [#809](https://github.com/GSU26SE55/backend/issues/809) | P3 | IoT/Heartbeat | Firmware luôn báo local queue depth bằng 0 |
| [#810](https://github.com/GSU26SE55/backend/issues/810) | P2 | Battery/IoT/MQTT | LWT parser coi mọi payload chứa `offline` là offline |
| [#811](https://github.com/GSU26SE55/backend/issues/811) | P2 | Battery/IoT/OTA | OTA log nhận enum/bytes/transition vô hiệu |

### 5.8 Lỗi mới từ vòng kiểm tra chéo thứ tư

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#812](https://github.com/GSU26SE55/backend/issues/812) | P2 | Auth/Gateway | Không restore client IP, anonymous limiter thành global |
| [#813](https://github.com/GSU26SE55/backend/issues/813) | P1 | Production/Security | Compose publish DB, broker UI, service và monitoring ra host |
| [#814](https://github.com/GSU26SE55/backend/issues/814) | P1 | Kubernetes/Security | Empty selector vô hiệu hóa default-deny NetworkPolicy |
| [#815](https://github.com/GSU26SE55/backend/issues/815) | P1 | Audit/Gateway/K8s | Helm ngắt Audit khỏi Gateway, PostgreSQL và RabbitMQ |
| [#816](https://github.com/GSU26SE55/backend/issues/816) | P1 | Production/K8s/HTTPS | HTTP pod redirect loop; Ticket bỏ disable override |
| [#817](https://github.com/GSU26SE55/backend/issues/817) | P1 | Shared/Idempotency | Raw global key replay response chéo service/user |
| [#818](https://github.com/GSU26SE55/backend/issues/818) | P2 | Shared/Idempotency | Exception/5xx poison reservation và retry |
| [#819](https://github.com/GSU26SE55/backend/issues/819) | P2 | Notification/SignalR | Query access token bị từ chối ở WebSocket hub |
| [#820](https://github.com/GSU26SE55/backend/issues/820) | P3 | Gateway/RateLimit | Sai claim làm authenticated request vẫn vào IP bucket |
| [#821](https://github.com/GSU26SE55/backend/issues/821) | P2 | Battery/Notification/Ticket | Lifecycle events không có consumer |
| [#822](https://github.com/GSU26SE55/backend/issues/822) | P1 | Security/Secrets | Apparent live Discord webhook nằm trong Git history |
| [#823](https://github.com/GSU26SE55/backend/issues/823) | P1 | Grafana/Security | Public staging Grafana hard-code admin password |
| [#824](https://github.com/GSU26SE55/backend/issues/824) | P2 | Ticket/SLA | Staff phụ trách không nhận SLA warning |
| [#825](https://github.com/GSU26SE55/backend/issues/825) | P2 | Ticket/SLA | Multi-replica phát trùng warning/breach |
| [#826](https://github.com/GSU26SE55/backend/issues/826) | P2 | Battery/Concurrency | Multi-replica tạo trùng anomaly/offline/escalation |
| [#827](https://github.com/GSU26SE55/backend/issues/827) | P2 | Battery/IoT | Calibration dedup sai và không notify Manager |
| [#828](https://github.com/GSU26SE55/backend/issues/828) | P2 | Email/Production | Admin invite rơi về localhost dù có WebBaseUrl |
| [#829](https://github.com/GSU26SE55/backend/issues/829) | P2 | Battery/IoT/Security | MQTT bỏ qua scope và revoked/disabled state |
| [#830](https://github.com/GSU26SE55/backend/issues/830) | P2 | Battery/IoT/Mapping | Device được quyền mọi battery cùng site |
| [#831](https://github.com/GSU26SE55/backend/issues/831) | P2 | Battery/IoT/MQTT | Topic battery serial không bind payload identity |
| [#832](https://github.com/GSU26SE55/backend/issues/832) | P2 | IoT/OTA | Boot switch fail làm mất khả năng rollback |
| [#833](https://github.com/GSU26SE55/backend/issues/833) | P2 | IoT/NTP | NTP provision được lưu nhưng không áp dụng |
| [#834](https://github.com/GSU26SE55/backend/issues/834) | P2 | IoT/MQTT Command | QoS1 redelivery lặp side effect |
| [#835](https://github.com/GSU26SE55/backend/issues/835) | P3 | IoT/Offline Queue | Sidecar idempotency fail nhưng enqueue vẫn success |
| [#836](https://github.com/GSU26SE55/backend/issues/836) | P3 | IoT/MQTT/Docs | Topic trong overall.md lệch code/firmware/ACL |
| [#837](https://github.com/GSU26SE55/backend/issues/837) | P2 | AI/Schema | Legacy 3-column được accept nhưng inference crash |
| [#838](https://github.com/GSU26SE55/backend/issues/838) | P3 | AI/gRPC | Prescribe thiếu named-reading parity |
| [#839](https://github.com/GSU26SE55/backend/issues/839) | P2 | AI/Validation | Infinity làm REST 500 nhưng gRPC OK |
| [#840](https://github.com/GSU26SE55/backend/issues/840) | P2 | AI/Safety | Unknown chemistry fallback NMC và mất LFP warning |
| [#841](https://github.com/GSU26SE55/backend/issues/841) | P2 | AI/Cache | REST/gRPC idempotency cache tách process |
| [#842](https://github.com/GSU26SE55/backend/issues/842) | P3 | AI/gRPC | Cancel stream không dừng inference |
| [#843](https://github.com/GSU26SE55/backend/issues/843) | P2 | AI/Concurrency | First requests race RAG/history initialization |
| [#844](https://github.com/GSU26SE55/backend/issues/844) | P2 | AI/RAG Safety | Retrieval rỗng vẫn cho ungrounded enriched output |
| [#845](https://github.com/GSU26SE55/backend/issues/845) | P2 | Battery/AI | HTTP 200 `{}` thành SOH 0/Normal hợp lệ |
| [#846](https://github.com/GSU26SE55/backend/issues/846) | P2 | Battery/Ticket/AI | Handoff làm rơi safety warning, SOP và action steps |

### 5.9 Lỗi mới từ vòng kiểm tra chéo thứ năm

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#847](https://github.com/GSU26SE55/backend/issues/847) | P1 | Helm/File/Battery/Ticket | Thiếu toàn bộ topology gRPC nội bộ |
| [#848](https://github.com/GSU26SE55/backend/issues/848) | P1 | Helm/Security | Secret placeholder và externalSecretName không được dùng |
| [#849](https://github.com/GSU26SE55/backend/issues/849) | P2 | Ticket/Concurrency | Auto-close/rating tạo side effect trùng giữa replica |
| [#850](https://github.com/GSU26SE55/backend/issues/850) | P2 | Battery/Ambient | Dữ liệu vô hiệu/tương lai được persist và poison latest |
| [#851](https://github.com/GSU26SE55/backend/issues/851) | P2 | Battery/Ticket/AI | SOH unknown bị encode thành measured 0% |
| [#852](https://github.com/GSU26SE55/backend/issues/852) | P2 | AI/Artifacts | Mixed artifacts qua startup rồi crash inference đầu tiên |
| [#853](https://github.com/GSU26SE55/backend/issues/853) | P2 | Battery/AI | Fallback client nuốt cancellation và vẫn gọi HTTP |
| [#854](https://github.com/GSU26SE55/backend/issues/854) | P2 | Battery/IoT/Security | Legacy SensorIngest key vượt environmental scope/identity |
| [#855](https://github.com/GSU26SE55/backend/issues/855) | P2 | Battery/IoT/MQTT | QoS0 subscription và auto-ACK làm mất telemetry lỗi |
| [#856](https://github.com/GSU26SE55/backend/issues/856) | P2 | Battery/IoT/OTA | Không kiểm hardware revision/device model compatibility |
| [#857](https://github.com/GSU26SE55/backend/issues/857) | P2 | Battery/IoT/Command | Offline command queue volatile, không giới hạn/TTL |
| [#858](https://github.com/GSU26SE55/backend/issues/858) | P3 | IoT/Command | set_interval ACK success nhưng mất sau reboot |
| [#859](https://github.com/GSU26SE55/backend/issues/859) | P3 | Battery/IoT/Command | Backend nhận command firmware luôn reject |
| [#860](https://github.com/GSU26SE55/backend/issues/860) | P3 | IoT/Simulator | Partial JSONL crash flush; compaction không atomic |
| [#861](https://github.com/GSU26SE55/backend/issues/861) | P2 | Backend/Helm/Security | Chín app container chạy root, helper security không dùng |
| [#862](https://github.com/GSU26SE55/backend/issues/862) | P2 | Production/Supply Chain | Floating latest tags làm deploy không reproducible |
| [#863](https://github.com/GSU26SE55/backend/issues/863) | P2 | Kubernetes/Reliability | Readiness dùng metrics/static response, bỏ dependency |
| [#864](https://github.com/GSU26SE55/backend/issues/864) | P2 | Battery/Aggregate | Optional time filter gây PostgreSQL 42P08/HTTP 500 |
| [#865](https://github.com/GSU26SE55/backend/issues/865) | P2 | Auth/2FA/RateLimit | Limiter bỏ qua challenge token trong body |

### 5.10 Lỗi mới từ vòng kiểm tra hội tụ thứ sáu

Vòng sáu dùng fault-path và trust-boundary audit độc lập cho Backend, AI và IoT. Regression liên quan có **41/41 Ticket**, **13/13 Auth Google OAuth**, **121/121 AI schema/gRPC/RAG** và **118/118 IoT native** pass. Chính các kết quả pass này xác nhận test suite hiện tại chưa bao phủ các đường authorization, persistence failure, proto presence và hardware fault mới phát hiện; không có regression test nào bị diễn giải sai thành bằng chứng “không có lỗi”.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#867](https://github.com/GSU26SE55/backend/issues/867) | P2 | IoT/OTA/Safety | Vẫn flash khi không persist được rollback/verify state |
| [#868](https://github.com/GSU26SE55/backend/issues/868) | P2 | IoT/Provisioning | Partial NVS write vẫn được ACK provision thành công |
| [#869](https://github.com/GSU26SE55/backend/issues/869) | P2 | IoT/INA226/Battery | I²C failure thành reading 0V hợp lệ và tạo SensorMismatch giả |
| [#870](https://github.com/GSU26SE55/backend/issues/870) | P2 | IoT/MQTT | CONNECT được coi thành công dù online publish/command subscribe fail |
| [#871](https://github.com/GSU26SE55/backend/issues/871) | P1 | Ticket/Chat/Security | Staff không liên quan export internal chat và gọi AI trên ticket khác |
| [#872](https://github.com/GSU26SE55/backend/issues/872) | P1 | Ticket/Maintenance/Security | Staff tạo giả maintenance log trên ticket không được assign |
| [#873](https://github.com/GSU26SE55/backend/issues/873) | P1 | Auth/Google OAuth/Security | Access/refresh token bị log nguyên văn |
| [#874](https://github.com/GSU26SE55/backend/issues/874) | P2 | Ticket/Maintenance | Dữ liệu compliance vô lý được accept; summary quá dài có thể 500 |
| [#875](https://github.com/GSU26SE55/backend/issues/875) | P2 | Ticket/Chat Receipt | HTTP 200 receipt có thể mất sau DB failure/restart |
| [#876](https://github.com/GSU26SE55/backend/issues/876) | P3 | Ticket/Chat AI | Domain/dependency/persistence failure trả HTTP 200 |
| [#877](https://github.com/GSU26SE55/backend/issues/877) | P2 | Ticket/AI Contract | Ticket gọi VerifyTicket RPC không tồn tại ở AI server |
| [#878](https://github.com/GSU26SE55/backend/issues/878) | P2 | AI/REST/gRPC | Reading row width trộn lẫn qua validation rồi crash inference |
| [#879](https://github.com/GSU26SE55/backend/issues/879) | P3 | AI/REST-gRPC | Explicit zero PackConfig bị gRPC coi như absent/default |
| [#880](https://github.com/GSU26SE55/backend/issues/880) | P2 | AI/RAG Safety | Tài liệu relevance 0 vẫn cho ungrounded output `enriched=true` |

Tất cả issue #867–#880 đã được đọc lại từ GitHub: milestone `E2E`, label `type: fix`, đúng role và đúng priority. Vì vòng sáu tạo thêm 14 issue nên điều kiện dừng chưa đạt; vòng bảy bắt buộc tiếp tục với checklist mới và phải tạo **0 issue mới** mới được coi là hội tụ.

### 5.11 Lỗi mới từ vòng kiểm tra hội tụ thứ bảy

Vòng bảy đổi checklist sang nested-resource/ownership, persistence split-brain, numerical-model output, gRPC resource starvation và kiểm tra artifact firmware sau clean build. **75/75 AI targeted**, **10/10 SMS claim**, **7/7 Auth session-limit**, **1/1 File delete**, **9/9 Ticket KB**, cùng hai firmware mock/real target đều build pass. Tuy nhiên binary inspection chứng minh target `esp32-s3-real` vẫn là mock; build xanh được ghi đúng là repro của #884, không phải real-hardware pass.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#881](https://github.com/GSU26SE55/backend/issues/881) | P2 | IoT/Serial CLI/Security | `set apikey` in plaintext ra Serial log |
| [#882](https://github.com/GSU26SE55/backend/issues/882) | P2 | IoT/DS18B20 | Một probe disconnect làm mất temperature của mọi battery phía sau |
| [#883](https://github.com/GSU26SE55/backend/issues/883) | P2 | IoT/Offline Queue | I/O failure có thể xóa cả batch cũ lẫn batch thay thế |
| [#884](https://github.com/GSU26SE55/backend/issues/884) | P1 | IoT/Firmware Build | Target `esp32-s3-real` thực tế undef flag và ship mock telemetry |
| [#885](https://github.com/GSU26SE55/backend/issues/885) | P2 | AI/Model Safety/Battery | Non-finite output thành SOH 100% giả và REST/gRPC lệch nhau |
| [#886](https://github.com/GSU26SE55/backend/issues/886) | P2 | AI/gRPC/Availability | Idle PredictStream chiếm hết shared worker và chặn unary/health |
| [#887](https://github.com/GSU26SE55/backend/issues/887) | P1 | SMS/Gateway/Security | Gateway khác claim được stale targeted OTP/SMS |
| [#888](https://github.com/GSU26SE55/backend/issues/888) | P2 | FileStorage/Consistency | Object xóa trước metadata commit tạo live metadata trỏ file mất |
| [#889](https://github.com/GSU26SE55/backend/issues/889) | P2 | Auth/Sessions | Refresh tại session cap revoke nhầm device khác |
| [#890](https://github.com/GSU26SE55/backend/issues/890) | P3 | Ticket/Knowledge Base | Version route bỏ parent ArticleId và trộn article |

Tất cả #881–#890 đã được xác minh trên GitHub có milestone `E2E`, `type: fix`, đúng role/priority. Vòng bảy thêm 10 issue nên chưa đạt điều kiện dừng; bắt buộc chạy vòng tám với checklist độc lập và chỉ hội tụ nếu vòng đó tạo 0 issue.

### 5.12 Lỗi mới từ vòng kiểm tra hội tụ thứ tám

Vòng tám chuyển sang negative-space audit: transaction liên kho, shared-row concurrency, quyền participant trên nested route, PII/provider logging, REST event-loop/resource admission, firmware supply chain và secret propagation trong công cụ vận hành MQTT. Regression liên quan có **25/25 Email consumer**, **3/3 Auth reset-password**, **27/27 SMS claim/report**, **53/53 Ticket chat/auth** và **37/37 AI router/extractor** pass. Các suite xanh nhưng thiếu đúng fault/concurrency/privacy/load cases đã tái hiện dưới đây.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#891](https://github.com/GSU26SE55/backend/issues/891) | P3 | IoT/Firmware/Supply Chain | Caret dependency ranges và không có lock làm artifact không reproducible |
| [#892](https://github.com/GSU26SE55/backend/issues/892) | P2 | IoT/MQTT/Security | Provision/bootstrap đưa plaintext broker password vào argv/log |
| [#893](https://github.com/GSU26SE55/backend/issues/893) | P2 | SMS/Concurrency | Conflict shared gateway row bị coi nhầm là duplicate report |
| [#895](https://github.com/GSU26SE55/backend/issues/895) | P2 | Email/Privacy | Recipient email và raw provider response bị ghi vào log |
| [#897](https://github.com/GSU26SE55/backend/issues/897) | P2 | Ticket/Chat/Authorization | Watcher/Delegate bị chặn không nhất quán ở chat subroute |
| [#898](https://github.com/GSU26SE55/backend/issues/898) | P2 | Auth/Password Reset | DB failure làm cháy reset token trước khi password commit |
| [#899](https://github.com/GSU26SE55/backend/issues/899) | P2 | SMS/Daily Quota | Nhiều claim chưa report vượt `DailyLimit` |
| [#900](https://github.com/GSU26SE55/backend/issues/900) | P2 | AI/REST/Availability | Synchronous inference/LLM chặn toàn bộ FastAPI event loop |
| [#901](https://github.com/GSU26SE55/backend/issues/901) | P3 | AI/REST/Resource Limits | Oversized JSON được parse hết trước validation, không có body limit |

Tất cả chín issue vòng tám đã được tạo với reproduction, actual/expected, impact và acceptance criteria. #894/#896 là khoảng số do hai POST song song trả JSON lỗi và GitHub không tạo resource; kiểm tra trực tiếp hai URL trả 404, sau đó hai issue bị thiếu được tạo tuần tự ở #898/#899 nên không có defect nào bị mất hoặc tạo trùng. Vì vòng tám vẫn có 9 issue mới, điều kiện dừng chưa đạt và vòng chín đầy đủ là bắt buộc.

### 5.13 Lỗi mới từ vòng kiểm tra hội tụ thứ chín

Vòng chín chạy lại inventory **73 controller / 374 route / 49 consumer / 42 worker**, kiểm tra production Compose, shared middleware/pagination, OTA build–storage–download, heartbeat cross-contract, MassTransit retry, LLM structured safety và firmware CI/BMS. Regression có **118/118 IoT native**, **539 AI pass + 1 known #759**, **17/17 Battery AI**, **25/25 Ticket saga/restart**, **5/5 Ticket Blog consumer**, **23/23 Shared logging/pagination**, **2/2 Battery firmware** và **6/6 Battery lifecycle** pass. Các suite hiện tại thiếu đúng boundary/retry/storage/schema cases đã tái hiện.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#902](https://github.com/GSU26SE55/backend/issues/902) | P2 | IoT/CI/Security | Secret-history guard luôn fail dù không có secret match |
| [#903](https://github.com/GSU26SE55/backend/issues/903) | P3 | IoT/CI/Coverage | Infra-only changes không trigger infra validation workflow |
| [#904](https://github.com/GSU26SE55/backend/issues/904) | P2 | IoT/BMS/JK/Safety | Signed int32 current bị truncate 16-bit và đảo chiều charge/discharge |
| [#905](https://github.com/GSU26SE55/backend/issues/905) | P1 | IoT/OTA/Version | Version build không đăng ký được và post-flash verify luôn fail |
| [#906](https://github.com/GSU26SE55/backend/issues/906) | P2 | Battery/IoT/Heartbeat | Resource/thermal fields được nhận 200 nhưng không persist |
| [#907](https://github.com/GSU26SE55/backend/issues/907) | P2 | AI/LLM Safety | Type coercion đảo unsafe verdict và bypass action-step checks |
| [#908](https://github.com/GSU26SE55/backend/issues/908) | P1 | Production/File/Ticket Voice | Thiếu gRPC server/client config làm FileStorage crash-loop |
| [#909](https://github.com/GSU26SE55/backend/issues/909) | P2 | Ticket/Blog/Retry | Commit terminal failure trước throw vô hiệu hóa mọi broker retry |
| [#910](https://github.com/GSU26SE55/backend/issues/910) | P2 | Shared/Security Logging | Raw query log lộ unsubscribe token và OAuth code/state |
| [#911](https://github.com/GSU26SE55/backend/issues/911) | P3 | Shared/Pagination | PageNumber cực lớn overflow thành negative database offset/5xx |
| [#912](https://github.com/GSU26SE55/backend/issues/912) | P2 | Battery/OTA Storage | Cả local/PVC và object mode đều không tạo usable artifact URL |
| [#913](https://github.com/GSU26SE55/backend/issues/913) | P2 | Battery/OTA Upload | Invalid/partial binary thành orphan vĩnh viễn, có thể đầy PVC |

Tất cả #902–#913 đã được đọc lại từ GitHub và có milestone `E2E`, label `type: fix`, đúng role/priority. Vòng chín thêm 12 issue nên chưa hội tụ; vòng mười phải audit lại đầy đủ và chỉ là vòng dừng nếu tạo 0 issue mới.

### 5.14 Lỗi mới từ vòng kiểm tra hội tụ thứ mười

Vòng mười dùng terminal-candidate checklist cho toàn bộ **73 controller / 374 route / 49 consumer / 42 worker**, 185 command, 126 query, schema REST/gRPC, model/RAG/history/safety, firmware scheduler/OTA/BMS và broker/Compose/script. Regression có **75/75 Ticket triage/validator**, **4/4 Maintenance API**, **234/234 AI focused**, **9/9 Battery AI**, **118/118 IoT native** pass; hai firmware profile mock/real build thành công, hai Compose render hợp lệ. Các suite chưa bao phủ đúng các contract, persistence và failure-path dưới đây.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#914](https://github.com/GSU26SE55/backend/issues/914) | P2 | IoT/HTTPS Backoff | Live batch bỏ qua backoff; mọi device dùng cùng jitter seed |
| [#915](https://github.com/GSU26SE55/backend/issues/915) | P2 | Ticket/Triage Audit | Manual priority áp dụng nhưng justification bắt buộc bị bỏ |
| [#916](https://github.com/GSU26SE55/backend/issues/916) | P2 | Ticket/Maintenance Lifecycle | PATCH trả 200 nhưng không thể đóng maintenance log |
| [#917](https://github.com/GSU26SE55/backend/issues/917) | P2 | Battery/IoT/OTA Contract | Release notes hợp lệ truncate firmware-check JSON 2 KB và chặn update |
| [#918](https://github.com/GSU26SE55/backend/issues/918) | P2 | AI/REST Contract | Boolean/numeric string bị ép thành telemetry hợp lệ và đổi model semantics |
| [#919](https://github.com/GSU26SE55/backend/issues/919) | P2 | AI/Safety Judge | Judge timeout fail-open và phát hành prescription chưa kiểm duyệt |
| [#920](https://github.com/GSU26SE55/backend/issues/920) | P2 | AI/Causal History | SOH history mất khi restart và tách giữa HTTP/gRPC process |

Tất cả #914–#920 đã được tạo tuần tự với reproduction, source evidence, actual/expected, impact và acceptance criteria. Vòng mười thêm 7 issue nên chưa hội tụ; vòng mười một đầy đủ là bắt buộc và chỉ được dừng nếu tạo 0 issue mới.

### 5.15 Lỗi mới từ vòng kiểm tra thứ 11

Bằng chứng chính: đối chiếu firmware scheduler/BMS/heartbeat với contract `overall.md`, audit authorization/outbox, nested-resource authorization, allocator concurrency, numerical-boundary/lock-order/deadline/safety của AI và controlled failure-path repro. Vòng này tạo 15 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#921](https://github.com/GSU26SE55/backend/issues/921) | P2 | IoT/BMS | Timeout 500 ms không dùng khiến polling multi-drop offline có thể chặn main loop khoảng 16 giây |
| [#922](https://github.com/GSU26SE55/backend/issues/922) | P2 | IoT/Battery/Heartbeat | Firmware bỏ cadence, clock-skew và OTA controls trong heartbeat ACK |
| [#923](https://github.com/GSU26SE55/backend/issues/923) | P2 | IoT/BMS | Modbus chính lỗi làm mất cả telemetry INA226/DS18B20 dự phòng đang khỏe |
| [#924](https://github.com/GSU26SE55/backend/issues/924) | P2 | IoT/INA226 | Một phép đo vật lý bị sao chép cho mọi battery asset |
| [#925](https://github.com/GSU26SE55/backend/issues/925) | P1 | Auth/Gateway/JWT | Token đã revoke vẫn hợp lệ trên mọi backend ngoài AuthService |
| [#926](https://github.com/GSU26SE55/backend/issues/926) | P1 | Auth/2FA SMS | Endpoint trả 200 nhưng không commit `SendSmsCommand` vào outbox |
| [#927](https://github.com/GSU26SE55/backend/issues/927) | P2 | Ticket/Chat Escalation | ACK review trả 200 nhưng không persist ACK event |
| [#928](https://github.com/GSU26SE55/backend/issues/928) | P2 | Ticket/Chat Templates | Staff bất kỳ publish Global template mà không có permission bắt buộc |
| [#929](https://github.com/GSU26SE55/backend/issues/929) | P2 | Ticket/Knowledge Base | Bộ cấp mã/version kiểu read-max va chạm khi concurrent |
| [#930](https://github.com/GSU26SE55/backend/issues/930) | P2 | Notification/Preferences | Timezone sai vẫn persist hoặc gây 500 thay vì validation có kiểm soát |
| [#931](https://github.com/GSU26SE55/backend/issues/931) | P2 | Battery/IoT Offline | Heartbeat interval hợp lệ vượt ngưỡng global và tạo false offline alert |
| [#932](https://github.com/GSU26SE55/backend/issues/932) | P2 | AI/Numerical Boundary | Telemetry finite overflow float32 và trả REST 500/gRPC INTERNAL |
| [#933](https://github.com/GSU26SE55/backend/issues/933) | P1 | AI/Concurrency | Health metrics và exhausted-budget lấy lock ngược thứ tự, có thể deadlock |
| [#934](https://github.com/GSU26SE55/backend/issues/934) | P2 | AI/LLM Deadline | Retry provider vượt chain deadline và query-generation wall-clock budget |
| [#935](https://github.com/GSU26SE55/backend/issues/935) | P1 | AI/Safety Gate | Negation scope bypass forbidden-action checks và làm mất LOTO/thermal injection |

### 5.16 Lỗi mới từ vòng kiểm tra thứ 12

Bằng chứng chính: firmware LittleFS recovery inspection, Helm render và shared JWT key-rotation audit. Vòng này tạo 3 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#936](https://github.com/GSU26SE55/backend/issues/936) | P2 | IoT/LittleFS | Auto-recovery mount xóa offline queue và MQTT CA certificate |
| [#937](https://github.com/GSU26SE55/backend/issues/937) | P2 | Auth/Shared JWT | Previous signing key bị ASP.NET authentication của mọi service bỏ qua |
| [#938](https://github.com/GSU26SE55/backend/issues/938) | P1 | Helm/Auth/Google OAuth | Callback URI render chứa prefix `/auth-service` không được map |

### 5.17 Lỗi mới từ vòng kiểm tra thứ 13

Bằng chứng chính: account lifecycle/SignalR authorization repro, DLQ metric lifecycle và multi-process LLM configuration review. Vòng này tạo 4 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#939](https://github.com/GSU26SE55/backend/issues/939) | P2 | Auth/Reactivation | Email đã tái sử dụng làm restore 90 ngày crash unique-index/500 |
| [#940](https://github.com/GSU26SE55/backend/issues/940) | P1 | Ticket/SignalR | Participant bị remove/tự leave vẫn có thể còn subscribe ticket chat |
| [#941](https://github.com/GSU26SE55/backend/issues/941) | P3 | Notification/DLQ | Xóa error queue để lại Prometheus gauge non-zero và false alert vĩnh viễn |
| [#942](https://github.com/GSU26SE55/backend/issues/942) | P2 | AI/LLM Rate Limit | Budget/concurrency reset theo process và không cấu hình được trong Compose |

### 5.18 Lỗi mới từ vòng kiểm tra thứ 14

Bằng chứng chính: soft-delete/reactivation conflict repro, SignalR reconnect state-machine audit và Prometheus cardinality inspection. Vòng này tạo 5 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#943](https://github.com/GSU26SE55/backend/issues/943) | P2 | Ticket/Blog | Tạo lại slug đã soft-delete qua validation rồi trả database 500 |
| [#944](https://github.com/GSU26SE55/backend/issues/944) | P2 | Auth/Roles | Tạo lại custom role đã soft-delete gây unique violation không được xử lý |
| [#945](https://github.com/GSU26SE55/backend/issues/945) | P2 | Auth/Reactivation | Profile tombstone ẩn còn lại làm các lần profile write sau bị lỗi |
| [#946](https://github.com/GSU26SE55/backend/issues/946) | P2 | Ticket/SignalR | LeaveTicket poison rejoin cùng socket; disconnect làm sai connected-user gauge |
| [#947](https://github.com/GSU26SE55/backend/issues/947) | P3 | Ticket/Metrics | Label chat theo ticket tạo cardinality Prometheus không giới hạn |

### 5.19 Lỗi mới từ vòng kiểm tra thứ 15

Bằng chứng chính: account delete/reactivate and revocation failure audit cùng concurrent 2FA replay repro. Vòng này tạo 3 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#948](https://github.com/GSU26SE55/backend/issues/948) | P2 | Auth/Delete/Reactivate/2FA | Trusted device sống qua self-delete và bypass 2FA sau restore |
| [#949](https://github.com/GSU26SE55/backend/issues/949) | P1 | Auth/Security Reconciliation | Revocation post-commit lỗi để state còn active và retry không sửa được |
| [#950](https://github.com/GSU26SE55/backend/issues/950) | P1 | Auth/2FA Replay | Consume challenge không atomic, concurrent verify có thể mint nhiều session |

### 5.20 Lỗi mới từ vòng kiểm tra thứ 16

Bằng chứng chính: cross-owner Ticket chat authorization, millis-rollover arithmetic và middleware registration inventory. Vòng này tạo 3 issue; #953 không tồn tại.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#951](https://github.com/GSU26SE55/backend/issues/951) | P1 | Ticket/Chat Authorization | Customer tạo/reply chat trên ticket của user bất kỳ |
| [#952](https://github.com/GSU26SE55/backend/issues/952) | P2 | IoT/Scheduler | So sánh deadline unsigned gây retry storm hoặc ngừng flush ở rollover 49,7 ngày |
| [#954](https://github.com/GSU26SE55/backend/issues/954) | P2 | Ticket/Idempotency | Đã đăng ký/config Idempotency-Key nhưng thiếu middleware nên retry write chạy hai lần |

### 5.21 Lỗi mới từ vòng kiểm tra thứ 17

Bằng chứng chính: audit transfer-owner lifecycle/audit trail và Ticket participant transition. Vòng này tạo 3 issue nên chưa hội tụ; #955 là pull request, không phải defect.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#956](https://github.com/GSU26SE55/backend/issues/956) | P1 | Battery/Transfer Owner | Transfer asset bỏ guard ticket đang mở bắt buộc |
| [#957](https://github.com/GSU26SE55/backend/issues/957) | P2 | Battery/Transfer Audit | Lý do transfer đã accept bị bỏ, làm mất audit evidence |
| [#958](https://github.com/GSU26SE55/backend/issues/958) | P1 | Ticket/Assignment | Participant có sẵn không được promote thành `PrimaryAssignee` |

### 5.22 Lỗi mới từ vòng kiểm tra thứ 18

Bằng chứng chính: assignment command/participant reconciliation audit. Vòng này tạo 1 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#959](https://github.com/GSU26SE55/backend/issues/959) | P1 | Ticket/Assignment | Supporter IDs persist không validate Staff và không reconcile participant |

### 5.23 Lỗi mới từ vòng kiểm tra thứ 19

Bằng chứng chính: **22/22** targeted Ticket assignment/account-sync tests pass, sau đó requirement-to-worker/handler audit chứng minh thiếu capacity enforcement, requeue và approval-timeout worker. Vòng này tạo 3 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#960](https://github.com/GSU26SE55/backend/issues/960) | P1 | Ticket/Assignment Workload | `MaxConcurrentTickets` không bao giờ được enforce |
| [#961](https://github.com/GSU26SE55/backend/issues/961) | P1 | Ticket/Account Status | Status consumer không requeue ticket bị ảnh hưởng |
| [#962](https://github.com/GSU26SE55/backend/issues/962) | P2 | Ticket/Approval | Thiếu worker timeout approval 24 giờ bắt buộc |

### 5.24 Lỗi mới từ vòng kiểm tra thứ 20

Bằng chứng chính: **25/25** targeted reprioritization/lifecycle tests pass; state-transition audit xác nhận ownership cũ vẫn còn sau demotion, còn route/command inventory xác nhận thiếu bulk import và QR onboarding. Vòng này tạo 3 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#963](https://github.com/GSU26SE55/backend/issues/963) | P1 | Ticket/Escalation | Demote `PrimaryHandler` để lại ownership và lifecycle authorization stale |
| [#964](https://github.com/GSU26SE55/backend/issues/964) | P2 | Battery/Bulk Import | Thiếu bulk import battery asset đã document |
| [#965](https://github.com/GSU26SE55/backend/issues/965) | P1 | Battery/QR Onboarding | Thiếu claim-code, QR render/persistence và customer claim flow |

### 5.25 Lỗi mới từ vòng kiểm tra thứ 21

Bằng chứng chính: requirement-to-route/entity/command/query/worker inventory trên **73 controller / 374 route / 185 command / 126 query / 49 consumer / 42 worker**, cộng source-flow audit cho GPS, auto-resolve và enum validation. Vòng này tạo 17 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#966](https://github.com/GSU26SE55/backend/issues/966) | P2 | Battery/IoT Monitoring | Thiếu heartbeat history và uptime endpoints bắt buộc |
| [#967](https://github.com/GSU26SE55/backend/issues/967) | P1 | Ticket/Relations/Watch | Thiếu hoàn toàn TicketRelation và TicketSubscription flows |
| [#968](https://github.com/GSU26SE55/backend/issues/968) | P1 | Ticket/SLA Pause | Thiếu pause limits, approval endpoints và enforcement worker |
| [#969](https://github.com/GSU26SE55/backend/issues/969) | P1 | Notification/Realtime | Thiếu topic-based SSE business API và replay |
| [#970](https://github.com/GSU26SE55/backend/issues/970) | P1 | Battery/Alert Controls | Thiếu silence, snooze, grouping và ACK timeline |
| [#971](https://github.com/GSU26SE55/backend/issues/971) | P1 | Auth/Ticket Bulk Ops | Thiếu user invite và ticket bulk mutation APIs |
| [#972](https://github.com/GSU26SE55/backend/issues/972) | P1 | Auth/GDPR Export | Export sync Auth-only thiếu async cross-service export bắt buộc |
| [#973](https://github.com/GSU26SE55/backend/issues/973) | P1 | Auth/Account Deletion | Thiếu cooling-off, cancel, ticket guard và anonymization lifecycle |
| [#974](https://github.com/GSU26SE55/backend/issues/974) | P1 | Ticket/Maintenance GPS | Tin tọa độ check-in mà không validate site radius |
| [#975](https://github.com/GSU26SE55/backend/issues/975) | P1 | Platform/App Management | Thiếu compatibility, feature flags và announcement APIs |
| [#976](https://github.com/GSU26SE55/backend/issues/976) | P2 | Ticket/Preventive Maintenance | Thiếu schedule, worker, APIs và compliance reports |
| [#977](https://github.com/GSU26SE55/backend/issues/977) | P2 | Ticket/Public KB | Thiếu public article và pre-ticket suggestion flows |
| [#978](https://github.com/GSU26SE55/backend/issues/978) | P2 | Platform/Webhooks | Thiếu subscription delivery và scoped partner API keys |
| [#979](https://github.com/GSU26SE55/backend/issues/979) | P1 | Platform/Mobile Deep Links | Thiếu AASA và assetlinks verification endpoints |
| [#980](https://github.com/GSU26SE55/backend/issues/980) | P2 | Ticket/Manager Assignment | Thiếu staff workload endpoint và complete capacity projection |
| [#981](https://github.com/GSU26SE55/backend/issues/981) | P2 | Battery/Ticket Alert Audit | Alert auto-resolve không propagate vào active ticket timeline |
| [#982](https://github.com/GSU26SE55/backend/issues/982) | P2 | Battery/Domain Validation | Undefined enum values được accept và persist |

### 5.26 Lỗi mới từ vòng kiểm tra thứ 22

Bằng chứng chính: **22/22 Auth Register**, **15/15 FileStorage handler** và **1/1 TicketCreate** targeted tests pass; gap/negative-path audit tiếp tục xác nhận các yêu cầu lifecycle không có hoặc không được enforce. Vòng này tạo 8 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#983](https://github.com/GSU26SE55/backend/issues/983) | P1 | Auth/Children Data | Registration dưới 18 tuổi thiếu age/guardian enforcement |
| [#984](https://github.com/GSU26SE55/backend/issues/984) | P1 | Ticket/Daily Limit | Thiếu giới hạn 10 ticket/customer/ngày và Manager alert |
| [#985](https://github.com/GSU26SE55/backend/issues/985) | P1 | FileStorage/Malware | Upload được expose `Ready` mà không qua scan gate bắt buộc |
| [#986](https://github.com/GSU26SE55/backend/issues/986) | P1 | Auth/Password Lifecycle | Thiếu 12 ký tự, history, expiry và first-login policy |
| [#987](https://github.com/GSU26SE55/backend/issues/987) | P1 | Auth/Legal Consent | Thiếu terms, privacy và cookie-consent surfaces |
| [#988](https://github.com/GSU26SE55/backend/issues/988) | P1 | Platform/Unified Search | Thiếu cross-entity search, suggestion, saved search và analytics |
| [#989](https://github.com/GSU26SE55/backend/issues/989) | P1 | Platform/Public Status | Thiếu status aggregation và subscription APIs |
| [#990](https://github.com/GSU26SE55/backend/issues/990) | P2 | Battery/IoT Clock Drift | Drift bị reject nhưng không persist device incident counter |

### 5.27 Lỗi mới từ vòng kiểm tra thứ 23

Bằng chứng chính: requirement-to-surface inventory cho mobile/admin/media/analytics/compliance và API Gateway protection. Vòng này tạo 6 issue nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#991](https://github.com/GSU26SE55/backend/issues/991) | P1 | Platform/Mobile Operations | Thiếu analytics ingestion, push-token cleanup, rating prompt và mobile-lite APIs |
| [#992](https://github.com/GSU26SE55/backend/issues/992) | P2 | Platform/Admin Operations | Thiếu impersonation, job monitor, cache management và dynamic config APIs |
| [#993](https://github.com/GSU26SE55/backend/issues/993) | P2 | FileStorage/Media Pipeline | Thiếu image variants và accessibility metadata |
| [#994](https://github.com/GSU26SE55/backend/issues/994) | P2 | Platform/Customer Success | Thiếu NPS, customer health và adoption/funnel flows |
| [#995](https://github.com/GSU26SE55/backend/issues/995) | P2 | Platform/Compliance | Thiếu PIA, DPA, incident-response và responsible-disclosure deliverables |
| [#996](https://github.com/GSU26SE55/backend/issues/996) | P1 | ApiGateway/Application Protection | Thiếu WAF filtering và lifecycle chặn IP vi phạm rate limit lặp lại |

### 5.28 Lỗi mới từ vòng kiểm tra thứ 24

Bằng chứng chính: **32/32 Battery targeted**, **418/418 full unit**, **539 AI pass + 1 known #759**, **16/16 Battery–AI bridge**, **25/25 Ticket saga/restart**, **118/118 IoT native**, **3/3 firmware build**, **2/2 HTTP** và **3/3 MQTT** pass. Failure-path audit xác nhận queue offline quá 5 phút bị clock-drift policy từ chối rồi firmware xóa dữ liệu. Vòng này tạo đúng 1 issue unique nên chưa hội tụ. #998 là duplicate #809, đã đóng `not_planned` và gỡ milestone, không tính vào tổng.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#997](https://github.com/GSU26SE55/backend/issues/997) | P1 | IoT/Battery/Offline Queue | Telemetry queue offline quá 5 phút bị clock-drift reject rồi firmware xóa vĩnh viễn |

### 5.29 Lỗi mới từ vòng kiểm tra thứ 25

Bằng chứng chính: **418/418 Battery unit**, **539 AI pass + 1 known #759**, **16/16 Battery–AI bridge**, **25/25 Ticket saga/restart**, **118/118 IoT native**, **3/3 firmware build**, **2/2 HTTP** và **3/3 MQTT** pass. Requirement-to-implementation và authorization audit tiếp tục xác nhận ba gap P1 dưới đây. Vòng này tạo 3 issue unique nên chưa hội tụ.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#999](https://github.com/GSU26SE55/backend/issues/999) | P1 | Battery/AI/Threshold Alerts | Threshold alert bypass AI classification và V2 enrichment |
| [#1000](https://github.com/GSU26SE55/backend/issues/1000) | P1 | AI/MLOps | Thiếu deployment, retraining, batch prediction, drift monitoring và model-admin surfaces |
| [#1001](https://github.com/GSU26SE55/backend/issues/1001) | P1 | Battery/Customer Dashboard | Customer không truy cập được SOH/anomaly dashboard của chính mình; routes bắt buộc không tồn tại |

### 5.30 Lỗi mới từ vòng kiểm tra thứ 26

Bằng chứng chính: **3/3 Auth targeted**, **418/418 Backend/Battery unit** từ gate liền trước, **539 AI pass + 1 known #759**, bridges **16/16 + 25/25**, **118/118 IoT native**, 3 firmware builds, 2 HTTP integrations và 3 real-MQTT integrations pass. Validation/readiness audit tạo 2 issue dưới đây.

| Issue | Mức | Phần | Tóm tắt |
|---|---|---|---|
| [#1002](https://github.com/GSU26SE55/backend/issues/1002) | P2 | Auth/AccountProfile | Timezone không hợp lệ được persist vào AccountProfile |
| [#1003](https://github.com/GSU26SE55/backend/issues/1003) | P1 | Project/Demo Readiness | Thiếu artifacts và scripts bắt buộc để chuẩn bị/chạy project demo |

### 5.31 Vòng kiểm tra hội tụ thứ 27 — zero-new

Vòng 27 full terminal audit không tạo issue mới. Backend đối chiếu terminal audit report với GitHub qua #1003; Battery **418/418**, Auth **3/3** và Compose config pass. AI có **539 pass + 1 known #759**, coverage **92%**, bridges **16/16 + 25/25**. IoT có **118/118 native**, 3 firmware builds, 2 HTTP integrations và 3 real-MQTT integrations pass. Không có dòng issue trong mục này vì kết quả là **0 defect mới**.

Mỗi issue ở trên có: source path/dòng hoặc requirement-to-implementation evidence, điều kiện tái hiện khi áp dụng, actual/expected, ảnh hưởng và hướng acceptance/regression test. Tất cả 276 issue E2E unique đã được kiểm tra lại có milestone E2E và label severity/role/type. Không tạo issue cho warning chưa chứng minh ảnh hưởng hoặc lỗi môi trường sandbox.

## 6. Những phần không thể xác nhận tuyệt đối

- Không gửi email/SMS/push thật qua Mailjet/Expo/nhà cung cấp ngoài để tránh gây external side effect. Consumer registration, queue binding, unit/integration test và backlog đã được kiểm tra.
- Không chạy Google OAuth callback thật, 2FA với thiết bị người dùng, real S3 cloud hoặc firmware OTA lên phần cứng thật.
- Không có HIL/chaos network lab; không tuyên bố pass các hạng mục đó.
- Browser/Playwright runtime không được expose trong phiên và backend không có UI. SSE đã được chạy runtime direct + Gateway bằng curl; SignalR được bao phủ bằng source/integration/contract audit; MQTT uplink/downlink được pub/sub runtime.
- Python remote CVE audit bị policy chặn; NuGet advisory scan đã chạy. Không suy diễn kết quả Python CVE.
- Admin demo seed credential không còn hợp lệ trong persistent DB nên vòng role-matrix runtime chỉ có Manager/Staff/Customer; authorization Admin được đối chiếu bằng integration tests và static endpoint inventory.
- Không deploy production stack ra public internet để tránh mở MinIO/default credential và Gateway sai route đã xác nhận bằng config [#787](https://github.com/GSU26SE55/backend/issues/787), [#788](https://github.com/GSU26SE55/backend/issues/788). Hai lỗi là deterministic từ effective production configuration, không cần gây exposure thật để kết luận.
- Kiểm tra cuối xác nhận cả 276 issue E2E unique (#722–#865, #867–#893, #895, #897–#952, #954, #956–#997 và #999–#1003) đều có milestone E2E. #866 là feature ngoài audit; #894/#896/#953 không tồn tại; #955 là PR; #998 là duplicate #809, đã đóng `not_planned` và gỡ milestone. Việc “toàn bộ” trong báo cáo nghĩa là toàn bộ source/docs/route/test có thể quan sát trong checkout và môi trường hiện có, không phải bảo đảm toán học rằng hệ thống không còn defect chưa biết. Round27 zero-new xác lập hội tụ trong phạm vi quan sát này.

## 7. Trạng thái workspace sau kiểm thử

- Backend source không bị sửa; checkout `dev` tại `1050d20` chỉ có báo cáo E2E mới ở root sau kiểm thử.
- Bốn Chroma artifact do AI test mutate đã được khôi phục từ `HEAD`; AI checkout `dev` tại `70dd5f1` chỉ còn `AI_SYSTEM_ANALYSIS.md` có sẵn của người dùng ở trạng thái untracked và file này không bị đụng tới.
- IoT checkout `dev` tại `61e4385` vẫn giữ nguyên các thay đổi có sẵn của người dùng trong cấu hình/script Mosquitto (`.gitignore`, `mosquitto.conf`, `gen-certs.sh`, `config/conf.d/`); audit không sửa hoặc hoàn nguyên chúng.
- Không xóa orphan Docker containers/volumes có sẵn và không reset dữ liệu người dùng. Build/test artifacts được giữ trong các đường dẫn ignored của công cụ.
- Vòng cuối xác minh Compose render/config, test cục bộ và các bridge nêu ở §5.31; không tuyên bố toàn bộ service/container vẫn đang chạy sau thời điểm khóa sổ. AI Docker deployment vẫn bị chặn bởi #734.

## 8. Thứ tự xử lý đề xuất

1. Chặn Helm production trước: #847–#848 cùng #813–#816; chart hiện vừa thiếu gRPC topology vừa không deploy Secret hợp lệ. Thu hồi/rotate credential ở #822–#823 ngay, không chờ release.
2. Đóng security perimeter/identity: #854, #861–#863 cùng #787–#789, #791, #806, #813–#817, #829–#831 và các P1 cũ.
3. Chặn mất/trùng side effect: #849, #853, #855, #857 cùng #818, #825–#827, #834–#835, #841–#843, #789–#794 và #764–#765.
4. Sửa độ đúng dữ liệu/AI: #850–#852, #856, #864–#865; sau đó #734, #781–#782 và #837–#846; rerun unified REST↔gRPC↔Battery↔Ticket E2E.
5. Sửa command/firmware/simulator #858–#860 và các IoT issue còn lại, rồi toàn bộ P3; dùng multi-replica, OpenAPI role matrix, broker fault/redelivery, Helm policy và cross-transport semantic tests làm regression gate.
