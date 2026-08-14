# Quy ước kỹ thuật

> **Tầng T0.** Chỉ những gì linter/formatter KHÔNG cưỡng chế được.

## Đặt tên

| Loại | Mẫu |
|---|---|
| Command / Handler | `{Entity}{Action}Command.cs` / `{Entity}{Action}CommandHandler.cs` |
| Query / Handler | `{Entity}Get{Scope}Query.cs` / `{Entity}Get{Scope}QueryHandler.cs` |
| DTO / Response | `{Entity}DTO.cs` / `{Entity}{Action}Response.cs` |
| Enum | `{Name}Enum.cs` — giá trị bắt đầu từ `1` |
| Consumer / Event | `{EventName}Consumer.cs` / `{Action}Event.cs` (dùng `record`) |
| Route | `api/batteries`, `api/battery-readings` — thường, số nhiều, kebab-case |

Field private `_camelCase`. Method async có hậu tố `Async`.
**Không** đổi tên method của `IGenericRepository` dù tên sai nghĩa — đó là di sản `SharedKernel`.

## Xử lý lỗi

- Lỗi nghiệp vụ → **HTTP 200** kèm `CommonResponse.IsSuccess = false` + `Message`.
- Lỗi validation → `ListErrors` (`Field` + `Detail`), thu thập **TẤT CẢ** lỗi, không fail sớm.
- Chỉ exception chưa bắt mới ra **500** (GlobalExceptionMiddleware).
- JWT thiếu/hết hạn → **401**; sai quyền → **403**.
- `IsSuccess` mặc định **true** ⇒ đường lỗi phải set `false` tường minh, quên là báo thành công.

```json
{ "isSuccess": false, "message": "Not found", "data": null, "listErrors": [] }
```

## Log

- PHẢI log: lỗi tích hợp bên ngoài, chuyển trạng thái ticket, hành động Admin, sự cố thiết bị.
- TUYỆT ĐỐI KHÔNG log: mật khẩu, access/refresh token, OAuth `code`/`state`, API key thiết bị,
  unsubscribe token, email người nhận ở dạng thô, nội dung response của nhà cung cấp email/SMS.
  (Đây là nội dung của nhiều issue trong milestone — đừng tạo thêm.)

## Kiểm tra đầu vào

- Validate trong `ValidateAsync()` của Command (pipeline `ValidationBehavior` chạy trước handler).
- Dữ liệu từ thiết bị IoT, webhook, upload: **không tin**. Kiểm phạm vi giá trị vật lý, kiểm
  thời gian không ở tương lai, kiểm thiết bị có quyền với đúng tài sản đó.

## Truy vấn dữ liệu

- Chống N+1: `.Include()` tường minh trong query handler.
- **LUÔN** `.Where(x => !x.IsDeleted)` — dự án không có global query filter.
- Truy vấn tính toán trên `sensor_readings`: **lọc `SensorSourceCode`** kẻo đếm gấp 3.
- Transaction bắt buộc khi ghi ≥ 2 bảng, hoặc khi ghi + publish event.
- Migration: tên mô tả rõ, có `Down()` chạy được, không sửa migration đã merge.

## Phụ thuộc

- Không thêm package mới nếu stack hiện tại đủ. Cần thì hỏi trước.
- Không dùng AutoMapper (dự án map tay).

## Git

- **KHÔNG commit, không push, không checkout/reset/stash.** Cây làm việc đang có thay đổi
  chưa commit của người dùng; người dùng tự commit sau khi review.

## Đặc thù môi trường

- Test chạy **native** trên máy (không trong container). Docker chỉ cần cho tầng
  integration/e2e.
- Stack đang chạy sẵn: gateway `http://localhost:4001`. Không tự `docker compose down`.
- **`--no-build` đo bản đã build lần trước** ⇒ luôn build trước khi test.
- macOS **không có lệnh `timeout`** — đừng bọc `dotnet test` bằng nó.
- Biến môi trường đọc từ `.env` / `.env.Docker` ở gốc repo. Không sửa 2 file này.
- Tầng e2e cần dữ liệu seed đã có sẵn trong stack đang chạy — không reset volume.

## Test

- Thêm test mới: được, thường là bắt buộc.
- Sửa test đã có: **chỉ khi spec đổi và issue nói rõ**. Không xoá assertion, không nới
  ngưỡng, không `[Skip]`, không đổi giá trị kỳ vọng cho khớp hành vi hiện tại.
- Test hồi quy phải **đỏ trên code cũ, xanh trên code mới**. Xanh cả hai bên = vô giá trị.
- Mock `IUnitOfWork`; không dựng DbContext thật trong unit test.
- Harness MassTransit: đặt `x.SetTestTimeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15))`
  nếu không muốn đỏ giả vì hết giờ ~1,2s.
