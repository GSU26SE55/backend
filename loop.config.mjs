/**
 * loop-engine — cấu hình cho repo backend GSU26SE55 (Solar Battery Maintenance).
 *
 * Mục đích cụ thể: chạy vòng lặp sửa lỗi cho milestone "E2E" (177 issue).
 * Mỗi issue = một file nhiệm vụ trong loop/tasks/GH-<số>.md.
 *
 * Thang kiểm chứng bám đúng Makefile của dự án — KHÔNG phát minh lệnh mới,
 * vì lệnh trong Makefile là thứ CI thật sự chạy.
 */

const SLN = 'SolarBatteryMaintainance.slnx';

/** Filter của `make ci-test` — giữ nguyên để loop đo đúng thứ CI đo. */
const UNIT_FILTER = 'FullyQualifiedName!~IntegrationTests&Category!=Performance';
const INTEG_FILTER = 'FullyQualifiedName~IntegrationTests&Category!=Performance';

/**
 * dotnet test chỉ xuất TRX; converter đưa về JUnit để engine thấy TỪNG test lỗi
 * (cần cho chữ ký lỗi → phát hiện giậm chân, và cách ly flaky).
 * Dùng `;` chứ không `&&`: test đỏ vẫn phải convert, nếu không engine mất report.
 */
const dotnetTest = (filter, trxDir, out) =>
  `dotnet test ${SLN} -c Release --no-build --filter "${filter}" ` +
  `--logger trx --results-directory ${trxDir} ; ` +
  `node tools/loop/trx2junit.mjs ${trxDir} ${out}`;

