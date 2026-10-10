#nullable enable

using System;
using System.Globalization;

namespace WarThunderChatTranslator.Configurations
{
    public static class ChatTtsConfig
    {
        public const string EnabledKey = "ChatTtsEnabled";
        public const string ProviderKey = "ChatTtsProvider";
        public const string WindowsVoiceIdKey = "ChatTtsWindowsVoiceId";
        public const string SherpaVoiceIdKey = "ChatTtsSherpaVoiceId";
        public const string SherpaNumThreadsKey = "ChatTtsSherpaNumThreads";
        public const string SpeakingRateKey = "ChatTtsSpeakingRate";
        public const string VolumeKey = "ChatTtsVolume";
        public const string SpeakAllyKey = "ChatTtsSpeakAlly";
        public const string SpeakEnemyKey = "ChatTtsSpeakEnemy";
        public const string SpeakSystemKey = "ChatTtsSpeakSystem";
        public const string QueueCapacityKey = "ChatTtsQueueCapacity";
        public const string MaxQueueAgeSecondsKey = "ChatTtsMaxQueueAgeSeconds";

        public const string ProviderWindows = "Windows";
        public const string ProviderSherpaOnnx = "SherpaOnnx";
        public const double DefaultSpeakingRate = 1.0;
        public const double DefaultVolume = 0.8;
        public const int DefaultQueueCapacity = 3;
        public const int MinQueueCapacity = 1;
        public const int MaxQueueCapacity = 50;
        public const int DefaultMaxQueueAgeSeconds = 8;
        public const int MinMaxQueueAgeSeconds = 1;
        public const int MaxMaxQueueAgeSeconds = 300;
        public const int DefaultSherpaNumThreads = 2;
        public const int MinSherpaNumThreads = 1;
        public const int MaxSherpaNumThreads = 16;

        public static bool IsEnabled() =>
            ApplicationConfig.GetBooleanSetting(EnabledKey);

        public static bool ShouldSpeakAlly() =>
            ApplicationConfig.GetBooleanSetting(SpeakAllyKey, fallback: true);

        public static bool ShouldSpeakEnemy() =>
            ApplicationConfig.GetBooleanSetting(SpeakEnemyKey, fallback: false);

        public static bool ShouldSpeakSystem() =>
            ApplicationConfig.GetBooleanSetting(SpeakSystemKey, fallback: false);

        public static string GetProvider()
        {
            var provider = ApplicationConfig.GetSettings(ProviderKey);
            return string.Equals(provider, ProviderSherpaOnnx, StringComparison.Ordinal)
                ? ProviderSherpaOnnx
                : ProviderWindows;
        }

        public static string GetVoiceId(string? providerId = null)
        {
            providerId ??= GetProvider();
            var key = string.Equals(providerId, ProviderSherpaOnnx, StringComparison.Ordinal)
                ? SherpaVoiceIdKey
                : WindowsVoiceIdKey;
            return ApplicationConfig.GetSettings(key) ?? string.Empty;
        }


        public static int GetSherpaNumThreads()
        {
            return int.TryParse(
                ApplicationConfig.GetSettings(SherpaNumThreadsKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? Math.Clamp(value, MinSherpaNumThreads, MaxSherpaNumThreads)
                : DefaultSherpaNumThreads;
        }

        public static double GetSpeakingRate()
        {
            return double.TryParse(
                ApplicationConfig.GetSettings(SpeakingRateKey),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? Math.Clamp(value, 0.5, 2.0)
                : DefaultSpeakingRate;
        }

        public static double GetVolume()
        {
            return double.TryParse(
                ApplicationConfig.GetSettings(VolumeKey),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? Math.Clamp(value, 0.0, 1.0)
                : DefaultVolume;
        }

        public static int GetQueueCapacity()
        {
            return int.TryParse(
                ApplicationConfig.GetSettings(QueueCapacityKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? Math.Clamp(value, MinQueueCapacity, MaxQueueCapacity)
                : DefaultQueueCapacity;
        }

        /// <summary>
        /// Returns the maximum time a queued message may wait before being skipped.
        /// A null value means queued messages never expire.
        /// </summary>
        public static TimeSpan? GetMaxQueueAge()
        {
            if (!int.TryParse(
                ApplicationConfig.GetSettings(MaxQueueAgeSecondsKey),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var seconds))
            {
                seconds = DefaultMaxQueueAgeSeconds;
            }

            if (seconds <= 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(Math.Clamp(seconds, MinMaxQueueAgeSeconds, MaxMaxQueueAgeSeconds));
        }

        public static int GetMaxQueueAgeSecondsOrZero()
        {
            var age = GetMaxQueueAge();
            return age.HasValue ? (int)Math.Round(age.Value.TotalSeconds) : 0;
        }
    }
}
