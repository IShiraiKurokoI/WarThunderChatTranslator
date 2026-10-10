#nullable enable

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

        public const string ApplicationLanguageKey = "ApplicationLanguage";
        public const string UnsupportedSystemLanguageNoticeKeyPrefix = "UnsupportedSystemLanguageNotice.";
        public const string NetworkProxyModeKey = "NetworkProxyMode";
        public const string ProxyAddressKey = "ProxyAddress";
        public const string ProxyAccountKey = "ProxyAccount";
        public const string ProxyPasswordKey = "ProxyPassword";
        public const string LastUpdateCheckDateKey = "LastUpdateCheckDate";
        public const string TranslateApiKey = "TranslateAPI";
        public const string AiTranslationProvidersKey = "AiTranslationProviders";
        public const string AiSelectedProviderIdKey = "AiSelectedProviderId";
        public const string TargetLanguageKey = "TargetLanguage";
        public const string FontFamilyKey = "FontFamily";
        public const string FontSizeKey = "FontSize";
        public const string FontStyleKey = "FontStyle";
        public const string AllyFontColorKey = "AllyFontColor";
        public const string EnemyFontColorKey = "EnemyFontColor";
        public const string SystemFontColorKey = "SystemFontColor";
        public const string ThemeKey = "Theme";
        public const string BackgroundCssKey = "BackgroundCSS";

        private static readonly HashSet<string> DashboardStyleKeys = new(StringComparer.Ordinal)
        {
            FontFamilyKey,
            FontSizeKey,
            FontStyleKey,
            AllyFontColorKey,
            EnemyFontColorKey,
            SystemFontColorKey,
            BackgroundCssKey
        };

        public const string GamePollingIntervalSecondsKey = "GamePollingIntervalSeconds";
        public const string WebPollingIntervalSecondsKey = "WebPollingIntervalSeconds";

        public const string ClearBattleChatCacheKey = "ClearBattleChatCache";
        public const string BattleChatClearModeKey = "BattleChatClearMode";
        public const string PhysicalChatCacheLimitKey = "PhysicalChatCacheLimit";
        public const string OpenOverlayOnStartupKey = "OpenOverlayOnStartup";
        public const string OpenDashboardOnStartupKey = "OpenDashboardOnStartup";
        public const string LanFirewallRuleDisabledByUserKey = "LanFirewallRuleDisabledByUser";

        public const string BattleChatClearModeNone = "None";
        public const string BattleChatClearModeLogical = "Logical";
        public const string BattleChatClearModePhysical = "Physical";

        public static event Action<string, string>? BattleChatClearModeChanged;

        public const string OverlayDisplayModeKey = "OverlayDisplayMode";
        public const string OverlayShowSourceLanguageKey = "OverlayShowSourceLanguage";
        public const string OverlayMonitorDeviceKey = "OverlayMonitorDevice";
        public const string OverlayXKey = "OverlayX";
        public const string OverlayYKey = "OverlayY";
        public const string OverlayWidthKey = "OverlayWidth";
        public const string OverlayHeightKey = "OverlayHeight";
        public const string OverlayOpacityPercentKey = "OverlayOpacityPercent";

        public const int DefaultPollingIntervalSeconds = 4;
        public const int MinPollingIntervalSeconds = 1;
        public const int MaxPollingIntervalSeconds = 60;

        public const int DefaultPhysicalChatCacheLimit = 400;
        public const int MinPhysicalChatCacheLimit = 50;
        public const int MaxPhysicalChatCacheLimit = 5000;

        public static void SaveSettings(string key, string value)
        {
            string? oldBattleChatClearMode = null;
            string? newBattleChatClearMode = null;
            var battleChatClearModeChanged = false;

            lock (SettingsLock)
            {
                var existingValue = LocalSettings.Values[key] as string;

                if (string.Equals(key, BattleChatClearModeKey, StringComparison.Ordinal))
                {
                    var legacyEnabledRaw = LocalSettings.Values[ClearBattleChatCacheKey] as string;
                    var legacyDisabled = bool.TryParse(legacyEnabledRaw, out var legacyEnabled) && !legacyEnabled;
                    oldBattleChatClearMode = legacyDisabled
                        ? BattleChatClearModeNone
                        : NormalizeBattleChatClearMode(existingValue);
                    newBattleChatClearMode = NormalizeBattleChatClearMode(value);

                    if (string.Equals(oldBattleChatClearMode, newBattleChatClearMode, StringComparison.Ordinal))
                    {
                        return;
                    }

                    LocalSettings.Values[BattleChatClearModeKey] = newBattleChatClearMode;
                    LocalSettings.Values[ClearBattleChatCacheKey] =
                        (!string.Equals(newBattleChatClearMode, BattleChatClearModeNone, StringComparison.Ordinal))
                            .ToString()
                            .ToLowerInvariant();
                    battleChatClearModeChanged = true;
                }
                else
                {
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

            if (battleChatClearModeChanged)
            {
                BattleChatClearModeChanged?.Invoke(
                    oldBattleChatClearMode ?? BattleChatClearModeLogical,
                    newBattleChatClearMode ?? BattleChatClearModeLogical);
            }
        }

        public static string? GetSettings(string key)
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

        public static bool GetBooleanSetting(string key, bool fallback = false)
        {
            var rawValue = GetSettings(key);
            return bool.TryParse(rawValue, out var value) ? value : fallback;
        }

        public static string GetBattleChatClearMode()
        {
            // Compatibility with the earlier two-setting implementation. If the old toggle
            // explicitly disabled per-battle cleanup, preserve that preference after upgrade.
            var legacyEnabled = GetSettings(ClearBattleChatCacheKey);
            if (bool.TryParse(legacyEnabled, out var enabled) && !enabled)
            {
                return BattleChatClearModeNone;
            }

            var configuredMode = GetSettings(BattleChatClearModeKey);
            return string.IsNullOrWhiteSpace(configuredMode)
                ? BattleChatClearModeLogical
                : NormalizeBattleChatClearMode(configuredMode);
        }

        private static string NormalizeBattleChatClearMode(string? value)
        {
            if (string.Equals(value, BattleChatClearModeNone, StringComparison.OrdinalIgnoreCase))
            {
                return BattleChatClearModeNone;
            }

            if (string.Equals(value, BattleChatClearModePhysical, StringComparison.OrdinalIgnoreCase))
            {
                return BattleChatClearModePhysical;
            }

            return BattleChatClearModeLogical;
        }

        public static int GetPhysicalChatCacheLimit()
        {
            var rawValue = GetSettings(PhysicalChatCacheLimitKey);
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit))
            {
                return DefaultPhysicalChatCacheLimit;
            }

            return Math.Clamp(limit, MinPhysicalChatCacheLimit, MaxPhysicalChatCacheLimit);
        }
    }
}
