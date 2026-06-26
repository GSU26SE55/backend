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

> **Lưu ý `Other = 0`:** Đây là ngoại lệ có chủ ý so với quy tắc BE chuẩn (enum bắt đầu từ 1). Giá trị `0` được chọn để `Other` là default value của C# enum — khi client không truyền `purpose` trong form upload, backend nhận `(FilePurposeEnum)0 = Other` và áp dụng whitelist `Other`. Không thay đổi giá trị này để tránh ảnh hưởng data đã có trong DB (`migration defaultValue: 0`). `FileStatusEnum` cũng bắt đầu từ `0` (`Uploaded=0`) — xem bảng bên dưới.

### `FileStatusEnum`

| Giá trị | Int | Ý nghĩa | Có thể tải không |
|---|---|---|---|
| `Uploaded` | 0 | Vừa upload xong, chưa qua xử lý | Có (legacy state) |
| `Processing` | 1 | Đang trong pipeline xử lý (virus scan, resize...) | **Không** — download/presigned-url trả `409 Conflict` |
| `Ready` | 2 | Đã xử lý xong, sẵn sàng phục vụ | Có |
| `Quarantined` | 3 | Bị cách ly (phát hiện virus hoặc nội dung vi phạm) | **Không** — trả 409 |
| `Deleted` | 4 | Đã bị xóa (soft delete) | **Không** — trả 404 |

> **Sprint 1:** Pipeline xử lý (resize, virus scan) chưa active. File upload xong được lưu metadata với trạng thái `Ready`. `Uploaded` là state dự phòng cho pipeline Sprint 2+ — Sprint 1 không sử dụng.

---

## TypeScript Types

Copy vào feature `file-storage`. Theo rule FE (enum không define inline trong types file):
- 2 enum `FilePurposeEnum`, `FileStatusEnum` → đặt ở `src/features/file-storage/enums/file-storage.enum.ts` (dùng `as const` object + type alias, không dùng TS `enum`).
- Các interface (`FileUploadResponse`, `FileMetadataResponse`, payload...) → đặt ở `src/features/file-storage/types/file-storage.types.ts`, **import + re-export** enum từ `enums/`.

Block dưới gộp chung cho dễ đọc; khi copy hãy tách enum ra file `enums/` theo trên.

```typescript
// Giữ enum value đồng bộ với backend — không tự ý đổi số.
// Rule FE: KHÔNG dùng TypeScript `enum` — dùng `as const` object + type alias.
export const FilePurposeEnum = {
  Other: 0,            // exception: 0 là default C# khi form không truyền purpose
  Avatar: 1,
  TicketAttachment: 2,
  MaintenancePhoto: 3,
  KbImage: 4,
  Firmware: 5,
} as const;
export type FilePurposeEnum =
  (typeof FilePurposeEnum)[keyof typeof FilePurposeEnum];

export const FileStatusEnum = {
  Uploaded: 0,
  Processing: 1,
  Ready: 2,
  Quarantined: 3,
  Deleted: 4,
} as const;
export type FileStatusEnum =
  (typeof FileStatusEnum)[keyof typeof FileStatusEnum];

export interface FileUploadResponse {
  fileId: string;          // UUID — lưu vào domain service để tham chiếu
  objectKey: string;       // khóa trong object storage — dùng cho endpoint legacy
  fileName: string;        // tên file gốc client gửi lên
  contentType: string;     // MIME type (e.g. "image/png")
  size: number;            // bytes
  publicUrl: string | null; // có giá trị nếu PublicBaseUrl được cấu hình — fallback GET /{id}/download khi null
}

export interface FileMetadataResponse {
  fileId: string;
  objectKey: string;
  fileName: string;
  contentType: string;
  size: number;
  folderName: string;
  purpose: FilePurposeEnum;
  status: FileStatusEnum;
  publicUrl: string | null;
  createdAt: string;       // ISO 8601 UTC
  updatedAt: string | null;
}
```

> **`publicUrl`:** Có giá trị khi `ObjectStorageOptions.PublicBaseUrl` được cấu hình (môi trường dev local với MinIO port 9090 đã có giá trị). FE **phải handle cả 2 case** — không assume `null`. Fallback về `GET /api/files/{fileId}/download` khi `null`. Dùng helper: `publicUrl ?? \`/api/files/${fileId}/download\``.
>
> **Hai field cấu hình URL khác nhau (đừng nhầm):** Backend có 2 option riêng biệt trong `ObjectStorageOptions`, cả hai trỏ về MinIO port 9090 ở dev nhưng dùng cho mục đích khác nhau:
> - `PublicBaseUrl` (vd `http://localhost:9090/solar-battery-files`) → dùng để build field `publicUrl` trả về **sau upload**.
> - `PublicServiceUrl` (vd `http://localhost:9090`) → dùng để **ký presigned URL** với hostname mà browser resolve được. Nếu không set thì fallback về `ServiceUrl` (host internal, browser không gọi được).
>
> Cả hai được set qua env var trong `docker-compose.yml` (`ObjectStorage__PublicBaseUrl`, `ObjectStorage__PublicServiceUrl`), không nằm trong `appsettings.json`.

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

