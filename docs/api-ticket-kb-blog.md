# API Documentation — TicketService · Knowledge Base & Blog

> Tài liệu này tách ra từ `api-ticket.md` — chứa toàn bộ các endpoint và DTO liên quan đến **Knowledge Base (KB)** và **Blog**.
> Base URL: `http://localhost:{port}/api`
> Content-Type: `application/json`
> Response wrapper chuẩn: `CommonResponse<T>` — xem cấu trúc đầy đủ tại `api-ticket.md`.
> **ID fields:** Tất cả `id` trong response đều là `string` (UUID). TypeScript dùng `string` cho mọi id field.
> **Enum serialize:** Toàn bộ response của TicketService dùng `JsonStringEnumConverter` → mọi enum trả về dạng **chuỗi** (vd `"Published"`, `"Charging"`). Khi filter/gửi request, enum cũng nhận **chuỗi tên enum** — gửi đúng tên (vd `Status=Published`), KHÔNG gửi số.

---

## Enums

### `KbArticleStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Draft` | 1 | Nháp |
| `PendingReview` | 2 | Chờ phê duyệt |
| `Published` | 3 | Đã xuất bản (Customer xem được) |
| `Archived` | 4 | Đã lưu trữ (ẩn) |

### `KbVersionStatusEnum`

