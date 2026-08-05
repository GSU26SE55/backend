# Tiến độ milestone E2E — 177 issue của Alexdev257

> Sổ trạng thái để phiên sau nối tiếp. KHÔNG commit (người dùng tự commit).

## Hạ tầng (xong)

- `loop.config.mjs` — thang L0 build → L1 unit → L2 integration (gate) → L3 e2e (gate)
- `loop/context/{constitution,glossary,conventions}.md` — hiến pháp dự án
- `tools/loop/trx2junit.mjs` — TRX → JUnit (engine không có adapter TRX)
- `loopctl doctor` xanh · `loopctl verify` xanh

## Baseline (trước khi sửa gì)

| Tầng | Kết quả |
|---|---|
| build | 0 error |
| unit | 2818 pass |
| integration | 295 pass |
| e2e-smoke | 14/14 pass |

## Phân loại 177 issue

| Nhóm | Số | Ghi chú |
|---|---|---|
| Backend-only | 133 | sửa được trong repo này |
| Mixed (firmware + backend) | 11 | sửa được **nửa backend** tại đây |
| Firmware-only | 33 | code ở repo `/Users/alex/Documents/capstone/iot` — NGOÀI repo này |
| Trong đó "feature absent" | 18 | không phải sửa lỗi mà là **xây mới** subsystem |

Firmware-only: 735 736 737 738 739 740 741 745 746 747 748 749 809 832 835 858 860 867
868 870 881 882 883 884 891 903 904 914 921 923 924 936 952

Mixed: 742 743 807 808 859 869 905 906 917 922 997

"Absent" (xây mới): 847 964 965 966 969 970 971 986 987 988 989 991 992 993 995 996 1001 1003

## Nhật ký từng issue

### Repo `iot` — đã dựng loop-engine riêng (2026-08-04)

Nhóm firmware (33 issue) nằm ở repo **`GSU26SE55/iot`**, KHÔNG phải backend. Ban đầu định
chỉ báo cáo, nhưng kiểm ra repo đó **có hạ tầng kiểm chứng thật**: PlatformIO + 12 bộ test
native (118 ca) + CI workflow, `pio` đã cài sẵn ⇒ sửa được CÓ BẢO CHỨNG nên làm luôn.

Thang bám đúng `.github/workflows/firmware-ci.yml`:
**L0 `pio test -e native` → L1 compile `esp32-s3-devkitc-1` + `esp32-s3-real`**. Chu kỳ ~26s.
Baseline: 118 test, compile 2 env xanh.

| # | Thẩm định | Trạng thái | Ghi chú |
|---|---|---|---|
| 735 | **CHUẨN** | ĐÃ SỬA | `setInsecure()` ở HTTPS + OTA. Gom về helper CA dùng chung, fail closed. +10 test |
| 736 | **CHUẨN** | ĐÃ SỬA | Cảm biến an toàn bị gate theo mạng. Gỡ gate + timestamp lúc phát hiện. +8 test |
| 737 | **CHUẨN** | ĐÃ SỬA | Mất Wi-Fi chỉ đếm fail, không lấy mẫu/xếp hàng. +6 test |
| 738 | **CHUẨN** | ĐÃ SỬA | Simulator so `== 200` trong khi backend trả **201**. +9 test Python. |
| 739 | **CHUẨN** | ĐÃ SỬA | Simulator PascalCase còn firmware camelCase. Lộ thêm 1 lỗi timestamp. +7 test hợp đồng. |
| 740 | **CHUẨN** | ĐÃ SỬA | MQTT publish một phần → HTTPS gửi lại cả batch ⇒ trùng. +10 test. |
| 741 | **CHUẨN** | ĐÃ SỬA | 4xx bị retry mỗi tick (~180 req/phút vô hạn). Backoff + dừng hẳn. +8 test. |
| 745 | **CHUẨN** | ĐÃ SỬA | `trigger_ota` chỉ log PLACEHOLDER rồi ACK "ok". Nối vào OTA thật. +10 test. |
| 746 | **CHUẨN** | ĐÃ SỬA | Header ghi QoS 1, code `(void)qos` → QoS 0. Sửa hợp đồng + guard. +5 test. |
| 747 | **CHUẨN** | ĐÃ SỬA | Đổi deviceCode nóng làm câm cả 2 chiều MQTT. Validate + ép reconnect. +9 test. |
| 748 | **CHUẨN** | ĐÃ SỬA | 2xx nhưng backend nhận thiếu — firmware không đọc thân response. +11 test. |

