using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    public static class AiProviderStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        public static IReadOnlyList<AiProviderConfig> GetProviders()
        {
            try
            {
                return ReadProviders();
            }
            catch
            {
                return Array.Empty<AiProviderConfig>();
            }
        }

        public static AiProviderConfig GetSelectedProvider()
        {
            var providers = GetProviders();
            var selectedId = ApplicationConfig.GetSettings(ApplicationConfig.AiSelectedProviderIdKey);
            return providers.FirstOrDefault(provider => provider.Id == selectedId);
        }

        public static string GetSelectedProviderId()
        {
            return ApplicationConfig.GetSettings(ApplicationConfig.AiSelectedProviderIdKey) ?? string.Empty;
        }

        public static void SetSelectedProvider(string providerId)
        {
            var providers = GetProviders();
            if (!providers.Any(provider => provider.Id == providerId))
            {
                return;
            }

            ApplicationConfig.SaveSettings(ApplicationConfig.AiSelectedProviderIdKey, providerId);
        }

        public static void SaveProvider(AiProviderConfig provider)
        {
            var providers = ReadProviders();
            var existingIndex = providers.FindIndex(item => item.Id == provider.Id);
            if (existingIndex >= 0)
            {
                providers[existingIndex] = provider;
            }
            else
            {
                providers.Add(provider);
            }

            SaveProviders(providers);

            if (string.IsNullOrWhiteSpace(GetSelectedProviderId()))
            {
                ApplicationConfig.SaveSettings(ApplicationConfig.AiSelectedProviderIdKey, provider.Id);
            }
        }

        public static void DeleteProvider(string providerId)
        {
            var providers = ReadProviders().Where(provider => provider.Id != providerId).ToList();
            SaveProviders(providers);

            if (GetSelectedProviderId() == providerId)
            {
                ApplicationConfig.SaveSettings(ApplicationConfig.AiSelectedProviderIdKey, providers.FirstOrDefault()?.Id ?? string.Empty);
            }
        }

        public static void ImportProviders(IReadOnlyCollection<AiProviderConfig> imported)
        {
            var providers = ReadProviders();
            var names = new HashSet<string>(providers.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var provider in imported)
            {
                // Import always creates a copy; external IDs never address local settings.
                var copy = JsonSerializer.Deserialize<AiProviderConfig>(JsonSerializer.Serialize(provider, JsonOptions), JsonOptions);
                copy.Id = Guid.NewGuid().ToString("N");
                var name = copy.Name;
                for (var suffix = 2; !names.Add(copy.Name); suffix++)
                {
                    copy.Name = $"{name} ({suffix})";
                }
                providers.Add(copy);
            }

            // One atomic file replacement for the complete bundle. Selection is separate.
            SaveProviders(providers);
        }

        public static AiProviderConfig CreateDefault(AiApiType apiType)
        {
            return apiType switch
            {
                AiApiType.OpenAIChatCompletions => new AiProviderConfig
                {
                    ApiType = AiApiType.OpenAIChatCompletions,
                    Name = "OpenAI Compatible",
                    BaseUrl = "https://api.openai.com/v1",
                    Model = "gpt-4o-mini"
                },
                AiApiType.Anthropic => new AiProviderConfig
                {
                    ApiType = AiApiType.Anthropic,
                    Name = "Anthropic",
                    BaseUrl = "https://api.anthropic.com",
                    Model = "claude-haiku-4-5",
                    Temperature = 1.0
                },
                _ => new AiProviderConfig
                {
                    ApiType = AiApiType.OpenAIResponses,
                    Name = "OpenAI Responses",
                    BaseUrl = "https://api.openai.com/v1",
                    Model = "gpt-4o-mini"
                }
            };
        }

        private static List<AiProviderConfig> ReadProviders()
        {
            var path = ApplicationConfig.AiProvidersFilePath;
            // Migrate on the next successful save; never delete the legacy value on failure.
            var raw = File.Exists(path) ? File.ReadAllText(path) : ApplicationConfig.GetSettings(ApplicationConfig.AiTranslationProvidersKey);
            if (string.IsNullOrWhiteSpace(raw)) return new List<AiProviderConfig>();
            var providers = JsonSerializer.Deserialize<List<AiProviderConfig>>(raw, JsonOptions);
            if (providers == null || providers.Any(provider => provider == null))
                throw new InvalidDataException("Invalid AI provider configuration.");
            return providers;
        }

        private static void SaveProviders(IReadOnlyCollection<AiProviderConfig> providers)
        {
            var path = ApplicationConfig.AiProvidersFilePath;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, JsonSerializer.Serialize(providers, JsonOptions), new UTF8Encoding(false));
                // Same-directory rename avoids partially written bundles and the 8 KiB
                // ApplicationData settings-value limit. Credentials stay in app-local storage.
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
