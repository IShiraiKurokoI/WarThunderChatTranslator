#nullable enable

using System.Collections.Generic;

namespace WarThunderChatTranslator.Entities
{
    internal sealed record SherpaTtsModelInfo(
        string Id,
        string DisplayName,
        string DirectoryPath,
        string ModelFile,
        string VoicesFile,
        string TokensFile,
        string DataDirectory,
        string Lexicon,
        string RuleFsts,
        IReadOnlyList<string> SpeakerNames,
        bool IsRecommended);
}