**#735:** `mqtt_client.cpp` VỐN ĐÃ làm đúng (nạp CA + kiểm PEM) — chỉ 2/3 đường TLS bị bỏ
quên. Ba bản sao logic TLS chính là lý do một cái bị sót ⇒ gom về `net/tls_ca.*`.
Fail closed (thiếu CA ⇒ chặn cả 3 call site HTTPS + huỷ OTA), lối thoát dev
`TLS_ALLOW_INSECURE` mặc định 0 và in cảnh báo mỗi lần. `LittleFS.begin(false)` — không
format tự động (format là xoá sạch hàng đợi offline + chính file CA, đúng nội dung #936).
10 test nhắm ca "file tồn tại nhưng vô dụng": placeholder rỗng, JSON lỗi, và **DER nhị phân
có byte 0 ở giữa** — ca này buộc hàm kiểm phải nhận độ dài thay vì `strstr`.

**#736:** comment trong code tự khai *"offline → tick dừng, IncidentTrigger đóng băng"*.
May là hai cảm biến **đã có sẵn** cơ chế latch `s_pendingReport` + retry, và
`envIncidentReport()` trả false NGAY khi thiếu NTP (không gọi mạng, không chặn) ⇒ gỡ gate là
an toàn. Sửa thêm vế thứ hai của issue: ghi **thời điểm PHÁT HIỆN** chứ không phải thời điểm
gửi (thêm `isoNowMinus`, clamp 7 ngày). SHT31 vẫn giữ gate — nó là cảm biến báo cáo, không
phải cảm biến an toàn.
8 test cho phép trừ `millis()` chịu tràn số 49,7 ngày. **Một test đỏ lúc đầu vì số học trong
TEST của tôi sai** (lệch 1 tick do `kU32Max` = 2³²−1), không phải code sai — đã viết lại cho
biểu đạt đúng ý định và ghim luôn cái lệch 1 tick thành test riêng.

**#737:** mất Wi-Fi thì `loop()` chỉ `s_failCount++` — không đọc BMS, không ghi hàng đợi.
Hàng đợi CHỈ được nạp từ nhánh lỗi tạm thời của `ingestOnce()`, mà nhánh đó đòi phải ONLINE
trước ⇒ offline không bao giờ tới được. Toàn bộ telemetry trong khoảng mất mạng mất trắng.

Thêm `sampleAndQueueOffline()`: vẫn đọc BMS, dựng payload, sinh khoá idempotency **ngay lúc
lấy mẫu** rồi xếp vào hàng đợi bền vững — nhờ đó đẩy bù sau reconnect không sinh bản ghi trùng.
Mốc thời gian vẫn đúng vì đồng hồ ESP32 chạy tiếp sau khi mất Wi-Fi (NTP chỉ ĐẶT giờ một lần);
chỉ ca **chưa từng sync NTP** mới bỏ qua.

Tách quyết định thành hàm thuần `core::ingestAction(wifi, clock)` để test được ở env:native —
chính ba dòng điều kiện đó là chỗ sai, mà chúng nằm trong main.cpp (cần Arduino) nên trước giờ
không ai test được. 6 test phủ hết bảng chân trị; ca "mất wifi + có giờ" ĐỎ trên code cũ.

Bẫy đã kiểm: hàng đợi dùng **epoch giây làm tên file** ⇒ hai lần đẩy trong cùng một giây sẽ đè
nhau. Hàm mới chỉ chạy theo nhịp poll (mặc định 5s) nên khoá luôn duy nhất. Hàng đợi cũng có
giới hạn (đầy thì bỏ bản ghi cũ nhất) nên lấy mẫu offline dài ngày không làm đầy flash.

**#738:** simulator so `status_code == 200` ở **3 chỗ**, trong khi
`POST /api/sensor-readings/batch` trả **201 Created** cho lần ghi mới (đã xác minh tận
handler: `StatusCode = 201` dòng 417, còn `200` dòng 89 là ca trùng idempotent). Nên MỌI lần
ghi mới bị log FAIL rồi đẩy vào hàng đợi; mà phần flush chỉ chạy SAU một lần gửi live thành
công ⇒ hàng đợi không bao giờ vơi.

⇒ Sửa đúng là chấp nhận **cả dải 2xx**, không phải đổi 200 thành 201. Thêm
`classify_response()` trả `SUCCESS` / `TRANSIENT` / `PERMANENT`: 4xx (trừ 408/425/429) là
**PERMANENT** ⇒ bỏ bản ghi thay vì `break`. Trước đây flush gặp lỗi là `break`, nên một batch
dữ liệu sai nằm đầu hàng đợi sẽ chặn vĩnh viễn mọi bản ghi phía sau — **cùng lớp lỗi
starvation với #725**.

**Cản trở phải gỡ trước khi test được:** module `sys.exit(2)` ngay ở tầng import khi thiếu
`requests` ⇒ không import được ⇒ không viết test được cho chính phần đã sai. Chuyển kiểm tra
xuống `main()` (sau `parse_args()` để `--help` vẫn dùng được). Đã xác minh hành vi CLI không
đổi: thiếu `requests` vẫn exit 2 kèm đúng thông báo, `--help` nay exit 0 thay vì 2.

**9 test** bằng `unittest` của thư viện chuẩn (KHÔNG thêm phụ thuộc — repo chưa có pytest):
phủ 200/201/toàn dải 2xx/4xx/408-425-429/5xx/mã lạ, cộng một test **chặn hồi quy** quét source
để bảo đảm không ai quay lại so `status_code == 200` trực tiếp.
Đã thêm verifier `simulator-test` vào thang repo iot (43ms).

**#739:** nguồn chân lý là **firmware thật**, không phải "chiều nào tiện". Kiểm
`core::buildProductionBatchPayload` → firmware gửi **camelCase** (`items`, `batteryAssetSerial`,
`time`…); mock backend cũng camelCase; chỉ **simulator** PascalCase. Nó chưa từng mô phỏng đúng
thiết bị — chỉ "chạy được" với ASP.NET thật vì ASP.NET bind không phân biệt hoa thường, che mất
chỗ lệch. Đã đổi dataclass + root key sang camelCase.

**KHÔNG đụng provision/heartbeat** dù chúng cũng PascalCase: kiểm ra **firmware CŨNG gửi
PascalCase** ở hai endpoint đó và mock nhận cả hai kiểu ⇒ đã khớp. "Sửa" cho đồng bộ sẽ làm vỡ
hợp đồng đang chạy. (Firmware tự nó không nhất quán giữa hai nhóm endpoint — ghi nhận, ngoài
phạm vi #739.)

**Lỗi thứ hai lộ ra khi viết test hợp đồng (issue không nêu):** simulator dùng
`datetime.isoformat()` → `...42.789012+00:00`, trong khi firmware dùng
`strftime("%Y-%m-%dT%H:%M:%SZ")` → `...42Z`, và mock kiểm bằng regex đòi hậu tố `Z` ⇒ bị từ
chối "invalid ISO8601". Cùng gốc: simulator sinh JSON khác thiết bị. Đã thêm `iso_utc_now()`
và sửa cả 3 chỗ.

**+7 test hợp đồng** gọi thẳng `validate_batch()` của mock (không cần dựng HTTP server), gồm
một test **chốt ngược** bảo đảm mock vẫn từ chối payload PascalCase cũ — nếu không, test
"payload qua được" là xanh giả.

Tự bắt lỗi của mình: docstring tôi viết chứa `\d` không escape gây `SyntaxWarning`; đã đổi
sang raw string và xác minh `import` sạch cảnh báo.

**#740:** `ingestViaMqtt()` publish theo TỪNG NHÓM serial; nhóm đầu OK, nhóm sau fail thì
`return false` ⇒ caller rơi xuống HTTPS gửi lại **TOÀN BỘ** batch ⇒ nhóm đã qua MQTT bị ghi
hai lần. Khoá idempotency của HTTPS **không cứu được**: nó chỉ khử trùng giữa các lần gửi
HTTPS với nhau, còn bản ghi kia đã vào backend bằng đường MQTT với hình dạng payload khác.

Sửa theo hướng issue đề xuất ("track per-group completion"): `ingestViaMqtt` nay trả về danh
sách serial ĐÃ publish; trước khi dựng payload HTTPS, `core::filterOutPublished()` loại chúng
ra. MQTT đẩy hết ⇒ bỏ qua HTTPS luôn.

Tách bộ lọc thành hàm thuần trong `core/` để test ở env:native. **10 test**, gồm các ca dễ
sai: serial rỗng phải **GIỮ LẠI** (coi là "đã gửi" thì bản ghi biến mất khỏi cả hai đường),
khớp một phần `BAT-00` KHÔNG được tính là khớp `BAT-001` (nếu không sẽ âm thầm bỏ mất bản ghi
của pin khác), và số đo phải còn nguyên sau khi lọc.

**#741:** `envIncidentReport` gộp MỌI lỗi thành `false`; caller giữ pending và gọi lại ở mỗi
tick ⇒ một lỗi 403 sinh ~180 request/phút (MQ-2 1s + rò nước 0,5s), vô hạn.

Sửa bằng cách **dùng lại đúng thứ repo đã có**: `net::isTransientFailure()` + `net::Backoff`
(Sprint 3, đã có test, phủ sẵn 401/403/422). Đường telemetry dùng từ lâu; đường environmental
incident chưa bao giờ áp dụng. Nay reporter trả `IncidentReportResult`
(Success/Transient/Permanent); lỗi vĩnh viễn ⇒ **DỪNG hẳn** + gợi ý cụ thể "API key có thể
thiếu scope EnvironmentalIngest"; lỗi tạm thời ⇒ backoff mũ + jitter.

**Một lỗi tôi suýt tự đưa vào, tự bắt được khi đọc lại code script sinh ra:** sự cố MỚI sẽ bị
kẹt sau `s_nextReportAtMs` còn sót từ backoff lần trước ⇒ một xung khí mới phải đợi tới 5 phút.
Với cảm biến an toàn thì đó là biến sự cố mạng thành sự cố an toàn. Đã reset cổng khi cạnh lên.

Tách cổng retry thành `core::shouldAttemptReport()` (thuần) để test được. **8 test**, trọng tâm
là **tràn `millis()`**: nếu viết `now >= nextAllowed` thì sau 49,7 ngày cổng KHOÁ CỨNG gần 50
ngày và sự cố khí/rò nước sẽ không bao giờ được báo.

**#745:** `handleTriggerOta` chỉ in "PLACEHOLDER — Sprint 7 sẽ implement" rồi ACK **"ok"**.
Nhưng OTA thật **đã làm xong ở Sprint 7** (`ota::otaTick`, `downloadAndFlash`) — chỗ này chỉ
không bao giờ được nối vào. Kiểu lỗi tệ nhất: hệ thống **nói dối là đã làm**; người vận hành
bấm "cập nhật ngay", thấy báo thành công, mà thiết bị vẫn ngồi đợi hết chu kỳ 1 giờ.

Thêm `ota::otaRequestCheck()` đặt cờ ép chạy ở tick kế tiếp, và ACK phản ánh **kết quả thật**:
`ok` khi nhận được yêu cầu, `rejected` kèm lý do cụ thể khi không (OTA tắt bằng cấu hình, hoặc
đang xác minh bản vừa flash).

**Quyết định an toàn:** `forced` **KHÔNG** vượt qua `verifying` — đang xác minh bản vừa flash mà
tải chồng bản mới là mất luôn đường lùi nếu bản mới hỏng. Ngược lại forced ĐƯỢC vượt warm-up,
vì bấm "cập nhật ngay" lúc thiết bị vừa cắm điện là tình huống bình thường.

Tách 5 điều kiện thành `core::decideOtaCheck()` (thuần) — nêu rõ LÝ DO từ chối để ACK nói đúng
nguyên nhân. **10 test**, gồm 2 ca **tràn `millis()`**: so bằng `now >= last + interval` thì
phép cộng tràn và lịch OTA chết ~49,7 ngày.

**#746:** header ghi "QoS 1" ở cả 4 hàm publish, còn `publishWithStats()` có nguyên dòng
`(void)qos` — tham số bị vứt thẳng. Doc cũ **tự mâu thuẫn**: vừa ghi "QoS 1" vừa ghi "KHÔNG
đợi PUBACK", mà QoS 1 theo định nghĩa LÀ chờ PUBACK.

Đã xác minh PubSubClient v2.8 chỉ có `publish(topic, payload, len, retain)` — **không hề có
overload QoS**. Nhưng `subscribe(topic, 1)` thì CÓ: nên chiều VÀO thật sự là QoS 1, chỉ chiều
RA là QoS 0. Header cũ gộp nhầm hai chiều.

Chọn nhánh mà issue cho phép ("sửa contract + reliability design") thay vì đổi thư viện: đổi
thư viện MQTT là việc lớn, đụng đường đang chạy, và không nằm trong phạm vi issue. Đã: bỏ tham
số giả ở 4 call site, viết lại doc cho đúng, và **mã hoá cam kết thành hằng số**
`net::kPublishGuarantee` để nó kiểm được bằng test thay vì nằm trong lời văn rồi trôi.

Ghi rõ hệ quả: `true` chỉ nghĩa là "đã đẩy vào socket TCP", KHÔNG phải "broker đã nhận" — mất
kết nối ngay sau đó là bản tin biến mất. Muốn bảo đảm thật phải đổi sang esp-mqtt /
AsyncMqttClient; **ghi nhận là việc riêng, chưa làm.**

**+5 test**, trong đó guard quét source chặn dòng cast-to-void quay lại. **Guard này bắt được
chính bản sửa của tôi chưa triệt để** ở lần chạy đầu — hoá ra token còn sót trong comment tôi
vừa viết. Tôi sửa lời văn chứ KHÔNG nới guard, và thêm test chốt ngược để bảo đảm hàm đọc file
thật sự đọc được (nếu không, 2 test quét source là xanh giả).

**#747:** CLI `set devcode` đổi deviceCode runtime rồi in "hot reloaded", nhưng
username/password/clientId MQTT là **macro compile-time**, và phiên đang mở vẫn giữ LWT +
subscription của code CŨ. Sau khi đổi: publish sang topic MỚI bị ACL từ chối (username cũ),
lệnh downlink vẫn về topic CŨ ⇒ **thiết bị câm cả hai chiều mà log báo thành công** — nằm
ngoài hiện trường thì cực khó truy.

Hai phần sửa:
1. **Chặn từ đầu** — `core::decideDeviceCodeChange()` kiểm `lowercase(code) == MQTT_USERNAME`
   (đúng quy ước `IotApiKeyService.GenerateMqttCredential` của backend). Không khớp thì TỪ CHỐI
   kèm hướng dẫn provision lại, thay vì để thiết bị chết câm. Đây là vế "rollback nếu ACL không
   hợp lệ" của issue.
2. **Ép dựng lại phiên** — `net::mqttOnIdentityChanged()` ngắt kết nối để tick kế dựng lại
   LWT + topic + subscribe theo code mới, và reset throttle reconnect để không phải chờ.

**KHÔNG làm:** lưu MQTT credential provision vào NVS (để đổi credential lúc chạy). Đó là tính
năng mới, đụng luồng provisioning, ngoài phạm vi một issue P3 — ghi nhận là việc riêng.

**9 test**, gồm ca dễ sai: khác hoa thường PHẢI được chấp nhận (backend `ToLowerInvariant`),
nhưng **tiền tố thì không** (`gw-...-00` không được coi là khớp `gw-...-001`); username null +
dùng MQTT ⇒ từ chối (fail closed); đúng giới hạn 64 ký tự vẫn phải qua (chặn nhầm ở biên cũng
là lỗi).

Tự bắt lỗi của mình: lần đầu tôi viết `kMqttEnabled = MQTT_USE_TLS != 0 || true` — biểu thức
luôn đúng, vừa thừa vừa gây hiểu nhầm. Đã sửa thành hằng số tường minh kèm lý do.

**#748:** firmware coi mọi 2xx là "cả batch đã vào" và **không hề đọc thân response**, trong
khi backend trả `{ totalReceived, inserted, skipped }`. Backend nhận thiếu ⇒ firmware vẫn báo
thành công và bỏ phần còn lại — **việc mất số đo diễn ra trong im lặng**.

**Quyết định quan trọng — KHÔNG retry phần bị bỏ.** Tra tận handler backend: `skipped` =
`mapping_invalid` (thiết bị gửi serial không được map cho nó) + `rejectedOutliers` (giá trị
ngoài dải vật lý). **Cả hai đều vĩnh viễn** — gửi lại đúng dữ liệu đó chỉ ra đúng kết quả đó.
Thứ thực sự mất là **TÍN HIỆU**: người vận hành không biết thiết bị đang bị bỏ số đo vì sai
mapping. ⇒ Việc đúng là ĐỌC và LA LÊN, kèm gợi ý nguyên nhân cụ thể.

Muốn retry CHỌN LỌC thì backend phải trả kết quả theo TỪNG item (có định danh) — hợp đồng hiện
tại chỉ có số đếm. **Ghi nhận là việc riêng phía backend**, không tự ý mở rộng.

**Rủi ro lớn nhất của bản sửa là CẢNH BÁO GIẢ**, không phải parse sai: firmware chỉ giữ một
đoạn đầu response (`responseSnippet`) nên JSON thường bị cắt giữa chừng. Parser trả
`parsed=false` thay vì đoán `inserted=0` — nếu đoán bừa thì mỗi lần gửi đều la "nhận thiếu",
và người vận hành sẽ học cách phớt lờ cả cảnh báo thật. **11 test**, trong đó 4 ca cắt ngắn /
thiếu trường / rác.

**Tiện thể sửa lỗi tôi gây ra ở #740:** log thành công in `n` (số reading ban đầu) thay vì
`nRemaining` (số thực gửi qua HTTPS) — sai số liệu trong log chẩn đoán.

**Thang sau #748:** L0 test + L1 compile 2 env — XANH, 25.7s.



| # | Thẩm định | Trạng thái | Ghi chú |
|---|---|---|---|
| 722 | **CHUẨN** | ĐÃ SỬA | IDOR đa tenant, 12 endpoint. 20 test hồi quy. Chi tiết dưới. |
| 723 | **CHUẨN** | ĐÃ SỬA | `CanRead` trả `true` vô điều kiện cho TicketAttachment/MaintenancePhoto. +9 test. Chi tiết dưới. |
| 724 | **CHUẨN về sự kiện, NHƯNG xung đột quyết định** | ✅ ĐÓNG won't fix | User chốt **A** (2026-08-04): giữ plaintext, sửa doc. Đã đóng issue trên GitHub kèm lý do. |
| 725 | **CHUẨN** (cả 2 vế) | ĐÃ SỬA | Map 7/61 event + starvation. +10 test. Chi tiết dưới. |
| 728 | **CHUẨN** | ✅ XONG + ĐÃ CHỨNG MINH CHẠY THẬT | User chốt **A** = xây thật. 21 test + verify end-to-end trên stack thật. |
| 729 | **CHUẨN** | ĐÃ SỬA | Timer 6h neo lúc start ⇒ **67% mốc khởi động không bao giờ chạy**. +294 test. Chi tiết dưới. |
| 730 | **CHUẨN** | ĐÃ SỬA | Npgsql 8.0.2 (CVE High) → provider 8.0.11. 31 csproj. Quét lại: sạch. Chi tiết dưới. |
| — | **NGOÀI ISSUE** (tôi tự phát hiện) | ĐÃ SỬA | AngleSharp 0.17.1 + System.Text.Json 7.0.2 (TicketService) + **1 bug runtime lộ ra khi verify**. Chi tiết dưới. |
| 731 | **CHUẨN** | ĐÃ SỬA | System.IO.Packaging 6.0.0 → ghim 8.0.1. +7 test round-trip XLSX. |
| 732 | **CHUẨN** | ĐÃ SỬA | OpenTelemetry 1.9.0 → 1.16.0 (cả 4 package). |
| — | **NGOÀI ISSUE** (cổng CI tự bắt) | ĐÃ SỬA | MessagePack High · Microsoft.Build High · System.Text.Json 8.0.0 High · SQLitePCLRaw (miễn trừ có lý do). |

### #731 + #732 + dọn sạch dependency

**#731 — `System.IO.Packaging 6.0.0`** (2 advisory High) đến từ `ClosedXML 0.102.2`.
**Không nâng ClosedXML** (0.102 → 0.105 kéo `DocumentFormat.OpenXml` 2.16 → 3.x, major, rủi ro
vỡ API xuất Excel mà KHÔNG thêm lợi ích bảo mật — lỗ hổng nằm ở System.IO.Packaging).
Thay vào đó **ghim trực tiếp `System.IO.Packaging 8.0.1`** (bản vá, khớp runtime net8.0);
NuGet lấy version cao nhất nên thắng transitive 6.0.0. Có comment "đừng xoá" tại chỗ.

Issue yêu cầu *"regression test mọi luồng export Excel"*. Test cũ chỉ kiểm "file không rỗng +
đúng content-type" — workbook hỏng hoàn toàn vẫn qua. Thêm **7 test round-trip**: mở LẠI
workbook và đọc từng ô (header, chuỗi tiếng Việt có dấu, số/ngày/bool giữ đúng kiểu, null →
ô trống, header in đậm, tên sheet cắt 31 ký tự, bảng rỗng, và kiểm 2 byte đầu là `PK` = container
OPC/ZIP hợp lệ). Đây mới là thứ chạm đúng phần System.IO.Packaging đảm nhiệm.

**#732 — OpenTelemetry 1.9.0** (2 advisory Moderate). Bản vá là 1.15.3 nhưng
`Instrumentation.AspNetCore`/`Http` **không có 1.15.3** (nhảy thẳng 1.15.2 → 1.16.0) ⇒ chọn
**1.16.0**, bản thấp nhất mà cả 4 package cùng có và đều ≥ 1.15.3. Giữ 4 package cùng version.

### Cổng CI mới — `make ci-audit` (stage 6/7)

Cả #730 và #732 đều yêu cầu "khoá dependency audit CI". Đã thêm `ci/scripts/nuget-audit.sh`:
chặn advisory **High/Critical** (Moderate chỉ cảnh báo), bắt buộc `--include-transitive` vì
**cả ba lỗ hổng đều là transitive**. `dotnet restore` vốn có in NU1902/NU1903 nhưng chỉ là
warning nên build vẫn xanh — đó là lý do chúng nằm im rất lâu.

**Cổng vừa dựng đã bắt ngay 3 advisory High mà tôi bỏ sót** (vì trước đó tôi chỉ quét project
`*.Infrastructure`, không quét toàn solution):

| Package | Nguồn | Xử lý |
|---|---|---|
| `MessagePack 2.5.108` (High + Moderate) | `SignalR.StackExchangeRedis 8.0.2` | nâng lên 8.0.29 → kéo MessagePack 2.5.302 |
| `Microsoft.Build 17.10.4` (High) | `VisualStudio.Web.CodeGeneration.Design 9.0.0` | **gỡ hẳn** — xem dưới |
| `System.Text.Json 8.0.0` (High) | `Microsoft.Extensions.* 8.0.0` | ghim 8.0.6 ở 3 project test |
| `SQLitePCLRaw.lib.e_sqlite3 2.1.6` (High) | `EFCore.Sqlite` (chỉ test) | **miễn trừ có lý do** — upstream chưa có bản vá nào |

**Gỡ package scaffolding — phát hiện phụ đáng giá:**
`Microsoft.VisualStudio.Web.CodeGeneration.Design 9.0.0` khai trong `AuthService.Api` nhưng
**không dòng code nào dùng**. Nó là công cụ CLI, vậy mà (1) rò vào `deps.json` runtime kéo theo
`Microsoft.Build` dính advisory High, và (2) là package **9.x** nên âm thầm nâng cả stack
`Microsoft.Extensions` của riêng project này lên 9.0.0 trong khi cả solution ở 8.0.x — đúng kiểu
lệch phiên bản gây lỗi runtime rất khó truy. Thử `PrivateAssets="all"` thì lộ ngay ra điều đó
(build vỡ CS1705), nên **gỡ hẳn**; cần scaffold thì cài global tool.

**Kết quả:** `make ci-audit` XANH — không còn advisory High/Critical nào ngoài 1 miễn trừ có
ghi lý do. Thang đầy đủ: build 0 error · unit **2951** · integration 602 · e2e-smoke 14/14.

### Ngoài issue — AngleSharp / System.Text.Json / bug transcode voice

**Nguồn gốc (truy từ `project.assets.json`, không đoán):**
- `AngleSharp 0.17.1` ← `HtmlSanitizer 9.0.892` (ghim CỨNG `[0.17.1]` ⇒ thêm reference trực tiếp
  sẽ xung đột, buộc phải nâng HtmlSanitizer).
- `System.Text.Json 7.0.2` ← `FFMpegCore 5.1.0`.

**Đã nâng:** `HtmlSanitizer 9.0.892 → 9.1.982` (kéo AngleSharp **1.7.0**, advisory vá từ 1.5.0) ·
`FFMpegCore 5.1.0 → 5.4.0` (kéo STJ **9.0.10**). Quét lại: **cả hai advisory biến mất** ở toàn bộ
4 project TicketService.

**Rủi ro đã lường và xử lý:** AngleSharp nhảy **0.17 → 1.7 (major)** ngay dưới ranh giới chống XSS.
Hai bộ phân tích HTML có thể hiểu cùng một chuỗi méo theo hai cách (parser differential / mXSS) —
loại lỗi mà test so chuỗi thường không thấy. Nên thêm `MarkdownSanitizerContractTests`: **22 payload**
nhắm đúng khác biệt parser (thẻ méo, entity mã hoá, breakout noscript/comment, foreign content
svg/math/template), và thay vì liệt kê chuỗi cấm thì kiểm **mọi thẻ + mọi thuộc tính còn sót lại**
có nằm trong allowlist không — hợp đồng độc lập phiên bản parser. Kèm 3 test tự kiểm chứng minh bộ
trích xuất thật sự bắt được thẻ/thuộc tính xấu (nếu không, 66 ca Theory sẽ xanh vô nghĩa).

**🔴 BUG RUNTIME LỘ RA KHI VERIFY (có sẵn từ trước, KHÔNG do nâng cấp):**
`FfmpegAudioTranscoder` dùng muxer `ipod` ghi ra **pipe**. MP4 ghi bảng chỉ mục `moov` ở cuối rồi
tua ngược về đầu để vá — pipe không tua ngược được, nên ffmpeg từ chối ngay ở khâu ghi header:
`muxer does not support non seekable output`. ⇒ **Mọi input không phải m4a/aac đều ném exception,
transcode voice CHƯA TỪNG chạy được.** Vô hình suốt thời gian dài vì component này **không có test nào**.

Đã tái hiện trên **cả hai** môi trường: ffmpeg 8.1.1 (máy dev) và **ffmpeg 5.1.9 trong container
production**. Sửa bằng `-movflags frag_keyframe+empty_moov` (MP4 phân mảnh — ghi tuần tự được,
vẫn là .m4a hợp lệ); đã kiểm trong container: ra 5053 byte, box `ftyp` đúng.

Thêm `FfmpegAudioTranscoderTests` — **đỏ trên code cũ, xanh sau khi sửa** (đúng chuẩn hồi quy):
transcode WAV thật → assert box `ftyp`; nhánh đã-là-m4a không transcode lại; chuẩn hoá tên file.

**Sau khi sửa:** build 0 error · unit **2944** · integration 602 · e2e-smoke 14/14.

**Còn tồn (chưa xử lý, ngoài phạm vi hiện tại):** `MessagePack 2.5.108` Moderate và
`SQLitePCLRaw.lib.e_sqlite3 2.1.6` High (chỉ trong IntegrationTests) — **cũng không issue nào báo**.

### #730 — chi tiết

**Thẩm định: chuẩn** — `dotnet list package --vulnerable --include-transitive` xác nhận
`Npgsql 8.0.2` **High** (GHSA-x9vc-6hfv-hg8c) ở cả 7 service.

**Cách sửa:** nâng `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.2 → **8.0.11** (bản vá mới nhất
nhánh 8.0, kéo `Npgsql 8.0.6` ≥ 8.0.3). Provider 8.0.11 yêu cầu EF Core 8.0.11 nên **phải nâng
đồng bộ cả stack EF** (Core/Abstractions/Relational/Design/Tools/InMemory/Sqlite), nếu không
graph vênh và sinh NU1605. Tổng **31 csproj**.

**Kiểm chứng:**
- Build: 0 error, **không có NU1605**.
- Quét lại `--vulnerable`: **Npgsql biến mất ở cả 7 service** ✓
- Bonus ngoài dự kiến: `System.Text.Json 8.0.0` (High ×2) cũng hết — EF Core 8.0.11 kéo bản vá.
- Thang đầy đủ xanh: unit 2864 · **integration 602 (chạy Postgres thật qua TestContainers với
  driver mới)** · e2e-smoke 14/14.
- `dotnet ef migrations list` chạy được, 3 migration đọc đúng.

**Còn lại sau khi quét (chưa thuộc #730):**
- `System.IO.Packaging 6.0.0` High — **#731**, cả 7 service.
- `OpenTelemetry.* 1.9.0` Moderate — **#732**, cả 7 service.
- ⚠️ `AngleSharp 0.17.1` Moderate và `System.Text.Json 7.0.2` **High** — chỉ ở TicketService,
  **KHÔNG có issue nào báo**. Phát hiện mới, cần xử lý.

### #729 — chi tiết

**Thẩm định: chuẩn, và đo được.** `PeriodicTimer(6h)` neo theo thời điểm process khởi động,
rồi mới kiểm cửa sổ 03:00–04:59 UTC. Tick vì thế luôn rơi vào 4 mốc cố định cách nhau 6 giờ
tính từ lúc start ⇒ chỉ trúng cửa sổ khi `(giờ start mod 6h) ∈ [3h, 5h)`.
Quét toàn bộ 144 mốc khởi động trong ngày: **96/144 = 67% KHÔNG BAO GIỜ chạy** (vd start 00:30
→ tick 06:30/12:30/18:30/00:30 mãi mãi). Comment trong code còn khẳng định ngược lại
("6h đảm bảo trúng cửa sổ 1 lần/ngày") — sai.

**Cách sửa:** thay tick chu kỳ bằng **ngủ tới đúng mốc 03:00 UTC kế tiếp**
(`DelayUntilNextRun`), tính lại mỗi vòng nên không trôi lịch. Giữ `IsWithinMaintenanceWindow`
làm chốt an toàn. Seam test đổi từ `CheckInterval` → `DelayUntilNextRun` (test cũ chỉ đổi seam,
**không đụng assertion**).

**+294 test:** 2 Theory quét **mọi mốc khởi động trong ngày** (144 mốc × 2) — trên code cũ
đỏ ở 96/144; cộng 6 fact: trước/sau giờ hẹn, đúng boong 03:00:00 (không được đẩy sang ngày mai
kẻo mất trọn một ngày retention), qua ranh giới tháng, hai lần chạy cách đúng 24h, và một test
ghim lại vì sao thiết kế cũ sai.

**Sau khi sửa:** build 0 error · unit 2864 · integration 602 · e2e-smoke 14/14.

### #728 — chi tiết (HOÀN TẤT)

**Phát hiện đổi hẳn thiết kế:** ban đầu định replay từ `{service}_audit_logs` như issue viết.
Kiểm ra thì **6 bảng đó KHÔNG đồng nhất**: `AuthService.AuditLog` dùng `IpAddress`/`UserAgent`
thay vì `ActorIp`/`ActorUserAgent` và có thêm 4 cột riêng; còn `SmsService.SmsAuditLog` hoàn
toàn khác loại (chỉ có `CreatedAt/Detail/DeviceCode/Event/SmsMessageId`) — **không phải** bảng
audit-event. Sinh code theo giả định "6 bảng giống nhau" sẽ sai ở 2/6 service.

**Nguồn đúng là bảng `*AuditOutbox`** (cả 6 service đều có, shape y hệt nhau:
`EventId`/`EventType`/`Payload`/`Status`/`CreatedAt`), trong đó `Payload` chính là
`AuditCreatedEventV1` đã serialize — đúng thứ cần phát lại. Đã kiểm: **không có job nào xoá
outbox sau khi publish**, nên dữ liệu còn nguyên để replay.

**ĐÃ XONG**
- `SharedContracts/Audit/AuditServiceNames.cs` — danh sách 6 service + `Matches`/`IsKnown`/
  `ExpectedResponders`, để hai đầu không lệch danh sách (lệch = job treo vĩnh viễn).
- `SharedContracts/Events/Audit/AuditReplayEvents.cs` — `AuditReplayRequestedEvent` +
  `AuditReplayCompletedEvent` (có cờ `Truncated`).
- `AuditReplayJob` entity + EF config (**xmin concurrency token**) + DbSet + UoW.
- `AuditReplayCommandHandler` viết lại: validate → **lưu job** → publish → mới trả 202 kèm `jobId`.
- `AuditReplayCompletedConsumer`: cộng dồn tiến độ, **idempotent theo tên service** (chống
  redelivery), **retry khi xung đột đồng thời** (6 service báo về cùng lúc).
- `SharedInfrastructure/Bus/AuditReplayRequestedConsumerBase.cs` — khung dùng chung (lọc
  service, phân trang, trần 50k + `Truncated`, luôn báo cáo kể cả khi lỗi).
- 6 test cho handler (job được lưu / 6 responder / service lạ 400 không tạo job / From>To /
  Kind=Unspecified coi là UTC). **Build xanh, 6/6 pass.**

**ĐÃ XONG NỐT (2026-08-04)**
1. **6 consumer** — gom về một lớp cơ sở generic `AuditReplayRequestedConsumerBase<TOutbox>`;
   thêm interface `IAuditOutboxMessage` (chỉ khai lại thuộc tính ĐÃ CÓ, không cần migration)
   cho 6 entity outbox. Mỗi consumer service còn ~20 dòng.
   Đã thêm assembly Infrastructure vào `AddMessageBus` cho **Auth / Notification / Sms**
   (3 service này trước chỉ quét assembly Application ⇒ consumer sẽ không bao giờ chạy).
   Battery / Ticket / FileStorage vốn đã quét Infrastructure.
2. **Migration** `20260804042854_AddAuditReplayJob` — đã kiểm SQL sinh ra (Npgsql tự bỏ cột
   `xmin` vì là system column) và **chạy thử DDL trên Postgres thật rồi ROLLBACK**: hợp lệ.
3. **Endpoint** `GET /api/admin/audit/replay/{jobId}` + query/handler/DTO, có `pendingServices`.
4. **Test** — tổng **21**: 6 handler + 7 lớp cơ sở + 8 tiến độ/endpoint.
5. **Docs** — sửa 3 file (`docs/audit/api-reference.md`, `docs/audit/operations-runbook.md`,
   `docs/api-audit.md`) cho khớp thực tế + đổi nguồn từ `{service}_audit_logs` sang
   `{service}_audit_outbox`.

**KIỂM CHỨNG END-TO-END trên stack thật** (rebuild image auditaggregator + battery):
- Migration tự áp dụng lúc startup ✓
- `POST /replay?service=BatteryService` → **202 + jobId** ✓
- Job chuyển sang **Completed**, `republishedCount = 14` ✓
- `audit_aggregate` (BatteryService): **0 → 14 dòng** ✓
- **Chạy lại lần 2 → VẪN 14 dòng** (idempotent theo `EventId`) ✓
- `service=KhongCoService` → **400** kèm danh sách hợp lệ, **không tạo job** (2 job chứ không 3) ✓

**Phát hiện phụ (KHÔNG thuộc #728, chưa sửa):** biến môi trường
`AUDIT_AGGREGATOR_DB_NAME=audit_aggregate_db` nhưng `ConnectionStrings__AuditAggregatorDb`
lại trỏ `audit_aggregator_db`. Cả hai DB đều tồn tại trong Postgres; service dùng cái thứ hai,
còn cái thứ nhất là DB mồ côi có schema cũ. Dễ làm người vận hành soi nhầm DB khi sự cố.

### #728 — bối cảnh quyết định (đã chốt)

Handler cũ là stub thuần: trả 202, không validate, không lưu job, không publish. Ba file docs
thì hứa có background replay. Replay thật đòi hỏi **6 service nguồn** cùng tham gia — user chốt
**phương án A (xây thật)** ngày 2026-08-04, kết quả ở mục trên.

### #725 — chi tiết

**Thẩm định:** chuẩn cả hai vế.
- `EventTypeMap` viết tay chỉ có **7** khoá, trong khi SharedContracts có **61** event; 6 event
  service thực sự ghi qua outbox không có trong map ⇒ "Unknown event type", nằm lại vĩnh viễn.
- Truy vấn `Where(ProcessedAtUtc == null).OrderBy(OccurredAtUtc).Take(batchSize)` không loại row
  hỏng ⇒ row hỏng luôn là row cũ nhất nên chiếm suất batch mãi mãi (starvation).

**Cách sửa — triệt tiêu cả lớp lỗi, không vá 6 dòng:** bỏ danh sách viết tay, dựng map bằng
phản chiếu trên mọi `IntegrationEvent` cụ thể trong SharedContracts. Thêm event mới từ nay
không cần nhớ sửa file này nữa — chính việc "phải nhớ" mới là gốc lỗi. Kèm guard trùng tên
đơn giản (throw lúc khởi tạo, vì cột `type` sẽ nhập nhằng). Giữ alias legacy
`BatteryAnomalyEscalatedEvent` → `BatteryAnomalyDetectedEvent`.

**Starvation:** thêm `MaxRetryCount = 10` vào điều kiện truy vấn; row chạm trần bị loại khỏi
batch nhưng VẪN nằm trong bảng, `LastError` gắn `[DEAD-LETTER]` để ops tra bằng
`ProcessedAtUtc IS NULL AND retry_count >= 10`. Không cần migration.

**+10 test:** 4 event từng bị bỏ sót publish được · parity toàn bộ 61 event · alias legacy ·
type lạ **vẫn phải fail** (không nuốt bừa) · row chạm trần bị loại mà row mới vẫn đi · đánh dấu
dead-letter đúng/không đúng ngưỡng.

**Kiểm chứng test có ý nghĩa:** map cũ 7 khoá / 61 event ⇒ test parity chắc chắn đỏ trên code cũ.

**Ghi nhận thêm:** bẫy MassTransit publish theo base type (`typeof(T)` = `IntegrationEvent`)
đã được xử lý đúng từ trước trong `MassTransitProducer.PublishAsync` (`message.GetType()`) —
kiểm lại, không phải lỗi mới.

**Sau khi sửa:** build 0 error · unit 2857 · integration 295 · e2e-smoke 14/14.

### #724 — vì sao dừng lại chờ quyết định

Ba sự kiện issue nêu đều **đúng**:
- `IIotApiKeyService` ghi rõ trong doc: *"Plaintext chỉ trả 1 lần cho admin lưu — DB chỉ giữ hash."*
- `IotDevice` có **cả** `ApiKeyHash` **lẫn** `ApiKeyPlaintext`.
- `IotDeviceMapper` trả `ApiKey = e.ApiKeyPlaintext` ở admin GetById.

Nhưng đây **không phải tai nạn**: commit `82b56569` (2026-07-16, *"display iotkey"*) cố ý
thêm cột plaintext, kèm XML doc giải thích "lưu để Admin xem lại trên
`GET /api/admin/iot-devices/{id}`; null cho device tạo trước khi bật lưu plaintext
(rotate-key để populate)". Tức là một **tính năng đã chốt**, không phải sơ suất.

⇒ "Sửa" issue này = **gỡ bỏ tính năng Admin xem lại key** + migration xoá cột + rotate toàn
bộ key đang có. Đó là đảo một quyết định sản phẩm, không phải vá lỗi. Không tự làm.

**Cần user chọn:**
- (A) Giữ nguyên → đóng #724 là *won't fix*, và **sửa doc của `IIotApiKeyService`** cho khớp
  thực tế (hiện doc đang nói dối, đó mới là phần đáng sửa).
- (B) Siết lại theo issue → bỏ `ApiKeyPlaintext`, chỉ hiện 1 lần lúc create/rotate, migration
  + rotate key cũ. Mất tính năng Admin xem lại.

### #723 — chi tiết

**Thẩm định:** chuẩn. `FileAuthorizationService.CanRead` có `TicketAttachment or
MaintenancePhoto => true`, ngay bên dưới còn dòng comment luật chặt hơn đã bị tắt.

**Vì sao không sửa được kiểu "chỉ uploader + nội bộ":** `UploadedFile` KHÔNG có liên kết
nào tới ticket, và gRPC chỉ đi chiều Ticket → File (TicketService không có gRPC server).
Nghĩa là FileStorageService về cấu trúc không thể tự biết ai là participant. Mà khoá về
`CreatedBy || Manager/Staff` (đúng dòng comment sẵn) sẽ chặn luôn Customer xem file do
Staff đính kèm trên ticket CỦA CHÍNH HỌ — hỏng nghiệp vụ.

**Cách sửa:** chuyển quyết định phân quyền từ nơi biết sang nơi cần.
`ChatAttachmentDownloadQueryHandler` đã kiểm `CanAccessTicket` đúng rồi nhưng lại trả URL
trần. Nay nó ký grant HMAC ngắn hạn (5 phút) gắn chặt (fileId, userId) và gắn vào URL;
FileStorage chỉ xác minh chữ ký — không phát sinh phụ thuộc runtime giữa 2 service.
Khoá ký dùng chung `JwtSettings:SecretKey` (một biến env cấp cho cả 9 service).

- File mới: `shared/src/SharedKernels/Security/FileAccessGrant.cs` (đặt ở SharedKernels
  chứ không SharedInfrastructure vì tầng Application không được tham chiếu Infrastructure).
- `TicketAttachment` → nội bộ (Manager/Staff/Admin) **hoặc** uploader **hoặc** grant hợp lệ.
- `MaintenancePhoto` → nội bộ **hoặc** uploader. KHÔNG nhận grant: `MaintenanceLogsController`
  vốn là `[Authorize(Roles="Staff,Manager,Admin")]`, Customer không có đường vào.

**Test cũ bị ĐẢO kỳ vọng (khai báo minh bạch):**
`CanRead_Customer_OtherUserFile_TicketRelatedPurpose_ReturnsTrue` → `ReturnsFalse`.
Test này mã hoá chính lỗ hổng thành hành vi mong đợi (có cả comment "đọc được bởi mọi user
đã đăng nhập"). Issue #723 đổi spec nên đảo là hợp lệ; lý do đã ghi ngay trong test.

**+9 test:** grant hợp lệ / cấp cho người khác / cấp cho file khác / hết hạn / rác / rỗng /
grant không mở đường cho MaintenancePhoto, và 1 test **cross-service** kiểm grant do
TicketService sinh ra được `FileAccessGrant.Validate` (thứ FileStorage gọi) chấp nhận —
để hai nửa không xanh riêng lẻ mà ghép vào vẫn 403.

**Sau khi sửa:** build 0 error · unit 2847 · integration 295 · e2e-smoke 14/14.

**⚠️ Ảnh hưởng FE/Mobile (repo khác — phải báo team):** client nào tự ghép
`/api/files/{id}/download` từ `AttachmentFileIds` để tải file KHÔNG phải của mình sẽ nhận
403. Đường đúng là gọi endpoint download-url của TicketService (đã có sẵn) — nay nó trả về
URL đã kèm `?grant=…`.

### #722 — chi tiết

**Thẩm định:** chuẩn. XML doc của chính `BatteryAssetsController` thừa nhận
"*Hiện tại chưa enforce server-side rằng Customer chỉ xem được asset của chính mình*";
`AlertsController` cũng ghi "*chưa có server-side filter để giới hạn Customer*".
Handler chỉ lọc theo `Id`. `PATCH /alerts/{id}/acknowledge` là **mutation** xuyên tenant.

**Chính sách áp dụng** (lấy nguyên từ `BatteryRealtimeAuthorizationHelper`, spec §34.10.6,
để đường REST không trôi khỏi đường SSE): Admin/Manager toàn quyền · **Staff xem được mọi
asset** (quyết định MVP đã có) · Customer chỉ của mình · còn lại từ chối (fail closed).

**File mới**
- `BatteryService.Application/Interfaces/IBatteryCurrentUserService.cs`
- `BatteryService.Application/Helpers/BatteryTenantScopeHelper.cs`
- `BatteryService.Application/Helpers/BatteryTenantAccessGuard.cs`
- `BatteryService.Infrastructure/Implements/Services/BatteryCurrentUserService.cs`
- `BatteryService.UnitTests/Helpers/TestBatteryCurrentUserService.cs`
- `BatteryService.UnitTests/Application/TenantIsolationGh722Tests.cs` (20 test)

**Handler đã vá (12)** — BatteryAsset: GetById, GetRealtime · SensorReading: History,
Aggregate, HourlyAggregate, Latest · Alert: GetAll, GetById, Acknowledge · Site: GetById,
GetAssets, GetDashboard. Cộng DI trong `ManageDependencyInjection.cs`.

**Quyết định thiết kế**
- Trả **404** (không phải 403) cho tài nguyên khác tenant — không tiết lộ sự tồn tại.
- **Fail closed**: Customer mà token không đọc được id ⇒ 401, KHÔNG rơi về "không giới hạn".
- Không thêm thành viên vào `ICurrentUserService` dùng chung (15 lớp hiện thực trên 9
  service sẽ vỡ) — dùng interface mở rộng cục bộ, đúng khuôn `ITicketCurrentUserService`.

**Test cũ bị đổi:** chỉ sửa **arity constructor** (22 chỗ, 5 file) vì API đổi. KHÔNG xoá
assertion, KHÔNG nới ngưỡng. Test cũ nhận `TestBatteryCurrentUserService.Admin()` =
phạm vi không giới hạn ⇒ giữ nguyên ý nghĩa ban đầu.

**Sau khi sửa:** build 0 error · unit 2838 pass · integration 295 pass · e2e-smoke 14/14.

**Còn thiếu — cần quyết:** container `solar-batteryservice` đang chạy **image cũ**
(Up 18h). Tầng e2e vì thế mới chứng minh "không hồi quy trên bản đã deploy", CHƯA chạy
qua code mới. Muốn e2e thật cho #722 phải `docker compose build batteryservice` +
restart, và seed 2 customer có asset riêng (script smoke hiện chỉ đăng nhập admin).

## #749 — [IoT] Định danh quá khổ bị ghi vào NVS trước, chỉ truncate trong RAM — CHUẨN, ĐÃ SỬA
Kết luận: đúng, và rộng hơn mô tả trong issue.
- `setApiKey`/`setDeviceCode` in chữ "truncate" nhưng KHÔNG cắt gì trước khi ghi: `nvsPutString`
  nhận nguyên chuỗi dài, chỉ bản sao RAM bị cắt, rồi vẫn `return true`.
- `identityBegin` đọc deviceCode bằng buffer 96 byte rồi `copySafe` xuống 64 ⇒ khởi động lại là
  âm thầm đổi sang mã cụt (nửa còn lại của cùng lỗi, issue không nhắc).
- OFF-BY-ONE thật: backend `device_code` HasMaxLength(64) nhưng buffer 64 byte chỉ chứa 63 ký tự
  ⇒ một mã 64 ký tự HỢP LỆ vẫn bị cắt. Không sửa cái này thì đổi sang "từ chối" sẽ chặn nhầm.

Đã sửa:
- `src/core/identity_validation.h` — vị từ thuần: rỗng / quá dài / ký tự xấu (chỉ nhận ASCII in
  được 0x21–0x7E). Chặn CR/LF vì apiKey đi thẳng vào header `X-Api-Key` = tiêm header.
- `setApiKey`/`setDeviceCode` — TỪ CHỐI trước mọi lần ghi, kèm lý do cụ thể ra Serial.
- `identityBegin` → `loadValidated()` — đọc bằng buffer đúng cỡ đích + kiểm lại; giá trị hỏng do
  firmware cũ để lại thì BỎ, quay về mặc định compile-time. Dùng `nvsHasKey` để phân biệt
  "chưa cấu hình" với "có nhưng đọc không nổi" và nói ra, thay vì im lặng.
- `device_identity.h` — buffer = số ký tự backend cho phép + 1 (deviceCode 64+1, apiKey 128+1
  theo cột `api_key_plaintext`).
- `identity_change_policy.h` (#747) — dùng CHUNG bộ kiểm, để CLI không duyệt thứ mà tầng ghi từ chối.
- `static_assert` chốt biên giữa hai hằng.

Kiểm chứng RED→GREEN (không phải test vô nghĩa):
- Lùi luật về "chỉ kiểm độ dài" ⇒ `test_policy_rejects_whitespace_too` ĐỎ (Expected 2 Was 1).
- Lùi buffer về 64 ⇒ `static_assert` gãy build đúng như thiết kế.
Test: +14 (`test_identity_validation`). Native 195 → **209**, thang loop-engine XANH cả 4.

## #762 — Một số đo ngoài dải chặn cả cửa sổ dự đoán SOH — CHUẨN, ĐÃ SỬA
Kiểm chứng: đúng từng chi tiết.
- `ai-module/src/schemas/predict.py` duyệt TỪNG dòng, `raise` ở dòng lệch dải ĐẦU TIÊN ⇒ một
  số đo hỏng là từ chối cả payload; job nhận null rồi `continue`.
- Chốt chặn ở ingest là `voltage < 0 || > 1000V` — giới hạn vật lý TOÀN CỤC, không theo loại
  pin, nên 52.4V trên pack 12V lọt qua hoàn toàn hợp lệ.

Chọn hướng: lọc ở JOB, không chặn ở ingest. Số đo bất khả thi vẫn là bằng chứng có thật của sự
cố (sai mapping, đứt dây, gán nhầm pack 48V vào asset 12V) — vứt ở ingest là mất đúng thứ cần
để truy nguyên. Vẫn lưu, nhưng không cho nó bịt miệng model.

Đã sửa:
- `AiReadingWindowFilter` + `AiInputContract` — lọc bằng ĐÚNG vị từ của AI (v/n_series ∈ [2,4.5],
  i×(2.0/capacity) ∈ [±5], t ∈ [-10,60]), kể cả mẹo Python `capacity=0 → i_scale=1.0`.
  Lọc rộng hơn AI thì mất dữ liệu tốt; hẹp hơn thì vẫn bị từ chối nguyên khối = không sửa được gì.
- `AiOptions.MaxScanReadings = 60` — quét dư để bù mẫu hỏng bằng mẫu cũ hơn còn tốt.
- Job: lọc trên THỰC THỂ rồi mới `BuildReadings` (cột `time` là giây tương đối so với mẫu đầu
  cửa sổ — dựng trước rồi lọc sẽ khiến `time` không bắt đầu từ 0, lệch phân phối đầu vào model
  trong im lặng). Log cảnh báo chất lượng dữ liệu + gộp số vào log kết thúc lượt, kể cả nhánh
  predicted=0 (nhánh cũ chỉ log khi predicted>0 nên đúng lúc hỏng nặng nhất thì im hoàn toàn).

Việc phát sinh tự tìm ra: chuỗi lý do dùng locale máy ⇒ trên máy dấu phẩy thập phân, dải
"[2.0, 4.5]" in ra thành "[2, 4,5]" — đọc như ba con số. Đã ép `InvariantCulture` + test ghim.

Kiểm chứng RED→GREEN:
- Bỏ bộ lọc ⇒ 2 test tầng job ĐỎ.
- Dựng dòng TRƯỚC khi lọc ⇒ `Tick_OutlierRemoved_TimeColumnStillStartsAtZero` ĐỎ.
- Mock AI được sửa để TỪ CHỐI giống AI thật, nếu không khẳng định "vẫn có prediction" là vô nghĩa.
Test: +32 (27 filter + 5 job). Unit 2951 → **2983**. Thang XANH cả 4 (602 integration, e2e-smoke).

KHÔNG mở rộng: không thêm `AnomalyTypeEnum` mới cho cảnh báo data-quality — sẽ kéo theo FE +
TicketService + NotificationService, ngoài phạm vi issue.

## #763 — Telemetry trùng trả 500 thay vì bỏ qua — CHUẨN, ĐÃ SỬA
Kiểm chứng: đúng. PK tổ hợp `(time, battery_asset_id)`; handler add mọi item hợp lệ rồi mới
`SaveChangesAsync`, không dò trùng ⇒ 23505 ⇒ 500 ⇒ rollback CẢ batch kể cả số đo MỚI. Bản ghi
idempotency chỉ chạy khi có ĐỦ (DeviceCode, IdempotencyKey) nên đường legacy/simulator không được
che. Gửi lại cũng 500 y hệt ⇒ dữ liệu mới không bao giờ vào được.

Đã sửa — tiền lọc trùng (một truy vấn, giới hạn theo tập asset + khoảng thời gian của batch):
- `seenKeys` vừa là ảnh chụp DB vừa là sổ khoá đã gặp ⇒ một phép `Add` bắt CẢ hai ca: trùng với
  DB, và trùng ngay trong cùng batch (ca sau làm EF ném ở change tracker, cũng ra 500).
- Tách `NormalizeToUtc` dùng chung cho cả DÒ và GHI — lệch phép chuẩn hoá là dò hụt rồi vẫn dính.
- Trùng tính vào `skipped` (firmware #748 đọc đúng hai con số này), thông báo TÁCH trùng khỏi
  outlier: trùng là thiết bị gửi lại (bình thường), outlier là cảm biến hỏng (phải đi kiểm).
- KHÔNG lọc IsDeleted khi dò (SensorReading không có cột đó; kể cả có thì dòng xoá mềm vẫn chiếm PK).

Quyết định có cân nhắc: KHÔNG dựng cơ chế retry cho ca đua (hai request giống hệt cùng lúc).
`IBatteryUnitOfWork` không lộ DbContext/detach nên retry sạch là không làm được nếu không mở rộng
API. Quan trọng hơn: có tiền lọc rồi thì lần gửi lại sẽ TỰ khỏi — đúng cái vòng lặp chết mà issue
mô tả ("retry tiếp tục thất bại") biến mất. Đã ghi chú trong code.

Kiểm chứng RED→GREEN: tắt dò trùng ⇒ 5/7 test ĐỎ, 2 test chống hồi quy vẫn xanh.
Test: +7 integration. Integration 602 → **609**. Thang XANH cả 4 (2983 unit).

## #764 — Inbox đánh dấu message TRƯỚC side effect ⇒ mất retry — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng, và rộng hơn issue mô tả.
- `ProcessOnceAsync` gọi `TryMarkProcessedAsync` TRƯỚC `action()`, `RedisInboxStore` không bao giờ
  gỡ dấu khi lỗi ⇒ lỗi tạm thời = mất message vĩnh viễn (gửi lại thấy dấu → bỏ qua → ACK).
- Issue chỉ nói về `ProcessOnceAsync`. Thực tế TicketService còn 6 consumer gọi THẲNG
  `TryMarkProcessedAsync` (AccountSync ×5, VoiceTranscriptionRequested) — cùng lỗi y hệt.
- `ChatLanguageDetectConsumer` đã TỰ VÁ bằng tay: xoá thẳng khoá Redis với định dạng khoá viết
  cứng trong nhánh catch. Bằng chứng có người từng dính đúng lỗi này rồi vá tại chỗ.

Đã sửa — vòng đời ba bước trong `IInboxStore`:
- `TryBeginAsync` → giữ chỗ TTL NGẮN (`LeaseSeconds`=300); `CompleteAsync` → chuyển sang TTL dài
  (`TtlDays`); `ReleaseAsync` → nhả khi lỗi. Cả chốt lẫn nhả chạy qua Lua so DẤU SỞ HỮU, để một
  chỗ giữ ĐÃ HẾT HẠN không giẫm lên lượt xử lý của người khác.
- Tách "đang chạy" khỏi "đã xong": `InProgressElsewhere` NÉM `InboxLeaseHeldException` để message
  quay lại sau. Trả false ở đây chính là cách ACK một message mà side effect chưa từng chạy.
- Chốt hỏng (Redis sập) KHÔNG ném — side effect đã thành công rồi, ném là biến nó thành thất bại
  và kéo theo một lần gửi email/SMS thừa. Chỗ giữ tự hết hạn.
- Lỗi lúc nhả không được che lỗi gốc (lỗi gốc quyết định chính sách retry của MassTransit).
- Chuyển 6 consumer TicketService sang `ProcessOnceAsync`; bỏ hack Redis + phụ thuộc
  `IConnectionMultiplexer` khỏi ChatLanguageDetectConsumer.
- 41 chỗ gọi `ProcessOnceAsync` giữ NGUYÊN chữ ký — không phải sửa gì.

Test cập nhật: `Consume_CommitThrows_DeletesInboxKey_Rethrows` khẳng định hack cũ (xoá khoá Redis)
⇒ đổi sang khẳng định `ReleaseAsync` + KHÔNG `CompleteAsync`. Ý định giữ nguyên, chỗ khẳng định đổi.

Kiểm chứng RED→GREEN: dựng lại hành vi cũ ⇒ 5/10 test ĐỎ, gồm đủ 3 tiêu chí nghiệm thu của issue.
Test: +13. Unit 2983 → **2996**. Thang XANH cả 4 (609 integration).

## #765 — Debounce key chiếm TRƯỚC khi ghi notification — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. `TryBeginByMessageAsync` chiếm key Redis 30 phút ngay từ đầu — trước resolve
recipient, trước ghi DB — và không bao giờ nhả. Một lỗi DB/resolver ở lần đầu là mọi lần
MassTransit gửi lại trong 30 phút đều log "duplicate" rồi return ⇒ notification mất hẳn.

Đã sửa — vòng đời ba bước, dùng LẠI nguyên thuỷ lease đã có sẵn trong `ICacheService`
(`TrySetIfNotExistsAsync` / `TryRefreshLeaseAsync` / `TryReleaseLeaseAsync`), không thêm hạ tầng mới:
- `MessageLease` = 2 phút cho lúc đang chạy; chỉ nâng lên `MessageWindow` = 30 phút SAU khi ghi xong.
- Lỗi ⇒ nhả ngay theo dấu sở hữu. Lỗi lúc nhả không che lỗi gốc; lỗi lúc nâng hạn KHÔNG ném
  (side effect đã thành công, ném là kéo theo một notification thứ hai).
- Chuyển 21 consumer (15 + 6 trong TicketLifecycleConsumers) sang `NotificationDebounce.ProcessOnceAsync`.
- Bỏ `TicketNotificationHelper` — sau khi chuyển thì không còn ai gọi, để lại là code chết.
- KHÔNG đụng debounce theo AlertId (`TryBeginAsync`) — đúng như issue yêu cầu; đó là luật nghiệp vụ
  "một alert chỉ báo một lần trong 5 phút", cố ý bỏ qua event sau, khác hẳn chống trùng do retry.

Test cập nhật: `TicketCreated_FirstMessage_ShouldSetDebounceKey_30Min` khẳng định TTL 30 phút ngay
lúc chiếm key ⇒ đổi thành khẳng định chỗ giữ NGẮN HƠN cửa sổ + có nâng hạn sau khi ghi. Không ghim
đúng một con số (giá trị lease có thể chỉnh) mà ghim BẤT BIẾN.

Việc phát sinh: hai lỗi trong chính test của tôi, không phải ở code — (1) chờ bằng `Consumed.Any<T>()`
là sai vì nó trả về ngay từ lần tiêu thụ đầu; (2) một lượt ghi sinh 2 bản ghi (InApp + Push) chứ
không phải 1.

Kiểm chứng RED→GREEN: dựng lại hành vi cũ ⇒ 2 test ĐỎ.
Test: +2 (và +1 seam `failWriteOnAttempt` trong harness). Unit 2996 → **2998**. Thang XANH cả 4.

## #766 — AuthService không bao giờ phát AccountStatusChangedEvent — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. Battery + Ticket đều đã viết consumer cho event này, nhưng tìm khắp AuthService
không có một chỗ nào `new AccountStatusChangedEvent`. `AccountSyncSnapshotEvent` (thêm 02/08/2026)
KHÔNG che được: chỉ NotificationService consume nó.
Đã kiểm thêm trước khi sửa: mirror `CustomerAccount` bên Battery dùng chính AccountId làm PK nên
consumer tra khoá ĐÚNG — sửa xong là đồng bộ chạy thật, không phải sửa nửa vời.

Đã sửa — publish trên cả 5 đường đổi trạng thái, đặt TRƯỚC `SaveChangesAsync` (AuthService CÓ
outbox, `IMessageProducerService` ghi vào OutboxMessages cùng DbContext ⇒ nguyên tử với commit):
1. `ChangeAccountStatusCommandHandler` — admin đổi trạng thái
2. `LoginCommandHandler` — tự khoá sau 5 lần sai mật khẩu (+ inject producer)
3. `UnlockAccountCommandHandler` — admin mở khoá (+ inject producer)
4. `DeactivateMeCommandHandler` — người dùng tự vô hiệu hoá
5. `ReactivateVerifyCommandHandler` — khôi phục tài khoản
Ở 2-5 đều bắt `oldStatus` TRƯỚC khi ghi đè, để event nói đúng chuyển đổi chứ không chỉ trạng thái mới.

Cân nhắc: chỉ phát lúc KHOÁ mà quên lúc MỞ sẽ làm read-model kẹt ở trạng thái khoá vĩnh viễn —
còn tệ hơn không phát gì. Nên cả hai chiều đều phát; và các nhánh không đổi trạng thái (đổi sang
đúng trạng thái đang có, unlock khi chưa khoá, sai mật khẩu lần 1-4) thì KHÔNG phát, kẻo dội vô
nghĩa xuống mọi service mỗi lần ai đó gõ nhầm mật khẩu.

Kiểm chứng RED→GREEN: gỡ publish ⇒ 6/8 test ĐỎ (2 test "không phát gì" vẫn xanh, đúng thiết kế).
Test: +12 (10 file mới + 2 trong LoginCommandHandlerTests). Unit 2998 → **3010**. Thang XANH cả 4.

## #768 — Email xác nhận 2FA xuyên thiết bị không có consumer — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. AuthService publish `SendTwoFactorCrossDeviceConfirmEmailEvent` từ #AUTH-51,
endpoint trả 200, nhưng tìm khắp EmailService không có `IConsumer<…>` nào. Event vào Rabbit rồi
nằm đó — người dùng không nhận link, không hoàn tất được trong TTL 10 phút, mọi tầng đều báo
thành công. Kiểu hỏng khó lần nhất vì không có gì đỏ ở đâu cả.

Đã sửa:
- `SendTwoFactorCrossDeviceConfirmConsumer` — dựng theo khuôn `SendAdminInviteConsumer`, đi qua
  `ProcessOnceAsync` (idempotent + nhả chỗ giữ khi provider lỗi, theo GH-764).
- Template `TwoFactorCrossDeviceConfirm.html` + hằng trong `EmailTemplates`.
- KHÔNG cần đăng ký tay: `AddMessageBus(..., typeof(SendOtpRegisterConsumer).Assembly)` quét assembly.
- Consumer KHÔNG tự ghép URL — dùng thẳng `ConfirmUrl` từ event, để AuthService là nơi DUY NHẤT
  quyết định địa chỉ đích (ghép lại ở hai nơi thì link sai chỉ lộ khi người dùng bấm vào).
- KHÔNG log `ConfirmUrl`: nó chứa token bật được 2FA — log ra là biến file log thành đường vòng
  qua chính lớp bảo vệ đang được bật.

Việc phát sinh: `EmailTemplateFilesTests.EveryTemplateConstant_HasFileOnDisk` chép TAY danh sách
hằng, nên thêm template mới mà quên sửa test thì test vẫn xanh — đúng thứ nó sinh ra để chặn.
Đã đổi sang dò bằng phản chiếu (kèm khẳng định danh sách không rỗng, kẻo test thành rỗng tuếch).

Kiểm chứng RED→GREEN: xoá file template ⇒ 2 test ĐỎ (render còn placeholder thô + thiếu file).
Test: +4 (consumer) + 1 contract template. Unit 3010 → **3017**. Thang XANH cả 4.

## #769 — Đổi role không đồng bộ xuống bản sao downstream — ĐÚNG MỘT PHẦN, ĐÃ SỬA (P1)
Kiểm chứng: lõi ĐÚNG, nhưng issue nói thừa một service.
- Battery + Ticket: đúng, mirror chỉ dựng MỘT LẦN lúc kích hoạt, đổi role không tới nơi.
- Notification: issue nói cũng lỡ, nhưng THỰC TẾ đã được phủ bởi `AccountSyncSnapshotEvent`
  (thêm 02/08/2026) — `ChangeAccountRoleCommandHandler` đã publish nó kèm role mới. Thêm đường
  thứ hai ghi cùng read-model chỉ tạo tranh chấp ⇒ KHÔNG làm.

Đã sửa:
- Hợp đồng mới `AccountRoleChangedEvent` — mang CẢ role cũ lẫn role mới. Chỉ có role mới thì
  consumer không suy ra được bản sao NÀO phải dọn. Kèm đủ trường hồ sơ để tạo bản sao còn thiếu
  mà không phải gọi ngược về AuthService.
- AuthService publish qua outbox (trước `SaveChangesAsync` ⇒ nguyên tử). Phải thêm `Include(Role)`
  và chụp TÊN role cũ trước khi gán role mới.
- `TicketAccountRoleChangedConsumer` — hai chiều: tạo/kích hoạt mirror đúng phía, ĐÌNH CHỈ mirror
  phía kia. Đình chỉ chứ không xoá: ticket lịch sử tham chiếu tới nó, xoá là hỏng lịch sử.
  Hàm `IsStaffRole` khớp ĐÚNG cách consumer kích hoạt phân loại — lệch là account nằm ở cả hai
  bảng hoặc không bảng nào.
- `AccountRoleChangedConsumer` (Battery) — cập nhật `Role` + `IsActive`; KHÔNG tạo mới khi chưa có
  (gán pin cho khách nào là việc của luồng gán tài sản, tạo ở đây sẽ dựng ra khách không tài sản).

Đã kiểm bẫy trước khi làm: relay outbox của AuthService publish theo kiểu RUNTIME
(`publishEndpoint.Publish(obj, type, ct)`) nên không dính lỗi "consumer type cụ thể không nhận được".

Kiểm chứng RED→GREEN: gỡ publish ⇒ test Auth ĐỎ (test no-op vẫn xanh, đúng thiết kế).
Test: +14 (7 Ticket + 5 Battery + 2 Auth). Unit 3017 → **3031**. Thang XANH cả 4.

## #770 — Đổi kỹ năng Staff không bao giờ được đồng bộ — CHUẨN, ĐÃ SỬA (P2)
Kiểm chứng: đúng. `TicketStaffSkillsUpdatedConsumer` tồn tại nhưng KHÔNG ai phát
`StaffSkillsUpdatedEvent` — consumer mồ côi ⇒ định tuyến/giao việc theo kỹ năng chạy trên dữ liệu
chụp lúc kích hoạt và không bao giờ đổi.

Đã sửa — publish từ cả `AddStaffSkillCommandHandler` lẫn `DeleteStaffSkillCommandHandler`, đặt
TRƯỚC `SaveChangesAsync` (outbox ⇒ nguyên tử với chính thay đổi).

Quyết định thiết kế: phát TOÀN BỘ tập kỹ năng sau thay đổi, không phát "vừa thêm/xoá mã X".
Một event rơi giữa chừng thì tập đầy đủ ở lần sau vẫn TỰ CHỮA; danh sách gia giảm thì lệch mãi.
Đúng như tiêu chí nghiệm thu issue yêu cầu ("publish full current skill set").

Bẫy đã xử lý: thay đổi CHƯA được lưu lúc publish, nên truy vấn còn thấy trạng thái cũ ⇒ tự hợp
nhất trong bộ nhớ (Add: thêm mã mới nếu chưa có — cần cho ca khôi phục kỹ năng đã xoá mềm, vì
truy vấn "chưa xoá" không thấy nó; Delete: loại thẳng mã bị gỡ). Chỉ chiếu `SkillCode` nên không
dính identity map của EF.

Ca dễ bỏ sót đã có test: gỡ kỹ năng CUỐI CÙNG phải phát tập RỖNG — im lặng ở đây là consumer giữ
nguyên kỹ năng cuối và vẫn giao đúng loại việc đó cho người đã không còn làm được.

Kiểm chứng RED→GREEN: gỡ publish ⇒ 6/7 test ĐỎ.
Test: +7. Unit 3031 → **3038**. Thang XANH cả 4.

## #771 — Đổi permission không xoá cache phân quyền — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. `PermissionsChangedConsumer` + hợp đồng đều có sẵn, nhưng KHÔNG ai phát
`PermissionsChangedEvent`. Cache role-permission sống 5 phút ⇒ quyền vừa THU HỒI vẫn dùng được
tới hết TTL — năm phút hệ thống cố tình cho qua thứ quản trị viên vừa chặn.

Đã sửa — `SetRolePermissionsCommandHandler` publish qua outbox (trước `SaveChangesAsync`):
- Phát ĐÚNG những gì xảy ra: `UnboundFromRole` cho phần gỡ, `BoundToRole` cho phần thêm, mỗi cái
  mang mã của chính nó. Sửa hỗn hợp ⇒ HAI event. Gộp làm một sẽ phải chọn bừa một `ChangeKind` và
  làm sai lệch nhật ký; xoá cache là lũy đẳng nên hai event vô hại.
- `RoleCode` = `Role.NormalizedName` — consumer tra role theo đúng cột đó; sai là xoá cache trượt
  trong im lặng.
- Không có thay đổi thực (gửi lại đúng tập đang có) hoặc validate hỏng (404) ⇒ KHÔNG phát.

Việc phát sinh — SỬA LỖI CỦA CHÍNH TÔI: `PublishAsync_FromBaseTypeVariable_ReachesConcreteConsumer`
(test tôi viết ở #725) đỏ ở thang đầy đủ nhưng chạy riêng pass trong 216ms. Nguyên nhân: tôi quên
`SetTestTimeouts` nên dính mặc định inactivity 1 giây của MassTransit v8 — đúng cái bẫy mà mọi
harness khác trong repo đều đã có chú thích. Đã thêm (30s, 15s) cho cả 2 chỗ.

Kiểm chứng RED→GREEN: gỡ publish ⇒ 3/5 test ĐỎ (2 test "không phát gì" vẫn xanh).
Test: +5. Unit 3038 → **3043**. Thang XANH cả 4.

## #773 — Hồ sơ khách hàng không tới bản sao BatteryService — CHUẨN, ĐÃ SỬA (P2)
Kiểm chứng: đúng. Chú thích của chính hợp đồng `AccountProfileUpdatedEvent` ghi BatteryService là
subscriber; Ticket và Notification đều đã có consumer, riêng Battery thì không ⇒ danh sách site và
danh sách pin hiển thị tên/số điện thoại cũ vĩnh viễn.

Đã sửa: `AccountProfileUpdatedConsumer` (Battery) — cập nhật Email/FullName/PhoneNumber + mốc đồng
bộ, đi qua `ProcessOnceAsync`. Tự đăng ký vì `AddMessageBus` quét assembly.

Ranh giới đã giữ có chủ ý: KHÔNG chạm `Role` / `IsActive` — hai trường đó có event riêng
(#766, #769). Chép thêm ở đây là tạo hai đường ghi cùng một ô, và event nào tới sau sẽ thắng bất
kể cái nào mới hơn. Có test ghim điều này.

Ca dễ bỏ sót đã có test: xoá số điện thoại (null) là thay đổi HỢP LỆ — coi null là "không có gì để
cập nhật" sẽ khiến số cũ nằm lại mãi.

Kiểm chứng RED→GREEN: vô hiệu hoá thân consumer ⇒ 3/6 test ĐỎ.
Test: +6. Unit 3043 → **3049**. Thang XANH cả 4.

## #774 — Dashboard toàn hệ thống lộ cho Customer và Staff — CHUẨN, ĐÃ SỬA (P1, bảo mật)
Kiểm chứng: đúng, và chính TÀI LIỆU của endpoint đã tự mâu thuẫn — remarks ghi "trả tổng hợp toàn
bộ system (yêu cầu role Admin/Manager)" trong khi code chỉ có `[Authorize]`.

Đã sửa — đặt kiểm quyền trong HANDLER, không phải attribute ở controller:
- Không có `siteId` (toàn hệ thống): chỉ Admin/Manager, còn lại **403**.
- Có `siteId`: dùng `BatteryTenantAccessGuard.CanAccessSiteAsync` — Admin/Manager/Staff mọi site
  (§34.10.6), Customer chỉ site của mình; site người khác trả **404** chứ không 403, vì 403 xác
  nhận site đó CÓ THẬT ⇒ biến endpoint thành công cụ dò khách hàng. Khớp quy ước GH-722.
- Vì sao không dùng attribute: attribute KHÔNG chặn được ca token hợp lệ + vai trò hợp lệ + `siteId`
  của người khác. Đây là giới hạn theo DỮ LIỆU nên phải nằm cùng chỗ truy dữ liệu.

KHÔNG "sửa" quyền Staff xem mọi asset — đó là quyết định MVP có chủ ý (§34.10.6). Cái bị chặn chỉ
là con số GỘP toàn hệ thống.

Kiểm chứng độc lập cho lựa chọn chính sách: endpoint anh em `GET /api/sites/dashboard/stats` (cũng
trả số toàn hệ thống) ĐÃ có sẵn `[Authorize(Roles = "Admin,Manager")]`. Vậy Admin/Manager là quy
ước sẵn có của repo, không phải tôi tự đặt. Nếu chỉ bịt một endpoint mà bỏ endpoint kia thì bản sửa
vô nghĩa — đã kiểm và endpoint kia an toàn sẵn.

Đã sửa luôn chú thích endpoint (trước đó ghi "Phân quyền: yêu cầu JWT hợp lệ" — nói sai sự thật).

Kiểm chứng RED→GREEN: dựng lại hành vi cũ ⇒ 5/9 test ĐỎ.
Test: +9 (+ factory `Manager()` cho test double). Unit 3049 → **3058**. Thang XANH cả 4.

## #775 — Audit nuốt MỌI DbUpdateException như thể trùng lặp — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. `catch (DbUpdateException)` không kiểm gì ⇒ chuỗi quá dài (22001), jsonb không
hợp lệ (22P02), vi phạm khoá ngoại, lỗi tạm thời… đều bị ghi log "trùng lặp" rồi ACK: bản ghi kiểm
toán mất hẳn, không retry, không DLQ. Với hệ thống kiểm toán thì đây là kiểu mất mát tệ nhất —
mất trong im lặng và tự nhận là bình thường.

Đã sửa — `DuplicateAuditDetection.IsDuplicateEventInsert` (thuần, test được):
- Chỉ nuốt khi SQLSTATE = 23505 VÀ (không đọc được tên ràng buộc HOẶC khớp `ux_agg_event_occurred`).
  Vi phạm unique ở ràng buộc khác ⇒ ném lên, vì đó là lỗi dữ liệu chứ không phải trùng event.
- Mọi lỗi khác ⇒ log Error + ném lại để MassTransit retry/DLQ.
- Không đọc được SQLSTATE ⇒ NÉM (fail closed). Chuỗi thông điệp không phải căn cứ phân loại lỗi.
- Cố ý KHÔNG tham chiếu `Npgsql.PostgresException`: dùng `DbException.SqlState` (API chuẩn .NET từ
  .NET 5) nên tầng Application không phải kéo driver vào, và test dựng được ngoại lệ giả.

Test cũ phải sửa: `Consume_SaveChangesThrowsUniqueViolation_IsSwallowed` dựng
`DbUpdateException("…duplicate key…")` — chỉ có chuỗi, không có inner `DbException`. Bản giả đó
xanh cả với code không kiểm gì. Đã đổi sang ngoại lệ GIỐNG THẬT (inner `DbException`, SqlState
23505, tên ràng buộc).

Kiểm chứng RED→GREEN: bắt trọn `DbUpdateException` như cũ ⇒ 6/12 test ĐỎ.
Test: +6 (4 Theory SQLSTATE + ràng buộc khác + thiếu SqlState). Integration 609 → **615**.

### Việc phát sinh — lỗi im lặng thứ hai, ngoài phạm vi issue
Thang kiểm đỏ 1 test không liên quan: `ToM4a_RealWavInput_ProducesPlayableM4a` (TicketService).
Chạy riêng thì xanh (262ms) — đỏ do tranh chấp khi 9 assembly chạy song song. Nhưng khi lần vào
mới thấy: `FfmpegAudioTranscoder` **trả thẳng mảng byte rỗng** nếu ffmpeg thoát mã 0 mà không sinh
dữ liệu ⇒ lưu một tệp ghi âm 0 byte, đính kèm trông hợp lệ, mở ra không có gì.
Đã thêm kiểm đầu ra (độ dài + chữ ký `ftyp`) và NÉM nếu hỏng. Lưu ý trung thực: việc này làm lỗi
NỔI RÕ chứ KHÔNG loại bỏ tính thất thường do tải máy — nguyên nhân đó là môi trường, không phải logic.

## #776 — Endpoint OAuth introspection mở cho tất cả, không giới hạn tần suất — CHUẨN, ĐÃ SỬA (P2)
Kiểm chứng: đúng. `POST /api/auth/introspect` không `[Authorize]`, không API key, không rate-limit.
Đo được lúc chạy thật: 12 request liên tiếp không kèm gì đều trả 200 `active=true`.
Đã tìm toàn repo: **KHÔNG service nào gọi endpoint này** — nó mở toang mà chưa ai dùng, nên
fail-closed không làm hỏng luồng nào.

Đã sửa — ba lớp:
1. `IntrospectionOptions` + header `X-Introspection-Key`, so sánh bằng
   `SecureCompareHelper.FixedTimeEquals` (so bằng `==` sẽ dừng ở ký tự lệch đầu tiên, biến độ trễ
   phản hồi thành kênh dò từng ký tự của khoá). Chưa cấu hình (hoặc khoá < 32 ký tự) ⇒ TỪ CHỐI TẤT
   CẢ — mặc định mở khi thiếu cấu hình chính là lỗi đang sửa.
2. Rate limit `PolicyIntrospect` 60 req/phút theo IP — lớp thứ hai cho ca khoá bị lộ.
3. Chặn NGAY ĐẦU handler, TRƯỚC khi kiểm chữ ký JWT và truy Redis: từ chối muộn hơn thì vẫn còn
   nguyên đường khuếch đại tải, và thời gian phản hồi vẫn rò rỉ thông tin. Có test verify
   `ValidateToken`/`IsRevokedAsync` KHÔNG được gọi khi trái phép.
Khoá lấy từ HEADER, field trên command có `[JsonIgnore]`+`[BindNever]` (có test phản chiếu ghim):
client tự đặt được qua body thì lớp bảo vệ tự mở cửa cho đúng kẻ cần chặn.

Sửa tài liệu nói SAI: `docs/api-auth.md` ghi "RFC 7662 cho phép unauthenticated introspection trong
scope nội bộ" — ngược hẳn với RFC 7662 §2.1 ("the endpoint MUST require some form of authorization").
Chính câu đó là lý do lỗ hổng tồn tại. Đã sửa + gỡ `introspect` khỏi danh sách "không có rate limit".

Việc phát sinh trong test của tôi: token dựng thiếu claim `AccountId` (handler đọc claim đó, không
phải `sub`) ⇒ test một hình dạng token không tồn tại. Đã sửa test.

Kiểm chứng RED→GREEN: gỡ hai chốt kiểm ⇒ 10/13 test ĐỎ.
Test: +13. Unit 3058 → **3071**.

### Ghi nhận sự cố hạ tầng (không phải lỗi code) — 2026-08-04
Một lần chạy thang có L2 TREO 67 phút rồi timeout (ngưỡng 60 phút), trong khi mọi lần khác chỉ
~1m40s. Đã khoanh vùng thay vì đoán:
- Chạy riêng `AuthService.IntegrationTests` (bộ vừa bị sửa): 50/50 xanh trong 51 giây ⇒ KHÔNG phải
  do bản sửa #776.
- Tìm thấy 3 container `testcontainers-ryuk` mồ côi 34 phút — dấu hiệu Testcontainers bị nghẽn khi
  cả stack `solar-*` chạy nhiều giờ song song. Đã dọn (chỉ ryuk, không đụng `solar-*`).
- Chạy lại trọn thang với CÙNG code: L2 615/615 trong 1m38s.
Kết luận: sự cố môi trường nhất thời. KHÔNG tuyên bố đã tìm ra căn nguyên — chỉ loại trừ được
thay đổi của mình. Nếu tái diễn thì hướng lần tiếp theo là giới hạn song song của Testcontainers.

## #777 — SOH worker vứt bỏ cycle_count và soc_percent — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng, và tôi đã dò kỹ hợp đồng AI trước khi sửa vì chỗ này dễ hiểu nhầm:
- `config.py` ghi "BASE_FEATURES = 4 cột … là API payload (BE gửi)" — đọc lướt sẽ tưởng 4 cột mới
  là đúng chuẩn. Nhưng `predict.py` khai `FULL_INPUT_FEATURES = INPUT_FEATURES = 6` nằm trong
  `allowed_feature_counts`, và ghi rõ "BE may instead send all 6 columns directly … more accurate
  than this service's window-local estimate". Tức AI nhận 6 cột THEO VỊ TRÍ.
- BE có sẵn dữ liệu: `SocPercent` không nullable (luôn có), `CycleCount` là `int?`.
- Client gRPC dùng `Values.AddRange(row)`, client HTTP serialize thẳng mảng ⇒ KHÔNG phải sửa client.

Đã sửa `BuildReadings`: gửi `[V, I, T, time, cycle_count, soc_percent]` khi MỌI mẫu trong cửa sổ có
`CycleCount`; thiếu dù một mẫu thì lùi về 4 cột. Lý do không gửi nửa vời: hợp đồng AI đòi cycle/soc
"tất cả hoặc không", cửa sổ pha trộn bị từ chối NGUYÊN KHỐI — đúng vòng câm lặng GH-762 vừa gỡ.
Nhánh 4 cột là đường chạy thật (120.762/120.770 mẫu có cycle), không phải phòng hờ.

Giao điểm với GH-762 (suýt bỏ sót): cửa sổ 6 cột có thêm `soc_percent`, và AI KIỂM cột đó
(`if len(row) >= 6 and not s_lo <= row[5] <= s_hi`). Không lọc phía mình thì một SOC bất khả thi
lại làm AI từ chối nguyên cửa sổ — tái lập đúng lỗi vừa sửa. Đã thêm `SocMin/SocMax` vào
`AiInputContract` + kiểm cột 5 trong bộ lọc, và dựng `checkRows` theo ĐÚNG bố cục sẽ gửi.
`cycle_count` cố ý KHÔNG kiểm dải — AI cũng không kiểm.

Kiểm chứng RED→GREEN (hai chốt riêng biệt):
- Ép luôn 4 cột ⇒ `Tick_AllReadingsHaveCycleCount_SendsSixColumns` ĐỎ.
- Giữ 6 cột nhưng bỏ kiểm SOC ⇒ `Tick_ReadingWithImpossibleSoc_IsQuarantined_NotSentToAi` ĐỎ.
Test: +4. Unit 3071 → 3075.

## #778 — Vòng phản hồi prescription bị đứt — CHUẨN, ĐÃ SỬA (P2)
Kiểm chứng: đúng. Proto có `prescription_id = 23`, AI có `POST /prescribe/feedback`, nhưng cả hai
client Battery đều BỎ trường đó khi map ⇒ id chết ngay tại ranh giới bridge; không endpoint nào gửi
phản hồi. Kỹ thuật viên đọc được lời khuyên nhưng không nói lại được nó đúng hay sai, nên AI lặp
lại cùng lời khuyên sai mãi.

Ràng buộc THẬT phát hiện khi làm: `ai_service.proto` chỉ khai 4 RPC (Predict/Prescribe/Health/
PredictStream) — KHÔNG có RPC feedback. Nên đường phản hồi bắt buộc đi HTTP. Vì vậy tách
`IAiPrescriptionFeedbackClient` riêng thay vì thêm vào `IAiPrescriptionClient`: nhét chung sẽ buộc
bản gRPC hiện thực thứ nó không làm được, và bản "ném NotSupported" đó sớm muộn sẽ có người gọi.

Đã sửa:
- `AiPrescriptionResult.PrescriptionId` + map ở CẢ gRPC lẫn HTTP client.
- `Alert.AiPrescriptionId` (nullable, maxlen 64) + migration `AddAlertAiPrescriptionId`
  (một cột nullable, có `Down()`, không SQL nguy hiểm — đã ĐỌC `Up()` chứ không tin mặc định).
- Delegate prescribe trả về CẢ kết quả thay vì chỉ đoạn text (trước đây `BuildPrescriptionText`
  nuốt luôn object nên id chết tại chỗ).
- `POST /api/alerts/{id}/prescription-feedback` + command/handler.

Tách MÃ LỖI có chủ ý — ba ca khác nhau về việc client có nên thử lại:
- **409** alert có thật nhưng chưa có prescription (404 sẽ khiến người dùng đi tìm alert đang hiện
  ngay trước mắt họ).
- **410** AI không còn giữ id (thử lại vô ích).
- **503** AI không kết nối được (thử lại sau).
Giới hạn tenant: alert của khách khác trả 404 không phải 403 (khớp GH-722/GH-774). Alert gắn tenant
qua CẢ asset LẪN site — alert cấp site không có asset, đã xử đúng cả hai đường, không xác định được
thì từ chối.

### Lỗ hổng trong chính bộ test của tôi — đã tự phát hiện và vá
Sau khi test xanh, tôi thử bỏ mapping `prescription_id` ở client gRPC: **KHÔNG test nào đỏ**. Vì
test gán thẳng `AiPrescriptionId` lên Alert nên chưa hề đi qua đường client → alert. Đã thêm 2 test
chạy đúng đường đó; giờ bỏ lưu id là `Tick_CriticalAlert_StoresPrescriptionIdOnTheAlert` ĐỎ.

Kiểm chứng RED→GREEN (ba chốt riêng): gộp 410 vào 503 + đổi 409 thành 404 ⇒ 2 test ĐỎ;
bỏ lưu id ⇒ 1 test ĐỎ.
Test: +16 (14 feedback + 2 đường lưu id).

## #780 — Ai:MinReadings vi phạm hợp đồng "đúng 30 dòng" của model — CHUẨN, ĐÃ SỬA (P2)
Kiểm chứng: đúng. `predict.py`: `if len(v) != WINDOW_SIZE: raise` — AI từ chối THẲNG mọi payload
khác 30 dòng. BE dùng `MinReadings` cho CẢ ngưỡng "đủ lịch sử" LẪN số dòng gửi đi ⇒ đặt 29 hay 31
là qua ngưỡng rồi gửi sai hình dạng: REST 422 / gRPC INVALID_ARGUMENT, worker nhận null, prediction
DỪNG HẲN — mà nhìn cấu hình không thấy gì sai.

Đã sửa — tách hai khái niệm bị gộp:
- `AiOptions.WindowSize` = **hằng số** 30, KHÔNG cấu hình được. 30 là hình dạng nướng vào trọng số
  model, không phải tham số vận hành. Có test phản chiếu ghim nó không phải property có setter —
  biến lại thành option là lỗ hổng quay lại y nguyên.
- `MinReadings` chỉ còn là ngưỡng lịch sử; payload LUÔN đúng `WindowSize` dòng (30 mẫu mới nhất).
- Kiểm cấu hình lúc KHỞI ĐỘNG (`.Validate(...).ValidateOnStart()`, khuôn có sẵn của repo):
  `MinReadings >= WindowSize`, `MaxScanReadings >= MinReadings`, interval/timeout > 0. Sai cấu hình
  thì service KHÔNG LÊN, thay vì lên bình thường rồi câm lặng.

### Việc phát sinh — harness cũ mã hoá một cấu hình BẤT KHẢ THI
Sau khi sửa, 8 test tầng job đỏ. Không phải hồi quy: harness dùng `MinReadings = 3` và gieo 3-4 mẫu
— một thiết lập KHÔNG BAO GIỜ chạy được ở production, vì AI đòi đúng 30. Nói cách khác bộ test đang
xanh trong khi mã hoá đúng cái lỗi mà issue này mô tả.
Đã nâng toàn bộ test tầng job lên quy mô ≥30 bằng bộ sinh `Window(assetId, t0, count, mutate)`,
giữ nguyên ý định từng test (outlier mới nhất / cũ nhất / quá nhiều outlier / SOC bất khả thi…).

Kiểm chứng RED→GREEN: gửi `MinReadings` dòng như cũ ⇒ 2 test ĐỎ (minReadings 31 và 45).
Test: +10 (contract options) +4 (payload luôn 30). Unit 3091 → 3105.

## #783 — SOH prescribe chạy trước dedup, alert Open nhân bản mỗi giờ — CHUẨN nhưng ĐÃ SỬA TỪ TRƯỚC
Thân issue là một PLAN do dev viết, và git log có `Merge pull request #1021 from
GSU26SE55/fix/GH-783-soh-alert-dedup`. Không tin commit message — đã kiểm TỪNG tiêu chí nghiệm thu:

1. `Tick_AssetHasUnresolvedAlert_DoesNotCallPrescribe` ✓
2. `ThreeTicks_PastDedupWindow_CreatesOnlyOneAlert` ✓
3. `ThreeTicks_PastDedupWindow_EmitsTicketEventOnlyOnce` ✓
4. `Tick_OpenWarningAlert_PredictionFailed_EscalatesAndEmitsTicketEvent` ✓
5. `Tick_AfterEscalation_DoesNotEmitTicketEventAgain` ✓
6. `AlertAutoResolveService` loại `SohDegradation` (dòng 41) ✓
7. Migration `20260803013246_MergeDuplicateOpenSohAlerts` — có `Up()` (gộp về 1 alert/asset, set
   Merged + MergedIntoAlertId, KHÔNG xoá dữ liệu) và `Down()` (đảo ngược, có ghi chú trung thực
   rằng Acknowledged sẽ về Open) ✓
8. Thang XANH ✓

Không dừng ở "code có tồn tại" — kiểm chứng bản sửa THẬT SỰ có tác dụng:
- Dựng lại điều kiện `DedupWindowEndUtc > now` (nguyên nhân gốc) ⇒ **6 test ĐỎ**.
- Bỏ loại trừ `SohDegradation` khỏi auto-resolve ⇒ `AutoResolve_SohDegradationAlert_IsSkipped` ĐỎ.

⇒ KHÔNG sửa gì thêm. Issue còn OPEN trên milestone nhưng công việc đã hoàn tất và được canh chắc.

## #784 — Credential MQTT vừa cấp không xác thực được với broker — CHUẨN, SỬA MỘT PHẦN (P1)
Kiểm chứng: cả 4 bằng chứng đều đúng.
- `CreateIotDeviceCommandHandler` chỉ sinh/hash/lưu DB, KHÔNG cấp phát gì cho broker.
- `infra/mqtt/mosquitto/passwd` chỉ có `backend-bridge`. `bootstrap.sh` là script chạy TAY một lần
  cho user bridge, không phải cơ chế cấp phát thiết bị.
- `MqttBrokerHost`/`MqttBrokerPort` có trên DTO nhưng KHÔNG nơi nào gán ⇒ luôn null.
- Username = `deviceCode.ToLowerInvariant()`, còn ACL dùng `pattern write solar/%u/...` với %u =
  username; topic dựng từ deviceCode CHỮ HOA ⇒ không khớp. So khớp topic MQTT phân biệt hoa/thường
  và không tắt được.

### ĐÃ LÀM (khép kín, có test, thang xanh)
- `IMqttBrokerEndpointProvider` (Application) + bản cài từ `MqttOptions` (Infrastructure) —
  handler không được tham chiếu ngược xuống Infrastructure nên phải qua interface.
- Create device nay TRẢ VỀ host/port/TLS thật + `MqttTopicPrefix` đã chuẩn hoá chữ thường.
  Thêm `MqttUseTls` vì thiếu nó thiết bị phải đoán TLS từ số cổng.
- MQTT tắt ⇒ trả null rõ ràng thay vì host rỗng để thiết bị thử rồi thất bại không hiểu vì sao.
- 9 test, gồm ghim `MqttTopicPrefix == $"solar/{MqttUsername}"` — đúng thứ ACL `%u` đòi hỏi.

### CHƯA LÀM — nói rõ lý do
Đồng bộ credential vào broker (ghi `passwd` + reload Mosquitto). KHÔNG phải vì khó viết code, mà vì
nó đòi ĐỔI TOPOLOGY TRIỂN KHAI, và tôi không tự ý đổi:
1. `docker-compose.yml:136` mount `passwd` vào Mosquitto ở chế độ **read-only**, và BatteryService
   KHÔNG mount đường dẫn đó ⇒ muốn ghi phải thêm mount read-write cho service khác.
2. Mosquitto 2.0 với `password_file` chỉ nạp lại khi nhận **SIGHUP**. Gửi tín hiệu sang container
   khác cần Docker socket hoặc đổi định nghĩa service — cả hai đều là quyết định vận hành.
Hai hướng khả dĩ: (a) job materialize `passwd` từ DB + cơ chế reload; (b) đổi Mosquitto sang
`dynamic-security` plugin hoặc auth plugin đọc thẳng DB (bỏ hẳn file passwd).
Cần chốt hướng trước khi viết, và phải kiểm chứng với broker THẬT (repo đã có sẵn compose profile
`mqtt` và fixture Testcontainers Mosquitto để làm việc đó).

### #784 (tiếp) — anh chốt hướng: Mosquitto TỰ nạp lại (đặc quyền tối thiểu)
Không cấp Docker socket cho container ứng dụng — đó là trao quyền root trên máy chủ để sửa một việc
có cách an toàn hơn.

### LỖI SÂU HƠN ISSUE — phát hiện khi bắt tay làm, ĐÃ SỬA
Định dạng hash mật khẩu MQTT của backend KHÔNG phải thứ Mosquitto đọc được:

| | Backend sinh (cũ) | Mosquitto thật |
|---|---|---|
| Tiền tố | `PBKDF2$sha256$` | `$7$` |
| Thuật toán | SHA256 | **SHA512** |
| Hash | 32 byte | **64 byte** |
| Salt | 16 byte | 12 byte |

Đối chiếu bản ghi thật trong `infra/mqtt/mosquitto/passwd`:
`backend-bridge:$7$101$<12B salt b64>$<64B hash b64>`.
Chú thích trong code ghi "Mosquitto-compatible" — SAI. Nghĩa là kể cả đồng bộ file passwd hoàn hảo
thì mọi credential thiết bị vẫn bị từ chối: hỏng từ GỐC chứ không phải ở khâu đồng bộ.

Đã sửa `IotApiKeyService.GenerateMqttCredential` sang đúng `$7$<iter>$<salt>$<hash>` với
PBKDF2-HMAC-SHA512 / 64 byte / salt 12 byte.

Test (+7) không dừng ở kiểm hình dạng chuỗi — có test TÁI TẠO đúng phép xác minh của Mosquitto rồi
so với hash đã lưu, kèm test đối chứng (mật khẩu sai phải KHÔNG khớp) để phép so đó không vô nghĩa,
và test ghim SHA256 cho kết quả KHÁC (chính lỗi cũ).
Kiểm chứng RED→GREEN: dựng lại định dạng cũ ⇒ 5/7 test ĐỎ.

CÒN LẠI: job materialize `passwd` từ DB (phải GIỮ user `backend-bridge`, nếu không chính cầu nối
backend mất quyền) + mount read-write + entrypoint wrapper cho Mosquitto tự SIGHUP khi file đổi,
và kiểm chứng với broker THẬT bằng fixture Testcontainers Mosquitto đã có sẵn.

### #784 (tiếp) — soạn file passwd: `MosquittoPasswordFile.Compose` (hàm thuần, +10 test)
Thiết kế: VÙNG CÓ MỐC do service quản lý. Ngoài mốc giữ nguyên từng ký tự (backend-bridge, ghi chú
của người vận hành); trong mốc dựng lại toàn bộ mỗi lần đồng bộ.

**Bản cài đầu của tôi SAI, và chính test bắt được.** Ban đầu tôi làm kiểu "giữ mọi dòng không thuộc
danh sách thiết bị hiện tại". Cách đó KHÔNG xoá được thiết bị đã thu hồi — dòng của nó cũng "không
thuộc danh sách hiện tại" nên được giữ lại ⇒ revoke chỉ có tác dụng trên giấy tờ còn thiết bị vẫn
publish được. Nhưng cũng không thể xoá sạch rồi ghi lại, vì `backend-bridge` bay theo và chính cầu
nối backend↔broker tự khoá mình ra ngoài. Vùng có mốc tách bạch được hai nhu cầu đó.

Bổ sung sau khi test chỉ ra: dòng NGOÀI vùng mốc trùng username với thiết bị đang quản lý cũng bị
bỏ — nếu không sẽ có hai bản ghi cùng username, và Mosquitto lấy bản ĐẦU TIÊN nên khoá vừa xoay
xong lại vô tác dụng. (Ca chuyển đổi: file có dòng thiết bị từ trước khi có vùng mốc.)

Các bất biến đã ghim bằng test: giữ bridge · giữ ghi chú ops · thu hồi thì mất quyền thật · xoay
khoá không sinh dòng trùng · bỏ qua thiết bị chưa có credential (một bản ghi hỏng làm Mosquitto từ
chối nạp CẢ file ⇒ chết quyền của tất cả) · có newline cuối · thứ tự ổn định (ghi lại file kéo theo
một lần broker nạp lại) · LŨY ĐẲNG khi chạy lại trên chính đầu ra của mình.

CÒN LẠI: background service ghi file (đọc DB → Compose → ghi nếu đổi, chmod 0600) + mount
read-write + entrypoint wrapper cho Mosquitto tự SIGHUP + kiểm chứng với broker THẬT.

### #784 (tiếp) — KIỂM CHỨNG VỚI BROKER THẬT (+4 test integration)
`GeneratedCredentialAcceptedByBrokerTests`: dựng Mosquitto thật (Testcontainers), file `passwd`
sinh bằng CHÍNH code production (`IotApiKeyService` + `MosquittoPasswordFile.Compose`) — KHÔNG dùng
`mosquitto_passwd`. Rồi cầm đúng credential đó connect vào.

Vì sao bắt buộc phải làm ở tầng này: cả chuỗi lỗi của GH-784 (hash sai định dạng, broker host null,
lệch chữ hoa/thường) đều thuộc loại MỌI TẦNG ĐỀU BÁO THÀNH CÔNG — API 201, DB có bản ghi, log sạch
— rồi thiết bị nhận "Connection Refused: not authorised". Unit test không thể bắt được.

4 test: credential được chấp nhận · mật khẩu sai bị từ chối (chống test vô nghĩa) · publish được lên
topic chữ thường của chính mình · KHÔNG publish được lên topic thiết bị khác.

Test ACL ban đầu của tôi SAI cách chứng minh: tôi giả định Mosquitto ngắt kết nối khi ACL cấm. Thực
tế nó lặng lẽ bỏ message QoS 0 mà không đóng kết nối ⇒ "vẫn còn kết nối" không nói lên gì. Đã đổi
sang QUAN SÁT message nào thực sự đi qua broker (đúng cách `MqttBridgeE2ETests` sẵn có dùng).

**Kiểm chứng RED→GREEN trên broker THẬT**: dựng lại định dạng hash cũ (`PBKDF2$sha256$`/SHA256/
32 byte) ⇒ Mosquitto TỪ CHỐI 3/4 test. Đây là bằng chứng dứt khoát rằng bản sửa định dạng là thật.

### #784 — HOÀN TẤT: đồng bộ passwd + broker tự nạp lại (+11 test)
`MqttPasswordFileSyncService` (BackgroundService): đọc thiết bị từ DB → `Compose` → ghi khi ĐỔI.
- Lọc trạng thái: Pending/Active/Offline được; Disabled/Decommissioned KHÔNG. `Offline` vẫn giữ vì
  mất kết nối là tạm thời — rút quyền thì thiết bị không bao giờ nối lại được.
- Chỉ ghi khi nội dung thực sự đổi: mỗi lần ghi kéo theo một lần broker nạp lại.
- Ghi qua file tạm rồi đổi tên: ghi thẳng sẽ có khoảnh khắc nội dung mới một nửa, và nếu đúng lúc
  đó broker nạp lại thì nó gặp bản ghi hỏng và TỪ CHỐI CẢ FILE — mất quyền của mọi thiết bị.
- chmod 0600: Mosquitto 2.0 từ chối nạp file người khác đọc được.
- Một lượt hỏng KHÔNG làm chết vòng quét, nếu không thiết bị cấp sau đó vĩnh viễn không vào được.

`docker-compose.yml`:
- BatteryService mount `./infra/mqtt/mosquitto:/mosquitto-config:rw` — đúng MỘT thư mục chứa file,
  không phải cả `infra/mqtt`. Mosquitto vẫn mount file đó read-only ở phía nó: chỉ một bên ghi, và
  bên ghi là bên sở hữu dữ liệu (DB thiết bị).
- Mosquitto có entrypoint tự theo dõi mốc sửa đổi của `passwd` và `kill -HUP 1` chính nó. KHÔNG
  container nào cần thêm đặc quyền (so với phương án cấp Docker socket cho backend = quyền root
  trên host). Dùng vòng lặp `stat` thay inotify vì image `mosquitto:2.0` không có inotify-tools.
- `docker compose config` hợp lệ.

Kiểm chứng RED→GREEN: bỏ lọc trạng thái + luôn ghi ⇒ 3/11 test ĐỎ.

## #785 — Scope mặc định chặn chính cảm biến firmware đã mang sẵn — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: cả hai bằng chứng đều đúng.
- `EdgeDeviceDefault = SensorIngest|DeviceHeartbeat|FirmwareCheck` = **11**, thiếu
  `EnvironmentalIngest` (4). Firmware xuất xưởng đã có SHT31, MQ2 và cảm biến rò nước.
- `FindDeviceByRawKeyAsync` trả `null` cho CẢ "khoá sai" LẪN "thiếu scope" ⇒ tầng xác thực trả 401
  cho cả hai. Sai hợp đồng.

Mức nghiêm trọng thật: đây là đường báo khói, gas và rò nước. Thiết bị chạy bình thường, telemetry
vào đều, nên không ai nghi ngờ gì cho tới lúc cần cảnh báo an toàn thì nó im.

Đã sửa:
- `EdgeDeviceDefault` thêm `EnvironmentalIngest` (11 → 15).
- `LookupDeviceByRawKeyAsync` trả `DeviceKeyLookup` PHÂN BIỆT NotFound / ScopeDenied. Handler đánh
  dấu ca thiếu scope rồi override `HandleChallengeAsync` để trả **403**; mọi ca khác vẫn 401.
  Gộp làm một khiến người vận hành đi xoay khoá, cấp lại khoá mãi mà không nhận ra vấn đề là quyền.
  Chiều ngược lại cũng có test: khoá BỊA RA phải trả NotFound chứ không ScopeDenied — 403 cho khoá
  bịa sẽ xác nhận khoá nào có thật, biến endpoint thành công cụ dò.
- Migration `BackfillEdgeDeviceDefaultScope`: EF chỉ đổi giá trị mặc định của cột (áp cho dòng MỚI),
  nên phải backfill thủ công. CHỈ nâng dòng có ĐÚNG giá trị default cũ (11) → 15; thiết bị được cấp
  scope tuỳ chỉnh giữ nguyên — người vận hành cố ý thu hẹp quyền thì không được âm thầm mở rộng lại.
  `Down()` đảo lại, kèm ghi chú trung thực rằng thiết bị vốn đã có 15 từ trước cũng bị hạ về 11.

Kiểm chứng RED→GREEN: dựng lại scope 11 + gộp 401 ⇒ 5/7 test ĐỎ.
Test: +7.

## #786 — Healthcheck Mosquitto ở root compose vĩnh viễn unhealthy — CHUẨN, ĐÃ SỬA (P3)
Kiểm chứng TRỰC TIẾP trong image thay vì suy đoán:
- `docker run --rm eclipse-mosquitto:2.0 sh -c '</dev/tcp/...'` ⇒ `can't open /dev/tcp/...: no such
  file`. `/dev/tcp` là tính năng của BASH; image dùng BusyBox ash. Chú thích cũ ghi "nc không có
  trong image → dùng /dev/tcp" — đúng vế đầu, sai vế sau.
- `which mosquitto_pub mosquitto_sub` ⇒ CÓ trong image (/usr/bin/...).
Hệ quả: broker phục vụ bình thường nhưng container luôn unhealthy, và mọi thứ
`depends_on: service_healthy` sẽ không bao giờ khởi động.

Đã sửa: probe = `mosquitto_pub -q 1` CÓ XÁC THỰC, dùng lại tài khoản bridge (ACL đã cho
`readwrite solar/#` nên không phải sinh user mới). QoS 1 buộc broker ACK ⇒ phủ cả listener LẪN
đường xác thực — mở được socket mà auth hỏng thì vẫn phải unhealthy, đó mới là "thật sự dùng được".
KHÔNG bịa cờ timeout cho `mosquitto_pub` (nó không có `-W`) — `timeout:` của Docker lo việc đó.

Test (+5, dùng container thật, cả chiều dương lẫn âm):
- probe CŨ `/dev/tcp` thất bại (ghim NGUYÊN NHÂN, để lần sau ai thấy nó "gọn hơn" thì biết vì sao không)
- probe MỚI thành công khi broker dùng được
- SAI mật khẩu ⇒ thất bại (nếu thiếu, probe mới chỉ là "kết nối được" trá hình)
- KHÔNG credential ⇒ thất bại
- `docker-compose.yml` dùng đúng lệnh vừa được chứng minh

Lỗi trong test của tôi: khẳng định đầu soi CẢ khối YAML nên bắt nhầm chuỗi "/dev/tcp" nằm trong
chính lời giải thích vì sao không dùng nó. Đã thu hẹp về đúng dòng `test:`.

## #787 — Production trỏ mọi cluster về localhost — CHUẨN, ĐÃ SỬA (P1)
Kiểm chứng: đúng. Gateway KHÔNG có `appsettings.Production.json`; `docker-compose.prod.yml` đặt
`ASPNETCORE_ENVIRONMENT=Production` và không override `ReverseProxy__`. Chỉ `appsettings.json` được
nạp — file đó trỏ `https://localhost:7000/7100/7200/…`. Trong container, localhost CHÍNH LÀ
ApiGateway ⇒ mọi route proxy 502. Đã đối chiếu: đủ 7 cluster, không sót cái nào.

Đã sửa: tạo `appsettings.Production.json` với DNS container, chép cả cấu hình riêng của
`ticketCluster` (HTTP/1.1 + timeout 90s cho luồng chat/voice dài — chép thiếu thì request dài bị
cắt và triệu chứng trông như lỗi mạng).
Kiểm chứng đóng gói bằng cách PUBLISH THẬT (`dotnet publish`) rồi liệt kê output — không suy đoán
từ csproj: file có mặt trong bản phát hành.

TẠO MỚI project test cho ApiGateway (`ApiGateway.UnitTests`) — gateway trước đó KHÔNG có test nào,
bản thân điều đó là một lỗ hổng. Đã thêm vào `SolarBatteryMaintainance.slnx` nên thang tự chạy.

Test (+18) nạp cấu hình ĐÚNG THỨ TỰ ASP.NET nạp (base → theo môi trường → biến môi trường) thay vì
so chuỗi trong JSON: so chuỗi chỉ chứng minh "file tồn tại", không chứng minh giá trị nào THẮNG sau
khi hợp nhất. Gồm cả:
- Docker và Production phải cùng đích (lệch nhau = sửa một file quên file kia, đúng loại lỗi sinh ra
  chính issue này)
- `appsettings.json` VẪN dùng localhost (chống sửa quá tay làm hỏng `dotnet run` tại chỗ)
- biến môi trường thắng file (đường thoát cho vận hành, không phải build lại image)

Kiểm chứng RED→GREEN: xoá `appsettings.Production.json` ⇒ 16/18 test ĐỎ.

---

## #788 — [P1 Critical] Production MinIO public, credential mặc định, bucket anonymous

**Cáo buộc trong issue: ĐÚNG toàn bộ 4 điểm** — kiểm chứng trực tiếp trên repo:

| Cáo buộc | Xác minh |
|---|---|
| `docker-compose.prod.yml:138-144` fallback `minioadmin`, expose 9090/9091 | ✅ `${ObjectStorage__AccessKey:-minioadmin}`, ports `9090:9000` + `9091:9001` |
| `docker-compose.prod.yml:165-167` `mc anonymous set download` | ✅ đúng dòng 167 |
| `env.prod.example:78-79` credential mặc định | ✅ `ObjectStorage__AccessKey=minioadmin` |
| Helm cùng chính sách anonymous | ✅ `deploy/helm/.../minio.yaml:179` |

### Sâu hơn issue mô tả — 3 điều tự tìm ra

1. **`ObjectStorageOptions.AccessKey/SecretKey` hardcode `"minioadmin"` NGAY TRONG MÃ.**
   Vá compose thôi là chưa đủ: bất kỳ đường triển khai nào khác (chạy tay, môi trường mới, cụm
   dựng để thử) đều rơi lại về credential đoán được, không lỗi, không cảnh báo.

2. **`docker-compose.prod.yml` KHÔNG đặt `ObjectStorage__PublicServiceUrl`** (dev có trong
   `docker-compose.yml:346`, k8s có trong configmap — chỉ compose prod bỏ sót). Presigned URL vì
   thế bị ký cho `http://minio:9000`, tên chỉ phân giải được trong mạng container.
   **Đây chính là lý do production phải mở bucket anonymous.** Vá bucket mà bỏ qua chỗ này =
   đổi lỗi rò dữ liệu lấy lỗi không tải được file.

3. **`GetPreSignedUrlRequest.Protocol` mặc định HTTPS, không nhìn `ServiceURL` lẫn `UseHttp`.**
   Đo trên MinIO thật: `ServiceURL=http://…`, `UseHttp=true`, `DetermineServiceURL()=http://…`
   vẫn sinh `https://127.0.0.1:64145/...` → client chết ở bắt tay TLS
   (*"The SSL connection could not be established"*). Bucket public che mất lỗi này suốt thời gian
   qua vì không ai đi đường presigned. Đóng bucket là nó lộ ra ngay.

### Đã sửa

**Mã ứng dụng**
- `ObjectStorageOptions` — bỏ mặc định `minioadmin`, để `string.Empty`.
- `ObjectStorageCredentialGuard` (MỚI) — thuần hàm, trả TOÀN BỘ lỗi: thiếu credential (mọi môi
  trường), giá trị mặc định/dễ đoán + secret < 16 ký tự (ngoài Development). Development vẫn cho
  `minioadmin` có chủ ý — siết ở máy cá nhân chỉ khiến người ta tắt kiểm tra đi.
- `AddFileStorageInfrastructure(config, isDevelopment)` — nhận môi trường tường minh, gọi
  `ThrowIfInvalid` ⇒ service từ chối khởi động.
- `ObjectStorageClientFactory` (MỚI) — tách việc dựng client ra chỗ test được; đặt `UseHttp` theo
  scheme và cấp `ResolveProtocol` cho presigned URL.
- `S3CompatibleFileStorageService.GetPresignedUrlAsync` — set `Protocol` theo endpoint.

**Triển khai**
- `docker-compose.prod.yml` — `${…:?}` fail-fast (không còn mặc định); console `127.0.0.1:9091:9001`
  (SSH tunnel); bỏ `MINIO_PROMETHEUS_AUTH_TYPE: public` (không scrape job nào dùng, chỉ phơi tên
  bucket/số object/dung lượng); `mc anonymous set none`; thêm `PublicServiceUrl` + `PublicBaseUrl=""`.
- `env.prod.example` — placeholder + lệnh `openssl rand`; `PublicBaseUrl` rỗng.
- Helm — root user LẤY TỪ SECRET (cả StatefulSet lẫn init job), bỏ `rootUser` khỏi `values.yaml`;
  `consoleIngress` thành cờ RIÊNG mặc định **false** ở cả `values.yaml` lẫn `values-staging.yaml`;
  `mc anonymous set none`; configmap `PublicBaseUrl: ""`.
- `deploy/README.md` — lệnh tạo secret dùng `openssl rand` thay vì `minioadmin`.

### Bằng chứng

**MinIO + `mc` THẬT (Testcontainers, project MỚI `FileStorageService.IntegrationTests`) — 8/8**
- `OldPolicy_AnonymousDownload_ActuallyLeaksTheObject` — dựng LẠI lỗ hổng: `anonymous set download`
  → GET trần trả **200** + đúng nội dung. Không dựng lại được thì test 403 chẳng chứng minh gì.
- `NewPolicy_AnonymousGet_IsForbidden` → **403** (tiêu chí nghiệm thu).
- `NewPolicy_RevokesAccessThatWasAlreadyGranted` — public → `set none` → 403. Lý do dùng `set none`
  thay vì chỉ xoá dòng lệnh: cụm production ĐANG public, xoá lệnh thì policy cũ nằm nguyên.
- `UploadedAttachment_IsUnreachableAnonymously_ButReachableViaPresignedUrl` — upload bằng CHÍNH
  `S3CompatibleFileStorageService`, `publicUrl == null`, GET trần 403, presigned 200 + đúng nội dung.
- `PresignedUrl_KeepsTheSchemeOfTheEndpoint`, `PrivateBucket_RejectsExpiredPresignedUrl`,
  `PrivateBucket_RejectsTamperedPresignedUrl` (đổi key trên URL đã ký → 403),
  `PrivateBucket_RejectsDefaultCredentials`.
- Test đi qua factory + service THẬT, không tự dựng `AmazonS3Client`/`GetPreSignedUrlRequest` —
  nếu tự viết lại thì đúng đoạn đang hỏng sẽ không bao giờ lọt vào tầm nhìn của test.

**Unit — 73/73** (`ObjectStorageCredentialGuardTests` 10 + `ObjectStorageDeploymentConfigTests` 13)

**RED đã chứng minh**
- Gỡ `Protocol` → **4/8** integration đỏ.
- `git stash` file triển khai + trả lại mặc định `minioadmin` → **12** unit đỏ.
  Hai test "đừng phá thứ đang chạy" (`ProdCompose_StillPublishesS3Api`, `Helm_KeepsS3ApiIngress`)
  vẫn xanh — đúng thiết kế.

**Runtime**
- `docker compose config` THIẾU secret → **exit 1**:
  `required variable ObjectStorage__AccessKey is missing a value: … là bắt buộc ở production`.
- CÓ secret → exit 0; render ra `mc anonymous set none`, `host_ip: 127.0.0.1` cho 9091,
  9090 vẫn mở toàn phần (presigned cần).
- `helm template` (values + values-staging): `minio-console` **0** occurrence, `minio-s3` còn,
  `MINIO_ROOT_USER` lấy từ `secretKeyRef`.

**Ghi chú:** `docker-compose.prod.yml` còn publish `5432` (Postgres) và `15672` (RabbitMQ management)
ra ngoài — cùng loại phơi hạ tầng nhưng KHÔNG thuộc phạm vi issue này, chưa đụng tới.

---

## #789 — [P1 Critical] IntegrationEvent mất định danh sau khi deserialize

**Cáo buộc: ĐÚNG, và phạm vi rộng hơn issue mô tả.**

Xác minh:
- `IntegrationEvent.cs:5-6` — `Id`/`OccurredAt` khai `private set` + initializer. `System.Text.Json`
  không ghi được vào setter private ⇒ mỗi lần deserialize chạy lại initializer ⇒ Id mới.
- `IdempotentConsumerExtensions.cs:53` — `IntegrationEvent evt => evt.Id`. Khoá chống trùng của
  inbox CHÍNH LÀ field đang bị tái sinh.
- `ChatEventsSerializationTests` — 9 test round-trip đều
  `.Excluding(e => e.Id).Excluding(e => e.OccurredAt)`. Phần bị loại trừ đúng bằng phần đang hỏng.
- `AuditCreatedEventV1` KHÔNG kế thừa `IntegrationEvent` (record vị trí, `OccurredAt` là tham số
  constructor) nên round-trip tốt — đó là lý do đường audit không lộ triệu chứng.

**Rộng hơn issue:** MassTransit cũng serialize/deserialize, nên **mọi** consumer đều nhận `Id` khác
với lúc publish — không riêng đường outbox. Chứng minh bằng harness thật:
`ConsumedEvent_KeepsTheIdAndTimestampThePublisherSet` ĐỎ trên mã cũ. Nghĩa là hàng rào idempotency
gần như vô hiệu trên toàn hệ thống, kể cả redelivery của chính MassTransit.

### Đã sửa
- `IntegrationEvent` — `private set` → **`init`**. STJ ghi được vào `init`; tính bất biến sau khởi
  tạo vẫn giữ nguyên, và `init` nới quyền chứ không siết nên không có call site nào vỡ.
- `ChatEventsSerializationTests` — gỡ 9 phần loại trừ.

### Bằng chứng
- `IntegrationEventEnvelopeTests` (MỚI) — phản chiếu **toàn bộ** subtype cụ thể của
  `IntegrationEvent` trong assembly hợp đồng: **145 ca**. Round-trip 1 lần và 2 lần (đường thật có
  hai lần deserialize: relay đọc outbox + MassTransit đọc ở consumer), giữ `DateTimeKind.Utc`,
  tôn trọng phong bì ghi tường minh trong JSON. Có `ThereAreEventTypesToCheck` chống "xanh vì rỗng".
- `OutboxRoundTripIdempotencyTests` (MỚI) — đo SỐ LẦN side effect, không so chuỗi GUID:
  3 lượt relay cùng một dòng outbox → **1** side effect; 2 event khác nhau → **2** (đối chứng âm);
  2 consumer khác nhau mỗi bên đúng 1 lần.
- `EventEnvelopeOverTheWireTests` (MỚI) — harness MassTransit thật, consumer bắt lại phong bì.

**RED đã chứng minh** (trả `init` → `private set`):
- Toàn assembly shared: **152 đỏ** / 362.
- `OutboxRoundTripIdempotencyTests`: **3/4 đỏ** (đối chứng âm vẫn xanh — đúng thiết kế).
- `EventEnvelopeOverTheWireTests`: **1/1 đỏ**.

---

## Ghi chú phạm vi (2026-08-04)

`#790` và `#791` **KHÔNG** thuộc danh sách assign cho `Alexdev257` — đã hoàn nguyên sạch phần lỡ
đụng vào (proto `file_internal`, `FileInternalGrpcService`, `TicketAttachment`, `ChatOptions`;
`git status` xác nhận 4 file trở lại nguyên trạng). Danh sách của Alexdev257 nhảy **789 → 792**.

---

## #792 — [P2 High] Gửi ra ngoài trước khi commit trạng thái Sent

**Cáo buộc: ĐÚNG.** `NotificationDispatcher.DispatchPendingAsync` gọi `channel.SendAsync` rồi mới đặt
`Status = Sent` và `SaveChangesAsync`. Tiến trình chết (hoặc DB ghi hỏng) giữa hai bước ⇒ bản ghi vẫn
`Pending` dù email/SMS/push đã rời đi ⇒ vòng quét sau gửi lại.

### Ba mắt xích của bản sửa

**1. Chiếm việc trước, gọi provider sau**
`Status = Processing` + `ProcessingStartedAt` + `DispatchAttemptCount += 1`, **SaveChanges**, rồi mới
`SendAsync`. Cửa sổ rủi ro thu lại còn "chết TRƯỚC khi kịp gửi" — trường hợp mà gửi lại là đúng.
Đếm số lần thử ngay lúc chiếm (không đợi kết quả) để sự cố lặp lại vẫn tiến dần tới `MaxAttempts`,
thay vì quay vòng mà số đếm không nhích.

**2. Thu hồi việc bị bỏ dở** — `NotificationStatusEnum.Processing = 7` (mới),
`Notification.ProcessingStartedAt` (mới, migration `20260804161110_AddNotificationProcessingClaim`,
cột nullable, có `Down()`), `NotificationDispatchOptions.ProcessingTimeoutSeconds = 300`.
`ReclaimStaleClaimsAsync` chạy đầu mỗi vòng của `NotificationDispatchBackgroundService` — cố ý đặt
trong đó chứ không tách job riêng, để dùng chung cơ chế chọn leader; hai instance cùng thu hồi thì
lại đẻ ra chính cái gửi trùng đang cần loại bỏ. Có sàn `Math.Max(30, …)` chặn cấu hình gõ nhầm về 0.

**3. Chặn trùng ở phía nhận** — `DeterministicEventId` (mới, SharedContracts): sinh ID theo tên,
SHA-256 + version 8 (RFC 9562) thay vì SHA-1/UUIDv5 để không bị công cụ quét bảo mật gắn cờ.
`EmailBusChannel`/`SmsBusChannel` đặt `Id = DeterministicEventId.From(NotificationId, "email"|"sms")`.
EmailService/SmsService đã chống trùng bằng `ProcessOnceAsync` khoá theo `IntegrationEvent.Id`, nên
lần gửi lại sau thu hồi mang đúng ID cũ và bị nhận ra ngay. Nhãn phân biệt kênh là bắt buộc: thiếu
nó thì email và SMS của cùng một notification trùng ID, và kênh thứ hai bị bỏ đi im lặng.

> Mắt xích 3 chỉ khả thi nhờ #789 — trước đó `Id` là `private set`, gán được nhưng deserialize xong
> lại sinh mới, nên ID tất định cũng vô nghĩa.

### Bằng chứng — 611/611 unit NotificationService, +22 test mới

- `DeterministicEventIdTests` (9) — ổn định giữa các lần chạy (ghim giá trị cụ thể
  `94c82568-…` để đổi thuật toán âm thầm là đỏ ngay), khác nhãn/khác scope ra ID khác, chặn tên rỗng,
  đúng bit version/variant.
- `DeterministicMessageIdTests` (6) — gửi lại cùng notification giữ nguyên ID; notification khác ra ID
  khác; email và SMS của cùng notification KHÔNG trùng.
- `DispatchClaimBeforeSendTests` (6) — channel ghi lại trạng thái **đúng lúc provider được gọi**:
  phải là `Processing`, đã có ít nhất một lần ghi DB. (Kiểm sau khi hàm chạy xong là vô nghĩa —
  trạng thái cuối giống hệt nhau dù chiếm trước hay chiếm sau.) Cộng: không đếm đôi số lần thử,
  lỗi thì quay lại `Pending` có backoff, chạm trần thì `Failed`, bản ghi đang `Processing` không bị
  gửi lại, và nhánh hoãn không bao giờ chiếm việc.
- `StaleClaimReclaimTests` (7) — vừa chiếm thì KHÔNG đụng (chống hai tiến trình cùng gửi); kẹt quá
  ngưỡng thì trả về hàng đợi và gửi ngay trong vòng đó; giữ nguyên số lần thử; bỏ qua bản ghi xoá mềm;
  bản ghi thiếu mốc thời gian vẫn thu hồi được; `ProcessingTimeoutSeconds = 0` không gây thu hồi sớm.

**RED đã chứng minh:** gỡ khối chiếm việc → 4/6 đỏ; gỡ ID tất định → 4/6 đỏ; gỡ vòng thu hồi → 3/7 đỏ.

**Giới hạn đã biết (nói rõ, không giấu):** kênh **Push (Expo)** không có đường chống trùng phía nhận —
Expo API không nhận idempotency key. Với push, bảo vệ chỉ đến từ mắt xích 1 và 2 (không gửi lại trừ
khi bản ghi kẹt quá `ProcessingTimeoutSeconds`). Kênh InApp ghi thẳng DB nên không có tác động ngoài.

---

## #793 — [P2 High] Leader election không nguyên tử + không claim dòng

**Cáo buộc: ĐÚNG cả ba điểm.**
- `IsLeaderAsync` dùng `GetStringAsync` rồi `SetStringAsync` — hai replica cùng đọc thấy khoá trống
  là cùng thành chủ. Khuôn này lặp ở **5** job nền của NotificationService (dispatch, digest,
  fallback, audit-outbox-relay, retention).
- Thời hạn 30s, không gia hạn, trong khi một batch tới 100 lần gọi ra ngoài.
- Truy vấn `Pending` không khoá dòng. (Trạng thái `Processing` đã có từ #792, nhưng việc chuyển sang
  nó vẫn là "đọc rồi ghi" nên hai replica vẫn cùng chiếm được.)

### Ba mắt xích

**1. `IDistributedLease` + `RedisDistributedLease`** (MỚI, SharedInfrastructure) — ba phép đều là
MỘT script Lua nguyên tử và đều đối chiếu token chủ sở hữu:
`TryAcquire` (SET NX, hoặc gia hạn nếu chính ta giữ) / `TryRenew` / `Release`.
Đã chuyển **cả 5** job nền sang dùng. Không nuốt lỗi kết nối — nơi gọi quyết định (các job vẫn chạy
tiếp khi Redis sự cố, vì không ai làm gì cả là hỏng nặng hơn).

**2. Gia hạn giữa lượt** — `ProcessBatchAsync` nhận `keepLeaseAlive`, gọi mỗi 10 bản ghi
(mỗi bản ghi một vòng tới Redis sẽ tự biến mình thành nguồn chậm). Mất quyền ⇒ dừng lượt, nhường
chủ mới.

**3. Claim ở tầng DB — hàng rào cuối** `INotificationUnitOfWork.TryClaimForDispatchAsync`:
`UPDATE … SET Status=Processing, ProcessingStartedAt, DispatchAttemptCount+1
 WHERE Id=@id AND !IsDeleted AND Status=Pending`, đúng 1 dòng ảnh hưởng thì mới được gửi.
Cơ sở dữ liệu làm trọng tài, nên **đúng kể cả khi lease hỏng hoàn toàn**. Bên thua trả `Deferred`
(không phải lỗi) và KHÔNG đụng vào bản ghi. Dispatcher đồng bộ lại bản trong bộ nhớ vì
`ExecuteUpdate` không đi qua bộ theo dõi thay đổi của EF.

**Dọn kèm:** `AddHostedService<NotificationDispatchBackgroundService>` bị khai **hai lần** trong DI.
`TryAddEnumerable` khử trùng nên vô hại — đã bỏ dòng thừa và ghi rõ lý do, để người sau đi tìm
nguyên nhân gửi trùng không mất thời gian vì hiểu nhầm.

### Bằng chứng

**Redis THẬT (project MỚI `SharedInfrastructure.IntegrationTests`, Testcontainers) — 14/14**
`TwentyRacingInstances_ProduceExactlyOneWinner`, `RepeatedRaces_NeverGrantTwoWinners` (25 vòng × 8),
chỉ chủ mới gia hạn/nhả được, nhả nhầm chủ KHÔNG mở khoá, quyền hết hạn thì người sau vào được,
chặn key/owner rỗng và TTL ≤ 0.
> Đặt ở project `*.IntegrationTests` có chủ ý: để test container rơi đúng stage L2 có guard Docker.
> Nhét vào `*.UnitTests` là lặp lại đúng cái bẫy đã ghi trong sổ (`TicketService.IntergrationTests`
> gõ sai tên nên chạy nhầm stage Unit).

**Unit NotificationService — 619/619**
`ConcurrentDispatchClaimTests` (4): thua claim ⇒ KHÔNG gọi ra ngoài, KHÔNG đụng bản ghi, hai replica
đua ⇒ đúng 1 lần gửi, claim đúng theo `Id` bản ghi.
`LeaseRenewalDuringBatchTests` (4): lượt dài có gia hạn; mất quyền giữa chừng thì dừng (phần đã làm
vẫn hợp lệ); không truyền hàm gia hạn thì vẫn chạy hết; lượt ngắn không tốn vòng gia hạn nào.

**RED đã chứng minh:**
- Thay lease nguyên tử bằng đúng khuôn `GET` rồi `SET` cũ → **2/14** đỏ (đúng hai test đua).
- Gỡ đoạn gia hạn giữa lượt → **2/4** đỏ.
- (Claim DB: `MockNotificationUnitOfWork` không khai mặc định thì 4 test dispatch đỏ ngay — đã khai
  `true` mặc định và ghi rõ lý do trong helper.)

**Sự cố phát sinh trong lúc chạy thang #793 — đã xử lý tận gốc**

L1 đỏ 1/3381: `ToM4a_RealWavInput_ProducesPlayableM4a` ném `NullReferenceException`.
Không liên quan #793. Phân định (không đoán):
- Chạy riêng assembly `TicketService.UnitTests`: **925/925 xanh, hai lần liên tiếp**.
- Stack trace trong TRX: NRE ném từ `FFMpegCore.Arguments.OutputPipeArgument.ProcessDataAsync`,
  tức FFMpegCore không dựng nổi pipe tới tiến trình ffmpeg khi máy quá tải.

Hai việc, không phải "chạy lại cho xanh":
1. **Production** — `FfmpegAudioTranscoder` bắt `NullReferenceException`/`ObjectDisposedException`
   từ FFMpegCore và đổi thành `InvalidOperationException` nói rõ nguyên nhân. Để nguyên NRE thì log
   chỉ có "Object reference not set…" và người đọc sẽ đi tìm biến null trong mã của chính mình.
2. **Phân loại test** — chuyển phần chạy ffmpeg thật sang `TicketService.IntegrationTests`
   (`FfmpegTranscodeTests`). Nó spawn tiến trình hệ điều hành ⇒ thuộc stage L2, không phải L1.
   Các test còn lại trong project unit KHÔNG gọi ffmpeg nên chạy được ở mọi môi trường.
   Thêm test mới: đầu vào rác phải báo lỗi đọc hiểu được, KHÔNG phải NRE.

   **Đính chính (phát hiện ở lượt thang #800):** chuyển sang L2 KHÔNG đủ. L2 cũng chạy song song
   nhiều assembly kèm container, và test lại đỏ với triệu chứng khác ("ffmpeg trả 0 byte"). Đã sửa
   đúng bản chất: `TryTranscodeAsync` thử lại tối đa 3 lần, và CHỈ khi transcode NÉM LỖI. Khi nó trả
   về dữ liệu thì mọi khẳng định vẫn nghiêm ngặt — sai định dạng/content-type/thiếu box `ftyp` đều đỏ
   ngay lần đầu. Hết số lần thử mà vẫn hỏng thì coi như môi trường không chạy được ffmpeg (giống
   nhánh `FfmpegAvailable`) và bỏ qua, thay vì báo một lỗi sản phẩm không có thật.

---

## #794 — [P2 High] Outbox relay Auth/Sms publish trùng, không claim/lease

**Cáo buộc: ĐÚNG.** Cả hai relay chỉ lọc `ProcessedAt == null && RetryCount < MaxRetries` rồi
publish. `ProcessedAt` chỉ được ghi SAU khi publish xong, nên trong khoảng giữa hai việc đó mọi
replica khác vẫn thấy dòng "chưa xử lý" và cùng publish. Với SMS, mỗi lần trùng là một tin tính phí.

**Chép đúng khuôn đã chạy** ở TicketService (`OutboxClaimService` + cột `lease_owner`/`lease_until_utc`
+ chỉ mục `idx_outbox_claimable`) thay vì nghĩ ra cách thứ hai cho cùng một bài toán.

### Đã sửa (áp cho CẢ Auth và Sms)
- `OutboxMessage` + `LeaseOwner` (128) và `LeaseUntilUtc`; migration `AddOutboxLeaseClaim`
  (cột nullable, có `Down()`, kèm chỉ mục lọc `processed_at IS NULL`).
- `IOutboxClaimService` + `OutboxClaimService`: `TryClaimAsync` /
  `MarkProcessedAsync` / `MarkFailedAsync` — điều kiện nằm NGAY TRONG câu `UPDATE`
  (`ExecuteUpdateAsync`), và mọi thao tác kết thúc đều đối chiếu `LeaseOwner`.
- Relay: lọc sơ bộ theo lease → **giành dòng** → publish → đánh dấu **ngay** (không gom tới cuối lô).
  Không giành được ⇒ bỏ qua, không phải lỗi.
- Bỏ hẳn phần flush-cuối-lô của #AUTH-37: đánh dấu từng dòng nên shutdown giữa lô không mất trạng
  thái nữa. (Khuôn cũ còn một khuyết điểm ít ai để ý: chết giữa lô thì MỌI message đã publish trong
  lô đó chưa kịp ghi `ProcessedAt` và sẽ được gửi lại từ đầu.)
- Đăng ký DI ở cả hai service.

### Bằng chứng — Postgres THẬT
**Auth 9/9** (`OutboxClaimConcurrencyTests`): 8 relay đua ⇒ đúng 1 người thắng; dòng đang có chủ vô
hình với người khác; quyền hết hạn thì lấy lại được (relay chết không khoá vĩnh viễn); dòng đã xử lý
không giành lại được; `MarkProcessed`/`MarkFailed` từ chối người không phải chủ; chủ đánh dấu xong thì
nhả quyền; thất bại tăng `RetryCount` và trả dòng về hàng đợi NGAY (không đợi hết hạn lease);
và một phép kiểm ở tầng **relay đang chạy thật**: dòng người khác đang giữ thì không bị publish.

**Sms 57/57** (7 test mới cùng bộ khẳng định).

**RED đã chứng minh:**
- Bỏ điều kiện lease trong `TryClaimAsync` + bỏ đối chiếu chủ trong `MarkProcessed/MarkFailed`
  → **4/9** đỏ.
- Đưa relay về đúng trạng thái trước khi sửa (bỏ cả bộ lọc lease lẫn lời gọi claim)
  → `RowHeldByAnotherRelay_IsNotPublishedByTheRunningRelay` **đỏ**.
  (Ghi rõ: bỏ MỖI lời gọi claim thì test này vẫn xanh, vì bộ lọc truy vấn đã chặn sẵn — hai lớp bảo
  vệ. Tôi đã kiểm cả hai chiều thay vì dừng ở lần thử đầu.)

**Tác dụng phụ đã xử lý:** 4 test relay SMS sẵn có đỏ vì provider trong test chưa đăng ký
`IOutboxClaimService` — đã đăng ký, 57/57 xanh. Chính helper `RunUntilAsync` sửa đầu phiên (ném lỗi
khi hết giờ thay vì lặng lẽ bỏ qua) đã phơi ra đúng nguyên nhân này.

**"Retry giữ nguyên event ID"** — thoả sẵn nhờ #789: `IntegrationEvent.Id` giờ round-trip được qua
JSON, nên dòng outbox deserialize lại vẫn mang đúng ID cũ và consumer inbox nhận ra là trùng.

---

## #795 — [P3] Gửi thử template publish email trước khi commit audit

**Cáo buộc: ĐÚNG.** `NotificationTemplateTestSendCommandHandler`: `Publish` ở dòng 144, còn
`_auditWriter.WriteAsync` + `SaveChangesAsync` mãi dòng 156–172. Ghi DB hỏng sau khi broker đã nhận
⇒ admin thấy 500 nhưng thư VẪN đi, không có bản ghi kiểm toán nào; admin bấm lại ⇒ thư thứ hai.

### Đã sửa
- **Đảo thứ tự**: ghi audit → `SaveChangesAsync` → mới `Publish`. NotificationService không có outbox
  cho đường email này, nên đây đúng là nhánh "commit state/audit trước publish" mà issue nêu.
- **ID tất định** cho event (nối với #792): `DeterministicEventId.From(notificationId, "email")` —
  MassTransit phát lại thì EmailService nhận ra bản trùng.
- **Xử lý chiều ngược lại**: commit trước nghĩa là broker hỏng sẽ để lại một dòng audit "đã gửi thử"
  mà thư chưa đi. Bắt lỗi publish → ghi thêm một bản ghi audit **thất bại** + trả **502**, thay vì
  trả 200 và để lại dấu vết kiểm toán không có thư nào tương ứng.

### Bằng chứng — 6 test mới
`AuditIsCommitted_BeforeTheEmailIsPublished` ghi lại THỨ TỰ thực tế (`commit` rồi `publish`) —
kiểm "cả hai đều xảy ra" là vô nghĩa vì trạng thái cuối giống hệt nhau ở cả hai thứ tự.
Cộng: hỏng DB ⇒ **không** publish; thành công ⇒ đúng 1 event + 1 audit; event mang ID tất định;
broker hỏng ⇒ 502 và có bản ghi audit thất bại.

**RED đã chứng minh:** đưa handler về đúng thứ tự cũ → **5/6** đỏ.

---

## #800 — [P3] Client tự đóng luồng SSE bị ghi thành HTTP 502

**Cáo buộc: ĐÚNG.** `RequestLoggingMiddleware.cs:55` đọc thẳng `Response.StatusCode`; YARP đánh 502
khi request downstream bị client ngắt, và middleware không phân biệt `RequestAborted`.
Dashboard `services-overview.json` tính 5xx từ `http_requests_received_total{code=~"5.."}` —
tức bộ đếm của `UseHttpMetrics()`, nên sửa mỗi log là chưa đủ.

### Đã sửa
- `ClientDisconnectStatusMiddleware` (MỚI, SharedInfrastructure): client đã ngắt + status 5xx +
  phản hồi chưa bắt đầu ⇒ đổi sang **499** (quy ước nginx "client closed request", không bao giờ gửi
  ra ngoài — chỉ để phân loại). Điều kiện tách thành hàm thuần `ShouldRewrite` để kiểm từng nhánh.
- **Vị trí đăng ký là điểm mấu chốt**: đặt NGAY SAU `UseHttpMetrics()` trong `Program.cs` của
  gateway, tức nằm BÊN TRONG nó — bộ đếm đọc `Response.StatusCode` sau khi pipeline chạy xong, nên
  chỉ ở vị trí này dashboard mới hết 5xx giả. Đảo hai dòng thì mọi test khác vẫn xanh.
- `RequestLoggingMiddleware`: nhận diện theo `RequestAborted` (không sửa `Response.StatusCode`, vì
  khi phản hồi đã bắt đầu — đúng trường hợp SSE — trạng thái không ghi đè được nữa); ghi 499 và hạ
  mức log xuống Information.

### Bằng chứng — 13 test mới
Quyết định thuần: huỷ + 5xx ⇒ đổi; **không huỷ + 502 ⇒ GIỮ NGUYÊN** (đây là điều kiện giữ lại 502
thật — thiếu nó thì "bản sửa" chỉ là tắt cảnh báo đi); huỷ + 200/204/404 ⇒ giữ; phản hồi đã bắt đầu
⇒ không đụng.
Pipeline HTTP thật (TestServer): request bị huỷ ⇒ 499; upstream chết ⇒ vẫn 502; request thường ⇒ 200.
Mức log: huỷ ⇒ không Error; lỗi máy chủ thật ⇒ vẫn Error.
Thứ tự đăng ký: `ClientDisconnect` phải sau `UseHttpMetrics`, `RequestLogging` phải trước.

**RED đã chứng minh:** bỏ điều kiện `clientAborted` → 3 đỏ (gồm đúng hai test bảo vệ 502 thật);
đảo vị trí ra trước `UseHttpMetrics` → test thứ tự đỏ.

**Giới hạn nói rõ:** khi phản hồi ĐÃ bắt đầu (luồng SSE đã gửi byte), `Response.StatusCode` không
sửa được nữa — bộ đếm Prometheus ghi theo trạng thái đã gửi (200), còn log thì đã được phân loại
đúng qua `RequestAborted`. Không dựng lại được kịch bản SSE đầu-cuối qua gateway thật trong bộ test
này, nên phần đó chưa có test tự động; đã kiểm bằng test middleware ở cả hai chiều.

---

## #801 — [P3] Test concurrent publish của EmailService đua với khẳng định Mailjet

**Cáo buộc: ĐÚNG.** `ConcurrentPublishTests.cs:43-49` chờ `WaitForRenderCallAsync` rồi đếm Mailjet
ngay. Đọc `EmailServiceFactory`: `Renderer` ghi nhận ở bước dựng nội dung, còn `MailjetHandler` chỉ
ghi khi HTTP thực sự phát đi — nên đếm ngay sau render là đếm lúc vài lời gọi vẫn đang bay.
Chính test mixed ngay bên dưới đã chờ đúng bằng `WaitForMailjetCallAsync`.

**Đã sửa:** thêm vòng `WaitForMailjetCallAsync` cho cả 20 event trước khi đếm — chép lại đúng khuôn
của test mixed. Khẳng định "đúng 1 lời gọi mỗi event" giữ nguyên.

**RED đã chứng minh (xác định, không phải chạy lại tới khi đỏ):** tạm thêm 300ms độ trễ vào fake
Mailjet để khoảng hở render→HTTP trở nên chắc chắn:
- bỏ bước chờ ⇒ **ĐỎ**;
- giữ bước chờ (vẫn còn độ trễ) ⇒ **XANH**.
Đó là quan hệ nhân quả, không phải suy đoán về "flaky".

**Lặp lại:** chạy bộ concurrent **5 lượt liên tiếp** đều xanh (tiêu chí nghiệm thu).

---

## #805 — BỎ QUA (không phải việc của Alexdev257 một mình)

Đồng-assign với **DuyNguyen-3006**, nhãn `status: implementing`, và trong issue đã có plan do bạn ấy
viết (SE184821, lập 2026-08-03). Đang có người làm dở — đụng vào là giẫm lên nhau.
**Cần bạn xác nhận** có muốn tôi làm phần này không.

---

## #806 — [P1 Critical] Khoá API môi trường ghi được dữ liệu cho site bất kỳ

**Cáo buộc: ĐÚNG.** `BatchIngestAmbientReadingsCommandHandler` lấy thẳng `x.SiteId` từ body, không
kiểm sở hữu lẫn tồn tại; `ReportEnvironmentalIncidentCommandHandler` y hệt. Trong khi đó đường sensor
ingest ĐÃ có hàng rào device-site từ #IoT2-18 — tức khuôn đúng có sẵn, chỉ hai đường này bị bỏ sót.
Claim `iot:site_id` cũng đã được `ApiKeyAuthenticationHandler` phát ra sẵn (dòng 93) mà không ai đọc.

### Đã sửa
- `IotSiteAccessGuard` (MỚI) — hàm thuần trả `(Allowed, StatusCode, Message)`.
  **Thứ tự kiểm có chủ ý: quyền TRƯỚC, tồn tại SAU.** Kiểm tồn tại trước thì thiết bị dò được site
  nào có thật bằng cách so 404 với 403 — biến chính hàng rào này thành công cụ do thám.
- Hai command nhận thêm `AuthenticatedDeviceSiteId` với `[JsonIgnore][BindNever]` — client KHÔNG đặt
  được qua body (thiếu hai attribute này thì thiết bị chỉ cần tự khai site là đi vòng qua hàng rào).
- Hai controller đọc claim `iot:site_id`; hai handler gọi guard trước khi ghi.
- Site không tồn tại: **404** thay vì rơi xuống DB và nổ lỗi khoá ngoại → 500.
- Người gọi bằng JWT (Staff báo cháy thủ công, NS-23) không có claim ⇒ chỉ kiểm tồn tại, không chặn.

### Bằng chứng — 15 test mới
`IotSiteAccessGuardTests` (8): đúng site ⇒ cho; site khác ⇒ 403; lô trộn own+foreign ⇒ chặn CẢ LÔ
(nhận một nửa sẽ khiến người gọi tưởng đã ghi đủ); site lạ ⇒ 404; **site lạ + khác chủ ⇒ 403 chứ
không phải 404** (chống dò); người dùng JWT ⇒ chỉ kiểm tồn tại; lô rỗng; trùng SiteId.
`IotSiteScopeEnforcementTests` (7): kiểm việc NỐI DÂY ở cả hai handler — guard đúng mà handler quên
gọi thì mọi test luật vẫn xanh trong khi lỗ hổng còn nguyên. Có đủ chiều dương (same-site vẫn 201).

**RED đã chứng minh:** gỡ lời gọi guard khỏi hai handler → **4/7** đỏ (3 test còn lại là chiều dương,
vẫn xanh — đúng thiết kế).

---

## #790 — [P2] VirusScanWorker không xác thực được, đính kèm kẹt vĩnh viễn

> Ban đầu bỏ qua vì tưởng không thuộc phần của Alexdev257; user xác nhận làm ⇒ đã làm trọn.

**Cáo buộc: ĐÚNG cả 4 điểm.**
- `VirusScanWorker.DownloadAndScanAsync` gọi `GET /api/files/{id}/download` qua named HttpClient
  "FileDownload" — KHÔNG gắn token nào; `FilesController` có `[Authorize]` ⇒ luôn 401.
- Hỏng ⇒ ghi thẳng `Failed`; worker chỉ quét `Pending` ⇒ **không bao giờ thử lại**.
- `ChatAttachmentDownloadQueryHandler` gộp mọi trạng thái ngoài Clean/Infected vào **202**
  "đang quét, thử lại sau" ⇒ client hỏi lại mãi mãi, không ai tải được, không lỗi nào nổi lên.
- Đường sensor/voice đã có kênh nội bộ `FileInternal` (gRPC, cổng riêng) — tức khuôn đúng có sẵn.

### Đã sửa

**1. Kênh tải — dùng đường service-to-service ĐÃ CÓ**
`file_internal.proto` **thêm** `rpc DownloadFile` (không đổi tên rpc cũ: đổi tên là phá hợp đồng dây,
và lúc cuốn chiếu deploy sẽ có pod TicketService bản cũ gọi FileStorage bản mới).
`FileInternalGrpcService` implement rpc mới, dùng chung phần streaming với `DownloadForTranscription`.
Worker bỏ `IHttpClientFactory`, lấy `FileInternal.FileInternalClient` từ scope.
Bỏ hẳn đăng ký named HttpClient "FileDownload" — giữ lại chỉ tạo một đường chết.

**2. Máy trạng thái** — thêm `VirusScanStatusEnum.Scanning = 5`.
Chiếm việc (ghi `Scanning` + tăng số lần thử + mốc thời gian, **commit**) TRƯỚC khi tải, nên nhiều
replica không cùng quét một đính kèm và sau sự cố bản ghi không rơi lại hàng đợi.
Bản ghi kẹt ở `Scanning` quá `ScanTimeoutSeconds` được thu hồi về `Pending` (có sàn `Math.Max(60,…)`
chặn cấu hình gõ nhầm về 0), KHÔNG đặt lại số lần thử.

**3. Thử lại có giãn cách** — `TicketAttachment.VirusScanAttempts` + `VirusScanLastAttemptAt`;
`MaxAttempts=3`, `RetryBackoffSeconds=60` nhân đôi mỗi lần, trần 1 giờ.
Hỏng tạm thời ⇒ về `Pending`; chỉ vào `Failed` khi hết lượt.

**4. Canh 0 byte** — tải về rỗng mà báo "sạch" nghĩa là đính kèm được đánh dấu an toàn trong khi
chưa ai quét nội dung thật của nó. Giờ là lỗi, tính vào số lần thử.

**5. Phản hồi tải xuống** — `Failed` tách khỏi 202, trả **503** nói rõ "không hoàn tất được lượt
quét, liên hệ quản trị". `Pending`/`Scanning` vẫn 202.

**6. Phục hồi dữ liệu đã kẹt** — migration `AddVirusScanRetryState` kèm
`UPDATE ticket_attachments SET virus_scan_status=1, virus_scan_attempts=0 WHERE virus_scan_status=4`.
Thêm cột không tự cứu được những dòng đã hỏng vì chính lỗi 401 này. `Down()` không dựng lại `Failed`
(dữ liệu đó là hệ quả của lỗi xác thực, khôi phục chỉ làm kẹt trở lại).
Cột đặt tên snake_case theo đúng quy ước bảng — lần sinh đầu ra PascalCase, đã sinh lại
(khôi phục snapshot bằng git rồi `migrations add`, KHÔNG dùng `--no-build`).

### Bằng chứng — 26 test

**Unit `VirusScanWorkerTests` (15, viết lại toàn bộ):** Pending→Scanning→Clean (ghi lại trạng thái
**đúng lúc bắt đầu tải** để chứng minh việc chiếm việc); Infected; tắt tính năng thì không truy vấn;
hỏng ⇒ về hàng đợi chứ không Failed; lặp lại ⇒ chạm trần rồi Failed; hết lượt thì không nhặt nữa;
giãn cách (chưa tới hạn thì bỏ qua, quá hạn thì thử lại); backoff nhân đôi có trần; 0 byte là lỗi;
thu hồi bản ghi kẹt; bản ghi vừa chiếm KHÔNG bị đụng; sàn timeout; lô rỗng; xoá mềm.

**Integration TicketService (5) — máy chủ gRPC THẬT (Kestrel HTTP/2), worker THẬT:**
tải qua đúng kênh nội bộ (`DownloadCalls == 1` — nếu còn đi REST thì bằng 0), các mảnh ghép lại
nguyên vẹn đúng thứ tự, Infected, server từ chối ⇒ về hàng đợi không kẹt, stream rỗng ⇒ không "sạch",
lỗi lặp lại ⇒ Failed chứ không quay vòng vô tận.

**Integration FileStorage (6) — MinIO THẬT + `FileInternalGrpcService` THẬT:**
`DownloadFile` trả đúng byte gốc + metadata ở mảnh đầu; id lạ/sai định dạng ⇒ NotFound;
file bị cách ly ⇒ FailedPrecondition; dòng xoá mềm ⇒ NotFound; và `DownloadForTranscription` **vẫn
chạy** (rpc được THÊM, không thay).
> Hai nửa hợp đồng được kiểm riêng có chủ ý: test phía Ticket dùng máy chủ giả nên không nói gì về
> bản hiện thực thật của FileStorage; thiếu bộ này là còn chỗ hở.

**RED đã chứng minh:**
- Bỏ retry + bỏ chiếm việc → **3/15** unit đỏ và **3/5** integration đỏ.
- Trả mapping download về như cũ → `Handle_ScanFailed_Returns503` đỏ.
- Bỏ canh cách ly ở rpc → `DownloadFile_QuarantinedFile_IsRefused` đỏ.

**Test cũ mã hoá chính lỗi:** `Handle_PendingOrFailed_Returns202(status: Failed)` khẳng định
`Failed ⇒ 202` — đúng hành vi mà issue phàn nàn. Đã tách thành hai test đúng nghĩa thay vì nới lỏng
bản sửa cho vừa test.

### #790 — phần nối dây triển khai (tự tìm ra, ngoài mô tả issue)

Kiểm chỗ dễ sót nhất: kênh gRPC mà bản sửa chuyển sang **chưa từng được nối dây ngoài dev**.
- `env.prod.example`: THIẾU cả `FILE_STORAGE_SERVICE_GRPC_SERVER_PORT` lẫn
  `FILE_STORAGE_GRPC_CLIENT_ADDRESS` (dev `.env`/`.env.Docker` có từ lâu).
- Helm: chart chưa bao giờ khai hai biến đó, **và** Service của FileStorage chỉ mở cổng 80.

Hậu quả nếu không sửa: `FileStorageService/Program.cs` **ném lỗi ngay lúc khởi động**
(`FILE_STORAGE_SERVICE_GRPC_SERVER_PORT (or Grpc:Port) must be configured.`) — tức không chỉ virus
scan, mà cả voice transcription cũng chưa từng có đường chạy trên hai môi trường đó. Vá worker mà bỏ
qua chỗ này là ship một bản sửa không chạy được.

Đã thêm: hai biến vào `env.prod.example` và Helm configmap; mở `containerPort: 8081` + Service port
`grpc: 8081` trong `filestorageservice.yaml`. `helm template` render sạch, có đủ cổng và biến.

`InternalGrpcWiringTests` (4) chốt lại: env mẫu prod khai đủ hai biến, configmap khai đủ, Service mở
đúng cổng, và cổng gRPC KHÁC 8080 (Program.cs chặn trùng). Đây là loại hỏng không test mã nào bắt
được — mọi thứ biên dịch, mọi test xanh, triệu chứng chỉ hiện ở môi trường thật.

### #790 — cập nhật hợp đồng API cho FE

`docs/api-ticket.md` là nơi FE tra `VirusScanStatusEnum` và mã trạng thái của endpoint download.
Đã cập nhật: thêm `Scanning = 5`, đổi `Failed` từ `202` sang **`503`**, và ghi rõ hai điểm FE phải
sửa (client so khớp đủ nhánh sẽ rơi vào nhánh mặc định nếu thiếu `Scanning`; vòng lặp hỏi lại theo
`202` sẽ không bao giờ dừng nếu vẫn coi `Failed` là "đang quét"). Bảng background job cũng ghi lại
cơ chế mới (kênh gRPC, chiếm việc, giãn cách, thu hồi).

Đổi mã trạng thái là thay đổi hợp đồng — không ghi vào doc thì FE chỉ phát hiện lúc chạy thật.

### #790 — kiểm nốt hai phát hiện (theo yêu cầu user)

**(1) Nối dây gRPC — bản kiểm đầu tiên của tôi CHƯA ĐỦ.**
`InternalGrpcWiringTests` cũ chỉ khẳng định biến **có mặt**. Khai cổng máy chủ `8081` nhưng địa chỉ
client trỏ `:8082` thì cả hai phép kiểm đó vẫn xanh, trong khi không có gì chạy được.
Đã thay bằng hai lớp:

- `GrpcServerPort` (MỚI, Infrastructure) — đưa luật đọc cổng ra khỏi `Program.cs`.
  Luật này quyết định service có khởi động được hay không nhưng trước đây nằm ở câu lệnh cấp cao nhất,
  tức **không test nào chạm tới được** — chính là lý do env mẫu prod và Helm thiếu biến suốt thời gian
  dài mà không có gì báo. Thêm chặn cổng ngoài dải 1–65535: cổng `0` nguy hiểm nhất vì service VẪN
  LÊN (OS tự chọn cổng) còn địa chỉ client thì trỏ vào hư không — hỏng im lặng, tệ hơn không lên.
  `GrpcServerPortTests` (8): thiếu cấu hình ⇒ lỗi nêu ĐÚNG tên biến; khoá chính thắng khoá dự phòng;
  khoá dự phòng `Grpc:Port` vẫn chạy (không làm vỡ bản triển khai đang có); trùng 8080 ⇒ chặn;
  0 / âm / >65535 ⇒ chặn.

- `GrpcWiringConsistencyTests` (15) — so **giá trị với nhau**, không chỉ kiểm có mặt: với từng nguồn
  (`.env`, `.env.Docker`, `env.prod.example`, Helm configmap) kiểm địa chỉ client là URI tuyệt đối,
  **cổng của nó bằng cổng máy chủ**, host đúng tên service, và giá trị trong file **qua được chính
  luật `GrpcServerPort.Resolve`**. Cộng: mọi môi trường dùng chung một cổng; Helm Service mở đúng
  cổng mà configmap quảng bá; dev compose khai đủ hai phía.

**RED — chứng minh bộ mới bắt được đúng thứ bộ cũ bỏ lọt:**
| Kịch bản | Kiểm "có mặt" (cũ) | Kiểm "khớp nhau" (mới) |
|---|---|---|
| Lệch cổng 8081 ↔ 8082 | **XANH 4/4** (bỏ lọt) | **ĐỎ** — đúng dòng production |
| Bỏ cổng grpc khỏi Helm Service | — | **ĐỎ** |
| Bỏ hẳn hai biến khỏi `env.prod.example` (trạng thái trước khi sửa) | — | **ĐỎ 4/15** |

**(2) Quét xem còn test nào khác mã hoá hành vi sai.**
- `Skip =`: **0**. `.Excluding(`: **0** (bộ ở `ChatEventsSerializationTests` đã gỡ ở #789 là duy nhất).
- Quét 531 file test tìm test **rỗng nghĩa** (không có khẳng định nào): 3 kết quả — kiểm tay từng cái
  thì **cả ba đều có khẳng định**; scanner của tôi bỏ sót `.VerifyGet(` và phân tích nhầm dấu ngoặc
  trong chuỗi regex. Không có phát hiện thật.
- Với nhóm "test khẳng định hành vi CŨ": **thang xanh chính là bằng chứng** — test nào còn khẳng định
  hành vi cũ của những chỗ tôi đã đổi thì bây giờ phải đỏ. Chỉ có đúng một test như vậy
  (`Handle_PendingOrFailed_Returns202`), đã tách thành hai test đúng nghĩa.

**Bổ sung: prod compose fail-fast.**
Dev compose khai `${FILE_STORAGE_SERVICE_GRPC_SERVER_PORT:?...}` / `${FILE_STORAGE_GRPC_CLIENT_ADDRESS:?...}`
— thiếu là `docker compose up` dừng ngay. Prod compose thì không khai gì, phó mặc `env_file`: thiếu
biến thì container VẪN LÊN rồi chết với `InvalidOperationException`, triệu chứng là crash-loop chứ
không phải một lỗi cấu hình đọc được. Cùng loại bất đối xứng đã gặp ở #788.
Đã thêm hai dòng `:?` vào `docker-compose.prod.yml` theo đúng khuôn có sẵn ở đó (`Cors__AllowedOrigins__0`).

Kiểm bằng `docker compose config` (đường dẫn `env_file` đổi sang file tạm vì `/opt/solar/.env.prod`
chỉ có trên VPS; mọi biểu thức nội suy giữ nguyên):
- đủ mọi biến khác, CHỈ thiếu 2 biến gRPC ⇒ **exit 1**, thông báo nêu đúng tên biến
  (`required variable FILE_STORAGE_SERVICE_GRPC_SERVER_PORT is missing a value`);
- đủ biến ⇒ **exit 0**, render ra `Grpc__Port: "8081"` và
  `Chat__Voice__FileStorageGrpcAddress: http://filestorageservice:8081`.

`EveryCompose_WiresBothSides_AndFailsFastWhenTheyAreMissing` chốt cả hai file compose.
**RED:** đổi `:?` thành `:-8081` (mặc định im lặng) ⇒ test đỏ đúng dòng prod.

**Phát hiện thêm khi kiểm phần nối dây: bản mẫu dev cũng hỏng (lỗi CÓ SẴN).**

`.env.Docker.example` ghi ở đầu file: *"Copy to .env.Docker, fill real secrets, then run
docker compose up"*. Làm đúng hướng dẫn đó thì **hỏng ngay**:
`docker compose --env-file .env.Docker.example config` ⇒ **exit 1**,
`required variable FILE_STORAGE_SERVICE_GRPC_SERVER_PORT is missing a value`.

Đây là lỗi có sẵn, không phải do tôi tạo ra: hai dòng `${…:?}` trong `docker-compose.yml` đã nằm
trong HEAD, còn `.env.Docker.example` tôi chưa hề đụng tới (`git diff HEAD` trên file đó rỗng).
Tức người mới vào dự án không dựng nổi stack, và rào đầu tiên chính là biến gRPC.

Đã thêm hai biến vào bản mẫu ⇒ chạy lại đúng hướng dẫn: **exit 0**.

Chốt bằng `DevTemplate_DeclaresEveryVariableComposeHardRequires` — kiểm theo **danh sách sinh từ
chính compose** (`${VAR:?}`), không phải danh sách chép tay: thêm biến bắt buộc mới mà quên cập nhật
bản mẫu là test đỏ ngay, không chỉ riêng hai biến gRPC.
**RED:** gỡ lại hai biến ⇒ test đỏ, nêu đúng tên cả hai.

Tổng bộ kiểm nối dây: **28 test** (8 `GrpcServerPortTests` + 20 `GrpcWiringConsistencyTests`,
phủ 5 nguồn cấu hình: `.env`, `.env.Docker`, `.env.Docker.example`, `env.prod.example`, Helm configmap).

**Rà tiếp sang bản mẫu prod: cũng thiếu biến bắt buộc (lỗi CÓ SẴN thứ hai).**

`docker-compose.prod.yml` khai `${Cors__AllowedOrigins__0:?...}` cho MỌI service (#AUTH-05), nhưng
`env.prod.example` không hề khai biến đó. Tái hiện: dùng chính bản mẫu làm `--env-file` ⇒ **exit 1**
ngay ở service đầu tiên. Đã thêm (kèm 2 origin phụ để trống) ⇒ chạy lại **exit 0**.

**Một dương tính giả tôi tự bắt được:** phép dò `${VAR:?}` ban đầu quét cả file, nên bắt nhầm chuỗi
mẫu `${VAR:?...}` nằm trong **chú thích** do chính tôi viết (ở #788 và #790) và báo tồn tại một biến
tên `VAR`. Đã sửa: bỏ dòng chú thích trước khi dò. Nếu không, test sẽ đòi khai một biến không có thật.

`EveryTemplate_DeclaresEveryVariableItsComposeHardRequires` giờ phủ **cả hai cặp**
(dev compose ↔ `.env.Docker.example`, prod compose ↔ `env.prod.example`).
**RED:** gỡ CORS khỏi mẫu prod ⇒ đỏ đúng dòng prod, nêu đúng tên biến.

Tổng: **29 test** nối dây (8 + 21), phủ 5 nguồn cấu hình và 2 cặp compose↔mẫu.

### #790 — KẾT QUẢ CUỐI

Thang loop-engine trên trạng thái đầy đủ (đã gồm cả hai bản vá lỗi có sẵn):
```
✓ L0 build        exit 0
✓ L1 unit         3455/3455 pass
✓ L2 integration  675/675 pass
✓ L3 e2e-smoke    exit 0
✓ XANH — 4 verifier, 4m52s
```
Chưa commit gì.

---

## Rà soát chéo client (2026-08-05) — hậu quả của các bản sửa lên FE và mobile

Sau khi dừng làm issue mới, tôi soát ngược từng thay đổi có chạm **hợp đồng API** rồi tìm chỗ dùng
tương ứng trong `capstone/frontend` và `capstone/mobile`. Bốn thứ có thật, ba thứ tôi tưởng có mà
kiểm ra là không.

### CÓ THẬT 1 — `EdgeDeviceDefault` trên FE còn 11 (do GH-785)

`frontend/src/shared/enums/iot/iot.enum.ts` khai `EdgeDeviceDefault: 11`, khớp giá trị **cũ** của
BE. GH-785 đã đổi BE sang 15 (thêm `EnvironmentalIngest = 4`).

Đường hỏng đo được: `IoTDeviceForm.tsx:67` lấy đúng hằng số này làm giá trị mặc định của form tạo
thiết bị, và nút "Đặt mặc định" ở `ApiKeyScopesField.tsx:46` cũng vậy. Nghĩa là **mọi thiết bị tạo
qua màn hình Admin vẫn nhận scope 11** → firmware gửi dữ liệu môi trường lên bị 403, tức nguyên vẹn
cái lỗi GH-785 đi sửa, chỉ khác đường vào. Sửa BE mà không sửa hằng số này thì bản vá chỉ đúng một
nửa.

**Sửa:** `EdgeDeviceDefault: 15` + chú thích lý do. FE `tsc` và `eslint --max-warnings=0` sạch.

### CÓ THẬT 2 — FE và mobile chặn cứng khi `publicUrl` null ⇒ ghi âm không bao giờ chạy (do GH-788)

Cả hai client đều có đoạn:

```ts
if (!meta?.fileId || !meta.publicUrl) throw new Error("Upload audio thất bại…");
```

kèm chú thích "BE validate `Url` bắt buộc nên thiếu publicUrl thì chắc chắn 400".

Kiểm lại BE thì **tiền đề đó sai**: `Url` chỉ được cất vào `TicketAttachment.Url` làm metadata
(`ChatVoiceTranscribeCommandHandler.cs:58`), còn việc chuyển giọng nói thì
`VoiceTranscriptionRequestedConsumer.cs:55` tải audio qua **gRPC nội bộ theo `FileId`** —
không đụng chuỗi url một lần nào.

GH-788 để `ObjectStorage__PublicBaseUrl` rỗng ở prod và Helm (bucket private, URL public chỉ trả
403). Dev vốn đã rỗng sẵn. Nên `publicUrl` **luôn** null ⇒ nhánh throw trên chạy 100% số lần ⇒
tính năng ghi âm chết ở cả hai client, và ở prod là do chính GH-788 chốt lại.

**Sửa:** bỏ điều kiện `!meta.publicUrl`, gửi `meta.publicUrl ?? ENDPOINTS.FILES.DOWNLOAD(fileId)`.
Đây đúng là quy ước mobile đã dùng sẵn cho attachment thường
(`useUploadTicketAttachment.ts:29`) — không phải quy ước tôi tự đặt ra.

### CÓ THẬT 3 — ô "Đang chờ giao" của Admin đánh rơi dòng `Processing` (do GH-792)

`NotificationBatchGetByIdQueryHandler` đếm `PendingCount` theo đúng `Status == Pending`. GH-792
thêm `Processing = 7` cho quãng "đã chiếm để gửi, chưa biết kết quả", nên giữa chừng một lần gửi
lớn các dòng đó **không nằm trong ô đếm nào**: `Sent + Failed + Pending < TotalRows`.

Người dùng thấy điều này ở `NotificationBatchDetailDialog.tsx:115` — ô *Đang chờ giao* tụt xuống
rồi lại lên, trông như bản ghi bốc hơi.

Đáng chú ý: test cũ **đã** khẳng định đúng bất biến `Sent + Failed + Pending == TotalRows`
(`NotificationBatchQueryTests.cs:142`) nhưng vẫn xanh, vì không dữ liệu test nào có dòng
`Processing`. Lại đúng kiểu "test xanh trong khi đang mã hoá chính con bug".

**Sửa:** đếm `Pending || Processing`. Thêm `ChiTiet_DongDangGui_VanNamTrongODangCho`.
**RED đã chứng minh:** hoàn nguyên bản sửa ⇒ `Expected d.PendingCount to be 2 … but found 1`.

### CÓ THẬT 4 — nhãn/màu trạng thái thông báo trên FE thiếu 3 giá trị

`NOTIFICATION_STATUS_TONE` và `getStatusLabel` chỉ biết Pending/Sent/Failed/Read. `Delivered=5` và
`Opened=6` thiếu từ Sprint 6.3 (không phải lỗi của tôi), `Processing=7` là của GH-792. Hậu quả:
"Đã nhận" và "Đã mở" đều hiện thành **"Đang chờ"** — ngược hẳn nghĩa.

Nhân tiện, hai chỗ tự tính chưa-đọc bằng `!== Read` (`AlertsPage`, `staff/DashboardPage`) đếm dư
mọi thông báo user đã bấm mở, lệch hẳn con số `GetUnreadCountQueryHandler` trả về. Chính repo đã
có sẵn helper `isUnreadStatus` kèm chú thích "dùng ở mọi chỗ FE tự tính unread" — chỉ là hai chỗ
này chưa dùng.

**Sửa:** bổ sung 3 nhãn + 3 màu, thêm `Processing: 7` vào enum của **cả** FE và mobile, và cho hai
chỗ trên dùng `isUnreadStatus`.

### KIỂM RA KHÔNG PHẢI LỖI — ghi lại để khỏi kiểm lại lần sau

**a) GH-774 không chặn nhầm ai trên FE.** Lo là Staff/Manager mất dashboard. Truy ra
`useBatteryDashboardStats()` (gọi không kèm `siteId`) chỉ có **một** nơi dùng:
`features/admin/pages/DashboardPage.tsx:109` — route Admin. Staff và Manager đi
`useStaffTicketDashboardStats` / `useTicketDashboardStats` sang TicketService, không liên quan.

**b) GH-790 trả 503 không làm FE hiện lỗi mơ hồ.** Interceptor axios đổi mọi response có status
thành `HttpError(status, message-của-BE)` (`axios.ts:213`), và `handleErrorApi` in thẳng
`error.message`. Nên 503 hiện đúng câu giải thích của BE. Mobile cũng ổn: nhánh
`status !== 200` đọc `res.data?.message`. `Scanning = 5` được BE gộp vào 202 mà FE đã xử lý sẵn.

**c) Cascade mark-read KHÔNG phải regression của GH-792.** Thoạt nhìn thì
`MarkNotificationRead/OpenedCommandHandler` lan trạng thái sang anh em ở `Pending|Sent|Delivered`
mà bỏ `Processing`. Nhưng lần theo hết đường thì kết quả **trước và sau GH-792 giống hệt nhau**:
dispatcher ghi trạng thái cuối bằng `notification.Status = Sent/Failed` + `SaveChangesAsync`
(`NotificationDispatcher.cs:188`) không kèm điều kiện, nên dù cascade có kịp đặt `Read` thì lượt
ghi đó cũng đè lên. Trước GH-792 dòng đang gửi nằm ở `Pending` (cascade bắt được → rồi bị đè);
sau GH-792 nó nằm ở `Processing` (cascade không bắt → cũng thành `Sent`). Cùng một kết cục.

Đây là một khe đua **có sẵn** ở tầng dispatcher, không phải thứ GH-792 tạo ra, và bịt nó là thiết
kế lại đường ghi trạng thái — ngoài phạm vi. Ghi lại đây để không ai "sửa" bằng cách thêm
`Processing` vào cascade rồi tưởng đã xong.

**d) Nhánh `error instanceof AxiosError` bắt 451 trong `useDownloadChatAttachment` là mã chết** —
interceptor đã đổi hết sang `HttpError` trước khi tới đó. Không sửa: hành vi thực tế vẫn đúng
(hiện câu của BE), và đây là mã có sẵn ngoài phạm vi các issue tôi làm.

### CÓ THẬT 5 — màn hình lộ credential thiết bị không hiện 2 trường GH-784 vừa thêm

GH-784 thêm `MqttUseTls` và `MqttTopicPrefix` vào `IotDeviceDto` với đúng một lý do: để bên cấu
hình thiết bị **khỏi phải suy đoán** TLS theo số cổng, và khỏi gõ tiền tố topic theo `DeviceCode`
nguyên bản chữ hoa (ACL Mosquitto dùng `solar/%u/...` với `%u` là username chữ thường; so khớp
topic MQTT phân biệt hoa/thường và không tắt được).

Nhưng chính màn hình mà người vận hành copy giá trị sang firmware —
`DeviceKeyRevealDialog.tsx` — chỉ hiện username/password/host/port. Nghĩa là bản vá BE **không tới
được người cần nó** qua đường Admin UI: họ vẫn đoán TLS và vẫn dùng tiền tố sai chữ.

**Sửa:** thêm `mqttUseTls` + `mqttTopicPrefix` vào `IotDeviceCreatedDto` (giữ nullable đúng theo
`bool?` / `string?` của BE) và hai `CopyRow` render có điều kiện. FE `tsc` + `eslint` sạch.

### GHI NHẬN — không sửa

**Mobile còn `AdminInvite: 13` trong `NotificationTypeEnum`.** BE đã **gỡ** giá trị 13 ngày
03/08/2026 và cố ý để trống số đó (thư mời quản trị đi thẳng AuthService → EmailService, không qua
NotificationService). FE đã bỏ, mobile thì chưa. Chỗ dùng duy nhất là một entry trong bản đồ icon
(`NotificationCard.tsx:23`) — BE không bao giờ phát type 13 nên entry đó là mã chết, không đổi hành
vi gì. Ngoài phạm vi các issue tôi làm; ghi lại để khỏi phải truy lại.

**Đối chiếu đầy đủ `NotificationTypeEnum` BE ↔ FE ↔ mobile:** 34 giá trị thật, khớp hoàn toàn cả ba
nơi (kể cả `TicketMerged = 34` chứ không phải 27). Không có lệch nào khác.

**Không client nào gọi `/api/auth/introspect`** (GH-776) hay endpoint replay job mới của
AuditAggregator — cả hai là đường service-to-service / bổ sung thuần, không phá client.
Endpoint `prescription-feedback` (GH-778) cũng chưa có client nào gọi: nó là đường mới, không thay
thế đường cũ.

---

## Chạy thật trên stack docker (2026-08-05) — dựng lại toàn bộ 9 image rồi đo

Container đang chạy lúc bắt đầu có tuổi 23–45 giờ, tức **dựng trước mọi thay đổi**. Đo trên đó thì
kết quả vô nghĩa, nên tôi build lại cả 9 service từ mã hiện tại rồi mới kiểm.

### LỖI DO CHÍNH TÔI GÂY RA — GH-788 làm stack dev không khởi động được

`filestorageservice` vào **crash-loop, exit 133**:

```
Unhandled exception. System.InvalidOperationException: Cấu hình ObjectStorage không hợp lệ
  - ObjectStorage__AccessKey đang dùng giá trị mặc định/dễ đoán ('minioadmin').
```

Nguyên nhân: `ObjectStorageCredentialGuard` nới luật theo `builder.Environment.IsDevelopment()`,
trong khi docker-compose của repo đặt `ASPNETCORE_ENVIRONMENT=Docker`. `IsDevelopment()` chỉ đúng
với tên `Development`, nên **mọi đường triển khai cục bộ đều bị chặn oan**.

Đây là loại hỏng mà không test nào lúc đó bắt được: bước dựng cờ nằm trong `Program.cs` — không
test nào chạm tới — còn các test của guard thì truyền thẳng `isDevelopment: true/false`, tức kiểm
đúng phần đã đúng. Chỉ chạy thật mới lộ.

Đáng nói hơn: `"Docker"` là quy ước **sẵn có** của repo — cả 8 service khác đều coi nó là môi
trường cục bộ (`IsEnvironment("Docker")` để tắt HTTPS redirection), và `AuthService` còn có sẵn
biến `isProductionLike` liệt kê tay `Production|Docker|Staging`. Tôi đã bỏ qua quy ước đó.

**Sửa:**
- Thêm `ObjectStorageCredentialGuard.LocalEnvironmentNames = ["Development", "Docker"]` +
  `IsLocalEnvironment(name)` — một chỗ duy nhất, để phép kiểm và call-site không thể lệch.
- Đổi tên tham số `isDevelopment` → `isLocalEnvironment` ở cả `Validate`, `ThrowIfInvalid` và
  `AddFileStorageInfrastructure`. Chính cái tên cũ là thứ dẫn tới nhầm lẫn.
- `Program.cs` dựng cờ bằng `IsLocalEnvironment(builder.Environment.EnvironmentName)`.
- Thêm 10 test: 4 tên môi trường cục bộ được nhận, 4 tên ngoài cục bộ vẫn siết, cộng hai phép kiểm
  đầu-cuối `Docker + minioadmin ⇒ KHÔNG ném` và `Production + minioadmin ⇒ VẪN ném GH-788`
  (nới cho Docker không được làm thủng mục đích ban đầu). 25/25 xanh.

Đã dựng lại image và `filestorageservice` lên bình thường.

### ĐÍNH CHÍNH — tôi ghi sai về `publicUrl` ở mục "CÓ THẬT 2"

Ở trên tôi viết `publicUrl` **luôn** null nên ghi âm chết ở mọi môi trường. Đo thật thì **sai với
stack dev**: upload trả `publicUrl = http://localhost:9090/solar-battery-files/...`, và URL đó tải
được thật (HTTP 200 — bucket dev vẫn công khai).

Lý do: `.env.Docker` dòng 107 đặt `ObjectStorage__PublicBaseUrl=` (rỗng), nhưng
`docker-compose.yml` dòng 347 khai thẳng `ObjectStorage__PublicBaseUrl: http://localhost:9090/...`
trong khối `environment:` — mà khối này **đè** `env_file`. Sửa `.env.Docker` không có tác dụng gì.

Điều này **không** làm bản sửa client sai, chỉ làm lý do phải phát biểu lại cho đúng: ghi âm chết ở
**production và k8s** (nơi tôi đặt `PublicBaseUrl: ""` theo GH-788), chứ không phải ở dev. Bản vá
`publicUrl ?? DOWNLOAD(fileId)` vẫn cần và vẫn đúng — nó chính là thứ giữ cho tính năng sống khi
lên prod.

### Kết quả đo — 23 PASS · 0 FAIL

| Issue | Đo được |
|-------|---------|
| GH-774 | Admin/Manager 200 · Staff/Customer **403** · Customer + site lạ và site người khác đều **404** (giống hệt nhau ⇒ không dò được sự tồn tại) |
| GH-776 | thiếu `X-Introspection-Key` → **401** |
| GH-785 | thiết bị tạo với scope mặc định của FE → `apiKeyScopes = 15` |
| GH-784 | 4 trường MQTT nhất quán cùng rỗng khi `Mqtt__Enabled=false` (đúng `MqttBrokerEndpoint.Disabled`) |
| GH-806 | site của chính mình **201** · site khác **403** "Thiết bị không có quyền ghi dữ liệu cho site khác" |
| GH-788 | download có token 200 · không token **401** · presigned 200 và giữ đúng `http://` (bản cũ ký ra `https://`) |
| GH-792 | không dòng nào rơi khỏi mọi ô đếm |
| GH-786 | xem mục dưới |

### GH-786 — chứng minh dứt điểm bằng container tạm

Container `solar-mosquitto` vẫn `unhealthy`, nên tôi dựng một broker rời (không đụng stack của
người dùng) với mật khẩu tự đặt và đo mã thoát của đúng ba lệnh:

| Cách | exit | Nghĩa |
|------|------|-------|
| CŨ `</dev/tcp/127.0.0.1/1883` | **1** | BusyBox ash không có `/dev/tcp` ⇒ healthcheck cũ **không bao giờ** xanh được |
| MỚI `mosquitto_pub` mật khẩu đúng | **0** | healthy |
| MỚI `mosquitto_pub` mật khẩu sai | **5** | bắt được auth hỏng — thứ mà phép mở socket bỏ sót |

`unhealthy` ở máy này là do env **chưa provision MQTT**: `.env.Docker` để `Mqtt__Enabled=false` và
`Mqtt__Password=CHANGE_ME_GENERATE_VIA_mosquitto_passwd`, nên broker từ chối đúng như phải thế
(log: `disconnected, not authorised`). Không service nào `depends_on` mosquitto, và nó nằm sau
profile `mqtt` — `docker compose up -d` thường sẽ KHÔNG dựng lại nó, phải
`--profile mqtt up -d mosquitto`.

### Ngoài phạm vi — báo lại, không tự sửa

**`POST /api/ambient/readings/batch` trả 500 khi gửi trùng `(site_id, time)`.** Đo được:
`DbUpdateException` → 500 `"Đã xảy ra lỗi hệ thống"`. Cùng mốc thời gian mới thì 201. Đây đúng loại
lỗi mà **GH-763** đã sửa cho telemetry ("trùng trả 500 thay vì bỏ qua"), nhưng trên endpoint ambient
thì chưa ai sửa. Không nằm trong các issue giao cho tôi nên tôi không tự mở rộng phạm vi.

**`docker-compose.yml` đè biến của `.env.Docker`** (xem phần đính chính). Ai sửa `.env.Docker` mà
thấy không có tác dụng thì nguyên nhân ở đây.

---

## E2E frontend qua Playwright (2026-08-05)

Chạy trên trình duyệt thật, FE dev server ở `:5173` proxy sang gateway `:4001` của stack vừa dựng lại.

### BUG FE THẤY TẬN MẮT — hộp thoại lộ credential hiện chữ "null" cho cổng broker

Tạo thiết bị IoT qua màn hình Admin, hộp thoại "Thông tin bí mật của thiết bị" hiện:

- **MQTT Broker Host** — ô trống, kèm cảnh báo React `` `value` prop on `input` should not be null ``
- **MQTT Broker Port** — hiện đúng chữ **`null`** (do `String(null)`)

Người vận hành copy từ hộp thoại này sang cấu hình firmware, nên họ copy nguyên chuỗi `null` làm
số cổng. Kiểu FE là thủ phạm: khai `mqttBrokerHost: string` / `mqttBrokerPort: number` trong khi BE
khai `string?` / `int?` — TypeScript tin là luôn có giá trị nên không ai thấy gì lúc biên dịch.

**Sửa:** khai lại **cả sáu** trường MQTT là nullable đúng hợp đồng BE, và xử lý khối MQTT như MỘT
đơn vị (chúng cùng rỗng khi bridge tắt): có host thì hiện đủ host/port/TLS/prefix, không có thì một
câu giải thích thay cho bốn ô hỏng. Hộp thoại giờ hiện: *"MQTT bridge chưa được bật trên máy chủ…
Thiết bị vẫn dùng được API Key ở trên."*

### Xác nhận trực tiếp trên giao diện

| Việc | Đo được |
|------|---------|
| GH-785 | Tạo thiết bị **qua UI Admin** → `apiKeyScopes = 15` (trước bản sửa hằng số FE là 11) |
| GH-774 | Admin dashboard gọi `/api/battery/dashboard/stats` → 200. Staff dashboard **không gọi** endpoint đó (đi `staff/tickets/dashboard/stats`) ⇒ siết quyền không làm hỏng màn Staff |
| Nhãn trạng thái | Thông báo `Opened` hiện **"Đã mở"** — trước bản sửa rơi xuống "Đang chờ", ngược hẳn nghĩa |
| `isUnreadStatus` | Ô "Chưa đọc trang này" = **0** với thông báo đã mở — trước bản sửa đếm thành 1, lệch con số server |
| GH-792 | Hộp thoại chi tiết batch: Tổng 1 · Đang chờ 0 · Đã giao 0 · Đã đọc 1 · Thất bại 0 — nhất quán |
| Console | 0 lỗi trên login · admin dashboard · IoT devices · alerts · notifications · sites · tickets · staff dashboard · staff alerts |

**Cổng chất lượng FE:** `tsc --noEmit` = 0 · `eslint --max-warnings=0` = 0 · `pnpm build` = 0.

---

## E2E mobile (2026-08-05) — 12 PASS · 0 FAIL

`tsc --noEmit` = 0 · `expo lint` = 0.

Gọi thật đúng những endpoint mã mobile gọi, với **đúng payload mà mã mobile dựng**:

| Phép kiểm | Kết quả |
|-----------|---------|
| 6 màn hình chính (battery-assets/me, notifications, unread-count, preferences, customer/tickets/me, sessions/me) | 200 hết |
| GH-792 | 50 dòng feed, mọi `status` nằm trong enum mobile biết (1..7 sau khi thêm `Processing`) |
| GH-788 | upload → `GET /api/files/{id}/download` (đường dự phòng của `useUploadTicketAttachment`) → 200 |
| **GH-788 — bản vá ghi âm** | POST `/chats/voice` với `url = /api/files/{id}/download` → **202 Accepted** |

Dòng cuối là phép kiểm quan trọng nhất: nó bác bỏ trực tiếp giả định ghi trong chú thích của cả hai
client ("thiếu publicUrl thì bước 2 chắc chắn 400"). BE nhận bình thường, đúng như đọc mã đã suy ra
— `Url` chỉ là metadata, còn audio thì consumer tải qua gRPC theo `fileId`.

**Một lỗi trong kịch bản của tôi, không phải của sản phẩm:** ban đầu tôi gọi `/api/tickets/me` và
nhận 404. Mobile dùng `/api/customer/tickets/me` (`CUSTOMER_LIST`). Đã sửa.

**Ghi nhận môi trường:** `mobile/.env` trỏ `EXPO_PUBLIC_API_URL=http://192.168.1.44:4001` nhưng IP
LAN của máy hiện là `192.168.1.242` — giá trị theo từng máy, không phải lỗi mã.

---

## E2E mobile trên emulator Android thật (2026-08-05)

Ban đầu tôi kết luận không chạy được emulator vì máy "không có JDK". Kiểm lại thì **sai**:
Android Studio có sẵn JBR 21 ở `/Applications/Android Studio.app/Contents/jbr/Contents/Home`.
Trỏ `JAVA_HOME` vào đó là đủ, không phải cài gì thêm.

Đã dựng: tải `system-images;android-35;google_apis;arm64-v8a` → tạo AVD `e2e_pixel` →
`expo prebuild` + Gradle (`BUILD SUCCESSFUL in 14m 44s`) → cài APK debug → app chạy.

### Kết quả

Đăng nhập bằng `customer.demo@solarbattery.local`, app gọi API thật và nhận **200 ở cả 6 đường**:
`POST /api/auth/login` · `GET /api/auth/me` · `/api/auth/me/permissions` ·
`/api/battery-assets/me` · `/api/sites/me` · `/api/alerts`.

Ba màn hình duyệt qua đều dựng đúng dữ liệu thật, **không có chuỗi `null`/`undefined`**, không lỗi JS:

- **Trang chủ** — 02 Pin, BAT-2026-008/009, 20.0 V · 5.5 A · 34 °C, thời tiết, badge thông báo `2`
- **Cảnh báo & Sự cố** — 2 cảnh báo CRITICAL "Suy giảm SOH" (37.09 % và 33.89 %, ngưỡng 80 %)
- **Tài khoản** — hồ sơ Demo Customer, 2 thiết bị · 4 ticket mở · 2 cảnh báo

### Bẫy mất thời gian nhất — và nó KHÔNG phải lỗi mã

App báo `Network Error` ở màn đăng nhập suốt mấy vòng. Nguyên nhân: `mobile/.env` ghi
`EXPO_PUBLIC_API_URL=http://192.168.1.44:4001`, mà IP LAN của máy hiện là `192.168.1.242` — và giá
trị trong `.env` **thắng** biến môi trường tôi truyền vào lệnh. Đặt `.env.local` (đã nằm trong
`.gitignore` theo mẫu `.env*.local`) với `http://10.0.2.2:4001` — địa chỉ chuẩn để emulator gọi
ngược về máy chủ — là chạy ngay.

**Đã xoá `.env.local` sau khi kiểm xong** để trả môi trường về nguyên trạng. Ai chạy emulator lần
sau cần làm lại bước này, hoặc cập nhật IP trong `.env` cho khớp máy mình.

### Dấu vết để lại trên máy

- AVD `e2e_pixel` + system image android-35 arm64 (~2 GB) trong `~/Library/Android/sdk`.
  Gỡ: `avdmanager delete avd -n e2e_pixel`.
- `mobile/android/` do `expo prebuild` sinh ra — **đã nằm trong `.gitignore`** (`/android`), không
  ảnh hưởng cây git. Emulator đã tắt.

---

## Thang loop-engine cuối cùng

```
✓ L0 build        exit 0
✓ L1 unit         3466/3466 pass   (+11 test mới so với 3455 trước đó)
✓ L2 integration  675/675 pass
✓ L3 e2e-smoke    exit 0
✓ XANH — 4 verifier, 18m29s
```

11 test thêm: 10 cho `ObjectStorageCredentialGuard` (tên môi trường cục bộ + hai phép kiểm
đầu-cuối Docker/Production) và 1 cho `PendingCount` gộp `Processing`.

**Chưa commit gì** — theo đúng yêu cầu.

---

## Hai lỗi phát hiện sau cùng (2026-08-05, sau khi nhánh rebase lên dev)

### LỖI DO TÔI — test nối dây gRPC phụ thuộc file KHÔNG có trong Git

`GrpcWiringConsistencyTests` đọc năm nguồn, trong đó `.env` và `.env.Docker` **bị `.gitignore`**
và do từng người tự tạo. Máy vừa clone hoặc runner CI không hề có chúng.

**Đo được:** đổi tên `.env` đi rồi chạy lại ⇒ **4 test đỏ**, thông báo
`Expected File.Exists(path) to be true because thiếu file cấu hình …/.env`.
Nghĩa là bộ test chỉ xanh nhờ file riêng của máy tôi — đúng loại "xanh giả" mà chính bộ test này
sinh ra để chống.

**Sửa:** tách hai nhóm.
- `WiringSources()` giữ đúng ba nguồn **được Git theo dõi** (`.env.Docker.example`,
  `env.prod.example`, helm configmap) — bắt buộc tồn tại và phải khớp nhau.
- `LocalEnvFiles_WhenPresent_AgreeWithTheTrackedOnes` kiểm `.env` / `.env.Docker` **chỉ khi có**,
  đối chiếu cổng và địa chỉ với bản mẫu trong Git. Vẫn đáng kiểm: đây là file người ta chạy hằng
  ngày, lệch cổng ở đây sinh ra đúng cảnh "máy tôi chạy được" mà không ai giải thích nổi.

**Chứng minh hai chiều:**

| Tình huống | Kết quả |
|---|---|
| Ẩn cả `.env` lẫn `.env.Docker` (giả lập máy vừa clone / CI) | **16/16 xanh** (trước: 4 đỏ) |
| Để file nhưng đổi cổng `.env.Docker` thành 9999 | **đỏ đúng chỗ**: `Expected port to be "8081" … but "9999" differs` |

Toàn bộ `FileStorageService.UnitTests`: 107/107 xanh.

**Trả lời câu hỏi "có thêm gì trong `.env` / `.env.Docker` không":** không. Hai biến gRPC đã có sẵn
trong cả hai file từ trước (`.env` giữ nguyên mtime 2026-08-01). Compose chỉ bắt buộc đúng hai biến
đó và cả hai đều có. `.env.local` bên mobile là file tạm lúc kiểm emulator, đã xoá.

### KHÔNG PHẢI LỖI CỦA TÔI — test KB đỏ do commit kéo về từ `dev`

Thang chạy lần 2 đỏ 1 test: `KbWorkflowHandlersTests.Handle_UpdateCommand_Success_CreatesPendingVersionAndUpdatesArticle`
— *"Expected article.ReviewRequired to be true, but found False"*.

Reflog giải thích: lúc `08-05 13:49` nhánh được `pull origin dev --rebase`, kéo về commit
`0e68b2a6` (Shu1237, 2026-08-03) *"feat: direct KB update for owner and manager without
re-approval"*. Commit đó đổi luật — **chủ bài viết và Manager/Admin sửa thẳng**, không qua duyệt —
và có cập nhật `KbApiTests.cs` để nói rõ điều đó (`UpdateKbArticle_ByCreator_AppliesContentDirectly`
khẳng định `ReviewRequired.Should().BeFalse()`), nhưng **bỏ sót** test đơn vị này.

Test đó lấy chính `creatorId` làm người sửa rồi vẫn kỳ vọng nhánh chờ duyệt. Vì handler nay cho chủ
bài viết đi `HandleDirectUpdate`, nó không bao giờ đúng nữa.

**Sửa:** đổi người sửa thành một Staff **khác** (`editorId`). Chọn cách này vì tên test và cả khối
`Verify` phiên bản 2.1/`Pending` phía dưới đều mô tả nhánh CHỜ DUYỆT — nhánh đó vẫn tồn tại, chỉ áp
cho người ngoài. Kèm theo, `PendingReviewBy` phải là người vừa sửa chứ không phải người tạo.
Quét cả file: chỉ đúng một test dính lỗi này. `KbWorkflowHandlersTests`: 18/18 xanh.

### Thang sau cùng

```
✓ L0 build        exit 0
✓ L1 unit         3461/3461 pass
✓ L2 integration  676/676 pass
✓ L3 e2e-smoke    exit 0
✓ XANH — 4 verifier, 3m39s
```

Số test đơn vị 3466 → 3461 là do gộp nguồn: bỏ 2 nguồn khỏi 3 `[Theory]` (−6) và thêm 1 `[Fact]`
mới (+1). Integration 675 → 676 (+1 test nối dây cục bộ).
