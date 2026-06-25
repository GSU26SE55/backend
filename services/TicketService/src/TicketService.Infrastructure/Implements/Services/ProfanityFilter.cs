using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using TicketService.Application.Common.Models;
using TicketService.Application.Interfaces.Services;

namespace TicketService.Infrastructure.Implements.Services;

/// <summary>Config dict VN/EN từ <c>Chat:ProfanityWords</c> (appsettings.json) — chỉ cảnh báo (#519).</summary>
public class ProfanityFilter : IProfanityFilter
{
    private readonly ChatOptions _options;

    public ProfanityFilter(IOptions<ChatOptions> options)
    {
        _options = options.Value;
    }

    public bool ContainsProfanity(string body, out IReadOnlyList<string> matchedWords)
    {
        var matches = new List<string>();

        if (!string.IsNullOrWhiteSpace(body))
        {
            foreach (var words in _options.ProfanityWords.Values)
            {
                foreach (var word in words)
                {
                    if (!string.IsNullOrWhiteSpace(word) && body.Contains(word, StringComparison.OrdinalIgnoreCase))
                        matches.Add(word);
                }
            }
        }

        matchedWords = matches;
        return matches.Count > 0;
    }
}