**KbImage — intentional open access:** `KbImage` được thiết kế để mọi role đăng nhập (bao gồm Customer) đều có thể đọc. KB là nội dung hỗ trợ kỹ thuật dành cho tất cả user — không phải file nội bộ. Đây là quyết định có chủ ý, không phải oversight.

**Ownership check:** backend so sánh `UploadedFile.CreatedBy` với userId trong JWT. Nếu không phải owner và không thỏa rule role/purpose ở bảng trên, API trả `403 Forbidden`.

**JWT claim mapping:** FileStorageService resolve userId theo thứ tự ưu tiên: claim `NameIdentifier` **trước**, fallback về `AccountId` nếu `NameIdentifier` null. Role được đọc từ claim `role` hoặc role claim chuẩn ASP.NET Core. Token từ AuthService luôn chứa `NameIdentifier` — `AccountId` là fallback cho service legacy. Nếu token thiếu cả hai, mọi thao tác upload/read/delete bị xem là không đủ quyền.

**Path traversal protection:** Tất cả endpoint nhận `objectKey` qua query string (`DELETE ?objectKey=`, `GET /download?objectKey=`, `GET /presigned-url?objectKey=`) đều thực hiện: (1) reject nếu `objectKey` chứa `..` → `400`, (2) normalize: trim whitespace, strip leading `/\`, replace `\→/`. Behavior này nhất quán trên cả 3 endpoint — không chỉ riêng DELETE.

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
  "message": "Upload file thành công.",
  "data": {
    "fileId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "objectKey": "avatars/3fa85f64-abc1-...png",
    "fileName": "my-photo.png",
    "contentType": "image/png",
    "size": 204800,
    "publicUrl": "http://localhost:9090/solar-battery-files/avatars/3fa85f64-abc1-...png"
  }
}
```

> **`publicUrl` trong example:** Giá trị trên là ví dụ khi `PublicBaseUrl` được cấu hình (môi trường dev). Khi `PublicBaseUrl` không set, field này trả `null`. FE phải handle cả 2 case.

**Chi tiết `FileUploadResponse`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `fileId` | `Guid` | Không | ID metadata ổn định. **Lưu field này vào domain service để tham chiếu** |
| `objectKey` | `string` | Không | Khóa định danh file trong object storage (e.g., `avatars/abc123.png`). Tên file được sinh bằng GUID, không giữ tên gốc |
| `fileName` | `string` | Không | Tên file gốc client gửi lên (e.g., `my-photo.png`) |
| `contentType` | `string` | Không | MIME type (e.g., `image/png`, `application/pdf`) |
| `size` | `long` | Không | Kích thước file theo byte |
| `publicUrl` | `string?` | Null nếu storage không cấu hình public base URL | URL public trực tiếp nếu bucket public; ngược lại null và phải dùng download/presigned-url |

> **`publicUrl`:** Có giá trị khi `ObjectStorageOptions.PublicBaseUrl` được cấu hình — môi trường dev local với MinIO đã trả giá trị (xác nhận qua Swagger). FE phải handle cả 2 case, không assume `null`.

**Lỗi thường gặp:**
- `400` — Không có file trong request
- `400 isSuccess=false` — File rỗng, thiếu phần mở rộng, hoặc phần mở rộng không hợp lệ với `purpose`
- `401` — Chưa đăng nhập hoặc access token không hợp lệ/hết hạn (controller có `[Authorize]`)
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

Nếu ASP.NET Core reject request trước controller vì multipart request quá lớn, `GlobalExceptionMiddleware` vẫn trả JSON. Lưu ý: middleware để `message` **rỗng** và chỉ đẩy chi tiết vào `listErrors`:

