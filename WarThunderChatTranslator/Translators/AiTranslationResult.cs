using GTranslate;
using GTranslate.Results;

namespace WarThunderChatTranslator.Translators
{
    public sealed class AiLanguage : ILanguage
    {
        public AiLanguage(string name, string iso6391, string iso6393 = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? iso6391 : name;
            ISO6391 = string.IsNullOrWhiteSpace(iso6391) ? "und" : iso6391;
            ISO6393 = string.IsNullOrWhiteSpace(iso6393) ? ISO6391 : iso6393;
        }

        public string Name { get; }
        public string ISO6391 { get; }
        public string ISO6393 { get; }
    }

    public sealed class AiTranslationResult : ITranslationResult
    {
        public AiTranslationResult(
            string translation,
            string source,
            string service,
            ILanguage sourceLanguage,
            ILanguage targetLanguage)
        {
            Translation = translation;
            Source = source;
            Service = service;
            SourceLanguage = sourceLanguage;
            TargetLanguage = targetLanguage;
        }

        public string Translation { get; }
        public string Source { get; }
        public string Service { get; }
        public ILanguage SourceLanguage { get; }
        public ILanguage TargetLanguage { get; }
    }
}
