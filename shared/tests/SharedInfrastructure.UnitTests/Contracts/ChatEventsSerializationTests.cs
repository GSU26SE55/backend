using System.Text.Json;
using SharedContracts.Events.Chats;

namespace SharedInfrastructure.UnitTests.Contracts;

/// <summary>
/// GH-789 — round-trip PHẢI so cả <c>Id</c> lẫn <c>OccurredAt</c>.
/// </summary>
/// <remarks>
/// Bản trước loại đúng hai trường không round-trip được (<c>Excluding(e =&gt; e.Id)</c>,
/// <c>Excluding(e =&gt; e.OccurredAt)</c>). Test vì thế xanh liên tục trong khi mỗi lần deserialize
/// lại sinh một <c>Id</c> mới — mà <c>Id</c> chính là khoá chống trùng của inbox. Nói cách khác,
/// phần bị loại trừ đúng là phần đang hỏng.
/// </remarks>
public class ChatEventsSerializationTests
{
    [Fact]
    public void ChatCreatedEvent_RoundTrips()
    {
        var evt = new ChatCreatedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "Staff A",
            "Hello ticket", false, new List<Guid> { Guid.NewGuid() },
            Guid.NewGuid(), null);

        var result = JsonSerializer.Deserialize<ChatCreatedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ChatEditedEvent_RoundTrips()
    {
        var evt = new ChatEditedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, "old", "new", "typo fix");

        var result = JsonSerializer.Deserialize<ChatEditedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ChatDeletedEvent_RoundTrips()
    {
        var evt = new ChatDeletedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);

        var result = JsonSerializer.Deserialize<ChatDeletedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ChatMentionedEvent_RoundTrips()
    {
        var evt = new ChatMentionedEvent(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "Manager B", Guid.NewGuid(), true);

        var result = JsonSerializer.Deserialize<ChatMentionedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ChatReactedEvent_RoundTrips()
    {
        var evt = new ChatReactedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 1, false, Guid.NewGuid());

        var result = JsonSerializer.Deserialize<ChatReactedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ParticipantAddedEvent_RoundTrips()
    {
        var evt = new ParticipantAddedEvent(Guid.NewGuid(), Guid.NewGuid(), 3, 4, Guid.NewGuid());

        var result = JsonSerializer.Deserialize<ParticipantAddedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ParticipantRemovedEvent_RoundTrips()
    {
        var evt = new ParticipantRemovedEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "out of scope");

        var result = JsonSerializer.Deserialize<ParticipantRemovedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ParticipantRoleChangedEvent_RoundTrips()
    {
        var evt = new ParticipantRoleChangedEvent(Guid.NewGuid(), Guid.NewGuid(), 4, 3, Guid.NewGuid());

        var result = JsonSerializer.Deserialize<ParticipantRoleChangedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }

    [Fact]
    public void ChatEscalationReviewRequestedEvent_RoundTrips()
    {
        var evt = new ChatEscalationReviewRequestedEvent(
            Guid.NewGuid(), Guid.NewGuid(), "TK-001", Guid.NewGuid(), "Mention Manager on P1 ticket");

        var result = JsonSerializer.Deserialize<ChatEscalationReviewRequestedEvent>(JsonSerializer.Serialize(evt));

        result.Should().BeEquivalentTo(evt);
    }
}
