#nullable enable

using System;
using System.Globalization;

namespace WarThunderChatTranslator.Configurations
{
    public static class ContentFilterConfig
    {
        public const string MinimumSeverityKey = "ContentFilterMinimumSeverity";
        public const string FilterDisplayKey = "ContentFilterDisplay";
        public const string FilterTtsKey = "ContentFilterTts";
        public const string TtsActionKey = "ContentFilterTtsAction";

        public const string TtsActionRemoveMatchedTerms = "RemoveMatchedTerms";
        public const string TtsActionSkipMessage = "SkipMessage";

        public const int DefaultMinimumSeverity = 2;
        public const int MinSeverity = 1;
        public const int MaxSeverity = 3;

        public static bool ShouldFilterDisplay() =>
            ApplicationConfig.GetBooleanSetting(FilterDisplayKey, fallback: false);

        public static bool ShouldFilterTts() =>
            ApplicationConfig.GetBooleanSetting(FilterTtsKey, fallback: false);

        public static int GetMinimumSeverity()
        {
            return int.TryParse(
                ApplicationConfig.GetSettings(MinimumSeverityKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var severity)
                ? Math.Clamp(severity, MinSeverity, MaxSeverity)
                : DefaultMinimumSeverity;
        }

        public static string GetTtsAction()
        {
            var value = ApplicationConfig.GetSettings(TtsActionKey);
            return string.Equals(value, TtsActionSkipMessage, StringComparison.Ordinal)
                ? TtsActionSkipMessage
                : TtsActionRemoveMatchedTerms;
        }
    }
}
