using System;

using System.Collections.Generic;

namespace WarThunderChatTranslator.Entities
{
    public enum AiApiType
    {
        OpenAIResponses,
        OpenAIChatCompletions,
        Anthropic
    }

    public sealed class AiProviderConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public AiApiType ApiType { get; set; } = AiApiType.OpenAIResponses;
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiKey { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool UseTemperature { get; set; }
        public double Temperature { get; set; } = 0.1;
        public string CustomPrompt { get; set; } = string.Empty;
        public int? MaxOutputTokens { get; set; }
        public string MaxTokensField { get; set; } = "max_tokens";
        public double? TimeoutSeconds { get; set; }
        public Dictionary<string, string> ExtraHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        // Deeplinks supply an explicit API base, including any gateway path/version.
        public bool UseExactBaseUrl { get; set; }
    }
}