Trạng thái của một bản ghi lịch sử (`KbArticleVersion`) — khác với `KbArticleStatusEnum` của bài viết chính.

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Pending` | 1 | Chờ duyệt |
| `Approved` | 2 | Đã duyệt |
| `Rejected` | 3 | Bị từ chối |
| `Archived` | 4 | Bản sao lưu (snapshot) |

### `KbReferenceTypeEnum`

Dùng khi gán bài viết Knowledge Base vào Ticket.

| Giá trị | Int | Ý nghĩa | Ràng buộc |
|---|---|---|---|
| `ConsultedDuringResolve` | 1 | Tham khảo khi xử lý | Chỉ gán được **trước** khi ticket `Resolved` |
| `ProvidedToCustomer` | 2 | Cung cấp cho khách hàng | Gán được đến hết state `Resolved`; **không** gán được bài `isInternalOnly` (→ `422`) |
| `GeneratedAfterResolve` | 3 | Tạo ra sau khi xử lý xong | Gán được đến hết state `Resolved` |

> Từ `ClosedPendingRate` trở đi, **mọi type** đều bị chặn (`409`). Chi tiết bảng quy tắc: xem `POST /api/knowledge-base/references` (Nhóm 11).

### `BlogPostStatusEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Generating` | 1 | AI đang tạo nội dung (async) |
| `GenerationFailed` | 2 | AI tạo thất bại — có thể chỉnh sửa thủ công |
| `Draft` | 3 | Nháp — chờ Manager/Admin publish |
| `Published` | 4 | Đã xuất bản (mọi user đăng nhập xem được) |
| `Archived` | 5 | Đã lưu trữ (ẩn) |

### `BlogPostOriginEnum`

| Giá trị | Int | Ý nghĩa |
|---|---|---|
| `Manual` | 1 | Tạo thủ công bởi Staff/Manager/Admin |
| `AiGeneratedFromKb` | 2 | AI tạo từ bài viết KB (qua `generate-from-kb`) |

---

## DTOs — Knowledge Base

### `KbArticleDTO` (detail — `GET /{id}`, response của `update`)

> Enum `category`/`status` trả về **dạng chuỗi** (`JsonStringEnumConverter`).

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bài viết |
| `code` | `string` | Không | Mã bài viết (KB-YYYY-NNNN) |
| `category` | `TicketCategoryEnum` | Không | Enum chuỗi (e.g. `"Charging"`) |
| `title` | `string` | Không | Tiêu đề |
| `symptoms` | `string` | Không | Triệu chứng |
| `diagnosisSteps` | `string` | Không | Các bước chẩn đoán |
| `solutionSteps` | `string` | Không | Các bước xử lý |
| `recommendedParts` | `string[]?` | Có | Danh sách linh kiện (**mảng**, không phải string) |
| `tags` | `string[]` | Không | Danh sách thẻ |
| `isTemplate` | `bool` | Không | `true` = bài viết này là mẫu (template) để copy cấu trúc |
| `status` | `KbArticleStatusEnum` | Không | Enum chuỗi (e.g. `"Published"`) |
| `isInternalOnly` | `bool` | Không | Bài chỉ nội bộ (ẩn với Customer) |
| `version` | `int` | Không | Số phiên bản chính (Major Version) |
| `viewCount` | `int` | Không | Lượt xem |
| `helpfulCount` | `int` | Không | Lượt hữu ích |
| `reviewRequired` | `bool` | Không | Có đang chờ duyệt thay đổi không |
| `pendingReviewBy` | `string?` | Có | ID người đã submit chờ duyệt |
| `managerRejectReason` | `string?` | Có | Lý do Manager từ chối (nếu có) |
| `createdByUserId` | `string` | Không | Người tạo |
| `createdAt` | `string` | Không | Thời điểm tạo (ISO 8601 UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

### `KbArticleListItemDTO` (item trong danh sách — `GET /api/knowledge-base`)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `title` | `string` | Tiêu đề |
| `category` | `TicketCategoryEnum` | Enum chuỗi |
| `status` | `KbArticleStatusEnum` | Enum chuỗi |
| `isTemplate` | `bool` | Bài viết mẫu |
| `viewCount` | `int` | Lượt xem |
| `helpfulCount` | `int` | Lượt hữu ích |
| `reviewRequired` | `bool` | Có đang chờ duyệt không |
| `createdAt` | `string` | Thời điểm tạo (UTC) |

> ⚠️ List item **KHÔNG** có `tags` (chỉ detail mới có). **CÓ** `reviewRequired` + `createdAt`.

### `KbArticleVersionDTO` (phiên bản trong lịch sử)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID phiên bản (`KbArticleVersion`) — dùng cho `compare`/`rollback` |
| `articleId` | `string` | ID bài viết gốc |
| `majorVersion` | `int` | Major version |
| `minorVersion` | `int` | Minor version |
| `status` | `KbVersionStatusEnum` | Enum chuỗi (e.g. `"Approved"`) |
| `title` / `symptoms` / `diagnosisSteps` / `solutionSteps` | `string` | Nội dung snapshot |
| `recommendedParts` | `string[]?` | Linh kiện snapshot |
| `tags` | `string[]` | Thẻ snapshot |
| `changeDescription` | `string` | Mô tả thay đổi của phiên bản |
| `changedBy` | `string` | Người thực hiện thay đổi |
| `createdAt` | `string` | Thời điểm tạo phiên bản (UTC) |

### `KbArticleDiffDTO` (kết quả `compare`)

| Field | Type | Mô tả |
|---|---|---|
| `fromVersion` | `string` | Nhãn phiên bản gốc |
| `toVersion` | `string` | Nhãn phiên bản đích |
| `titleDiff` / `symptomsDiff` / `diagnosisStepsDiff` / `solutionStepsDiff` / `recommendedPartsDiff` / `tagsDiff` | `DiffSection` | Diff từng trường |

**`DiffSection`:** `{ oldValue: string; newValue: string; isChanged: bool }`

### `KbArticleTemplateDTO` (kết quả `copy-template`)

| Field | Type | Mô tả |
|---|---|---|
| `category` | `TicketCategoryEnum` | Enum chuỗi |
| `symptoms` / `diagnosisSteps` / `solutionSteps` | `string` | Nội dung mẫu |
| `recommendedParts` | `string[]?` | Linh kiện mẫu |
| `tags` | `string[]` | Thẻ mẫu |

> Không có `id`/`title` — chỉ là cấu trúc để fill vào form tạo bài mới.

### `KbArticleSuggestDTO` (kết quả `suggest`)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `title` | `string` | Tiêu đề |
| `symptoms` | `string` | Triệu chứng |
| `helpfulCount` | `int` | Lượt hữu ích |
| `viewCount` | `int` | Lượt xem |
| `isInternalOnly` | `bool` | `true` = bài nội bộ, **không được** gán vào ticket với `referenceType = ProvidedToCustomer` (BE chặn `422`). ⚠️ Các endpoint suggest hiện **lọc sẵn** bài nội bộ khỏi kết quả nên giá trị thường là `false` |

### `KbArticleActionDTO`

Payload nhẹ dùng cho các hành động chuyển trạng thái.

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài viết |
| `code` | `string` | Mã bài viết |
| `status` | `KbArticleStatusEnum` | Trạng thái hiện tại sau thao tác (enum chuỗi) |

### `KbUsageStatsDTO`

| Field | Type | Mô tả |
|---|---|---|
| `kbArticleId` | `string` | ID bài viết |
| `kbArticleCode` | `string` | Mã bài viết |
| `kbArticleTitle` | `string` | Tiêu đề bài viết |
| `totalReferences` | `int` | Tổng số tham chiếu chưa bị xóa |
| `byType` | `KbUsageByTypeDTO` | Đếm tham chiếu theo từng `KbReferenceTypeEnum` |

**`KbUsageByTypeDTO`:**

| Field | Type | Mô tả |
|---|---|---|
| `consultedDuringResolve` | `int` | Số lần `ReferenceType = ConsultedDuringResolve` |
| `providedToCustomer` | `int` | Số lần `ReferenceType = ProvidedToCustomer` |
| `generatedAfterResolve` | `int` | Số lần `ReferenceType = GeneratedAfterResolve` |

### `TicketKbReferenceDTO` (Nhóm 11 — `GET .../references`)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bản ghi tham chiếu |
| `ticketId` | `string` | Không | ID ticket |
| `kbArticleId` | `string` | Không | ID bài viết KB |
| `kbArticleCode` | `string` | Không | Mã bài viết (snapshot lúc gán) |
| `kbArticleTitle` | `string?` | Có | Tiêu đề bài viết (join từ KB hiện tại) |
| `referencedByUserId` | `string` | Không | Người gán |
| `referenceType` | `KbReferenceTypeEnum` | Không | Loại tham chiếu (**chuỗi**, vd `"ConsultedDuringResolve"`) |
| `note` | `string?` | Có | Ghi chú |
| `createdAt` | `string` | Không | Thời điểm gán (UTC) |

---

## DTOs — Blog

### `BlogPostDTO` (detail — `GET /api/blog/{id}`)

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bài blog |
| `title` | `string` | Không | Tiêu đề |
| `slug` | `string` | Không | URL-friendly slug (unique) |
| `summary` | `string` | Không | Tóm tắt ngắn |
| `contentHtml` | `string` | Không | Nội dung HTML đầy đủ |
| `status` | `BlogPostStatusEnum` | Không | Enum chuỗi (e.g. `"Published"`) |
| `origin` | `BlogPostOriginEnum` | Không | Enum chuỗi (`"Manual"` hoặc `"AiGeneratedFromKb"`) |
| `sourceKbArticleId` | `string?` | Có | ID bài KB nguồn (chỉ có nếu `origin = AiGeneratedFromKb`) |
| `blogTemplateId` | `string?` | Có | ID template đã dùng (nếu có) |
| `authorUserId` | `string` | Không | ID người tạo |
| `currentVersion` | `int` | Không | Số phiên bản hiện tại |
| `createdAt` | `string` | Không | Thời điểm tạo (ISO 8601 UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

### `BlogPostListItemDTO` (item trong danh sách)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài blog |
| `title` | `string` | Tiêu đề |
| `slug` | `string` | Slug |
| `summary` | `string` | Tóm tắt ngắn |
| `status` | `BlogPostStatusEnum` | Enum chuỗi |
| `origin` | `BlogPostOriginEnum` | Enum chuỗi |
| `authorUserId` | `string` | ID người tạo |
| `currentVersion` | `int` | Phiên bản hiện tại |
| `createdAt` | `string` | Thời điểm tạo (UTC) |
| `updatedAt` | `string?` | Thời điểm cập nhật gần nhất |

### `BlogPostVersionDTO` (phiên bản trong lịch sử)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bản ghi version |
| `blogPostId` | `string` | ID bài blog gốc |
| `versionNumber` | `int` | Số thứ tự version (1, 2, 3...) |
| `title` | `string` | Tiêu đề snapshot |
| `summary` | `string` | Tóm tắt snapshot |
| `contentHtml` | `string` | Nội dung HTML snapshot |
| `changedByUserId` | `string` | ID người thực hiện thay đổi |
| `changeNote` | `string?` | Ghi chú thay đổi (nullable) |
| `createdAt` | `string` | Thời điểm tạo version (UTC) |

### `BlogDiffDTO` (kết quả `compare`)

| Field | Type | Mô tả |
|---|---|---|
| `oldVersionNumber` | `int` | Số version cũ |
| `newVersionNumber` | `int` | Số version mới |
| `oldContentHtml` | `string` | Nội dung HTML version cũ |
| `newContentHtml` | `string` | Nội dung HTML version mới |

> ⚠️ Blog diff **chỉ so sánh `contentHtml`** — không có diff từng field như KB. FE tự render diff từ 2 HTML string.

### `BlogPostActionDTO` (response nhẹ sau action)

| Field | Type | Mô tả |
|---|---|---|
| `id` | `string` | ID bài blog |
| `title` | `string` | Tiêu đề |
| `status` | `BlogPostStatusEnum` | Trạng thái sau thao tác (enum chuỗi) |
| `currentVersion` | `int` | Phiên bản hiện tại |

### `BlogTemplateDTO`

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID template |
| `name` | `string` | Không | Tên template — max 200 ký tự |
| `description` | `string` | Không | Mô tả template |
| `contentHtml` | `string` | Không | Nội dung HTML mẫu |
| `isActive` | `bool` | Không | Template đang hoạt động hay đã vô hiệu hóa |
| `createdByUserId` | `string` | Không | ID Admin tạo |
| `createdAt` | `string` | Không | Thời điểm tạo (UTC) |
| `updatedAt` | `string?` | Có | Thời điểm cập nhật gần nhất |

---

## Nhóm 8 — Knowledge Base (tra cứu — mọi role đã đăng nhập)

Base path: `/api/knowledge-base`
**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}` (mọi role đã đăng nhập). **KHÔNG anonymous.**

