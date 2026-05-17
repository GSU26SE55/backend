# API Documentation — FileStorageService

> Base URL: `http://localhost:{port}/api`
> Content-Type mặc định: `application/json` (trừ upload dùng `multipart/form-data`)
> Response wrapper chuẩn: `CommonResponse<T>`

---

## Cấu trúc Response chung

```json
{
  "isSuccess": true,
  "statusCode": 200,
  "message": "...",
  "data": { ... },
  "listErrors": []
}
```

**Lưu ý quan trọng:** Các endpoint download (`GET /download`, `GET /{id}/download`) khi **thành công** trả về binary stream trực tiếp (không bọc trong `CommonResponse`). Chỉ khi lỗi mới trả JSON.

---

## Enums

### `FilePurposeEnum`

| Giá trị | Int | Ý nghĩa | Sử dụng khi |
|---|---|---|---|
| `Other` | 0 | Mục đích chung / không phân loại | Không thuộc loại nào dưới đây |
| `Avatar` | 1 | Ảnh đại diện tài khoản | Upload avatar user — kết hợp với `AccountProfile.AvatarFileId` |
| `TicketAttachment` | 2 | File đính kèm trong ticket hỗ trợ | Upload khi tạo/comment ticket |
| `MaintenancePhoto` | 3 | Ảnh chụp trong quá trình bảo trì | Upload khi staff log bảo trì |
| `KbImage` | 4 | Hình ảnh trong knowledge base | Upload cho bài viết KB |
| `Firmware` | 5 | File firmware thiết bị | Upload firmware cho battery management system |

### `FileStatusEnum`

| Giá trị | Int | Ý nghĩa | Có thể tải không |
|---|---|---|---|
| `Uploaded` | 0 | Vừa upload xong, chưa qua xử lý | Có (legacy state) |
| `Processing` | 1 | Đang trong pipeline xử lý (virus scan, resize...) | **Không** — download/presigned-url trả `409 Conflict` |
| `Ready` | 2 | Đã xử lý xong, sẵn sàng phục vụ | Có |
| `Quarantined` | 3 | Bị cách ly (phát hiện virus hoặc nội dung vi phạm) | **Không** — trả 409 |
| `Deleted` | 4 | Đã bị xóa (soft delete) | **Không** — trả 404 |

> **Sprint 1:** Pipeline xử lý (resize, virus scan) chưa active. File upload xong được lưu metadata với trạng thái `Ready`. `Uploaded` là legacy state để tương thích dữ liệu cũ.

---

## Flow khuyến nghị

```
1. Upload file:
   POST /api/files/upload
   → nhận fileId + objectKey

2. Lưu fileId vào domain service (e.g., AccountProfile.AvatarFileId)

3. Hiển thị/tải file:
   - Tải trực tiếp qua server:  GET /api/files/{fileId}/download
   - Presigned URL (bypass server): GET /api/files/{fileId}/presigned-url

4. Xóa file:
   DELETE /api/files/{fileId}
```

**Ưu tiên dùng endpoint theo `fileId`** thay vì `objectKey` cho các service mới. Endpoint theo `objectKey` giữ lại để tương thích ngược.

---

## FE Feedback Resolution — 2026-05-17

