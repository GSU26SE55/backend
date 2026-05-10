using SharedKernels.Domain;

namespace SharedInfrastructure.UnitTests.Domain;

public class BaseEntityTests
{
    private class FakeEntity : AuditableEntity
    {
        public string? Name { get; set; }
    }

    private class CreatedEvent : DomainEvent { }
    private class UpdatedEvent : DomainEvent { }

    [Fact]
    public void DomainEvents_DefaultsToEmptyList()
    {
        var e = new FakeEntity();
        e.DomainEvents.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void AddDomainEvent_AppendsToList()
    {
        var e = new FakeEntity();
        var ev1 = new CreatedEvent();
        var ev2 = new UpdatedEvent();

        e.AddDomainEvent(ev1);
        e.AddDomainEvent(ev2);

        e.DomainEvents.Should().HaveCount(2);
        e.DomainEvents.Should().ContainInOrder(ev1, ev2);
    }

    [Fact]
    public void RemoveDomainEvent_RemovesSpecificInstance()
    {
        var e = new FakeEntity();
        var ev1 = new CreatedEvent();
        var ev2 = new UpdatedEvent();

        e.AddDomainEvent(ev1);
        e.AddDomainEvent(ev2);
        e.RemoveDomainEvent(ev1);

        e.DomainEvents.Should().ContainSingle().Which.Should().Be(ev2);
    }

    [Fact]
    public void DomainEvent_OccurredOn_SetAtConstruction_Utc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var ev = new CreatedEvent();
        var after = DateTime.UtcNow.AddSeconds(1);

        ev.OccurredOn.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void AuditableEntity_DefaultsAreSafe()
    {
        var e = new FakeEntity();

        e.Id.Should().Be(Guid.Empty);
        e.IsDeleted.Should().BeFalse();
        e.DeletedAt.Should().BeNull();
        e.UpdatedAt.Should().BeNull();
        e.CreatedBy.Should().BeNull();
    }
}
