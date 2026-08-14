#!/usr/bin/env bash
#
# Sinh BẢN ĐỒ CODEBASE — tầng T1.
#
# ĐÂY LÀ BẢN MẶC ĐỊNH — HÃY SỬA CHO KHỚP DỰ ÁN CỦA BẠN.
#
# Bản đồ tốt trả lời được 4 câu hỏi mà agent luôn cần và luôn phải grep để tìm:
#   1. Code nằm ở đâu, ranh giới module là gì?
#   2. Chạy MỘT test như thế nào? (chạy cả suite mỗi lần là cách đốt tiền)
#   3. Endpoint / entrypoint / lệnh có những gì?
#   4. Chỗ nào hay đổi nhất? (rủi ro tập trung ở đó)
#
# NGUYÊN TẮC: chỉ mục, không phải bản sao. Dưới 25% ngân sách context.
set -uo pipefail

FENCE='```'

echo "# BẢN ĐỒ CODEBASE (sinh tự động — ĐỪNG SỬA TAY)"
echo
echo "Commit: $(git rev-parse --short HEAD 2>/dev/null || echo 'không phải git repo')"
echo "Sinh lúc: $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo

echo "## Cây thư mục (2 tầng)"
echo "$FENCE"
find . -maxdepth 2 -type d \
  -not -path '*/.*' \
  -not -path '*/node_modules*' \
  -not -path '*/vendor*' \
  -not -path '*/target*' \
  -not -path '*/dist*' 2>/dev/null | sort | head -60
echo "$FENCE"
echo

echo "## TODO — bổ sung cho dự án của bạn"
echo
echo "Sửa file .loop/gen/map.sh để thêm:"
echo "- lệnh chạy MỘT test đơn lẻ"
echo "- danh sách endpoint / lệnh CLI / entrypoint"
echo "- ranh giới module (cái gì không được import cái gì)"
echo

echo "## File thay đổi nhiều nhất 90 ngày qua"
echo "$FENCE"
git log --since='90 days' --name-only --pretty=format: 2>/dev/null \
  | grep -v '^$' | sort | uniq -c | sort -rn | head -20 || echo "(không có lịch sử git)"
echo "$FENCE"