| Mã | Trạng thái BE | Ghi chú |
|---|---|---|
| C1 | Done | Đã document Authorization Scope; code enforce `CreatedBy` + role/purpose qua JWT claim. Resource-based access theo ticket/branch/assignment phải đi qua service sở hữu resource. |
| C2 | Accepted risk + documented | Presigned URL đã cấp không revoke được bằng DB status; khuyến nghị `expiresInMinutes=1` cho file nhạy cảm và move/xóa object khi quarantine active. |
| C3 | Fixed + documented | Endpoint legacy theo `objectKey` hiện lookup metadata DB, enforce owner/status; `Processing`/`Quarantined` trả `409`, `Deleted`/missing metadata trả `404`. |
| H1 | Done | Đã liệt kê extension whitelist theo `FilePurposeEnum`. |
| H2 | Done | Firmware dùng chung upload endpoint, chỉ Admin, size limit 20 MB, domain service firmware/device/battery lưu `fileId`. |
| H3 | Done | `Processing` trả `409`; FE polling metadata mỗi 2-5 giây nếu cần chờ `Ready`. |
| H4 | Done | File binary >20 MB trả `413`; middleware cũng trả JSON chuẩn khi ASP.NET Core reject request trước controller. |
| M1 | Fixed + deprecated | `DELETE ?objectKey=` hiện soft-delete metadata để tránh orphan record, nhưng vẫn deprecated cho FE mới. |
| M2 | Done | Cả hai endpoint presigned-url validate `expiresInMinutes` trong khoảng `1-1440`. |
| M3 | Done | Đã document thứ tự clear domain reference trước, rồi FileStorage cleanup; lỗi cleanup cần retry/job bù trừ. |

---

## Authorization Scope

FileStorageService chỉ biết metadata file (`purpose`, `status`, `createdBy`) và không biết ticket/branch/maintenance-log nào đang tham chiếu file. Vì vậy authorization trực tiếp tại FileStorage được enforce theo rule an toàn sau:

| Endpoint | Admin | Manager | Staff | Customer | Ghi chú |
|---|---|---|---|---|---|
| `POST /api/files/upload` | Có | Có | Có | Có | Mọi role đăng nhập được upload file cho chính mình. `Firmware` chỉ Admin. `KbImage` chỉ Admin/Manager. |
| `GET /api/files/{id}/metadata` | Mọi file | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | Ticket/maintenance attachment của người khác phải đi qua domain service sở hữu resource. |
| `GET /api/files/{id}/download` | Mọi file | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | `Processing`/`Quarantined` không tải được. |
| `GET /api/files/{id}/presigned-url` | Mọi file | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | Avatar/KB + file do chính mình upload | Rule giống download. |
| `DELETE /api/files/{id}` | Mọi file | File do chính mình upload, trừ `Firmware` | File do chính mình upload, trừ `Firmware` | File do chính mình upload, trừ `Firmware` | Domain reference phải được clear ở service sở hữu resource trước khi xóa file. |
| Endpoint theo `objectKey` | Giống endpoint theo `fileId` tương ứng | Giống endpoint theo `fileId` tương ứng | Giống endpoint theo `fileId` tương ứng | Giống endpoint theo `fileId` tương ứng | Endpoint legacy hiện vẫn lookup metadata DB để enforce owner/status. |

**Resource-based access:** nếu Manager cần xem ticket attachment thuộc branch hoặc Staff cần xem ảnh bảo trì của ticket được assign, Ticket/Maintenance service phải expose endpoint nghiệp vụ riêng và tự kiểm tra quyền theo ticket/branch/assignment trước khi proxy hoặc cấp file access. Không để FE gọi trực tiếp FileStorage cho file không do user hiện tại upload.

**Ownership check:** backend so sánh `UploadedFile.CreatedBy` với `AccountId`/`NameIdentifier` trong JWT. Nếu không phải owner và không thỏa rule role/purpose ở bảng trên, API trả `403 Forbidden`.

**JWT claim mapping:** FileStorageService resolve user hiện tại từ claim `NameIdentifier` hoặc `AccountId`; role được đọc từ claim `role` hoặc role claim chuẩn của ASP.NET Core. Nếu token thiếu user id hợp lệ, các thao tác upload/read/delete sẽ bị xem là không đủ quyền.

**Presigned URL risk:** presigned URL được issue tại thời điểm gọi API. Nếu file bị chuyển sang `Quarantined` sau đó, URL đã cấp vẫn có thể hoạt động trực tiếp với object storage đến khi hết hạn. Với file nhạy cảm, FE/domain service nên truyền `expiresInMinutes=1`; khi pipeline quarantine active, backend nên move/xóa object khỏi bucket phục vụ download để đóng window này.

