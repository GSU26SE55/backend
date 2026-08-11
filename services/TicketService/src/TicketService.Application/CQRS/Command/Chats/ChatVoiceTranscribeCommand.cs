using System.Text.Json.Serialization;
using MediatR;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.Chats;

public sealed class ChatVoiceTranscribeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    public const long MaxAudioFileSizeDefault = 20_971_520;
    private static readonly HashSet<string> AllowedAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/mp3", "audio/wav", "audio/x-wav", "audio/wave", "audio/ogg",
        "audio/webm", "video/webm", "audio/mp4", "audio/m4a", "audio/x-m4a", "audio/aac", "audio/flac", "audio/x-flac"
    };

    [JsonIgnore] public Guid TicketId { get; set; }
    [JsonIgnore] public Guid UserId { get; set; }
    [JsonIgnore] public ActorRoleEnum UserRole { get; set; }
    [JsonIgnore] public string UserDisplayName { get; set; } = string.Empty;
    [JsonIgnore] public List<string> UserPermissions { get; set; } = new();

    public Guid FileId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Url { get; set; } = string.Empty;

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();
        if (FileId == Guid.Empty)
            response.ListErrors.Add(new Errors { Field = nameof(FileId), Detail = "fileId is required." });
        if (string.IsNullOrWhiteSpace(FileName))
            response.ListErrors.Add(new Errors { Field = nameof(FileName), Detail = "fileName is required." });
        if (!AllowedAudioMimeTypes.Contains(ContentType))
            response.ListErrors.Add(new Errors { Field = nameof(ContentType), Detail = "Audio format is not supported." });
        if (SizeBytes <= 0 || SizeBytes > MaxAudioFileSizeDefault)
            response.ListErrors.Add(new Errors { Field = nameof(SizeBytes), Detail = "Audio size must be between 1 byte and 20 MB." });
        if (string.IsNullOrWhiteSpace(Url))
            response.ListErrors.Add(new Errors { Field = nameof(Url), Detail = "url is required." });
        response.IsSuccess = response.ListErrors.Count == 0;
        response.StatusCode = response.IsSuccess ? 200 : 400;
        return Task.FromResult(response);
    }
}
