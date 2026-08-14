#!/usr/bin/env node
/**
 * TRX → JUnit XML.
 *
 * `dotnet test` chỉ xuất TRX; loop-engine không có adapter TRX nhưng có `junit`.
 * Không dùng `exitcode` vì nó chỉ cho biết ĐỎ/XANH — mất id từng test, nên engine
 * không tính được chữ ký lỗi (phát hiện giậm chân) lẫn cách ly flaky.
 *
 * Gộp NHIỀU trx (dotnet test xuất một file cho mỗi test project) thành một
 * <testsuites>.
 *
 * Dùng:  node tools/loop/trx2junit.mjs <thư-mục-trx> <file-junit-ra>
 *
 * Không phụ thuộc package nào — chạy được bằng node trần.
 */
import fs from 'node:fs';
import path from 'node:path';

const [, , inDir, outFile] = process.argv;
if (!inDir || !outFile) {
  console.error('dùng: trx2junit.mjs <thư-mục-trx> <file-junit-ra>');
  process.exit(64);
}

/** Bóc thuộc tính của một thẻ mở, chịu được cả nháy đơn lẫn nháy kép. */
function attrs(tag) {
  const out = {};
  const re = /([\w:.-]+)\s*=\s*("([^"]*)"|'([^']*)')/g;
  let m;
  while ((m = re.exec(tag))) out[m[1]] = unescapeXml(m[3] !== undefined ? m[3] : m[4]);
  return out;
}

function unescapeXml(s) {
  return String(s)
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#x?[0-9a-fA-F]+;/g, (e) => {
      const hex = /^&#x/i.test(e);
      const code = parseInt(e.replace(/^&#x?/i, '').replace(/;$/, ''), hex ? 16 : 10);
      return Number.isFinite(code) ? String.fromCodePoint(code) : e;
    })
    .replace(/&amp;/g, '&'); // sau cùng, nếu không sẽ giải mã hai lần
}

function escapeXml(s) {
  return String(s ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    // XML 1.0 cấm ký tự điều khiển; giữ \t \n \r
    .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '');
}

/** Nội dung văn bản đầu tiên của <tag> bên trong `xml`, đã unescape. */
function firstText(xml, tag) {
  const m = new RegExp(`<${tag}[^>]*>([\\s\\S]*?)</${tag}>`).exec(xml);
  return m ? unescapeXml(m[1]) : '';
}

/** "00:00:01.2345678" → giây (số thực). */
function durationToSeconds(d) {
  if (!d) return 0;
  const m = /^(?:(\d+)\.)?(\d+):(\d+):(\d+(?:\.\d+)?)$/.exec(d.trim());
  if (!m) return 0;
  const [, days, hh, mm, ss] = m;
  return (
    (days ? Number(days) * 86400 : 0) + Number(hh) * 3600 + Number(mm) * 60 + Number(ss)
  );
}

const trxFiles = fs.existsSync(inDir)
  ? fs
      .readdirSync(inDir)
      .filter((f) => f.toLowerCase().endsWith('.trx'))
      .map((f) => path.join(inDir, f))
  : [];

const suites = [];
let totals = { tests: 0, failures: 0, skipped: 0 };

