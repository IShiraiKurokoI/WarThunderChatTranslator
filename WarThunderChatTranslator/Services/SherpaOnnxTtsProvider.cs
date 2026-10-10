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
        private readonly SherpaTtsModelManager _modelManager;
        private readonly SemaphoreSlim _engineLock = new(1, 1);
        // Native OfflineTts is not disposed/reloaded while a synthesis call is using it.
        private readonly SemaphoreSlim _synthesisLock = new(1, 1);
        private readonly SemaphoreSlim _cacheFinalizeLock = new(1, 1);
        private OfflineTts? _engine;
        private string _engineSignature = string.Empty;
        private bool _disposed;

        public SherpaOnnxTtsProvider(SherpaTtsModelManager? modelManager = null)
        {
            _modelManager = modelManager ?? SherpaTtsModelManager.Shared;
        }

        public string Id => ChatTtsConfig.ProviderSherpaOnnx;
        public string DisplayName => "Sherpa-ONNX";

        public IReadOnlyList<TtsVoiceInfo> GetVoices()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var model = ResolveSelectedModel()
                ?? throw new InvalidOperationException("No Sherpa-ONNX TTS model is installed.");

            if (model.SpeakerNames.Count > 0)
            {
                return model.SpeakerNames
                    .Select((name, sid) => new TtsVoiceInfo(
                        BuildVoiceId(model.Id, sid),
                        FormatSpeakerName(name, sid),
                        GuessSpeakerLanguage(name)))
                    .ToArray();
            }

            _synthesisLock.Wait();
            try
            {
                var engine = GetOrCreateEngineAsync(model, CancellationToken.None).GetAwaiter().GetResult();
                var count = Math.Max(1, engine.NumSpeakers);
                return Enumerable.Range(0, count)
                    .Select(sid => new TtsVoiceInfo(
                        BuildVoiceId(model.Id, sid),
                        $"Speaker {sid}",
                        "Local"))
                    .ToArray();
            }
            finally
            {
                _synthesisLock.Release();
            }
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

            ParseVoiceId(voiceId, out var requestedModelId, out var sid);
            var model = !string.IsNullOrWhiteSpace(requestedModelId)
                ? _modelManager.GetModel(requestedModelId)
                : ResolveSelectedModel();
            model ??= ResolveSelectedModel();
            if (model is null)
            {
                throw new InvalidOperationException("No Sherpa-ONNX TTS model is installed. Download or import a model first.");
            }

            var normalizedRate = Math.Clamp(speakingRate, 0.5, 2.0);
            var effectiveVoiceId = BuildVoiceId(model.Id, Math.Max(0, sid));
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
                    var engine = await GetOrCreateEngineAsync(model, cancellationToken).ConfigureAwait(false);
                    var speakerCount = engine.NumSpeakers;
                    if (speakerCount > 0)
                    {
                        sid = Math.Clamp(sid, 0, speakerCount - 1);
                    }
                    else
                    {
                        sid = 0;
                    }

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
            // Do not block the UI while native synthesis is running. The next synthesis/
            // voice inspection will observe the empty signature and reload the engine
            // after the current native call has left _synthesisLock.
            _engineSignature = string.Empty;
        }

        public async Task DeleteModelAsync(string modelId, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return;
            }

            // Serialize deletion with synthesis so the native engine cannot keep the model
            // files open while they are removed. A queued synthesis will resolve the next
            // available model after this method releases the lock.
            await _synthesisLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    _engine?.Dispose();
                    _engine = null;
                    _engineSignature = string.Empty;
                    _modelManager.DeleteModel(modelId);
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
        }

        private SherpaTtsModelInfo? ResolveSelectedModel()
        {
            var configured = _modelManager.GetModel(ChatTtsConfig.GetSherpaModelId());
            if (configured is not null)
            {
                return configured;
            }

            var models = _modelManager.GetInstalledModels();
            return models.FirstOrDefault(model => model.IsRecommended) ?? models.FirstOrDefault();
        }

        private async Task<OfflineTts> GetOrCreateEngineAsync(SherpaTtsModelInfo model, CancellationToken cancellationToken)
        {
            var threads = ChatTtsConfig.GetSherpaNumThreads();
            var signature = $"{model.Id}|{model.ModelFile}|{File.GetLastWriteTimeUtc(model.ModelFile).Ticks}|{threads}";
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
                config.Model.Kokoro.Model = model.ModelFile;
                config.Model.Kokoro.Voices = model.VoicesFile;
                config.Model.Kokoro.Tokens = model.TokensFile;
                config.Model.Kokoro.DataDir = model.DataDirectory;
                config.Model.Kokoro.Lexicon = model.Lexicon;
                config.Model.NumThreads = threads;
                config.Model.Debug = 0;
                config.Model.Provider = "cpu";
                config.RuleFsts = model.RuleFsts;
                config.MaxNumSentences = 1;

                _logger.Info("Loading Sherpa-ONNX TTS model {0} with {1} thread(s).", model.Id, threads);
                _engine = await Task.Run(() => new OfflineTts(config), cancellationToken).ConfigureAwait(false);
                _engineSignature = signature;
                _logger.Info("Sherpa-ONNX TTS model {0} loaded. SampleRate={1}, Speakers={2}.", model.Id, _engine.SampleRate, _engine.NumSpeakers);
                return _engine;
            }
            finally
            {
                _engineLock.Release();
            }
        }

        private static string BuildVoiceId(string modelId, int sid) => $"{modelId}|{sid.ToString(CultureInfo.InvariantCulture)}";

        private static void ParseVoiceId(string? voiceId, out string modelId, out int sid)
        {
            modelId = string.Empty;
            sid = 0;
            if (string.IsNullOrWhiteSpace(voiceId))
            {
                return;
            }

            var separator = voiceId.LastIndexOf('|');
            if (separator <= 0)
            {
                int.TryParse(voiceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out sid);
                return;
            }

            modelId = voiceId[..separator];
            int.TryParse(voiceId[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out sid);
        }

        private static string FormatSpeakerName(string rawName, int sid)
        {
            var category = rawName.StartsWith("zf_", StringComparison.OrdinalIgnoreCase)
                ? "中文女声"
                : rawName.StartsWith("zm_", StringComparison.OrdinalIgnoreCase)
                    ? "中文男声"
                    : rawName.StartsWith("af_", StringComparison.OrdinalIgnoreCase)
                        ? "American Female"
                        : rawName.StartsWith("am_", StringComparison.OrdinalIgnoreCase)
                            ? "American Male"
                            : rawName.StartsWith("bf_", StringComparison.OrdinalIgnoreCase)
                                ? "British Female"
                                : rawName.StartsWith("bm_", StringComparison.OrdinalIgnoreCase)
                                    ? "British Male"
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
                || rawName.StartsWith("am_", StringComparison.OrdinalIgnoreCase)
                || rawName.StartsWith("bf_", StringComparison.OrdinalIgnoreCase)
                || rawName.StartsWith("bm_", StringComparison.OrdinalIgnoreCase))
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
