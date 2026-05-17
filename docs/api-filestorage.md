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
| `Processing` | 1 | Đang trong pipeline xử lý (virus scan, resize...) | Tuỳ cấu hình |
| `Ready` | 2 | Đã xử lý xong, sẵn sàng phục vụ | Có |
| `Quarantined` | 3 | Bị cách ly (phát hiện virus hoặc nội dung vi phạm) | **Không** — trả 409 |
| `Deleted` | 4 | Đã bị xóa (soft delete) | **Không** — trả 404 |

> **Sprint 1:** Pipeline xử lý (resize, virus scan) chưa active. File upload xong sẽ ở trạng thái `Uploaded`. Các trạng thái khác chuẩn bị cho Sprint 3+.

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

## Endpoint

### `POST /api/files/upload`

**Mục đích:** Upload một file lên object storage. Ghi metadata vào database. Trả về `fileId` và `objectKey`.

**Auth:** Bắt buộc (mọi role đã đăng nhập)

**Content-Type:** `multipart/form-data`

**Giới hạn kích thước:** Tối đa 20 MB (cấu hình `RequestSizeLimit`)

**Form fields:**

| Field | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `file` | `IFormFile` | **Bắt buộc** | File binary cần upload |
| `folderName` | `string` | Không (mặc định `default`) | Thư mục logic để nhóm file trong storage. Ví dụ: `avatars`, `reports`, `warranty-documents` |
| `purpose` | `FilePurposeEnum` | Không (mặc định `Other = 0`) | Mục đích sử dụng file (xem enum bên trên) |

**Validation:**
- `file` không được null
- Kích thước file không được vượt quá giới hạn cấu hình
- Phần mở rộng file phải nằm trong danh sách whitelist

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
- `400 isSuccess=false` — File rỗng, kích thước vượt quá, phần mở rộng không hợp lệ
- `500` — Lỗi ghi lên object storage hoặc lưu metadata DB

**Lưu ý:** Nếu upload binary thành công nhưng lưu metadata DB thất bại, handler tự động xóa object vừa upload để tránh file mồ côi.

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
    "status": 0,
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
- `404` — Không tìm thấy metadata hoặc file đã bị xóa (status `Deleted`)

**Use case:**
- FE hiển thị tên file, dung lượng trong màn hình profile/ticket
- Các service khác kiểm tra `fileId` tồn tại và `status` trước khi gắn vào entity nghiệp vụ

---

### `GET /api/files/download?objectKey={key}`

**Mục đích:** Tải nội dung file trực tiếp qua server theo `objectKey` (endpoint cũ, tương thích ngược).

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `objectKey` | `string` | **Bắt buộc** | Khóa file trong object storage (e.g., `avatars/abc123.png`) |

**Response thành công `200`:**
- Body: Binary stream của file
- Header `Content-Type`: MIME type của file (e.g., `image/png`)
- Header `Content-Disposition`: `attachment; filename="original-name.png"`

> **Không bọc trong `CommonResponse`** — response trực tiếp là binary. Client xử lý như file download.

**Lỗi thường gặp:**
- `400` — `objectKey` rỗng hoặc chứa path traversal (`..`)
- `404` — File không tồn tại trong bucket
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
3. Nếu status = `Quarantined` → trả `409 Conflict`
4. Dùng `objectKey` trong metadata để đọc stream từ object storage
5. Trả binary stream về client

**Response thành công `200`:** Binary stream (giống endpoint cũ theo objectKey)

**Lỗi thường gặp:**
- `400` — `fileId` là empty GUID
- `401` — Chưa đăng nhập
- `404` — Không tìm thấy metadata hoặc file đã bị xóa
- `409` — File đang bị cách ly (Quarantined), không thể tải

**Use case điển hình:** Avatar display — AuthService trả `displayAvatarUrl` dạng `/api/files/{fileId}/download`. FE set `<img src="/api/files/{fileId}/download">`.

---

### `GET /api/files/presigned-url?objectKey={key}&expiresInMinutes={n}`

**Mục đích:** Tạo presigned URL để client tải file trực tiếp từ object storage (không qua server).

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
- `400` — `objectKey` rỗng hoặc `expiresInMinutes` ngoài khoảng 1–1440
- `500` — Lỗi tạo URL từ storage provider

**Lưu ý bảo mật:**
- Bất kỳ ai có URL trong thời gian còn hiệu lực đều có thể tải file
- Không log hoặc share presigned URL ở nơi công khai
- Với file nhạy cảm, dùng thời gian hết hạn ngắn (1–5 phút)

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
3. Nếu status = `Quarantined` → trả `409`
4. Tạo presigned URL từ `objectKey` trong metadata

**Response thành công `200`:** Giống endpoint cũ — `data` là presigned URL string.

**Lỗi thường gặp:**
- `400` — `fileId` không hợp lệ hoặc `expiresInMinutes` ngoài khoảng
- `401` — Chưa đăng nhập
- `404` — File không tìm thấy
- `409` — File đang bị cách ly

---

### `DELETE /api/files?objectKey={key}`

**Mục đích:** Xóa file vật lý khỏi object storage theo `objectKey` (endpoint cũ).

**Auth:** Bắt buộc

**Query params:**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `objectKey` | `string` | **Bắt buộc** | Khóa file cần xóa |

**Response thành công `204`:** Không có body (No Content).

**Lưu ý:**
- Endpoint này chỉ xóa file vật lý trong object storage
- **Không** cập nhật metadata DB, không cập nhật domain service giữ tham chiếu
- Service sử dụng file cần tự xóa tham chiếu sau khi gọi endpoint này

**Lỗi thường gặp:**
- `400` — `objectKey` rỗng
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
3. Xóa object vật lý trong storage theo `objectKey`
4. Đánh dấu metadata record là `Deleted` (soft delete)

**Response thành công `204`:** Không có body.

**Lưu ý:**
- Sau khi xóa, các endpoint metadata, download, presigned-url sẽ trả `404` cho fileId này
- Endpoint này không tự gỡ tham chiếu ở domain service khác (e.g., `AccountProfile.AvatarFileId` vẫn còn giá trị cũ)
- Nếu cần bỏ avatar: AuthService phải cập nhật `AvatarFileId = null` sau khi gọi endpoint xóa này

**Lỗi thường gặp:**
- `400` — `fileId` là empty GUID
- `401` — Chưa đăng nhập
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
| `404` | File không tìm thấy hoặc đã bị xóa |
| `409` | File đang ở trạng thái Quarantined, không thể tải |
| `500` | Lỗi object storage hoặc lỗi hệ thống |