for (const file of trxFiles) {
  const xml = fs.readFileSync(file, 'utf8');

  // testId → className, để JUnit có classname đúng (TRX để tên lớp ở TestDefinitions).
  const classById = new Map();
  const defRe = /<UnitTest\b([^>]*)>([\s\S]*?)<\/UnitTest>/g;
  let d;
  while ((d = defRe.exec(xml))) {
    const id = attrs(d[1]).id;
    const tm = /<TestMethod\b([^>]*)\/?>/.exec(d[2]);
    if (id && tm) classById.set(id, attrs(tm[1]).className || '');
  }

  // Tên suite = tên file dll trong <UnitTestResult ... > không có, nên lấy từ
  // TestDefinitions/codeBase; fallback về tên file trx.
  const codeBase = /<TestMethod\b[^>]*codeBase="([^"]*)"/.exec(xml);
  const suiteName = codeBase
    ? path.basename(unescapeXml(codeBase[1])).replace(/\.dll$/i, '')
    : path.basename(file, '.trx');

  const cases = [];
  let failures = 0;
  let skipped = 0;

  // Cả dạng tự đóng <UnitTestResult ... /> lẫn dạng có thân.
  const resRe = /<UnitTestResult\b([^>]*?)(\/>|>([\s\S]*?)<\/UnitTestResult>)/g;
  let r;
  while ((r = resRe.exec(xml))) {
    const a = attrs(r[1]);
    const body = r[3] || '';
    const outcome = (a.outcome || '').toLowerCase();
    const className = classById.get(a.testId) || '';
    // testName của TRX thường là FQN đầy đủ; bỏ tiền tố class cho gọn.
    let name = a.testName || '';
    if (className && name.startsWith(className + '.')) name = name.slice(className.length + 1);

    const c = {
      classname: className || suiteName,
      name: name || a.testName || '(không tên)',
      time: durationToSeconds(a.duration),
      outcome,
    };

    if (outcome === 'failed' || outcome === 'error') {
      failures++;
      c.failureMessage = firstText(body, 'Message') || 'Test thất bại';
      c.failureDetail = firstText(body, 'StackTrace') || '';
    } else if (outcome !== 'passed') {
      // NotExecuted / Skipped / Inconclusive / Timeout / Aborted…
      skipped++;
      c.skipped = true;
      c.skipReason = firstText(body, 'Message') || outcome;
    }
    cases.push(c);
  }

  if (cases.length === 0) continue; // trx rỗng (project không có test khớp filter)

  suites.push({ name: suiteName, cases, failures, skipped });
  totals.tests += cases.length;
  totals.failures += failures;
  totals.skipped += skipped;
}

const parts = ['<?xml version="1.0" encoding="UTF-8"?>'];
parts.push(
  `<testsuites tests="${totals.tests}" failures="${totals.failures}" skipped="${totals.skipped}">`,
);
for (const s of suites) {
  parts.push(
    `  <testsuite name="${escapeXml(s.name)}" tests="${s.cases.length}" failures="${s.failures}" errors="0" skipped="${s.skipped}">`,
  );
  for (const c of s.cases) {
    const head = `    <testcase classname="${escapeXml(c.classname)}" name="${escapeXml(c.name)}" time="${c.time}"`;
    if (c.failureMessage !== undefined) {
      parts.push(`${head}>`);
      parts.push(
        `      <failure message="${escapeXml(c.failureMessage.slice(0, 2000))}">${escapeXml(
          `${c.failureMessage}\n${c.failureDetail}`.slice(0, 8000),
        )}</failure>`,
      );
      parts.push('    </testcase>');
    } else if (c.skipped) {
      parts.push(`${head}>`);
      parts.push(`      <skipped message="${escapeXml(String(c.skipReason).slice(0, 500))}"/>`);
      parts.push('    </testcase>');
    } else {
      parts.push(`${head}/>`);
    }
  }
  parts.push('  </testsuite>');
}
parts.push('</testsuites>');

fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, parts.join('\n') + '\n');

console.error(
  `trx2junit: ${trxFiles.length} trx → ${suites.length} suite, ${totals.tests} test, ${totals.failures} đỏ, ${totals.skipped} bỏ qua → ${outFile}`,
);

// Không có trx nào = runner chưa hề chạy. Báo bằng exit code riêng để verifier
// phân biệt "môi trường hỏng" với "test đỏ" — engine có luật riêng cho ca này.
if (trxFiles.length === 0) {
  console.error('trx2junit: KHÔNG có file .trx nào — runner chưa sinh report (môi trường hỏng?)');
  process.exit(3);
}