---

### `GET /api/knowledge-base`

**Mục đích:** Tìm kiếm và liệt kê bài viết Knowledge Base. **Lọc theo role:**
- **Customer:** chỉ trả bài `Published` và `IsInternalOnly = false`. Param `Status` bị bỏ qua.
- **Staff / Manager / Admin:** thấy mọi trạng thái; lọc tự do theo `Status` (kể cả `PendingReview`, `Draft`, `Archived`). → đây là cách Manager/Admin liệt kê hàng chờ duyệt.

**Auth:** Bắt buộc (mọi role đã đăng nhập).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Q` | `string?` | Từ khóa — tìm trong `title` và `symptoms` |
| `Category` | `TicketCategoryEnum?` | Lọc theo danh mục lỗi — gửi **chuỗi tên enum** (vd `Charging`) |
| `Status` | `KbArticleStatusEnum?` | Lọc theo trạng thái — gửi **chuỗi tên enum** (vd `Published`). **Chỉ áp dụng cho internal role**; Customer bị bỏ qua |
| `Tag` | `string?` | Lọc theo **một** thẻ (số ít — không phải mảng) |
| `IsTemplate` | `bool?` | Lọc bài viết mẫu — `true` = chỉ trả bài có `isTemplate = true` |
| `PageNumber` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang |

> ⚠️ Param đúng theo `GetKbArticleListQuery`: tên là **`Q`** (không phải `Keyword`), **`Tag`** số ít (không phải `Tags[]`).

**Response thành công `200`:** `CommonResponse<PaginationResponse<KbArticleListItemDTO>>`

---

### `GET /api/knowledge-base/{id}`

**Mục đích:** Lấy thông tin chi tiết một bài viết Knowledge Base để đọc. Không tự động tăng lượt xem.

**Auth:** Bắt buộc (mọi role đã đăng nhập).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết hoặc đã bị xóa.

---

### `GET /api/knowledge-base/suggest`

**Mục đích:** Gợi ý các bài viết liên quan **theo Ticket** (cùng `Category`, ưu tiên `HelpfulCount`/`ViewCount` cao). Trả tối đa 5 bài đã `Published` và **không phải bài nội bộ** (`isInternalOnly = true` bị lọc).

**Auth:** Bắt buộc (mọi role đã đăng nhập).

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `TicketId` | `Guid` | ✅ | ID Ticket để gợi ý bài viết liên quan |

> ⚠️ Param là **`TicketId` (Guid)** — không phải `query` text.

**Response thành công `200`:** `CommonResponse<KbArticleSuggestDTO[]>` (tối đa 5 phần tử)

**Lỗi thường gặp:**
- `404` — Không tìm thấy Ticket.

---

### `POST /api/knowledge-base/{id}/helpful`

**Mục đích:** Người dùng đánh giá bài viết là hữu ích (Tăng HelpfulCount).

**Auth:** Bắt buộc (mọi role đã đăng nhập).

> ⚠️ BE chỉ `article.HelpfulCount++` rồi `SaveChanges` — **KHÔNG dedup theo UserId, không chống spam**. Mỗi request là +1. Client nên tự chặn double-tap (disable nút sau khi gọi).

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy bài viết

---

### `GET /api/knowledge-base/{id}/usage-stats`

**Mục đích:** Thống kê số lần bài viết được dùng làm tài liệu tham khảo trong các Ticket, chia theo `KbReferenceTypeEnum`.

**Auth:** Bắt buộc — **chỉ role `Manager` hoặc `Admin`** (`[Authorize(Roles = "Manager,Admin")]`). Staff/Customer không gọi được.

**Path param:** `id` — UUID của bài viết Knowledge Base.

**Response thành công `200`:** `CommonResponse<KbUsageStatsDTO>`

```json
{
  "isSuccess": true,
  "data": {
    "kbArticleId": "guid",
    "kbArticleCode": "KB-2606-0001",
    "kbArticleTitle": "Pin không sạc được khi nhiệt độ thấp",
    "totalReferences": 12,
    "byType": {
      "consultedDuringResolve": 8,
      "providedToCustomer": 3,
      "generatedAfterResolve": 1
    }
  }
}
```

**Lỗi thường gặp:**
- `401` — Chưa đăng nhập
- `403` — Không có role Manager/Admin
- `404` — Không tìm thấy bài viết

---

## Nhóm 9 — Knowledge Base (Internal - Staff/Manager/Admin)

Base path: `/api/internal/knowledge-base`
**Auth:** Bắt buộc — role `Staff`, `Manager` hoặc `Admin` (`[Authorize(Roles = "Staff,Manager,Admin")]`)

---

### `POST /api/internal/knowledge-base`

**Mục đích:** Tạo mới một bài viết Knowledge Base.
Bài viết được khởi tạo ở trạng thái **`PendingReview`**, đồng thời tạo một bản `KbArticleVersion` (V1.0) ở trạng thái `Pending`. Cần Manager/Admin duyệt để xuất bản.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `category` | `TicketCategoryEnum` | ✅ | Danh mục lỗi — gửi **chuỗi tên enum** (vd `Charging`), phải là enum hợp lệ |
| `title` | `string` | ✅ | Tiêu đề — không rỗng, max 200 ký tự |
| `symptoms` | `string` | ✅ | Triệu chứng — không rỗng, max 2000 ký tự |
| `diagnosisSteps` | `string` | ✅ | Bước chẩn đoán — không rỗng, max 4000 ký tự |
| `solutionSteps` | `string` | ✅ | Bước xử lý — không rỗng, max 4000 ký tự |
| `recommendedParts` | `string[]?` | Không | Linh kiện khuyến nghị thay thế |
| `tags` | `string[]?` | Không | Từ khóa — tối đa 10 thẻ, mỗi thẻ ≤ 50 ký tự |
| `isInternalOnly` | `bool` | Không (mặc định `false`) | `true` = ẩn với khách hàng |
| `isTemplate` | `bool` | Không (mặc định `false`) | `true` = đánh dấu bài là mẫu để Staff có thể dùng `copy-template` |

**Response thành công `201`:** `CommonResponse<KbArticleActionDTO>` (trả về `id`, `code`, `status`)

**Lỗi thường gặp:**
- `400` — Validation field (`Title`/`Symptoms`/`DiagnosisSteps`/`SolutionSteps` rỗng hoặc quá độ dài; `Category` không hợp lệ; `Tags` > 10)

---

### `PUT /api/internal/knowledge-base/{id}`

**Mục đích:** Cập nhật nội dung bài viết hiện có.
Hệ thống tự động lưu bản hiện tại vào lịch sử. Trạng thái bài viết sẽ chuyển về PendingReview để chờ Manager duyệt (trừ khi người cập nhật là Manager/Admin hoặc chủ sở hữu bài viết).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết.

**Request body:** Cùng các field như Create, **thêm**:

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `changeDescription` | `string?` | Không | Mô tả thay đổi (lưu vào version history). BE **không validate** — `null`/rỗng vẫn chấp nhận, nên gửi để audit |

> ⚠️ `changeDescription` là `string?` — không bắt buộc, nhưng nên gửi để audit trail.

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

---

### `GET /api/internal/knowledge-base/{id}/versions`

**Mục đích:** Xem danh sách lịch sử các phiên bản của bài viết.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleVersionDTO[]>`

