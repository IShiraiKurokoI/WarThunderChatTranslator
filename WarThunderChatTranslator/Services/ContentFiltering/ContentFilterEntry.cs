#nullable enable

namespace WarThunderChatTranslator.Services.ContentFiltering
{
    public enum ContentFilterMatchMode
    {
        Auto = 0,
        Word = 1,
        Contains = 2
    }

    public sealed class ContentFilterEntry
    {
        public string Term { get; set; } = string.Empty;
        public int Severity { get; set; } = 2;
        public string Language { get; set; } = "auto";
        public ContentFilterMatchMode MatchMode { get; set; } = ContentFilterMatchMode.Auto;

        public ContentFilterEntry Clone() => new()
        {
            Term = Term,
            Severity = Severity,
            Language = Language,
            MatchMode = MatchMode
        };
    }
}
