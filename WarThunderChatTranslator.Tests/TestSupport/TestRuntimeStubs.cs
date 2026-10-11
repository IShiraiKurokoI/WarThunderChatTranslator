#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using Windows.Storage;

// Production logging and Windows speech engines are deliberately not loaded in the headless test host.
namespace NLog
{
    public static class LogManager { public static Logger GetCurrentClassLogger() => new(); }
    public sealed class Logger
    {
        public void Warn(Exception exception, string message, params object[] args) { }
        public void Debug(string message, params object[] args) { }
        public void Debug(Exception exception, string message, params object[] args) { }
    }
}

namespace WarThunderChatTranslator.Services
{
    internal sealed class GameChatPollingService
    {
        public event Action<ChatMessage>? MessageProcessed;
        public void Emit(ChatMessage message) => MessageProcessed?.Invoke(message);
    }

    internal sealed class WindowsTtsProvider : ITtsProvider, IDisposable
    {
        public string Id => ChatTtsConfig.ProviderWindows;
        public string DisplayName => "Windows TTS";
        public IReadOnlyList<TtsVoiceInfo> GetVoices() => [new("win", "Windows Test Voice", "en")];
        public Task<StorageFile> SynthesizeAsync(string text, string voiceId, double speakingRate, CancellationToken cancellationToken = default)
            => Task.FromResult(new StorageFile("windows-mock.wav"));
        public void Dispose() { }
    }

    internal sealed class SherpaOnnxTtsProvider : ITtsProvider, IDisposable
    {
        public string Id => ChatTtsConfig.ProviderSherpaOnnx;
        public string DisplayName => "Sherpa ONNX";
        public IReadOnlyList<TtsVoiceInfo> GetVoices() => [new("sherpa", "Sherpa Test Voice", "en")];
        public Task<StorageFile> SynthesizeAsync(string text, string voiceId, double speakingRate, CancellationToken cancellationToken = default)
            => Task.FromResult(new StorageFile("sherpa-mock.wav"));
        public void Dispose() { }
    }
}