---

### `GET /api/internal/knowledge-base/{id}/versions/{versionId}`

**Mục đích:** Lấy chi tiết một phiên bản cụ thể trong lịch sử.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path params:** `id` — UUID bài viết · `versionId` — UUID phiên bản (`KbArticleVersion.id`).

**Response thành công `200`:** `CommonResponse<KbArticleVersionDTO>`

---

### `GET /api/internal/knowledge-base/{id}/compare`

**Mục đích:** So sánh sự khác biệt giữa hai phiên bản của bài viết.

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID bài viết.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `fromVersion` | `Guid` | ✅ | ID phiên bản gốc (`KbArticleVersion.id`) |
| `toVersion` | `Guid?` | Không | ID phiên bản đích. Bỏ trống → so sánh với **bản hiện tại** |

> ⚠️ `fromVersion`/`toVersion` là kiểu **`Guid`** (ID của version, không phải số version nguyên).

**Response thành công `200`:** `CommonResponse<KbArticleDiffDTO>` — 6 `DiffSection` (`titleDiff`, `symptomsDiff`, `diagnosisStepsDiff`, `solutionStepsDiff`, `recommendedPartsDiff`, `tagsDiff`), mỗi cái có `oldValue`/`newValue`/`isChanged`.

---

### `GET /api/internal/knowledge-base/templates/{id}`

