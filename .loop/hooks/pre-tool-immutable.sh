#!/usr/bin/env bash
#
# PreToolUse hook — chặn agent ghi đè vào vùng bất biến TRƯỚC khi việc đó xảy ra.
#
# Đây là lớp phòng thủ THỨ NHẤT (chặn sớm, thông báo rõ). Lớp thứ hai là
# `loopctl guard --check` bằng hash — bắt buộc phải có cả hai, vì hook này chỉ
# chặn được tool Write/Edit; agent vẫn có thể chạy `sed -i`, `rm`, hoặc một script.
#
# Cài đặt (.claude/settings.json):
#   {"hooks": {"PreToolUse": [{"matcher": "Write|Edit|MultiEdit",
#     "hooks": [{"type": "command", "command": ".loop/hooks/pre-tool-immutable.sh"}]}]}}
#
# Giao thức: stdin nhận JSON của tool call; exit 2 = chặn, stderr = lý do gửi cho model.

set -uo pipefail

payload="$(cat)"

# Lấy file_path bằng node (luôn có sẵn nếu bạn đang dùng loop-engine).
file_path="$(printf '%s' "$payload" | node -e "
let s='';
process.stdin.on('data', d => s += d).on('end', () => {
  try {
    const j = JSON.parse(s);
    const p = j.tool_input?.file_path || j.tool_input?.path || j.tool_input?.notebook_path || '';
    process.stdout.write(String(p));
  } catch { process.stdout.write(''); }
});
" 2>/dev/null)"

[ -z "$file_path" ] && exit 0

# Cho phép khi đang ở pha viết test có chủ đích.
if [ "${LOOP_PHASE:-}" = "author-tests" ]; then
  exit 0
fi

# Hỏi chính engine xem đường dẫn này có được bảo vệ không — dùng đúng một nguồn
# sự thật (config.immutable) thay vì lặp lại danh sách glob ở đây.
if loopctl guard --list 2>/dev/null | grep -qxF "${file_path#"$PWD/"}"; then
  cat >&2 <<EOF
BỊ CHẶN: "$file_path" nằm trong vùng BẤT BIẾN (test/spec/schema).

Verifier là thước đo. Sửa thước đo để nó báo xanh không phải là sửa lỗi.

Nếu bạn tin test SAI chứ không phải code sai:
  1. ĐỪNG sửa test.
  2. Ghi lập luận vào .loop/dispute.md — test nào, sai chỗ nào, hành vi đúng là gì,
     kèm bằng chứng từ spec.
  3. Dừng và báo cáo. Con người sẽ phân xử.

Hãy sửa code sản phẩm để thoả test.
EOF
  exit 2
fi

exit 0