---

## Endpoint

### `POST /api/files/upload`

**Mục đích:** Upload một file lên object storage. Ghi metadata vào database. Trả về `fileId` và `objectKey`.

**Auth:** Bắt buộc. Mọi role đăng nhập được upload file cho chính mình; riêng `Firmware` chỉ Admin, `KbImage` chỉ Admin/Manager.

**Content-Type:** `multipart/form-data`

**Giới hạn kích thước:** Tối đa 20 MB cho mọi `purpose`, bao gồm `Firmware`. Controller cho phép request multipart lớn hơn một chút để chứa form overhead; file binary vẫn bị validate ở mức 20 MB.

**Form fields:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `file` | `IFormFile` | **Bắt buộc** | File binary cần upload |
| `folderName` | `string` | Không (mặc định `default`) | Thư mục logic để nhóm file trong storage. Ví dụ: `avatars`, `reports`, `warranty-documents` |
| `purpose` | `FilePurposeEnum` | Không (mặc định `Other = 0`) | Mục đích sử dụng file (xem enum bên trên) |

**Validation:**
- `file` không được null
- Kích thước file không được vượt quá 20 MB
- Phần mở rộng file phải nằm trong whitelist theo `purpose`

**Extension whitelist:**

| Purpose | Định dạng được phép |
|---|---|
| `Other (0)` | `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`, `.pdf`, `.doc`, `.docx`, `.xls`, `.xlsx`, `.txt`, `.csv` |
| `Avatar (1)` | `.jpg`, `.jpeg`, `.png`, `.webp` |
| `TicketAttachment (2)` | `.jpg`, `.jpeg`, `.png`, `.pdf`, `.doc`, `.docx` |
| `MaintenancePhoto (3)` | `.jpg`, `.jpeg`, `.png` |
| `KbImage (4)` | `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif` |
| `Firmware (5)` | `.bin`, `.hex`, `.fw` |

**Firmware upload flow (`purpose=5`):**
- Dùng chung `POST /api/files/upload`, không có endpoint riêng trong FileStorageService.
- Chỉ Admin được upload.
- Size limit hiện tại vẫn là 20 MB.
- Sau upload, service quản lý firmware/device/battery phải lưu `fileId` vào entity nghiệp vụ tương ứng. FileStorageService không tự associate firmware với device model hoặc battery type.

**Response thành công `201`:**
```json
{
  "isSuccess": true,
  "statusCode": 201,
  "data": {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "objectKey": "avatars/3fa85f64-abc1-...png",
    "fileName": "my-photo.png",
    "contentType": "image/png",
    "size": 204800,
    "publicUrl": null
  }
}
```

**Chi tiết `FileUploadResponse`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `fileId` | `Guid` | Không | ID metadata ổn định. **Lưu field này vào domain service để tham chiếu** |
| `objectKey` | `string` | Không | Khóa định danh file trong object storage (e.g., `avatars/abc123.png`). Tên file được sinh bằng GUID, không giữ tên gốc |
| `fileName` | `string` | Không | Tên file gốc client gửi lên (e.g., `my-photo.png`) |
| `contentType` | `string` | Không | MIME type (e.g., `image/png`, `application/pdf`) |
| `size` | `long` | Không | Kích thước file theo byte |
| `publicUrl` | `string?` | Null nếu storage không cấu hình public base URL | URL public trực tiếp nếu bucket public; ngược lại null và phải dùng download/presigned-url |

**Lỗi thường gặp:**
- `400` — Không có file trong request
- `400 isSuccess=false` — File rỗng, thiếu phần mở rộng, hoặc phần mở rộng không hợp lệ với `purpose`
- `403` — Không đủ quyền upload với `purpose` yêu cầu, ví dụ non-Admin upload `Firmware`
- `413 isSuccess=false` — File vượt quá 20 MB. Nếu request bị ASP.NET Core reject trước controller, body vẫn theo JSON lỗi của middleware với `statusCode=413`
- `500` — Lỗi ghi lên object storage hoặc lưu metadata DB