**Mục đích:** Lấy chi tiết một bài viết Knowledge Base bất kỳ theo ID, kể cả bài là template hoặc không. Dùng khi cần preview nội dung template trước khi copy.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài viết (cần có `isTemplate = true` để dùng làm mẫu, nhưng endpoint này không validate điều đó).

**Response thành công `200`:** `CommonResponse<KbArticleDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài viết

---

### `GET /api/internal/knowledge-base/{id}/copy-template`

**Mục đích:** Sao chép cấu trúc bài viết mẫu để tạo bài mới. Chỉ áp dụng cho bài viết có `isTemplate = true` **hoặc** gắn tag **`template`** / **`example`** (so khớp không phân biệt hoa thường).

**Auth:** Bắt buộc (Staff, Manager, Admin)

**Path param:** `id` — UUID của bài viết mẫu.

**Response thành công `200`:** `CommonResponse<KbArticleTemplateDTO>` — gồm `category`, `symptoms`, `diagnosisSteps`, `solutionSteps`, `recommendedParts`, `tags` (**không** có `id`/`title`).

---

## Nhóm 10 — Knowledge Base (Admin/Manager Workflow)

Base path: `/api/admin/knowledge-base`
**Auth:** Bắt buộc — role `Manager` hoặc `Admin` (`[Authorize(Roles = "Manager,Admin")]`). Ngoại lệ: `DELETE` chỉ `Admin`.

Quản lý vòng đời bài viết: Phê duyệt / từ chối thay đổi, Xuất bản, Lưu trữ, Hoàn tác, Xóa.

---

### `POST /api/admin/knowledge-base/{id}/approve-review`

**Mục đích:** Chấp nhận các thay đổi (`PendingReview → Published`). Nội dung từ bản nháp được đắp lên bài viết chính.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/reject-review`

**Mục đích:** Từ chối thay đổi của Staff.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `reason` | `string` | ✅ | Lý do từ chối — không được rỗng/whitespace (`400` nếu thiếu) |

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/publish`

**Mục đích:** Xuất bản bài viết (→ `Published`).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/archive`

**Mục đích:** Lưu trữ bài viết (→ `Archived`, ngừng hiển thị với Customer).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `POST /api/admin/knowledge-base/{id}/rollback`

**Mục đích:** Hoàn tác nội dung bài viết về một phiên bản cũ trong lịch sử. Lấy nội dung phiên bản cũ đè lên bản hiện tại và tăng Version.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài viết.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `toVersionId` | `Guid` | ✅ | ID phiên bản (`KbArticleVersion.id`) cần khôi phục (`400` nếu thiếu/rỗng) |

**Response thành công `200`:** `CommonResponse<KbArticleActionDTO>`

---

### `DELETE /api/admin/knowledge-base/{id}`

**Mục đích:** Xóa mềm (soft delete) một bài viết Knowledge Base.

**Auth:** Bắt buộc — **chỉ role `Admin`** (`[Authorize(Roles = "Admin")]`). Manager KHÔNG được phép.

**Path param:** `id` — UUID bài viết.

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `403` — Không phải Admin
- `404` — Không tìm thấy bài viết

---

## Nhóm 11 — Ticket–KB References (Staff/Manager/Admin)

Base path: `/api/knowledge-base/references`
**Auth:** Bắt buộc — Staff, Manager hoặc Admin.

Gán bài viết Knowledge Base vào Ticket làm tài liệu tham khảo (lưu vết khi xử lý). `referencedByUserId` resolve từ JWT.

---

### `POST /api/knowledge-base/references`

**Mục đích:** Gán một bài viết KB vào một Ticket.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `ticketId` | `Guid` | ✅ | ID ticket |
| `kbArticleId` | `Guid` | ✅ | ID bài viết KB |
| `referenceType` | `KbReferenceTypeEnum` | ✅ | Loại tham chiếu |
| `note` | `string?` | ❌ | Ghi chú |

**Response thành công `200`:** `CommonResponse<object>` (idempotent theo cặp ticket+bài viết: nếu tham chiếu đã tồn tại — kể cả đã xóa mềm — sẽ được khôi phục và cập nhật `referenceType`/`note` mới).

**Quy tắc trạng thái ticket (theo `referenceType`):**

| Trạng thái ticket | `ConsultedDuringResolve` | `GeneratedAfterResolve` / `ProvidedToCustomer` |
|---|---|---|
| Trước `Resolved` (New → Escalated) | ✅ Gán được | ✅ Gán được |
| `Resolved` | ❌ `409` | ✅ Gán được — 2 type này về ngữ nghĩa xảy ra **lúc/sau khi resolve** |
| `ClosedPendingRate` / `Closed` | ❌ `409` | ❌ `409` |

**Quy tắc bài viết nội bộ:** bài có `isInternalOnly = true` **không thể** gán với `referenceType = ProvidedToCustomer` → `422`. Các type khác vẫn gán được bài nội bộ bình thường.

**Bảng status code:**

| Status | Trường hợp | `listErrors` |
|---|---|---|
| `400` | Lỗi field: `ticketId`/`kbArticleId` = Guid rỗng, `referenceType` không phải giá trị enum hợp lệ | Có — từng phần tử ghi rõ `field` + `detail` |
| `401` | Chưa đăng nhập | `null` |
| `403` | **Lỗi quyền:** Staff không phải người được phân công xử lý ticket (`assignedStaffId` khác), hoặc role không hợp lệ | `null` |
| `404` | Không tìm thấy Ticket hoặc Bài viết trong DB | `null` |
| `409` | **Xung đột trạng thái:** ticket đã chờ phê duyệt/hoàn thành theo bảng quy tắc trên | `null` |
| `422` | **Vi phạm rule nghiệp vụ:** bài nội bộ + `ProvidedToCustomer` | `null` |

