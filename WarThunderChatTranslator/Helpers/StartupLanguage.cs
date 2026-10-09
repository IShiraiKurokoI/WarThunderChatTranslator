using System;

namespace WarThunderChatTranslator.Helpers
{
    // No WinUI dependencies: system-language routing is covered by unit tests.
    public static class StartupLanguage
    {
        public const string English = "en-US";
        public const string SimplifiedChinese = "zh-CN";
        public const string TraditionalChinese = "zh-TW";

        public static string FromSystemCulture(string cultureName)
        {
            var name = Normalize(cultureName);

            // An explicit script takes priority over the region, even if both are present.
            if (IsTagOrChild(name, "zh-Hant") || IsTagOrChild(name, "zh-CHT") ||
                IsTagOrChild(name, "zh-TW") || IsTagOrChild(name, "zh-HK") ||
                IsTagOrChild(name, "zh-MO"))
            {
                return TraditionalChinese;
            }

            if (IsTagOrChild(name, "zh-Hans") || IsTagOrChild(name, "zh-CHS") ||
                IsTagOrChild(name, "zh-CN") || IsTagOrChild(name, "zh-SG") ||
                IsTagOrChild(name, "zh-MY") || string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase))
            {
                return SimplifiedChinese;
            }

            // English dialects are supported; every other language uses English fallback.
            return English;
        }

        public static bool IsSupportedSystemCulture(string cultureName)
        {
            var name = Normalize(cultureName);
            return IsTagOrChild(name, "en") ||
                string.Equals(FromSystemCulture(name), SimplifiedChinese, StringComparison.Ordinal) ||
                string.Equals(FromSystemCulture(name), TraditionalChinese, StringComparison.Ordinal);
        }

        public static bool NeedsUnsupportedSystemLanguageNotice(string savedLanguage, string systemLanguage) =>
            !HasSupportedSavedPreference(savedLanguage) && !IsSupportedSystemCulture(systemLanguage);

        public static string ResolveAppLanguage(string savedLanguage, string systemLanguage)
        {
            // Preserve a valid manual preference. Ignore a corrupt or unknown stored tag.
            // Automatic system-derived language is not persisted, so changes to the OS
            // display language can be observed at a subsequent launch.
            return FromSystemCulture(HasSupportedSavedPreference(savedLanguage)
                ? savedLanguage : systemLanguage);
        }

        private static bool HasSupportedSavedPreference(string language) =>
            !string.IsNullOrWhiteSpace(language) && IsSupportedSystemCulture(language);

        private static string Normalize(string value) => value?.Trim().Replace('_', '-') ?? string.Empty;

        private static bool IsTagOrChild(string name, string tag) =>
            string.Equals(name, tag, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(tag + "-", StringComparison.OrdinalIgnoreCase);
    }
}
