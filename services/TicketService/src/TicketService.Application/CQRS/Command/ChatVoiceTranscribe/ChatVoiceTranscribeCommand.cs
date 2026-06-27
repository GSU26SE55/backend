using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.Http;
using SharedContracts.Common.Responses;
using SharedContracts.Interfaces;
using TicketService.Application.DTOs.Response.Tickets;
using TicketService.Domain.Enums;

namespace TicketService.Application.CQRS.Command.ChatVoiceTranscribe;

public class ChatVoiceTranscribeCommand : IRequest<TicketActionResponse>, IValidatable<TicketActionResponse>
{
    public const long MaxAudioFileSizeDefault = 20_971_520; // 20MB — Gemini inline limit

    private static readonly HashSet<string> AllowedAudioMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg", "audio/wav", "audio/ogg", "audio/webm", "audio/mp4", "audio/flac"
    };

    [JsonIgnore]
    public Guid TicketId { get; set; }

    [JsonIgnore]
    public Guid UserId { get; set; }

    [JsonIgnore]
    public ActorRoleEnum UserRole { get; set; }

    [JsonIgnore]
    public string UserDisplayName { get; set; } = string.Empty;

    [JsonIgnore]
    public List<string> UserPermissions { get; set; } = new();

    [JsonIgnore]
    public IFormFile? AudioFile { get; set; }

    [JsonIgnore]
    public string AuthorizationHeader { get; set; } = string.Empty;

    public Task<TicketActionResponse> ValidateAsync()
    {
        var response = new TicketActionResponse();

        if (AudioFile is null || AudioFile.Length <= 0)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors { Field = "audioFile", Detail = "File audio là bắt buộc." });
            return Task.FromResult(response);
        }

        if (!AllowedAudioMimeTypes.Contains(AudioFile.ContentType))
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors
            {
                Field = "audioFile",
                Detail = $"Định dạng audio không hợp lệ. Chấp nhận: {string.Join(", ", AllowedAudioMimeTypes)}."
            });
        }

        if (AudioFile.Length > MaxAudioFileSizeDefault)
        {
            response.IsSuccess = false;
            response.StatusCode = 400;
            response.ListErrors.Add(new Errors
            {
                Field = "audioFile",
                Detail = "File audio vượt quá giới hạn 20 MB."
            });
        }

        return Task.FromResult(response);
    }
}