---

### `GET /api/knowledge-base/references?ticketId={ticketId}`

**Mục đích:** Lấy danh sách bài viết KB đã gán cho một Ticket (sắp xếp mới nhất trước).

**Query param:** `ticketId` — UUID của ticket.

**Response thành công `200`:** `CommonResponse<TicketKbReferenceDTO[]>`

> Trả về toàn bộ array — **không pagination**. Chỉ trả các tham chiếu chưa bị xóa (`!IsDeleted`).

---

### `DELETE /api/knowledge-base/references/{referenceId}`

**Mục đích:** Gỡ một tham chiếu KB khỏi Ticket (xóa mềm).

**Path param:** `referenceId` — UUID của bản ghi tham chiếu (`TicketKbReferenceDTO.id`).

**Response thành công `200`:** `CommonResponse<object>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy tham chiếu

---

## Nhóm 12 — Blog (Public — mọi role đã đăng nhập)

Base path: `/api/blog`
**Auth:** Bắt buộc — `Authorization: Bearer {accessToken}` (mọi role đã đăng nhập).

Chỉ trả bài có `status = Published`. FE không cần filter thêm.

---

### `GET /api/blog`

**Mục đích:** Danh sách bài blog đã Published (endpoint public — mọi user đăng nhập xem được).

**Auth:** Bắt buộc (mọi role).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Origin` | `BlogPostOriginEnum?` | Lọc theo nguồn gốc — gửi chuỗi (`Manual` hoặc `AiGeneratedFromKb`) |
| `Page` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang (mặc định 20) |

> ⚠️ `Status` không có trong query params của public endpoint — controller tự set `Status = Published`. Không cần gửi.

**Response thành công `200`:** `CommonResponse<PaginationResponse<BlogPostListItemDTO>>`

---

### `GET /api/blog/{id}`

**Mục đích:** Lấy chi tiết bài blog đã Published.

**Auth:** Bắt buộc (mọi role).

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy hoặc bài chưa Published.

---

## Nhóm 13 — Blog (Internal — Staff/Manager/Admin)

Base path: `/api/internal/blog`
**Auth:** Bắt buộc — role `Staff`, `Manager` hoặc `Admin`.

Xem toàn bộ bài blog (kể cả `Draft`, `Generating`, `GenerationFailed`), soạn thảo thủ công, xem lịch sử version, so sánh version.

---

### `GET /api/internal/blog`

**Mục đích:** Danh sách bài blog — internal view (filter theo `Status`, xem mọi trạng thái).

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `Status` | `BlogPostStatusEnum?` | Lọc theo trạng thái — gửi chuỗi (vd `Draft`, `Generating`) |
| `Origin` | `BlogPostOriginEnum?` | Lọc theo nguồn gốc |
| `Page` | `int` | Trang (mặc định 1) |
| `PageSize` | `int` | Số item/trang (mặc định 20) |

**Response thành công `200`:** `CommonResponse<PaginationResponse<BlogPostListItemDTO>>`

---

### `POST /api/internal/blog`

**Mục đích:** Tạo bài blog thủ công — khởi tạo ở trạng thái `Draft`. Cần Manager/Admin publish để xuất bản.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `title` | `string` | ✅ | Tiêu đề — không rỗng, max 256 ký tự |
| `slug` | `string` | ✅ | URL slug — không rỗng, max 300 ký tự, phải unique |
| `summary` | `string` | ✅ | Tóm tắt — không rỗng |
| `contentHtml` | `string` | ✅ | Nội dung HTML — không rỗng |
| `blogTemplateId` | `Guid?` | Không | ID template muốn áp dụng |

**Response thành công `201`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `400` — Validation field (title/slug/summary/contentHtml rỗng hoặc quá độ dài)
- `409` — Slug đã tồn tại

---

### `PUT /api/internal/blog/{id}`

**Mục đích:** Cập nhật nội dung bài blog. Có **optimistic concurrency check** qua `currentVersion`.

Mỗi lần update thành công: tạo `BlogPostVersion` snapshot + tăng `CurrentVersion`.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `title` | `string` | ✅ | Tiêu đề — max 256 ký tự |
| `slug` | `string` | ✅ | Slug — phải unique (trừ slug hiện tại của bài) |
| `summary` | `string` | ✅ | Tóm tắt |
| `contentHtml` | `string` | ✅ | Nội dung HTML |
| `changeNote` | `string?` | Không | Ghi chú thay đổi (lưu vào version history) |
| `currentVersion` | `int` | ✅ | Phiên bản hiện tại — phải khớp với `BlogPost.currentVersion` trong DB. Dùng để phát hiện concurrent edit |

> ⚠️ `currentVersion` là **bắt buộc** và phải khớp chính xác với giá trị hiện tại trong DB. Nếu không khớp → `409`. Lấy từ `GET /api/internal/blog` hoặc `BlogPostActionDTO` của lần update trước.