**Response `413` khi file vượt 20 MB:**

Nếu handler đọc được multipart và thấy binary >20 MB:

```json
{
  "isSuccess": false,
  "statusCode": 413,
  "message": "File vượt quá giới hạn 20 MB.",
  "data": null,
  "listErrors": [
    {
      "field": "file",
      "detail": "Kích thước file tối đa là 20 MB."
    }
  ]
}
```

Nếu ASP.NET Core reject request trước controller vì multipart request quá lớn, `GlobalExceptionMiddleware` vẫn trả JSON:

```json
{
  "isSuccess": false,
  "statusCode": 413,
  "message": "Request payload too large. File tối đa 20 MB.",
  "data": null,
  "listErrors": [
    {
      "field": "file",
      "detail": "Kích thước request vượt quá giới hạn cho phép."
    }
  ]
}
```

**Lưu ý:** Nếu upload binary thành công nhưng lưu metadata DB thất bại, handler sẽ cố gắng xóa object vừa upload để tránh file mồ côi. Nếu cleanup này cũng lỗi, lỗi metadata vẫn được giữ nguyên và object mồ côi cần được cleanup bằng job vận hành.

---

### `GET /api/files/{id}/metadata`

**Mục đích:** Lấy metadata của file theo `fileId`. Không tải binary, không tạo presigned URL.

**Auth:** Bắt buộc

**Path param:**

