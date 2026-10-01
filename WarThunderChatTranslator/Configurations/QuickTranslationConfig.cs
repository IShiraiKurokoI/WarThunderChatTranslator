using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Configurations
{
    public enum QuickTranslationAudioCue
    {
        RecordingStart,
        RecordingEnd,
        Success,
        TranslationFailure
    }

    public static class QuickTranslationConfig
    {
        public const string EnabledKey = "QuickTranslationEnabled";
        public const string RecognitionLanguageKey = "QuickTranslationRecognitionLanguage";
        public const string HotkeysKey = "QuickTranslationHotkeys";
        public const string RecordingStartTimingKey = "QuickTranslationRecordingStartTiming";
        public const string RecordingStartTimingOnPlaybackStart = "OnPlaybackStart";
        public const string RecordingStartTimingAfterPlayback = "AfterPlayback";

        public const string RecordingStartSoundModeKey = "QuickTranslationRecordingStartSoundMode";
        public const string RecordingStartPromptTextKey = "QuickTranslationRecordingStartPromptText";
        public const string RecordingStartVoiceIdKey = "QuickTranslationRecordingStartVoiceId";
        public const string RecordingStartSpeakingRateKey = "QuickTranslationRecordingStartSpeakingRate";
        public const string RecordingStartCustomAudioPathKey = "QuickTranslationRecordingStartCustomAudioPath";
        public const string RecordingStartCustomAudioDisplayNameKey = "QuickTranslationRecordingStartCustomAudioDisplayName";
        public const string RecordingStartCustomAudioSourcePathKey = "QuickTranslationRecordingStartCustomAudioSourcePath";

        public const string RecordingEndSoundModeKey = "QuickTranslationRecordingEndSoundMode";
        public const string RecordingEndPromptTextKey = "QuickTranslationRecordingEndPromptText";
        public const string RecordingEndVoiceIdKey = "QuickTranslationRecordingEndVoiceId";
        public const string RecordingEndSpeakingRateKey = "QuickTranslationRecordingEndSpeakingRate";
        public const string RecordingEndCustomAudioPathKey = "QuickTranslationRecordingEndCustomAudioPath";
        public const string RecordingEndCustomAudioDisplayNameKey = "QuickTranslationRecordingEndCustomAudioDisplayName";
        public const string RecordingEndCustomAudioSourcePathKey = "QuickTranslationRecordingEndCustomAudioSourcePath";

        public const string SuccessSoundModeKey = "QuickTranslationSuccessSoundMode";
        public const string SuccessPromptTextKey = "QuickTranslationSuccessPromptText";
        public const string SuccessVoiceIdKey = "QuickTranslationSuccessVoiceId";
        public const string SuccessSpeakingRateKey = "QuickTranslationSuccessSpeakingRate";
        public const string SuccessCustomAudioPathKey = "QuickTranslationSuccessCustomAudioPath";
        public const string SuccessCustomAudioDisplayNameKey = "QuickTranslationSuccessCustomAudioDisplayName";
        public const string SuccessCustomAudioSourcePathKey = "QuickTranslationSuccessCustomAudioSourcePath";

        public const string TranslationFailureSoundModeKey = "QuickTranslationTranslationFailureSoundMode";
        public const string TranslationFailurePromptTextKey = "QuickTranslationTranslationFailurePromptText";
        public const string TranslationFailureVoiceIdKey = "QuickTranslationTranslationFailureVoiceId";
        public const string TranslationFailureSpeakingRateKey = "QuickTranslationTranslationFailureSpeakingRate";
        public const string TranslationFailureCustomAudioPathKey = "QuickTranslationTranslationFailureCustomAudioPath";
        public const string TranslationFailureCustomAudioDisplayNameKey = "QuickTranslationTranslationFailureCustomAudioDisplayName";
        public const string TranslationFailureCustomAudioSourcePathKey = "QuickTranslationTranslationFailureCustomAudioSourcePath";

        public const string SuccessVolumeKey = "QuickTranslationSuccessVolume";

        public const string SoundModeSystem = "System";
        public const string SoundModeTts = "TTS";
        public const string SoundModeCustom = "Custom";
        public const double DefaultSpeakingRate = 1.0;

        // RegisterHotKey IDs for an application must stay in the application range.
        public const int MaxHotkeyId = 0xBFFF;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public static IReadOnlyList<QuickTranslationHotkey> DefaultHotkeys => new[]
        {
            new QuickTranslationHotkey { Id = 1, Enabled = true, Shortcut = "Ctrl+Alt+1", TargetLanguage = "en" },
            new QuickTranslationHotkey { Id = 2, Enabled = true, Shortcut = "Ctrl+Alt+2", TargetLanguage = "ru" },
            new QuickTranslationHotkey { Id = 3, Enabled = true, Shortcut = "Ctrl+Alt+3", TargetLanguage = "de" },
            new QuickTranslationHotkey { Id = 4, Enabled = true, Shortcut = "Ctrl+Alt+4", TargetLanguage = "ja" },
            new QuickTranslationHotkey { Id = 5, Enabled = true, Shortcut = "Ctrl+Alt+5", TargetLanguage = "ko" }
        };

        public static IReadOnlyList<QuickTranslationHotkey> GetHotkeys()
        {
            var raw = ApplicationConfig.GetSettings(HotkeysKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DefaultHotkeys.Select(item => item.Clone()).ToArray();
            }

            try
            {
                var items = JsonSerializer.Deserialize<List<QuickTranslationHotkey>>(raw, JsonOptions);
                if (items == null)
                {
                    return DefaultHotkeys.Select(item => item.Clone()).ToArray();
                }

                if (items.Count == 0)
                {
                    return Array.Empty<QuickTranslationHotkey>();
                }

                var normalized = new List<QuickTranslationHotkey>(items.Count);
                var usedIds = new HashSet<int>();

                foreach (var item in items.Where(item => item is not null))
                {
                    var id = item.Id;
                    if (id <= 0 || id > MaxHotkeyId || !usedIds.Add(id))
                    {
                        id = FindFirstAvailableId(usedIds);
                        usedIds.Add(id);
                    }

                    normalized.Add(new QuickTranslationHotkey
                    {
                        Id = id,
                        Enabled = item.Enabled,
                        Shortcut = item.Shortcut ?? string.Empty,
                        TargetLanguage = string.IsNullOrWhiteSpace(item.TargetLanguage) ? "en" : item.TargetLanguage
                    });
                }

                return normalized;
            }
            catch
            {
                return DefaultHotkeys.Select(item => item.Clone()).ToArray();
            }
        }

        public static void SaveHotkeys(IEnumerable<QuickTranslationHotkey> hotkeys)
        {
            var payload = new List<QuickTranslationHotkey>();
            var usedIds = new HashSet<int>();

            foreach (var item in hotkeys?.Where(item => item is not null) ?? Enumerable.Empty<QuickTranslationHotkey>())
            {
                var id = item.Id;
                if (id <= 0 || id > MaxHotkeyId || !usedIds.Add(id))
                {
                    id = FindFirstAvailableId(usedIds);
                    usedIds.Add(id);
                }

                payload.Add(new QuickTranslationHotkey
                {
                    Id = id,
                    Enabled = item.Enabled,
                    Shortcut = item.Shortcut ?? string.Empty,
                    TargetLanguage = string.IsNullOrWhiteSpace(item.TargetLanguage) ? "en" : item.TargetLanguage
                });
            }

            ApplicationConfig.SaveSettings(HotkeysKey, JsonSerializer.Serialize(payload, JsonOptions));
        }

        public static int FindFirstAvailableId(IEnumerable<int> ids)
        {
            var usedIds = ids is HashSet<int> set ? set : new HashSet<int>(ids ?? Enumerable.Empty<int>());
            for (var id = 1; id <= MaxHotkeyId; id++)
            {
                if (!usedIds.Contains(id))
                {
                    return id;
                }
            }

            throw new InvalidOperationException(Localization.GetString("QuickTranslationNoFreeHotkeyIds"));
        }

        public static bool IsEnabled()
        {
            return bool.TryParse(ApplicationConfig.GetSettings(EnabledKey), out var enabled) && enabled;
        }

        public static string GetRecordingStartTiming()
        {
            var value = ApplicationConfig.GetSettings(RecordingStartTimingKey);
            return string.Equals(value, RecordingStartTimingAfterPlayback, StringComparison.OrdinalIgnoreCase)
                ? RecordingStartTimingAfterPlayback
                : RecordingStartTimingOnPlaybackStart;
        }

        public static double GetSuccessVolume()
        {
            return double.TryParse(ApplicationConfig.GetSettings(SuccessVolumeKey), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? Math.Clamp(value, 0.0, 1.0)
                : 0.8;
        }

        public static double GetSpeakingRate(QuickTranslationAudioCue cue)
        {
            return double.TryParse(
                ApplicationConfig.GetSettings(GetSpeakingRateKey(cue)),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value)
                ? Math.Clamp(value, 0.5, 6.0)
                : DefaultSpeakingRate;
        }

        public static string GetSoundModeKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartSoundModeKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndSoundModeKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureSoundModeKey,
            _ => SuccessSoundModeKey
        };

        public static string GetPromptTextKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartPromptTextKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndPromptTextKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailurePromptTextKey,
            _ => SuccessPromptTextKey
        };

        public static string GetVoiceIdKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartVoiceIdKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndVoiceIdKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureVoiceIdKey,
            _ => SuccessVoiceIdKey
        };

        public static string GetSpeakingRateKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartSpeakingRateKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndSpeakingRateKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureSpeakingRateKey,
            _ => SuccessSpeakingRateKey
        };

        public static string GetCustomAudioPathKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartCustomAudioPathKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndCustomAudioPathKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureCustomAudioPathKey,
            _ => SuccessCustomAudioPathKey
        };

        public static string GetCustomAudioDisplayNameKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartCustomAudioDisplayNameKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndCustomAudioDisplayNameKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureCustomAudioDisplayNameKey,
            _ => SuccessCustomAudioDisplayNameKey
        };

        public static string GetCustomAudioSourcePathKey(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart => RecordingStartCustomAudioSourcePathKey,
            QuickTranslationAudioCue.RecordingEnd => RecordingEndCustomAudioSourcePathKey,
            QuickTranslationAudioCue.TranslationFailure => TranslationFailureCustomAudioSourcePathKey,
            _ => SuccessCustomAudioSourcePathKey
        };

        public static string GetDefaultPromptText(QuickTranslationAudioCue cue) => cue switch
        {
            QuickTranslationAudioCue.RecordingStart =>
                Localization.GetString("QuickTranslationDefaultRecordingStartPrompt"),
            QuickTranslationAudioCue.RecordingEnd =>
                Localization.GetString("QuickTranslationDefaultRecordingEndPrompt"),
            QuickTranslationAudioCue.TranslationFailure =>
                Localization.GetString("QuickTranslationDefaultTranslationFailurePrompt"),
            _ => Localization.GetString("QuickTranslationDefaultSuccessPrompt")
        };
    }
}
