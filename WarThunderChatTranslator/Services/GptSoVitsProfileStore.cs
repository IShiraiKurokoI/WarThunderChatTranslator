#nullable enable

using System.Text.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    internal sealed class GptSoVitsProfileStore
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly object _sync = new();
        private List<GptSoVitsVoiceProfile> _profiles;

        public static GptSoVitsProfileStore Shared { get; } = new();

        public string StorageDirectory { get; }
        public string StorageFilePath { get; }

        private GptSoVitsProfileStore() : this(GetDefaultStorageDirectory()) { }

        // An explicit directory keeps profile persistence testable without writing to the user's Documents folder.
        internal GptSoVitsProfileStore(string storageDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageDirectory);
            StorageDirectory = storageDirectory;
            StorageFilePath = Path.Combine(StorageDirectory, "profiles.json");
            _profiles = LoadProfiles();
        }

        private static string GetDefaultStorageDirectory()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var root = string.IsNullOrWhiteSpace(documents)
                ? AppContext.BaseDirectory
                : Path.Combine(documents, "WTChatTranslator");
            return Path.Combine(root, "VoiceProfiles", "GPT-SoVITS");
        }

        public IReadOnlyList<GptSoVitsVoiceProfile> GetProfiles()
        {
            lock (_sync)
            {
                return _profiles
                    .Select(profile => profile.Clone())
                    .ToArray();
            }
        }

        public GptSoVitsVoiceProfile? GetById(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            lock (_sync)
            {
                return _profiles
                    .FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.Ordinal))
                    ?.Clone();
            }
        }

        public void Upsert(GptSoVitsVoiceProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var normalized = Normalize(profile);
            lock (_sync)
            {
                var index = _profiles.FindIndex(item => string.Equals(item.Id, normalized.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    _profiles[index] = normalized;
                }
                else
                {
                    _profiles.Add(normalized);
                }
                SaveProfilesLocked();
            }
        }

        public bool Delete(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            lock (_sync)
            {
                var removed = _profiles.RemoveAll(profile => string.Equals(profile.Id, id, StringComparison.Ordinal)) > 0;
                if (removed)
                {
                    SaveProfilesLocked();
                }
                return removed;
            }
        }

        private List<GptSoVitsVoiceProfile> LoadProfiles()
        {
            try
            {
                if (!File.Exists(StorageFilePath))
                {
                    return new List<GptSoVitsVoiceProfile>();
                }

                var json = File.ReadAllText(StorageFilePath);
                var profiles = JsonSerializer.Deserialize<List<GptSoVitsVoiceProfile>>(json)
                    ?? new List<GptSoVitsVoiceProfile>();
                return profiles
                    .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
                    .Select(Normalize)
                    .ToList();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not load GPT-SoVITS voice profiles.");
                return new List<GptSoVitsVoiceProfile>();
            }
        }

        private void SaveProfilesLocked()
        {
            Directory.CreateDirectory(StorageDirectory);
            var json = JsonSerializer.Serialize(_profiles, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            var tempPath = StorageFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, StorageFilePath, true);
        }

        private static GptSoVitsVoiceProfile Normalize(GptSoVitsVoiceProfile profile)
        {
            return new GptSoVitsVoiceProfile
            {
                Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim(),
                Name = profile.Name?.Trim() ?? string.Empty,
                GptWeightsPath = profile.GptWeightsPath?.Trim() ?? string.Empty,
                SoVitsWeightsPath = profile.SoVitsWeightsPath?.Trim() ?? string.Empty,
                ReferenceAudioPath = profile.ReferenceAudioPath?.Trim() ?? string.Empty,
                PromptText = profile.PromptText?.Trim() ?? string.Empty,
                PromptLanguage = NormalizePromptLanguage(profile.PromptLanguage),
                TextLanguage = NormalizeTextLanguage(profile.TextLanguage)
            };
        }

        private static string NormalizePromptLanguage(string? language) => language switch
        {
            "en" => "en",
            "ja" => "ja",
            "ko" => "ko",
            "yue" => "yue",
            _ => "zh"
        };

        private static string NormalizeTextLanguage(string? language) => language switch
        {
            "all_zh" => "all_zh",
            "en" => "en",
            "all_ja" => "all_ja",
            "all_ko" => "all_ko",
            "all_yue" => "all_yue",
            "zh" => "zh",
            "ja" => "ja",
            "ko" => "ko",
            "yue" => "yue",
            "auto_yue" => "auto_yue",
            _ => "auto"
        };
    }
}