export default {
  version: 1,
  id: 'backend-e2e-milestone',
  kind: 'generic',
  root: '.',
  workdir: '.loop',

  context: {
    constitution: [
      'loop/context/constitution.md',
      'loop/context/glossary.md',
      'loop/context/conventions.md',
    ],
    docs: [],
    map: {
      generator: 'bash .loop/gen/map.sh',
      output: '.loop/cache/map.md',
      ttlSeconds: 3600,
      timeout: 180000,
    },
    taskDir: 'loop/tasks',
    maxTokens: 60000,
    targetUtilisation: 0.5,
    includeGitStatus: true,
    includeDiff: true,
    diffMaxLines: 400,
  },

  // ───────────────────────────────────────────────────────────────────────
  // RUNTIME — stack docker đã chạy sẵn (make docker-up). KHÔNG tự `up`/`down`:
  // stack này mất nhiều phút để khởi động lại và đang giữ dữ liệu seed mà
  // tầng L3 dựa vào. Chỉ kiểm tra sức khoẻ.
  // ───────────────────────────────────────────────────────────────────────
  runtime: {
    up: null,
    health: 'curl -fsS http://localhost:4001/health',
    seed: null,
    down: null,
    healthTimeout: 120000,
    healthInterval: 2000,
    baseUrl: 'http://localhost:4001',
    env: {},

    // Chứng minh bộ công cụ CHẠY ĐƯỢC trước khi tiêu hàng chục phút.
    // Thất bại ở đây = MÔI TRƯỜNG HỎNG, không phải kiểm chứng đỏ.
    verify: [
      'dotnet --version',
      'node --version',
      'docker info',
    ],
  },

  // ───────────────────────────────────────────────────────────────────────
  // VERIFIERS — rẻ trước, đắt sau.
  //   L0 build (~25s) → L1 unit (~3ph) → [cổng] L2 integration → L3 e2e → L4 e2e-ai
  // L2..L4 gateOnly: chỉ chạy khi L0+L1 đã xanh — chúng trả lời "xong chưa",
  // câu chỉ đáng hỏi khi loop sắp nói "rồi".
  //
  // L3 và L4 KHÔNG trùng nhau: L3 phủ gateway/auth/report/SLA/saga, L4 phủ
  // tích hợp AI ↔ BE. Trước khi có L4, toàn bộ luồng AI nằm ngoài mọi tầng
  // kiểm chứng dù nó là thứ sinh ra dự đoán, cảnh báo và ticket.
  // ───────────────────────────────────────────────────────────────────────
  verifiers: [
    {
      // exitcode chứ không text-regex: build hỏng có thể do restore/NETSDK/MSB
      // với định dạng khác `error CSxxxx`, regex sẽ bỏ sót và báo XANH nhầm.
      // exitcode không bao giờ parse sai; output đầy đủ vẫn vào excerpt.
      id: 'build',
      level: 0,
      cmd: 'make ci-build',
      adapter: 'exitcode',
      timeout: 900000,
    },
    {
      id: 'unit',
      level: 1,
      // BẪY ĐÃ TRẢ GIÁ: --no-build đo bản ĐÃ BUILD. Nếu L0 không chạy trước thì
      // đây đo code cũ. Thang kiểm chứng bảo đảm L0 luôn chạy trước L1.
      // Xoá trx cũ mỗi lần: TestResults/ của dự án còn ~279 file cũ, gộp nhầm
      // là đọc kết quả của lần chạy tuần trước.
      prepare: 'rm -rf .loop/out/trx-unit && mkdir -p .loop/out/trx-unit',
      cmd: dotnetTest(UNIT_FILTER, '.loop/out/trx-unit', '.loop/out/unit.xml'),
      adapter: 'junit',
      options: { report: '.loop/out/unit.xml' },
      timeout: 1800000,
    },
    {
      id: 'integration',
      level: 2,
      gateOnly: true,
      prepare: 'rm -rf .loop/out/trx-integ && mkdir -p .loop/out/trx-integ',
      cmd: dotnetTest(INTEG_FILTER, '.loop/out/trx-integ', '.loop/out/integration.xml'),
      adapter: 'junit',
      options: { report: '.loop/out/integration.xml' },
      timeout: 3600000,
    },
    {
      id: 'e2e-smoke',
      level: 3,
      gateOnly: true,
      cmd: 'bash tools/e2e-smoke.sh',
      adapter: 'exitcode',
      timeout: 900000,
    },
    {
      // Tầng riêng cho tích hợp AI ↔ BE.
      //
      // VÌ SAO TÁCH KHỎI L3: e2e-smoke.sh không có một dòng nào về
      // predict/prescribe/soh/anomaly — nó kiểm gateway/auth/report/SLA/saga.
      // Trước khi có tầng này, "L3 xanh" hoàn toàn không nói gì về AI, mà toàn
      // bộ luồng dự đoán/kê đơn/verify lại đi qua AI. Một lớp cả nghìn test vẫn
      // để lọt lỗi chỉ lộ ra khi gọi API thật trên dữ liệu thật.
      //
      // Kiểm CẢ HAI CHIỀU: BE gọi sang AI (8 RPC) và dữ liệu AI hiện ra qua API
      // của BE, kèm 2 round-trip thật (store AI tăng đúng số, prescriptionId AI
      // vừa cấp dùng lại được).
      //
      // ⚠️ Script CÓ ghi DB: nó đưa 1 ticket ManualByCustomer về trạng thái
      // Pending rồi gọi re-verify để mỗi lượt đều đi đúng đường thật thay vì rơi
      // vào nhánh dự phòng. Re-verify ngay sau đó ghi verdict lại nên trạng thái
      // cuối không đổi. Tắt bằng E2E_RESET_TICKET=0 nếu cần chạy chỉ-đọc.
      id: 'e2e-ai',
      level: 4,
      gateOnly: true,
      cmd: 'bash tools/e2e-ai-integration.sh',
      adapter: 'exitcode',
      timeout: 1800000,
    },
  ],

  // ───────────────────────────────────────────────────────────────────────
  // IMMUTABLE — vùng agent KHÔNG được sửa.
  //
  // CHỦ Ý: KHÔNG đưa services/**/tests/** vào đây. Guard coi việc THÊM file
  // trong vùng bất biến cũng là tamper và sẽ xoá — mà gần như mọi issue trong
  // milestone này đều yêu cầu THÊM test hồi quy ("Unit/integration test assert
  // … end-to-end"). Khoá cả cây test sẽ chặn đúng phần việc bắt buộc.
  //
  // Thay vào đó khoá THƯỚC ĐO và LUẬT CHƠI: kịch bản e2e, lệnh CI, cấu hình
  // loop, converter. Đó là những thứ sửa một dòng là cổng mở toang.
  // Việc "không được làm yếu assertion của test đã có" được kiểm bằng cách
  // đọc `git diff` trên cây test ở mỗi vòng (xem conventions.md §Test).
  // ───────────────────────────────────────────────────────────────────────
  immutable: [
    'tools/loop/**',
    'tools/e2e-smoke.sh',
    // Cùng lý do như e2e-smoke.sh: đây là THƯỚC ĐO của tầng L4. Sửa một dòng
    // trong này là cổng AI mở toang mà nhìn báo cáo vẫn thấy xanh.
    'tools/e2e-ai-integration.sh',
    'tools/e2e/**',
    'loop.config.mjs',
    'loop/context/**',
    'loop/rubrics/**',
    'Makefile',
    'Jenkinsfile',
    'ci/**',
    '.github/**',
    'Directory.Build.props',
    '.editorconfig',
  ],

  budget: {
    maxIterations: 8,
    maxTokens: null,
    maxUsd: null,
    maxWallClockMinutes: 180,
    maxConsecutiveNoProgress: 3,
  },

  flaky: {
    enabled: true,
    reruns: 1,
    quarantine: [],
  },

  actor: {
    provider: 'claude',
    model: null,
    extraArgs: ['--permission-mode', 'acceptEdits'],
    promptVia: 'file',
    timeout: 3600000,
    env: {},
  },

  judge: {
    enabled: true,
    provider: null,
    rubric: 'loop/rubrics/spec-compliance.md',
    timeout: 900000,
  },

  // Triệu chứng "công cụ chưa từng khởi động" riêng của .NET/dự án này.
  // Khớp ⇒ verdict nâng từ "test đỏ" thành "môi trường hỏng", để agent không
  // đi sửa code cho một sự cố hạ tầng.
  environmentBreakSigns: [
    'MSB1009',                       // project file không tồn tại
    'NETSDK1004',                    // thiếu project.assets.json → chưa restore
    'Unable to find package',
    'error NU1101',                  // không resolve được package
    'Cannot connect to the Docker daemon',
    'Connection refused (localhost:5432)',
    'No connection could be made because the target machine actively refused',
    'KHÔNG có file .trx nào',        // converter báo runner chưa hề chạy
  ],

  hooks: {
    beforeIteration: null,
    afterIteration: null,
    // KHÔNG commit tự động: người dùng tự commit sau khi review.
    onGreen: null,
    onEscalate: null,
  },

  report: { keepRuns: 200 },
};
