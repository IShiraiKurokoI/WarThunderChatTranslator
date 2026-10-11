#nullable enable

using System;

namespace WarThunderChatTranslator.Entities
{
    public sealed class GptSoVitsVoiceProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string GptWeightsPath { get; set; } = string.Empty;
        public string SoVitsWeightsPath { get; set; } = string.Empty;
        public string ReferenceAudioPath { get; set; } = string.Empty;
        public string PromptText { get; set; } = string.Empty;
        public string PromptLanguage { get; set; } = "zh";
        public string TextLanguage { get; set; } = "auto";

        public GptSoVitsVoiceProfile Clone() => new()
        {
            Id = Id,
            Name = Name,
            GptWeightsPath = GptWeightsPath,
            SoVitsWeightsPath = SoVitsWeightsPath,
            ReferenceAudioPath = ReferenceAudioPath,
            PromptText = PromptText,
            PromptLanguage = PromptLanguage,
            TextLanguage = TextLanguage
        };
    }
}
