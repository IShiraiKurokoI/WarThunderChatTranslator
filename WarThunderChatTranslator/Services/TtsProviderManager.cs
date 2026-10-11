#nullable enable

using System;
using WarThunderChatTranslator.Configurations;

namespace WarThunderChatTranslator.Services
{
    internal sealed class TtsProviderManager : IDisposable
    {
        private readonly WindowsTtsProvider _windows = new();
        private readonly SherpaOnnxTtsProvider _sherpa = new();
        private readonly GptSoVitsTtsProvider _gptSoVits = new();
        private bool _disposed;

        public ITtsProvider GetProvider(string? providerId = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            providerId ??= ChatTtsConfig.GetProvider();
            if (string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal))
            {
                return _sherpa;
            }
            if (string.Equals(providerId, ChatTtsConfig.ProviderGptSoVits, StringComparison.Ordinal))
            {
                return _gptSoVits;
            }
            return _windows;
        }

        public SherpaOnnxTtsProvider Sherpa => _sherpa;
        public GptSoVitsTtsProvider GptSoVits => _gptSoVits;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _windows.Dispose();
            _sherpa.Dispose();
            _gptSoVits.Dispose();
        }
    }
}
