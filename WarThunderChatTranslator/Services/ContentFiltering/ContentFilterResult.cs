#nullable enable

using System.Collections.Generic;

namespace WarThunderChatTranslator.Services.ContentFiltering
{
    public sealed class ContentFilterResult
    {
        public string OriginalText { get; init; } = string.Empty;
        public string FilteredText { get; init; } = string.Empty;
        public bool IsMatch { get; init; }
        public int MaxSeverity { get; init; }
        public IReadOnlyList<ContentFilterMatch> Matches { get; init; } = [];
    }

    public sealed record ContentFilterMatch(
        int Start,
        int Length,
        string Term,
        int Severity,
        ContentFilterMatchMode MatchMode);
}