**Lỗi `409` có thể gặp:**
- Version mismatch (concurrent edit): `"Bài viết đã được cập nhật bởi người khác. Vui lòng tải lại và thử lại."`
- Bài đang `Generating`: `"Bài viết đang được AI tạo, vui lòng thử lại sau."`
- Bài đã `Archived`: `"Bài viết đã được archive, không thể chỉnh sửa."`
- Slug trùng: `"Slug đã tồn tại."`

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>` (trả `currentVersion` mới sau update)

---

### `GET /api/internal/blog/templates`

**Mục đích:** Danh sách blog templates — dành cho Staff/Manager/Admin dùng khi soạn bài blog mới.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Query params:**

| Param | Type | Mô tả |
|---|---|---|
| `IsActive` | `bool?` | `true` = chỉ trả template đang hoạt động; `false` = chỉ inactive; bỏ trống = tất cả |

**Response thành công `200`:** `CommonResponse<List<BlogTemplateDTO>>`

---

### `GET /api/internal/blog/templates/{id}`

**Mục đích:** Chi tiết một blog template.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID template.

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template

---

### `GET /api/internal/blog/{id}/versions`

**Mục đích:** Danh sách lịch sử phiên bản của bài blog (mỗi lần `PUT` thành công tạo 1 version).

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<List<BlogPostVersionDTO>>`

---

### `GET /api/internal/blog/{id}/compare`

**Mục đích:** So sánh nội dung HTML giữa 2 phiên bản.

**Auth:** Bắt buộc (Staff, Manager, Admin).

**Path param:** `id` — UUID bài blog.

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `OldVersionNumber` | `int` | ✅ | Số version cũ hơn |
| `NewVersionNumber` | `int` | ✅ | Số version mới hơn |

> ⚠️ `OldVersionNumber`/`NewVersionNumber` là **số nguyên** (`versionNumber` trong `BlogPostVersionDTO`) — khác với KB compare dùng Guid.

**Response thành công `200`:** `CommonResponse<BlogDiffDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog hoặc version không tồn tại.

---

## Nhóm 14 — Blog (Admin/Manager Workflow)

Base path: `/api/admin/blog`
**Auth:** Bắt buộc — role `Manager` hoặc `Admin`.

Publish, Archive, xóa bài blog. Tạo blog từ KB bằng AI (async).

---

### `POST /api/admin/blog/generate-from-kb/{kbId}`

**Mục đích:** Tạo bài blog từ bài viết Knowledge Base bằng AI — **async, trả `202` ngay**, nội dung được điền sau qua event queue (RabbitMQ → DeepSeek → callback).

**Auth:** Manager hoặc Admin.

**Path param:** `kbId` — UUID bài viết KB nguồn.

**Luồng hoạt động:**
1. BE kiểm tra KB article `Published` và chưa có blog đang tồn tại.
2. Tạo `BlogPost` với `Status = Generating`, `Origin = AiGeneratedFromKb`.
3. Phát `BlogGenerationRequestedEvent` → RabbitMQ.
4. NotificationService/AI worker nhận event → gọi DeepSeek → điền nội dung → phát `BlogGenerationStatusChangedEvent`.
5. TicketService nhận callback → update `ContentHtml`, `Summary`, `Status = Draft`.

**Điều kiện bắt buộc:**
- KB article phải tồn tại và `status = Published` (→ `409` nếu không phải).
- KB article chưa có blog đang tồn tại với `status` không phải `Archived`/`GenerationFailed` (→ `409` nếu trùng).

**Response thành công `202`:** `CommonResponse<BlogPostActionDTO>` — `status = Generating`, `currentVersion = 0`.

```json
{
  "isSuccess": true,
  "statusCode": 202,
  "message": "Đã gửi yêu cầu tạo blog bằng AI. Bài viết sẽ sẵn sàng sau vài giây.",
  "data": {
    "id": "guid",
    "title": "Pin không sạc được khi nhiệt độ thấp",
    "status": "Generating",
    "currentVersion": 0
  }
}
```

**Lỗi thường gặp:**
- `404` — Không tìm thấy KB article
- `409` — KB chưa Published hoặc đã có blog đang tồn tại

> ⚠️ FE cần **poll** hoặc dùng notification để biết khi `status` chuyển `Draft` (generation hoàn tất) hoặc `GenerationFailed`. Sau khi `GenerationFailed`, vẫn có thể `PUT /api/internal/blog/{id}` để sửa thủ công.

---

### `POST /api/admin/blog/{id}/publish`

**Mục đích:** Publish bài blog (`Draft` → `Published`). Bài sẽ hiển thị trên `GET /api/blog`.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Lỗi `409` có thể gặp:**
- `status = Generating` hoặc `GenerationFailed`: `"Bài viết chưa sẵn sàng để publish."`
- `status = Published`: `"Bài viết đã được publish."`
- `status = Archived`: `"Bài viết đã được archive, không thể publish."`

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

---

### `POST /api/admin/blog/{id}/archive`

**Mục đích:** Lưu trữ bài blog (→ `Archived`, ngừng hiển thị trên public endpoint).

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog

---

### `DELETE /api/admin/blog/{id}`

**Mục đích:** Xóa mềm (soft delete) bài blog. Không thể khôi phục qua API.

**Auth:** Manager hoặc Admin.

**Path param:** `id` — UUID bài blog.

**Response thành công `200`:** `CommonResponse<BlogPostActionDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy bài blog

---

## Nhóm 15 — Blog Templates (Admin only — ghi)

Base path: `/api/admin/blog/templates`
**Auth:** **Chỉ Admin** (POST/PUT/DELETE). Để đọc template, dùng `GET /api/internal/blog/templates` (Nhóm 13).

---

### `POST /api/admin/blog/templates`

**Mục đích:** Tạo template mới.

**Auth:** **Chỉ Admin**.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `name` | `string` | ✅ | Tên template — không rỗng, max 200 ký tự |
| `description` | `string` | Không | Mô tả template |
| `contentHtml` | `string` | ✅ | Nội dung HTML mẫu — không rỗng |

**Response thành công `201`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `400` — Validation field (name/contentHtml rỗng hoặc name > 200 ký tự)