```json
{
  "isSuccess": false,
  "statusCode": 413,
  "message": "",
  "data": null,
  "listErrors": [
    {
      "field": "file",
      "detail": "Kích thước request vượt quá giới hạn cho phép (tối đa 20 MB)."
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
    "publicUrl": "http://localhost:9090/solar-battery-files/avatars/3fa85f64-abc1-...png",
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
1. Validate `objectKey`: reject nếu rỗng hoặc chứa `..` (path traversal) → `400`. Sau đó chuẩn hóa: trim whitespace, strip leading `/` hoặc `\`, replace `\` → `/`. Không lowercase. Client phải truyền đúng `objectKey` nhận được từ upload response.
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
- `500` — Object storage không khả dụng

**Polling khi `Processing`:** FE nên gọi `GET /api/files/{id}/metadata` mỗi 2–5 giây cho đến khi `status=Ready`, hoặc dừng và hiển thị lỗi nếu nhận `Quarantined`/`Deleted`.

**Use case điển hình:** Avatar display — AuthService trả `displayAvatarUrl` dạng `/api/files/{fileId}/download`.

> ⚠️ **Endpoint này yêu cầu `Authorization: Bearer` (controller có `[Authorize]`).** Thẻ `<img src="/api/files/{fileId}/download">` hoặc `<a href>` của browser **KHÔNG gửi** header này → sẽ nhận `401`. KHÔNG nhúng URL download trực tiếp vào `src`/`href`.
>
> **Cách đúng cho FE:** fetch qua axios (có interceptor attach token) → nhận blob → `URL.createObjectURL(blob)` → set vào `<img src={blobUrl}>`, và `URL.revokeObjectURL` khi unmount. Tham khảo pattern `AuthImage` / hook `useFileBlobUrl` (GH-36).
>
> **Hoặc** dùng `GET /api/files/{id}/presigned-url` để lấy URL đã ký (bao gồm chữ ký trong query string, không cần header) rồi set thẳng vào `src` — phù hợp file ít nhạy cảm, đặt `expiresInMinutes` ngắn.

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
- `500` — Lỗi tạo URL từ storage provider

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
1. Validate `objectKey`: reject nếu rỗng hoặc chứa `..` (path traversal) → `400`. Sau đó chuẩn hóa: trim whitespace, strip leading `/` hoặc `\`, replace `\` → `/`. Không lowercase. Client phải truyền đúng `objectKey` nhận được từ upload response.
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
- `500` — Lỗi xóa file từ object storage

---

## So sánh endpoint theo objectKey vs fileId

| Thao tác | Endpoint cũ (objectKey) | Endpoint mới (fileId) | Khuyến nghị |
|---|---|---|---|
| Download | `GET /download?objectKey=` | `GET /{id}/download` | Dùng theo fileId |
| Presigned URL | `GET /presigned-url?objectKey=` | `GET /{id}/presigned-url` | Dùng theo fileId |
| Metadata | — | `GET /{id}/metadata` | Không có endpoint tương ứng theo objectKey — đây là lý do ưu tiên dùng fileId cho service mới |
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
| `409` | File đang ở trạng thái Processing hoặc Quarantined — áp dụng cho `download` và `presigned-url` |
| `413` | Upload vượt giới hạn 20 MB |
| `500` | Lỗi object storage hoặc lỗi hệ thống |

---

## Changelog

### 2026-06-20 — Sync TypeScript Types block với doc FE

- **Block TypeScript Types dùng `enum`** — sửa sang `as const` object + type alias để khớp rule FE ("KHÔNG dùng TypeScript `enum`") và đồng bộ với `frontend/docs/api-filestorage.md`. Giá trị enum không đổi (`FilePurposeEnum` 0–5, `FileStatusEnum` 0–4) — chỉ đổi cách khai báo. Lời dẫn ở section TypeScript Types đã yêu cầu dùng `as const`, nay code khớp với lời dẫn.

### 2026-06-15 — Verify lần 2 vs source code

- **`413` message qua middleware** — docs ghi `message: "Request payload too large. File tối đa 20 MB."`, nhưng `GlobalExceptionMiddleware` thực tế trả `message: ""` (rỗng) và đẩy chi tiết vào `listErrors[0].detail = "Kích thước request vượt quá giới hạn cho phép (tối đa 20 MB)."`. Đã sửa JSON example.
- **Bổ sung field `PublicServiceUrl`** — code có 2 option URL riêng: `PublicBaseUrl` (build `publicUrl` sau upload) và `PublicServiceUrl` (ký presigned URL). Docs trước chỉ nhắc `PublicBaseUrl`. Đã thêm note phân biệt, cả 2 set qua env trong `docker-compose.yml`.
- **`401` cho `POST /upload`** — controller có `[Authorize]` nhưng mục "Lỗi thường gặp" của upload chưa liệt kê `401`. Đã bổ sung.
- **Use-case `<img src>` download sai** — đối chiếu với FE GH-36: endpoint `GET /{id}/download` cần `Authorization: Bearer` nên KHÔNG nhúng trực tiếp vào `<img>`/`<a>` (browser không gửi header → 401). FE phải fetch blob (pattern `AuthImage`/`useFileBlobUrl`) hoặc dùng presigned-url. Đã sửa note ở endpoint download.
- **Hướng dẫn đặt enum** — làm rõ enum tách ra `enums/file-storage.enum.ts`, types re-export (khớp rule FE), thay vì gợi ý copy nguyên block vào `types/`.

### 2026-05-19 — Fix enum values sau verify source code

- **FileStatusEnum values sai** — tài liệu 2026-05-18 ghi `1–5` nhưng source code thực tế là `0–4`. Đã sửa lại bảng enum, TypeScript types, và notes. Giá trị đúng: `Uploaded=0, Processing=1, Ready=2, Quarantined=3, Deleted=4`.
- **`publicUrl` không luôn `null`** — `publicUrl` có giá trị khi `PublicBaseUrl` config được set (môi trường dev MinIO port 9090 đã trả giá trị, xác nhận qua Swagger). Đã sửa note "luôn null" thành "handle cả 2 case".
- **`Uploaded` state với download** — xác nhận qua source code: handler chỉ block `Processing` và `Quarantined` bằng `409`; `Uploaded` không bị block, download trả `200`.

### 2026-05-18

- **FileStatusEnum** đổi giá trị từ `0–4` sang `1–5` theo quy tắc BE chuẩn (enum bắt đầu từ 1). Migration `FixFileStatusEnumDefaultValue` đã chạy để cập nhật DB default và migrate data hiện có. *(Giá trị này sau đó được xác nhận lại qua source code ngày 2026-05-19 — thực tế vẫn là `0–4`.)*
- Bổ sung section **TypeScript Types** để FE copy trực tiếp thay vì tự suy ra từ bảng.
- Làm rõ **KbImage open access** — intentional, Customer được đọc.
- Làm rõ **JWT claim priority** — `NameIdentifier` trước, fallback `AccountId`.
- Xác nhận **path traversal protection** áp dụng nhất quán trên cả 3 objectKey endpoint.

### 2026-05-17 — FE Feedback Resolution

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

## Nhóm — Audit Logs nội bộ (Option C — Sprint audit)

> Endpoint **dự phòng (fallback resilience)**: query trực tiếp bảng nguồn `file_audit_logs` ngay tại FileStorageService, dùng được kể cả khi `AuditAggregatorService` gặp sự cố. Enum `Severity`/`ActionCategory` dùng chung — xem [docs/api-audit.md](api-audit.md#enums--tập-giá-trị-cố-định).
>
> **Auth:** chỉ role `Admin` (`401` thiếu token / `403` sai role).

### `GET /api/admin/files/audit-logs`

**Mục đích:** Tra cứu audit log thao tác trên FILE, có phân trang + lọc — phục vụ điều tra GDPR/compliance truy cập dữ liệu.

**Tác dụng:** Trả lời "ai upload/download/xoá file nào, khi nào, có bị từ chối quyền không"; điều tra truy cập trái phép (data leak).

**Auth:** Admin.

**Query params (đều optional):**

| Param | Type | Bắt buộc | Mô tả |
|---|---|---|---|
| `action` | `string?` | Không | Mã action (vd `FileDownloaded`). Bỏ trống = tất cả |
| `fileId` | `string?` (UUID) | Không | Lọc theo file cụ thể (target) |
| `from` | `DateTime?` | Không | Mốc đầu (UTC) |
| `to` | `DateTime?` | Không | Mốc cuối (UTC) |
| `pageNumber` | `int` | Không (mặc định 1) | Số trang |
| `pageSize` | `int` | Không (mặc định 50, trần 100) | Số item/trang |

**Action codes (file, 6):** `FileUploaded` · `FileDownloaded` · `FileDeleted` · `AccessDenied` · `PresignedUrlGenerated` · `PresignedUrlRevoked`

**Response thành công `200`:** `CommonResponse<PaginationResponse<FileAuditLogDto>>` (mới nhất trước).

**`DTO FileAuditLogDto`:**

| Field | Type | Nullable | Mô tả |
|---|---|---|---|
| `id` | `string` | Không | ID bản ghi audit |
| `eventId` | `string` | Không | Idempotency key |
| `actionCode` | `string` | Không | Mã hành động |
| `severity` | `string` | Không | Mức độ (`Info`/`Warning`/`Critical`/`Security`) |
| `targetId` | `string?` | Null nếu không gắn | ID file (target) |
| `targetDisplay` | `string?` | Null / `[REDACTED]` sau GDPR | Tên hiển thị file |
| `actorAccountId` | `string?` | Null nếu hệ thống | Account thực hiện |
| `isSuccess` | `bool` | Không | Thành công/thất bại (vd `AccessDenied` → `false`) |
| `reason` | `string?` | Null nếu không có | Lý do (vd lý do từ chối quyền) |
| `occurredAt` | `DateTime` | Không | Thời điểm xảy ra (UTC) |

**Lỗi:** `401` / `403`.
