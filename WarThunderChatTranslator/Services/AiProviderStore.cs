using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;

namespace WarThunderChatTranslator.Services
{
    public static class AiProviderStore
    {
        private const string ProvidersKey = "AiTranslationProviders";
        private const string SelectedProviderIdKey = "AiSelectedProviderId";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            Converters = { new JsonStringEnumConverter() }
        };

        public static IReadOnlyList<AiProviderConfig> GetProviders()
        {
            var raw = ApplicationConfig.GetSettings(ProvidersKey);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<AiProviderConfig>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<AiProviderConfig>>(raw, JsonOptions) ?? new List<AiProviderConfig>();
            }
            catch
            {
                return Array.Empty<AiProviderConfig>();
            }
        }

        public static AiProviderConfig GetSelectedProvider()
        {
            var providers = GetProviders();
            var selectedId = ApplicationConfig.GetSettings(SelectedProviderIdKey);
            return providers.FirstOrDefault(provider => provider.Id == selectedId);
        }

        public static string GetSelectedProviderId()
        {
            return ApplicationConfig.GetSettings(SelectedProviderIdKey) ?? string.Empty;
        }

        public static void SetSelectedProvider(string providerId)
        {
            var providers = GetProviders();
            if (!providers.Any(provider => provider.Id == providerId))
            {
                return;
            }

            ApplicationConfig.SaveSettings(SelectedProviderIdKey, providerId);
        }

        public static void SaveProvider(AiProviderConfig provider)
        {
            var providers = GetProviders().ToList();
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
                ApplicationConfig.SaveSettings(SelectedProviderIdKey, provider.Id);
            }
        }

        public static void DeleteProvider(string providerId)
        {
            var providers = GetProviders().Where(provider => provider.Id != providerId).ToList();
            SaveProviders(providers);

            if (GetSelectedProviderId() == providerId)
            {
                ApplicationConfig.SaveSettings(SelectedProviderIdKey, providers.FirstOrDefault()?.Id ?? string.Empty);
            }
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

        private static void SaveProviders(IReadOnlyCollection<AiProviderConfig> providers)
        {
            ApplicationConfig.SaveSettings(ProvidersKey, JsonSerializer.Serialize(providers, JsonOptions));
        }
    }
}