| Param | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | FileId nhận được từ response upload |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "objectKey": "avatars/3fa85f64-abc1-...png",
    "fileName": "my-photo.png",
    "contentType": "image/png",
    "size": 204800,
    "folderName": "avatars",
    "purpose": 1,
    "status": 2,
    "publicUrl": null,
    "createdAt": "2026-05-16T08:00:00Z",
    "updatedAt": null
  }
}
```

**Chi tiết `FileMetadataResponse`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `fileId` | `Guid` | Không | ID metadata |
| `objectKey` | `string` | Không | Khóa file trong object storage |
| `fileName` | `string` | Không | Tên file gốc (originalFileName) |
| `contentType` | `string` | Không | MIME type |
| `size` | `long` | Không | Kích thước (byte) |
| `folderName` | `string` | Không | Thư mục logic đã upload |
| `purpose` | `FilePurposeEnum` | Không | Mục đích sử dụng (xem enum) |
| `status` | `FileStatusEnum` | Không | Trạng thái hiện tại của file (xem enum) |
| `publicUrl` | `string?` | Null nếu không có public URL | URL public nếu bucket cấu hình public |
| `createdAt` | `DateTime` | Không | Thời điểm upload (UTC) |
| `updatedAt` | `DateTime?` | Null nếu chưa cập nhật | Thời điểm cập nhật metadata gần nhất (UTC) |

**Lỗi thường gặp:**
- `400` — `fileId` là empty GUID
- `401` — Chưa đăng nhập
- `403` — File không thuộc quyền truy cập của account hiện tại
- `404` — Không tìm thấy metadata hoặc file đã bị xóa (status `Deleted`)

**Use case:**
- FE hiển thị tên file, dung lượng trong màn hình profile/ticket
- Các service khác kiểm tra `fileId` tồn tại và `status` trước khi gắn vào entity nghiệp vụ

---

### `GET /api/files/download?objectKey={key}`

**Mục đích:** Tải nội dung file trực tiếp qua server theo `objectKey` (endpoint cũ, tương thích ngược).

> **DEPRECATED cho FE mới:** FE và service mới phải dùng `GET /api/files/{id}/download`. Endpoint này vẫn tồn tại cho tương thích ngược nhưng hiện đã lookup metadata DB để enforce owner/status, không còn đọc thẳng object storage.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `objectKey` | `string` | **Bắt buộc** | Khóa file trong object storage (e.g., `avatars/abc123.png`) |

**Cách hoạt động:**
1. Chuẩn hóa `objectKey`
2. Lookup `UploadedFile` trong metadata DB
3. Check quyền truy cập giống endpoint theo `fileId`
4. Nếu status = `Processing` hoặc `Quarantined` → trả `409 Conflict`
5. Nếu status = `Deleted` hoặc không tìm thấy metadata → trả `404`
6. Nếu hợp lệ, đọc binary từ object storage

**Response thành công `200`:**
- Body: Binary stream của file
- Header `Content-Type`: MIME type của file (e.g., `image/png`)
- Header `Content-Disposition`: `attachment; filename="original-name.png"`

> **Không bọc trong `CommonResponse`** — response trực tiếp là binary. Client xử lý như file download.

**Lỗi thường gặp:**
- `400` — `objectKey` rỗng hoặc chứa path traversal (`..`)
- `401` — Chưa đăng nhập
- `403` — File không thuộc quyền truy cập của account hiện tại
- `404` — Không tìm thấy metadata hoặc file đã bị xóa
- `409` — File đang `Processing` hoặc `Quarantined`
- `500` — Object storage không khả dụng

---

### `GET /api/files/{id}/download`

**Mục đích:** Tải nội dung file trực tiếp qua server theo `fileId` (endpoint mới, khuyến nghị dùng).

**Auth:** Bắt buộc

**Path param:**

| Param | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | FileId cần tải |

**Cách hoạt động:**
1. Validate `fileId` khác empty GUID
2. Tìm metadata file chưa bị soft-delete
3. Check quyền truy cập theo Authorization Scope
4. Nếu status = `Processing` hoặc `Quarantined` → trả `409 Conflict`
5. Dùng `objectKey` trong metadata để đọc stream từ object storage
6. Trả binary stream về client

**Response thành công `200`:** Binary stream (giống endpoint cũ theo objectKey)

**Lỗi thường gặp:**
- `400` — `fileId` là empty GUID
- `401` — Chưa đăng nhập
- `403` — File không thuộc quyền truy cập của account hiện tại
- `404` — Không tìm thấy metadata hoặc file đã bị xóa
- `409` — File đang được xử lý (`Processing`) hoặc bị cách ly (`Quarantined`), không thể tải

**Polling khi `Processing`:** FE nên gọi `GET /api/files/{id}/metadata` mỗi 2–5 giây cho đến khi `status=Ready`, hoặc dừng và hiển thị lỗi nếu nhận `Quarantined`/`Deleted`.

**Use case điển hình:** Avatar display — AuthService trả `displayAvatarUrl` dạng `/api/files/{fileId}/download`. FE set `<img src="/api/files/{fileId}/download">`.

---

### `GET /api/files/presigned-url?objectKey={key}&expiresInMinutes={n}`

**Mục đích:** Tạo presigned URL để client tải file trực tiếp từ object storage (không qua server).

> **DEPRECATED cho FE mới:** FE và service mới phải dùng `GET /api/files/{id}/presigned-url`. Endpoint theo `objectKey` vẫn lookup metadata DB để enforce owner/status.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `objectKey` | `string` | **Bắt buộc** | Không được rỗng | Khóa file trong storage |
| `expiresInMinutes` | `int` | Không (mặc định 15) | 1–1440 | Thời gian hiệu lực URL (phút) |

**Response thành công `200`:**
```json
{
  "isSuccess": true,
  "statusCode": 200,
  "data": "https://s3.example.com/bucket/avatars/abc123.png?X-Amz-Signature=..."
}
```

| Field | Type | Mô tả |
|---|---|---|
| `data` | `string` | Presigned URL để tải file trực tiếp từ storage provider |

**Lỗi thường gặp:**
- `400` — `objectKey` rỗng/chứa path traversal (`..`) hoặc `expiresInMinutes` ngoài khoảng 1–1440
- `401` — Chưa đăng nhập
- `403` — File không thuộc quyền truy cập của account hiện tại
- `404` — Không tìm thấy metadata hoặc file đã bị xóa
- `409` — File đang `Processing` hoặc `Quarantined`
- `500` — Lỗi tạo URL từ storage provider

**Lưu ý bảo mật:**
- Bất kỳ ai có URL trong thời gian còn hiệu lực đều có thể tải file
- Không log hoặc share presigned URL ở nơi công khai
- Với file nhạy cảm, dùng thời gian hết hạn ngắn (1–5 phút)
- Nếu file bị `Quarantined` sau khi URL đã được issue, URL hiện hành vẫn hoạt động đến khi hết hạn vì object storage không biết status trong DB. Với file nhạy cảm, dùng `expiresInMinutes=1`.

---

### `GET /api/files/{id}/presigned-url?expiresInMinutes={n}`

**Mục đích:** Tạo presigned URL để tải file theo `fileId` (endpoint mới, khuyến nghị).

**Auth:** Bắt buộc

**Path param:**

| Param | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | FileId cần tạo presigned URL |

**Query params:**

| Param | Type | Bắt buộc | Validation | Mô tả |
|---|---|---|---|---|
| `expiresInMinutes` | `int` | Không (mặc định 15) | 1–1440 | Thời gian hiệu lực URL |

**Cách hoạt động:**
1. Validate `fileId` và `expiresInMinutes`
2. Tìm metadata file chưa bị xóa
3. Check quyền truy cập theo Authorization Scope
4. Nếu status = `Processing` hoặc `Quarantined` → trả `409`
5. Tạo presigned URL từ `objectKey` trong metadata

**Response thành công `200`:** Giống endpoint cũ — `data` là presigned URL string.

**Lỗi thường gặp:**
- `400` — `fileId` không hợp lệ hoặc `expiresInMinutes` ngoài khoảng
- `401` — Chưa đăng nhập
- `403` — File không thuộc quyền truy cập của account hiện tại
- `404` — File không tìm thấy
- `409` — File đang được xử lý hoặc bị cách ly

**Lưu ý bảo mật:** Presigned URL đã cấp không thể bị thu hồi bằng cách đổi `FileStatusEnum` trong DB. Nếu file bị quarantine sau khi issue URL, URL vẫn sống đến expiry; dùng `expiresInMinutes=1` cho file nhạy cảm.

---

### `DELETE /api/files?objectKey={key}`

**Mục đích:** Xóa file theo `objectKey` (endpoint cũ, tương thích ngược).

> **DEPRECATED — FE mới không dùng endpoint này.**
> FE và service mới phải dùng `DELETE /api/files/{id}`. Endpoint này hiện đã được sửa để lookup metadata DB và soft-delete metadata nhằm tránh orphan record, nhưng vẫn không nên dùng trong flow mới vì `fileId` ổn định hơn `objectKey`.

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `objectKey` | `string` | **Bắt buộc** | Khóa file cần xóa |

**Cách hoạt động:**
1. Chuẩn hóa `objectKey`
2. Lookup metadata DB
3. Check quyền xóa giống endpoint theo `fileId`
4. Xóa object vật lý trong storage
5. Đánh dấu metadata record là `Deleted` (soft delete)

**Response thành công `204`:** Không có body (No Content).

**Lỗi thường gặp:**
- `400` — `objectKey` rỗng hoặc chứa path traversal (`..`)
- `401` — Chưa đăng nhập
- `403` — Không có quyền xóa file này
- `404` — Không tìm thấy metadata hoặc file đã bị xóa
- `500` — Lỗi xóa file từ storage

---

### `DELETE /api/files/{id}`

**Mục đích:** Xóa file theo `fileId` (endpoint mới, khuyến nghị). Xóa object storage VÀ đánh dấu metadata là `Deleted`.

**Auth:** Bắt buộc

**Path param:**

| Param | Type | Mô tả |
|---|---|---|
| `id` | `Guid` | FileId cần xóa |

**Cách hoạt động:**
1. Validate `fileId`
2. Tìm metadata file chưa bị xóa
3. Check quyền xóa theo Authorization Scope
4. Xóa object vật lý trong storage theo `objectKey`
5. Đánh dấu metadata record là `Deleted` (soft delete)

**Response thành công `204`:** Không có body.

**Lưu ý:**
- Sau khi xóa, các endpoint metadata, download, presigned-url sẽ trả `404` cho fileId này
- Endpoint này không tự gỡ tham chiếu ở domain service khác (e.g., `AccountProfile.AvatarFileId`, ticket attachment, maintenance photo)
- FE không nên gọi trực tiếp FileStorage để xóa file đang được domain service tham chiếu. FE nên gọi endpoint nghiệp vụ của service sở hữu resource để service đó clear reference trước, rồi gọi FileStorage cleanup.

**Sau khi xóa, caller/domain service phải xử lý reference:**

| Purpose | Domain service cần update | Field/reference cần clear | Thứ tự khuyến nghị |
|---|---|---|---|
| `Avatar (1)` | AuthService | `AccountProfile.AvatarFileId = null` | Clear reference trước, sau đó gọi FileStorage delete |
| `TicketAttachment (2)` | Ticket/Maintenance service | Attachment reference trong ticket/comment | Remove attachment khỏi ticket trước, sau đó gọi FileStorage delete |
| `MaintenancePhoto (3)` | Ticket/Maintenance service | Photo reference trong maintenance log | Remove photo reference trước, sau đó gọi FileStorage delete |
| `KbImage (4)` | KnowledgeBase service | Image reference trong bài viết KB | Remove image khỏi content/reference trước, sau đó gọi FileStorage delete |
| `Firmware (5)` | Battery/Device/Firmware service | Firmware file reference | Disable/unpublish firmware reference trước, sau đó gọi FileStorage delete |

Nếu bước FileStorage delete thất bại sau khi domain reference đã clear, service sở hữu resource phải retry cleanup hoặc đưa vào job bù trừ. Cách này ưu tiên tránh dangling reference làm FE render broken image/link.

**Lỗi thường gặp:**
- `400` — `fileId` là empty GUID
- `401` — Chưa đăng nhập
- `403` — Không có quyền xóa file này
- `404` — File không tìm thấy hoặc đã bị xóa

---

## So sánh endpoint theo objectKey vs fileId

| Thao tác | Endpoint cũ (objectKey) | Endpoint mới (fileId) | Khuyến nghị |
|---|---|---|---|
| Download | `GET /download?objectKey=` | `GET /{id}/download` | Dùng theo fileId |
| Presigned URL | `GET /presigned-url?objectKey=` | `GET /{id}/presigned-url` | Dùng theo fileId |
| Metadata | — | `GET /{id}/metadata` | Chỉ có theo fileId |
| Xóa | `DELETE ?objectKey=` | `DELETE /{id}` | Dùng theo fileId (xóa cả metadata) |

---

## Bảng mã lỗi HTTP

| HTTP Code | Ý nghĩa |
|---|---|
| `201` | Upload thành công |
| `200` | Thành công (metadata, presigned-url) |
| `204` | Xóa thành công, không có body |
| `400` | Request không hợp lệ (thiếu file, fileId empty, expiresInMinutes sai khoảng) |
| `401` | Chưa đăng nhập hoặc token hết hạn |
| `403` | Đã đăng nhập nhưng file không thuộc quyền truy cập/xóa hoặc role không được upload purpose đó |
| `404` | File không tìm thấy hoặc đã bị xóa |
| `409` | File đang ở trạng thái Processing hoặc Quarantined, không thể tải/tạo presigned URL |
| `413` | Upload vượt giới hạn 20 MB |
| `500` | Lỗi object storage hoặc lỗi hệ thống |
