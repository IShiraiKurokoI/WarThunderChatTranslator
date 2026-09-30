using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Windows.Storage;

namespace WarThunderChatTranslator.Configurations
{
    static class ApplicationConfig
    {
        private static readonly ApplicationDataContainer LocalSettings = ApplicationData.Current.LocalSettings;
        private static readonly object SettingsLock = new();
        private static long _dashboardStyleVersion;

        private static readonly HashSet<string> DashboardStyleKeys = new(StringComparer.Ordinal)
        {
            "FontFamily",
            "FontSize",
            "FontStyle",
            "AllyFontColor",
            "EnemyFontColor",
            "SystemFontColor",
            "BackgroundCSS"
        };

        public const string GamePollingIntervalSecondsKey = "GamePollingIntervalSeconds";
        public const string WebPollingIntervalSecondsKey = "WebPollingIntervalSeconds";

        public const int DefaultPollingIntervalSeconds = 4;
        public const int MinPollingIntervalSeconds = 1;
        public const int MaxPollingIntervalSeconds = 60;

        public static void SaveSettings(string key, string value)
        {
            lock (SettingsLock)
            {
                var existingValue = LocalSettings.Values[key] as string;
                if (string.Equals(existingValue, value, StringComparison.Ordinal))
                {
                    return;
                }

                LocalSettings.Values[key] = value;
                if (DashboardStyleKeys.Contains(key))
                {
                    Interlocked.Increment(ref _dashboardStyleVersion);
                }
            }
        }

        public static string GetSettings(string key)
        {
            lock (SettingsLock)
            {
                return LocalSettings.Values[key] as string;
            }
        }

        public static IReadOnlyDictionary<string, string> GetSettingsSnapshot(params string[] keys)
        {
            var snapshot = new Dictionary<string, string>(keys.Length, StringComparer.Ordinal);
            lock (SettingsLock)
            {
                foreach (var key in keys)
                {
                    if (LocalSettings.Values[key] is string value)
                    {
                        snapshot[key] = value;
                    }
                }
            }

            return snapshot;
        }

        public static long GetDashboardStyleVersion()
        {
            return Interlocked.Read(ref _dashboardStyleVersion);
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