---

### `PUT /api/admin/blog/templates/{id}`

**Mục đích:** Cập nhật template (bao gồm toggle `isActive`).

**Auth:** **Chỉ Admin**.

**Path param:** `id` — UUID template.

**Request body:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `name` | `string` | ✅ | Tên template — max 200 ký tự |
| `description` | `string` | Không | Mô tả |
| `contentHtml` | `string` | ✅ | Nội dung HTML |
| `isActive` | `bool` | Không (mặc định `true`) | `false` để vô hiệu hóa template |

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template
- `400` — Validation field

---

### `DELETE /api/admin/blog/templates/{id}`

**Mục đích:** Xóa mềm template.

**Auth:** **Chỉ Admin**.

**Path param:** `id` — UUID template.

**Response thành công `200`:** `CommonResponse<BlogTemplateDTO>`

**Lỗi thường gặp:**
- `404` — Không tìm thấy template

---

## KB Chat Integration

Các endpoint trong hệ thống Chat liên quan đến Knowledge Base (xem chi tiết tại `api-ticket.md` — Nhóm Ticket Chats):

| Method | Path | Auth | Mô tả |
|---|---|---|---|
| `POST` | `/api/tickets/{ticketId}/chats/{id}/attach-kb` | Staff/Manager/Admin | Gắn KB article vào chat |
| `POST` | `/api/tickets/{ticketId}/chats/{id}/to-kb-draft` | Staff/Manager/Admin | Chuyển chat thành KB Draft |
| `GET` | `/api/tickets/{ticketId}/chats/{id}/kb-suggestions` | Staff/Manager/Admin | Gợi ý KB articles liên quan đến chat |

---

## Changelog

### 2026-07-19 — Di chuyển Blog Template GET, thêm KB Template GET (feat/GH-671-blog)

- **`GET /api/admin/blog/templates`** và **`GET /api/admin/blog/templates/{id}`**: **đã xóa** khỏi admin controller. Hai endpoint này chuyển sang Nhóm 13.
- **`GET /api/internal/blog/templates`** (mới — Nhóm 13): Danh sách blog templates, auth Staff/Manager/Admin.
- **`GET /api/internal/blog/templates/{id}`** (mới — Nhóm 13): Chi tiết blog template, auth Staff/Manager/Admin.
- **`GET /api/internal/knowledge-base/templates/{id}`** (mới — Nhóm 9): Lấy chi tiết bài viết KB kể cả là template, dùng để preview trước khi copy.
- **Nhóm 15**: Đổi mô tả — chỉ còn POST/PUT/DELETE (Admin only); đọc template qua Nhóm 13.

### 2026-07-18 — Thêm Blog module (feat/GH-671-blog)

- **Enums mới:** `BlogPostStatusEnum` (Generating/GenerationFailed/Draft/Published/Archived), `BlogPostOriginEnum` (Manual/AiGeneratedFromKb).
- **DTOs mới:** `BlogPostDTO`, `BlogPostListItemDTO`, `BlogPostVersionDTO`, `BlogDiffDTO`, `BlogPostActionDTO`, `BlogTemplateDTO`.
- **Nhóm 12** — `GET /api/blog`, `GET /api/blog/{id}`: public blog read (Published only).
- **Nhóm 13** — `GET /api/internal/blog`, `POST /api/internal/blog`, `PUT /api/internal/blog/{id}`, `GET /api/internal/blog/{id}/versions`, `GET /api/internal/blog/{id}/compare`: soạn thảo và lịch sử version.
- **Nhóm 14** — `POST /api/admin/blog/generate-from-kb/{kbId}` (async AI, `202`), `POST .../publish`, `POST .../archive`, `DELETE .../{id}`.
- **Nhóm 15** — CRUD template qua `/api/admin/blog/templates` (đọc: Staff+; ghi: Admin only).

### 2026-07-17 — Thêm field `isTemplate` (feat/GH-671.2)

- **`KbArticleDTO`:** thêm field `isTemplate` (`bool`) — đánh dấu bài viết là mẫu.
- **`KbArticleListItemDTO`:** thêm field `isTemplate` để FE filter bài mẫu trong list.
- **`POST /api/internal/knowledge-base`:** thêm field `isTemplate` vào request body (mặc định `false`).
- **`PUT /api/internal/knowledge-base/{id}`:** hỗ trợ cập nhật `isTemplate`.
- **`GET /api/knowledge-base`:** thêm query param `IsTemplate` (`bool?`) để lọc bài mẫu.
- **`GET /api/internal/knowledge-base/{id}/copy-template`:** mở rộng điều kiện — ngoài tag `template`/`example`, bài có `isTemplate = true` cũng dùng được.

### 2026-07-07 — KB reference rules update
- **`POST /api/knowledge-base/references`:** (1) nới quy tắc trạng thái — state `Resolved` cho phép gán 2 type after-resolve (`GeneratedAfterResolve`, `ProvidedToCustomer`); (2) chặn bài `isInternalOnly` với type `ProvidedToCustomer`; (3) chuẩn hóa status code: state lock đổi `403` → **`409`**, rule nội bộ trả **`422`**, `403` chỉ còn cho lỗi quyền.
- **`KbArticleSuggestDTO`:** thêm field `isInternalOnly` (bool).

### 2026-06-22 — Fix KB enum bị khai sai kiểu `int`
- `GetKbArticleListQuery.Category`/`.Status`, `KbArticleVersionDTO.status`, `KbArticleTemplateDTO.category` đổi sang đúng enum chuỗi — KHÔNG còn nhận/trả số.
- Bổ sung endpoint `GET /api/knowledge-base/{id}/usage-stats` (Manager/Admin only).
