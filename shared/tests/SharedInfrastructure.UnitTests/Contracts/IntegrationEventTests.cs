using SharedContracts.Events.Root;

namespace SharedInfrastructure.UnitTests.Contracts;

public class IntegrationEventTests
{
    private record FakeEvent(string Payload) : IntegrationEvent;

    [Fact]
    public void NewInstance_HasUniqueId()
    {
        var a = new FakeEvent("x");
        var b = new FakeEvent("x");

        a.Id.Should().NotBeEmpty();
        b.Id.Should().NotBeEmpty();
        a.Id.Should().NotBe(b.Id, "mỗi event là 1 instance riêng → Id phải khác");
    }

    [Fact]
    public void NewInstance_OccurredAt_CloseToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var evt = new FakeEvent("x");
        var after = DateTime.UtcNow.AddSeconds(1);

        evt.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        evt.OccurredAt.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void RecordEquality_BasedOnAllProperties()
    {
        // record equality dựa trên TẤT CẢ properties (kể cả Id auto-gen) → 2 instance khác nhau ngay
        // cả khi cùng payload, vì Id khác.
        var a = new FakeEvent("same");
        var b = new FakeEvent("same");

        a.Should().NotBe(b);
    }
}
