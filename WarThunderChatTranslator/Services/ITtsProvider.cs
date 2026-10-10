#nullable enable

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Entities;
using Windows.Storage;

namespace WarThunderChatTranslator.Services
{
    internal interface ITtsProvider
    {
        string Id { get; }
        string DisplayName { get; }
        IReadOnlyList<TtsVoiceInfo> GetVoices();
        Task<StorageFile> SynthesizeAsync(string text, string voiceId, double speakingRate, CancellationToken cancellationToken = default);
    }
}
