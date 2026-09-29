using System;
using System.Globalization;
using Windows.Storage;

namespace WarThunderChatTranslator.Configurations
{
    static class ApplicationConfig
    {
        private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;

        public const string GamePollingIntervalSecondsKey = "GamePollingIntervalSeconds";
        public const string WebPollingIntervalSecondsKey = "WebPollingIntervalSeconds";

        public const int DefaultPollingIntervalSeconds = 4;
        public const int MinPollingIntervalSeconds = 1;
        public const int MaxPollingIntervalSeconds = 60;

        public static void SaveSettings(string key, string value)
        {
            LocalSettings.Values[key] = value;
        }

        public static string GetSettings(string key)
        {
            return LocalSettings.Values[key] as string;
        }

        public static int GetPollingIntervalSeconds(string key)
        {
            var rawValue = GetSettings(key);
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return DefaultPollingIntervalSeconds;
            }

            return Math.Clamp(seconds, MinPollingIntervalSeconds, MaxPollingIntervalSeconds);
        }

        public static TimeSpan GetGamePollingInterval()
        {
            return TimeSpan.FromSeconds(GetPollingIntervalSeconds(GamePollingIntervalSecondsKey));
        }

        public static int GetWebPollingIntervalMilliseconds()
        {
            return checked(GetPollingIntervalSeconds(WebPollingIntervalSecondsKey) * 1000);
        }
    }
}
