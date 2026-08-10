using System.Collections;
using System.Reflection;
using System.Text.Json;
using SharedContracts.Events.Root;

namespace SharedInfrastructure.UnitTests.Contracts;

/// <summary>
/// GH-789 — phong bì event (<c>Id</c>, <c>OccurredAt</c>) phải sống sót qua vòng
/// serialize → deserialize, với MỌI loại event, không riêng vài loại có test viết tay.
/// </summary>
/// <remarks>
/// <para>
/// Relay của Auth/SMS/Battery/Ticket đều ghi event xuống outbox dạng JSON rồi deserialize lại trước
/// khi publish. Nếu <c>Id</c> tái sinh ở bước đó thì khoá chống trùng của inbox
/// (<c>ProcessOnceAsync</c> dùng chính <c>Id</c>) đổi theo mỗi lần chạy lại — relay retry, service
/// restart, hai relay cùng đọc một dòng: mỗi lần một khoá, và side effect chạy lại từ đầu.
/// </para>
/// <para>
/// Kiểm bằng phản chiếu trên toàn bộ assembly hợp đồng thay vì liệt kê tay: event mới thêm sau này
/// tự động được phủ, không phụ thuộc việc ai đó nhớ viết thêm test.
/// </para>
/// </remarks>
public class IntegrationEventEnvelopeTests
{
    /// <summary>Mọi kiểu event cụ thể trong assembly hợp đồng.</summary>
    public static TheoryData<Type> AllEventTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(IntegrationEvent).Assembly
                     .GetTypes()
                     .Where(t => !t.IsAbstract && typeof(IntegrationEvent).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }
        return data;
    }

    [Fact]
    public void ThereAreEventTypesToCheck()
    {
        // Chống "xanh vì rỗng": nếu bộ lọc phản chiếu hỏng, mọi [Theory] bên dưới sẽ im lặng không
        // chạy ca nào và test suite vẫn xanh.
        AllEventTypes().Should().HaveCountGreaterThan(20);
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void EveryEvent_KeepsItsIdentityAcrossJsonRoundTrip(Type eventType)
    {
        var original = (IntegrationEvent)Instantiate(eventType);

        var json = JsonSerializer.Serialize(original, eventType);
        var restored = (IntegrationEvent)JsonSerializer.Deserialize(json, eventType)!;

        restored.Id.Should().Be(original.Id,
            $"{eventType.Name}: Id là khoá chống trùng của inbox — đổi sau deserialize là mất idempotency");
        restored.OccurredAt.Should().Be(original.OccurredAt,
            $"{eventType.Name}: OccurredAt là mốc thời gian nghiệp vụ, không phải thời điểm đọc lại bản ghi");
    }

    [Theory]
    [MemberData(nameof(AllEventTypes))]
    public void EveryEvent_SurvivesTwoRoundTrips(Type eventType)
    {
        // Đường đi thật có HAI lần deserialize: relay đọc outbox, rồi MassTransit đọc lại ở phía
        // consumer. Một lần đúng mà lần hai sai thì hàng rào vẫn thủng.
        var original = (IntegrationEvent)Instantiate(eventType);

        var once = (IntegrationEvent)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(original, eventType), eventType)!;
        var twice = (IntegrationEvent)JsonSerializer.Deserialize(
            JsonSerializer.Serialize(once, eventType), eventType)!;

        twice.Id.Should().Be(original.Id, eventType.Name);
        twice.OccurredAt.Should().Be(original.OccurredAt, eventType.Name);
    }

    [Fact]
    public void OccurredAt_KeepsUtcKind_SoTimestampsStayComparable()
    {
        // DateTime mất Kind sẽ được diễn giải là giờ địa phương ở phía đọc — audit và correlation
        // lệch đúng bằng chênh lệch múi giờ mà không có dấu hiệu gì.
        var original = new EnvelopeProbeEvent();

        var restored = JsonSerializer.Deserialize<EnvelopeProbeEvent>(JsonSerializer.Serialize(original))!;

        restored.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ExplicitEnvelopeValues_AreHonoured_NotOverwrittenByInitializers()
    {
        // Nguồn sự thật là bản ghi outbox, không phải lúc chạy: dựng lại event từ JSON phải cho ra
        // đúng phong bì đã lưu.
        var id = Guid.NewGuid();
        var occurred = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var restored = JsonSerializer.Deserialize<EnvelopeProbeEvent>(
            $$"""{"Id":"{{id}}","OccurredAt":"2026-01-02T03:04:05Z","Note":"x"}""")!;

        restored.Id.Should().Be(id);
        restored.OccurredAt.Should().Be(occurred);
    }

    private sealed record EnvelopeProbeEvent : IntegrationEvent
    {
        public string Note { get; init; } = "probe";
    }

    // ───────────────────────────────────────────── dựng instance cho mọi hình dạng record

    /// <summary>
    /// Dựng một instance bất kỳ của <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// Event trong repo có hai hình dạng: record vị trí (tham số bắt buộc) và record thân rỗng dùng
    /// object initializer. Chọn constructor nhiều tham số nhất rồi bơm giá trị mặc định theo kiểu —
    /// giá trị là gì không quan trọng, phép kiểm chỉ soi phong bì.
    /// </remarks>
    private static object Instantiate(Type type)
    {
        var ctor = type.GetConstructors()
            .Where(c => c.GetParameters().All(p => CanSupply(p.ParameterType)))
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        Assert.True(ctor is not null, $"Không dựng được {type.FullName}: không có constructor nào cấp giá trị được");

        var args = ctor!.GetParameters().Select(p => SampleValue(p.ParameterType)).ToArray();
        return ctor.Invoke(args);
    }

    private static bool CanSupply(Type t)
        => t.IsValueType
           || t == typeof(string)
           || typeof(IEnumerable).IsAssignableFrom(t)
           || Nullable.GetUnderlyingType(t) is not null
           || t.IsClass && t.GetConstructor(Type.EmptyTypes) is not null;

    private static object? SampleValue(Type t)
    {
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null)
            return SampleValue(underlying);

        if (t == typeof(string))
            return "gh789";
        if (t == typeof(Guid))
            return Guid.NewGuid();
        if (t == typeof(DateTime))
            return new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        if (t == typeof(DateTimeOffset))
            return new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        if (t.IsEnum)
            return Enum.GetValues(t).GetValue(0);
        if (t.IsValueType)
            return Activator.CreateInstance(t);

        if (t.IsArray)
            return Array.CreateInstance(t.GetElementType()!, 0);

        if (t.IsGenericType)
        {
            var definition = t.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(IList<>)
                || definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
                || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(t.GetGenericArguments()[0]));

            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>)
                || definition == typeof(IReadOnlyDictionary<,>))
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(t.GetGenericArguments()));
        }

        return Activator.CreateInstance(t);
    }
}
