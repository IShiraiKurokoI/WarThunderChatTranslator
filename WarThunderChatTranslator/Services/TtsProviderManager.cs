#nullable enable

using System;
using WarThunderChatTranslator.Configurations;

namespace WarThunderChatTranslator.Services
{
    internal sealed class TtsProviderManager : IDisposable
    {
        private readonly WindowsTtsProvider _windows = new();
        private readonly SherpaOnnxTtsProvider _sherpa = new();
        private bool _disposed;

        public ITtsProvider GetProvider(string? providerId = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            providerId ??= ChatTtsConfig.GetProvider();
            return string.Equals(providerId, ChatTtsConfig.ProviderSherpaOnnx, StringComparison.Ordinal)
                ? _sherpa
                : _windows;
        }

        public SherpaOnnxTtsProvider Sherpa => _sherpa;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _windows.Dispose();
            _sherpa.Dispose();
        }
    }
}
