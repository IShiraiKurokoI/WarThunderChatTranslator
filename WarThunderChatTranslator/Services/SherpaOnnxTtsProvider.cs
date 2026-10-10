#nullable enable

using NLog;
using SherpaOnnx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using Windows.Storage;

namespace WarThunderChatTranslator.Services
{
    internal sealed class SherpaOnnxTtsProvider : ITtsProvider, IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _engineLock = new(1, 1);
        private readonly SemaphoreSlim _synthesisLock = new(1, 1);
        private readonly SemaphoreSlim _cacheFinalizeLock = new(1, 1);
        private OfflineTts? _engine;
        private string _engineSignature = string.Empty;
        private bool _disposed;

        public string Id => ChatTtsConfig.ProviderSherpaOnnx;
        public string DisplayName => "Sherpa-ONNX";

        public IReadOnlyList<TtsVoiceInfo> GetVoices()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureModelAvailable();

            return BundledKokoroTtsModel.SpeakerNames
                .Select((name, sid) => new TtsVoiceInfo(
                    BuildVoiceId(sid),
                    FormatSpeakerName(name, sid),
                    GuessSpeakerLanguage(name)))
                .ToArray();
        }

        public async Task<StorageFile> SynthesizeAsync(
            string text,
            string voiceId,
            double speakingRate,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("TTS text cannot be empty.", nameof(text));
            }

            EnsureModelAvailable();
            var sid = ParseVoiceId(voiceId);
            var normalizedRate = Math.Clamp(speakingRate, 0.5, 2.0);
            var effectiveVoiceId = BuildVoiceId(Math.Max(0, sid));
            var cacheKey = TtsCacheKey.Compute(text.Trim(), Id, effectiveVoiceId, normalizedRate);
            var fileName = cacheKey + ".wav";
            var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ChatTts", CreationCollisionOption.OpenIfExists);
            var cacheFolder = await root.CreateFolderAsync("TtsCache", CreationCollisionOption.OpenIfExists);
            if (await cacheFolder.TryGetItemAsync(fileName) is StorageFile cachedFile)
            {
                return cachedFile;
            }

            var tempFile = await cacheFolder.CreateFileAsync(
                $"{cacheKey}.{Guid.NewGuid():N}.tmp.wav",
                CreationCollisionOption.ReplaceExisting);

            try
            {
                await _synthesisLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var engine = await GetOrCreateEngineAsync(cancellationToken).ConfigureAwait(false);
                    var speakerCount = engine.NumSpeakers;
                    sid = speakerCount > 0 ? Math.Clamp(sid, 0, speakerCount - 1) : 0;

                    await Task.Run(() =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var generationConfig = new OfflineTtsGenerationConfig
                        {
                            Sid = sid,
                            Speed = (float)normalizedRate,
                            SilenceScale = 0.2f
                        };

                        var callback = new OfflineTtsCallbackProgressWithArg((_, _, _, _) =>
                            cancellationToken.IsCancellationRequested ? 0 : 1);
                        var audio = engine.GenerateWithConfig(text.Trim(), generationConfig, callback);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (audio is null || !audio.SaveToWaveFile(tempFile.Path))
                            {
                                throw new InvalidOperationException("Sherpa-ONNX did not produce a valid WAV file.");
                            }
                        }
                        finally
                        {
                            audio?.Dispose();
                        }
                    }, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _synthesisLock.Release();
                }

                await _cacheFinalizeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (await cacheFolder.TryGetItemAsync(fileName) is StorageFile raceWinner)
                    {
                        await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        return raceWinner;
                    }

                    await tempFile.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
                    return await cacheFolder.GetFileAsync(fileName);
                }
                finally
                {
                    _cacheFinalizeLock.Release();
                }
            }
            catch
            {
                try { await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                throw;
            }
        }

        public void InvalidateModel()
        {
            _engineSignature = string.Empty;
        }

        private static void EnsureModelAvailable()
        {
            if (BundledKokoroTtsModel.IsAvailable)
            {
                return;
            }

            var missing = string.Join(", ", BundledKokoroTtsModel.GetMissingComponents());
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(missing)
                    ? "The bundled Kokoro TTS model is unavailable."
                    : $"The bundled Kokoro TTS model is incomplete. Missing: {missing}.");
        }

        private async Task<OfflineTts> GetOrCreateEngineAsync(CancellationToken cancellationToken)
        {
            EnsureModelAvailable();
            var threads = ChatTtsConfig.GetSherpaNumThreads();
            var signature = $"{BundledKokoroTtsModel.ModelFile}|{File.GetLastWriteTimeUtc(BundledKokoroTtsModel.ModelFile).Ticks}|{threads}";
            if (_engine is not null && string.Equals(_engineSignature, signature, StringComparison.Ordinal))
            {
                return _engine;
            }

            await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_engine is not null && string.Equals(_engineSignature, signature, StringComparison.Ordinal))
                {
                    return _engine;
                }

                _engine?.Dispose();
                _engine = null;
                _engineSignature = string.Empty;

                var config = new OfflineTtsConfig();
                config.Model.Kokoro.Model = BundledKokoroTtsModel.ModelFile;
                config.Model.Kokoro.Voices = BundledKokoroTtsModel.VoicesFile;
                config.Model.Kokoro.Tokens = BundledKokoroTtsModel.TokensFile;
                config.Model.Kokoro.DataDir = BundledKokoroTtsModel.DataDirectory;
                config.Model.Kokoro.Lexicon = BundledKokoroTtsModel.Lexicon;
                config.Model.NumThreads = threads;
                config.Model.Debug = 0;
                config.Model.Provider = "cpu";
                config.RuleFsts = BundledKokoroTtsModel.RuleFsts;
                config.MaxNumSentences = 1;

                _logger.Info("Loading bundled Kokoro TTS model with {0} thread(s).", threads);
                _engine = await Task.Run(() => new OfflineTts(config), cancellationToken).ConfigureAwait(false);
                _engineSignature = signature;
                _logger.Info("Bundled Kokoro TTS model loaded. SampleRate={0}, Speakers={1}.", _engine.SampleRate, _engine.NumSpeakers);
                return _engine;
            }
            finally
            {
                _engineLock.Release();
            }
        }

        private static string BuildVoiceId(int sid) =>
            $"{BundledKokoroTtsModel.Id}|{sid.ToString(CultureInfo.InvariantCulture)}";

        private static int ParseVoiceId(string? voiceId)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
            {
                return 0;
            }

            var separator = voiceId.LastIndexOf('|');
            var sidText = separator >= 0 && separator < voiceId.Length - 1
                ? voiceId[(separator + 1)..]
                : voiceId;
            return int.TryParse(sidText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sid)
                ? Math.Max(0, sid)
                : 0;
        }

        private static string FormatSpeakerName(string rawName, int sid)
        {
            var category = rawName.StartsWith("zf_", StringComparison.OrdinalIgnoreCase)
                ? "中文女声"
                : rawName.StartsWith("zm_", StringComparison.OrdinalIgnoreCase)
                    ? "中文男声"
                    : rawName.StartsWith("af_", StringComparison.OrdinalIgnoreCase)
                        ? "American Female"
                        : rawName.StartsWith("bf_", StringComparison.OrdinalIgnoreCase)
                            ? "British Female"
                            : "Voice";
            return $"{rawName} · {category} · SID {sid}";
        }

        private static string GuessSpeakerLanguage(string rawName)
        {
            if (rawName.StartsWith("zf_", StringComparison.OrdinalIgnoreCase)
                || rawName.StartsWith("zm_", StringComparison.OrdinalIgnoreCase))
            {
                return "zh-CN";
            }

            if (rawName.StartsWith("af_", StringComparison.OrdinalIgnoreCase)
                || rawName.StartsWith("bf_", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }

            return "Local";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _synthesisLock.Wait();
            try
            {
                _engineLock.Wait();
                try
                {
                    _engine?.Dispose();
                    _engine = null;
                    _engineSignature = string.Empty;
                }
                finally
                {
                    _engineLock.Release();
                }
            }
            finally
            {
                _synthesisLock.Release();
            }

            _engineLock.Dispose();
            _synthesisLock.Dispose();
            _cacheFinalizeLock.Dispose();
        }
    }
}
