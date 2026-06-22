using System.Text.Json;
using SharedContracts.Events.Chats;

namespace SharedInfrastructure.UnitTests.Contracts;

public class ChatEventsSerializationTests
{
    [Fact]
    public void ChatCreatedEvent_RoundTrips()
    {
        var evt = new ChatCreatedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Staff A",
            "Hello ticket", false, new List<Guid> { Guid.NewGuid() });

        var result = JsonSerializer.Deserialize<ChatCreatedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ChatEditedEvent_RoundTrips()
    {
        var evt = new ChatEditedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "old", "new", "typo fix");

        var result = JsonSerializer.Deserialize<ChatEditedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ChatDeletedEvent_RoundTrips()
    {
        var evt = new ChatDeletedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);

        var result = JsonSerializer.Deserialize<ChatDeletedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ChatMentionedEvent_RoundTrips()
    {
        var evt = new ChatMentionedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "Manager B", Guid.NewGuid(), true);

        var result = JsonSerializer.Deserialize<ChatMentionedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ChatReactedEvent_RoundTrips()
    {
        var evt = new ChatReactedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 1, false);

        var result = JsonSerializer.Deserialize<ChatReactedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ParticipantAddedEvent_RoundTrips()
    {
        var evt = new ParticipantAddedEvent(Guid.NewGuid(), Guid.NewGuid(), 3, 4, Guid.NewGuid());

        var result = JsonSerializer.Deserialize<ParticipantAddedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ParticipantRemovedEvent_RoundTrips()
    {
        var evt = new ParticipantRemovedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "out of scope");

        var result = JsonSerializer.Deserialize<ParticipantRemovedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ParticipantRoleChangedEvent_RoundTrips()
    {
        var evt = new ParticipantRoleChangedEvent(Guid.NewGuid(), Guid.NewGuid(), 4, 3, Guid.NewGuid());

        var result = JsonSerializer.Deserialize<ParticipantRoleChangedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void ChatEscalationReviewRequestedEvent_RoundTrips()
    {
        var evt = new ChatEscalationReviewRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mention Manager on P1 ticket");

        var result = JsonSerializer.Deserialize<ChatEscalationReviewRequestedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt, options => options.Excluding(e => e.Id).Excluding(e => e.OccurredAt));
    }
}
