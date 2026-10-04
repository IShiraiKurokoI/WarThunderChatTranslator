namespace WarThunderChatTranslator.Configurations
{
    // Isolate persistence tests from the user's Windows settings.
    internal static class ApplicationConfig
    {
        public const string AiTranslationProvidersKey = "providers";
        public const string AiSelectedProviderIdKey = "selected";
        public static readonly Dictionary<string, string> Settings = new();
        public static int WriteCount;
        public static string AiProvidersFilePath { get; set; }
        public static string GetSettings(string key) => Settings.GetValueOrDefault(key);
        public static void SaveSettings(string key, string value)
        {
            Settings[key] = value;
            WriteCount++;
        }
    }
}
