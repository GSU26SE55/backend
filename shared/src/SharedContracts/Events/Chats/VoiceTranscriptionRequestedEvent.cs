using SharedContracts.Events.Root;

namespace SharedContracts.Events.Chats;

/// <summary>Requests asynchronous transcription of an already-uploaded audio attachment.</summary>
public record VoiceTranscriptionRequestedEvent(Guid ChatId, Guid TicketId, Guid FileId) : IntegrationEvent;
