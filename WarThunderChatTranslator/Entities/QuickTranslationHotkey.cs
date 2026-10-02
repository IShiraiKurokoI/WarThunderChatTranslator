namespace WarThunderChatTranslator.Entities
{
    public sealed class QuickTranslationHotkey
    {
        public int Id { get; set; }
        public bool Enabled { get; set; } = true;
        public string Shortcut { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = "en";

        public QuickTranslationHotkey Clone()
        {
            return new QuickTranslationHotkey
            {
                Id = Id,
                Enabled = Enabled,
                Shortcut = Shortcut,
                TargetLanguage = TargetLanguage
            };
        }
    }
}
