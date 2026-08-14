namespace SharedContracts.Events.Root;

/// <summary>
/// Phong bì chung của mọi integration event: định danh và thời điểm phát sinh.
/// </summary>
/// <remarks>
/// <para>
/// GH-789 — hai thuộc tính này dùng <c>init</c>, KHÔNG dùng <c>private set</c>.
/// </para>
/// <para>
/// <c>System.Text.Json</c> chỉ ghi vào thuộc tính có setter mà nó truy cập được: <c>init</c> thì có,
/// <c>private set</c> thì không. Với <c>private set</c>, mỗi lần deserialize sẽ chạy lại initializer
/// ⇒ <see cref="Id"/> thành một GUID MỚI và <see cref="OccurredAt"/> thành thời điểm deserialize.
/// </para>
/// <para>
/// Hậu quả không nằm ở chỗ "sai dữ liệu hiển thị": <c>ProcessOnceAsync</c> khoá chống trùng theo
/// <see cref="Id"/> (xem <c>IdempotentConsumerExtensions</c>). Id đổi sau mỗi lần deserialize nghĩa
/// là relay chạy lại, service khởi động lại, hay hai relay cùng đọc một dòng outbox đều tạo ra khoá
/// khác nhau — hàng rào idempotency trở thành hình thức, và một event nghiệp vụ có thể gửi email,
/// SMS, notification nhiều lần. Đường truyền qua MassTransit cũng deserialize nên lỗi này chạm tới
/// MỌI consumer, không riêng đường outbox.
/// </para>
/// <para>
/// <c>init</c> vẫn giữ tính bất biến sau khi khởi tạo — không ai gán lại được giữa chừng.
/// </para>
/// </remarks>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
