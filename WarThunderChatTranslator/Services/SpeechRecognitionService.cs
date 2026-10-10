using NLog;
using SherpaOnnx;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    public enum SpeechRecognitionServiceState
    {
        NotInitialized,
        WarmingUp,
        Ready,
        Recording,
        Recognizing,
        Unavailable
    }

    public sealed class SpeechRecognitionStateChangedEventArgs : EventArgs
    {
        public SpeechRecognitionStateChangedEventArgs(SpeechRecognitionServiceState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        public SpeechRecognitionServiceState State { get; }
        public string Detail { get; }
    }

    /// <summary>
    /// Local, non-streaming SenseVoice ASR. The model is loaded once and kept alive;
    /// microphone capture is handled separately and inference only runs after recording stops.
    /// </summary>
    public sealed class SpeechRecognitionService : IDisposable, IAsyncDisposable
    {
        public const string ModelFolderName = "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17";
        public const string ModelFileName = "model.int8.onnx";
        public const string TokensFileName = "tokens.txt";
        public const string ModelDisplayName = "SenseVoice zh-en-ja-ko-yue INT8";
        public const string ModelUpstreamName = "SenseVoiceSmall";
        public const string ModelAuthor = "FunAudioLLM (Alibaba Group / FunASR)";
        public const string ModelArtifactMaintainer = "Fangjun Kuang (csukuangfj) / k2-fsa sherpa-onnx";
        public const string ModelArtifactDate = "2024-07-17";
        public const string ModelLicenseName = "FunASR Model Open Source License Agreement v1.1";
        public const string RuntimeDisplayName = "sherpa-onnx";
        public const string RuntimeVersion = "1.13.8";
        public const string RuntimeLicenseName = "Apache-2.0";
        public const string ModelSha256 = "c71f0ce00bec95b07744e116345e33d8cbbe08cef896382cf907bf4b51a2cd51";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private readonly SemaphoreSlim _decodeLock = new(1, 1);
        private OfflineRecognizer _recognizer;
        private bool _disposed;
        private SpeechRecognitionServiceState _state = SpeechRecognitionServiceState.NotInitialized;
        private string _stateDetail;

        public event EventHandler<SpeechRecognitionStateChangedEventArgs> StateChanged;

        public static string ModelDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "SpeechModels", ModelFolderName);
        public static string ModelPath => Path.Combine(ModelDirectory, ModelFileName);
        public static string TokensPath => Path.Combine(ModelDirectory, TokensFileName);
        public static bool ModelFilesAvailable
        {
            get
            {
                try
                {
                    // Reject placeholders/partial downloads without hashing the 228 MiB model on every UI check.
                    return File.Exists(ModelPath)
                        && File.Exists(TokensPath)
                        && new FileInfo(ModelPath).Length > 220L * 1024 * 1024
                        && new FileInfo(TokensPath).Length > 250L * 1024;
                }
                catch
                {
                    return false;
                }
            }
        }

        public SpeechRecognitionServiceState State
        {
            get { lock (_stateLock) return _state; }
        }

        public string StateDetail
        {
            get { lock (_stateLock) return _stateDetail; }
        }

        // Kept for compatibility with the existing status/logging surface.
        public string CurrentLanguageTag => "auto-local";
        public bool IsRecognitionActive => State is SpeechRecognitionServiceState.Recording or SpeechRecognitionServiceState.Recognizing;

        public void PrepareForRecognition()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public Task WarmUpAsync(string ignoredLanguageTag, CancellationToken cancellationToken = default)
            => WarmUpAsync(cancellationToken);

        public async Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (_recognizer != null)
            {
                if (State is not SpeechRecognitionServiceState.Recording and not SpeechRecognitionServiceState.Recognizing)
                {
                    SetState(SpeechRecognitionServiceState.Ready, ModelDisplayName);
                }
                return;
            }

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_recognizer != null)
                {
                    SetState(SpeechRecognitionServiceState.Ready, ModelDisplayName);
                    return;
                }

                if (!ModelFilesAvailable)
                {
                    var detail = string.Format(Localization.GetString("QuickTranslationLocalModelMissingFormat"), ModelDirectory);
                    SetState(SpeechRecognitionServiceState.Unavailable, detail);
                    throw new FileNotFoundException(detail, !File.Exists(ModelPath) ? ModelPath : TokensPath);
                }

                SetState(SpeechRecognitionServiceState.WarmingUp, ModelDisplayName);
                _logger.Info("Loading local quick-translation ASR model. Model={0}", ModelPath);

                var recognizer = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var config = new OfflineRecognizerConfig();
                    config.FeatConfig.SampleRate = 16000;
                    config.FeatConfig.FeatureDim = 80;
                    config.ModelConfig.Tokens = TokensPath;
                    config.ModelConfig.SenseVoice.Model = ModelPath;
                    config.ModelConfig.SenseVoice.Language = "auto";
                    config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
                    config.ModelConfig.NumThreads = 1;
                    config.ModelConfig.Provider = "cpu";
                    config.ModelConfig.Debug = 0;
                    config.DecodingMethod = "greedy_search";
                    return new OfflineRecognizer(config);
                }, cancellationToken).ConfigureAwait(false);

                _recognizer = recognizer;
                if (State != SpeechRecognitionServiceState.Recording)
                {
                    SetState(SpeechRecognitionServiceState.Ready, ModelDisplayName);
                }
                _logger.Info("Local quick-translation ASR model loaded. Model={0}, Threads=1", ModelDisplayName);
            }
            catch (OperationCanceledException)
            {
                if (_recognizer == null)
                {
                    SetState(SpeechRecognitionServiceState.NotInitialized, null);
                }
                throw;
            }
            catch (Exception ex)
            {
                if (_recognizer == null)
                {
                    SetState(SpeechRecognitionServiceState.Unavailable,
                        string.Format(Localization.GetString("QuickTranslationLocalModelLoadFailedFormat"), ex.Message));
                }
                throw;
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public Task ReinitializeAsync(string ignoredLanguageTag, CancellationToken cancellationToken = default)
            => ReinitializeAsync(cancellationToken);

        public async Task ReinitializeAsync(CancellationToken cancellationToken = default)
        {
            await ReleaseAsync(cancellationToken).ConfigureAwait(false);
            await WarmUpAsync(cancellationToken).ConfigureAwait(false);
        }

        public void MarkRecording()
            => SetState(SpeechRecognitionServiceState.Recording, ModelDisplayName);

        public void MarkReady()
        {
            if (_recognizer != null)
            {
                SetState(SpeechRecognitionServiceState.Ready, ModelDisplayName);
            }
            else if (State != SpeechRecognitionServiceState.Unavailable)
            {
                SetState(SpeechRecognitionServiceState.NotInitialized, null);
            }
        }

        public async Task<string> RecognizeAsync(CapturedAudio audio, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (audio == null || audio.Pcm16.Length < 320)
            {
                throw new InvalidOperationException(Localization.GetString("QuickTranslationNoAudioCaptured"));
            }

            await WarmUpAsync(cancellationToken).ConfigureAwait(false);
            await _decodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var recognizer = _recognizer ?? throw new InvalidOperationException(Localization.GetString("QuickTranslationSpeechUnavailable"));
                SetState(SpeechRecognitionServiceState.Recognizing,
                    string.Format(Localization.GetString("QuickTranslationLocalRecognizingDetailFormat"), audio.Duration.TotalSeconds));

                var samples = audio.ToFloatSamples();
                var stopwatch = Stopwatch.StartNew();
                var text = await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = recognizer.CreateStream();
                    stream.AcceptWaveform(audio.SampleRate, samples);
                    recognizer.Decode(stream);
                    return stream.Result.Text?.Trim() ?? string.Empty;
                }, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                _logger.Info(
                    "Local speech recognition completed. AudioMs={0:0}, DecodeMs={1:0}, Characters={2}",
                    audio.Duration.TotalMilliseconds,
                    stopwatch.Elapsed.TotalMilliseconds,
                    text.Length);

                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new InvalidOperationException(Localization.GetString("QuickTranslationSpeechNoText"));
                }

                return text;
            }
            finally
            {
                _decodeLock.Release();
                MarkReady();
            }
        }

        // Legacy method name retained so older call sites fail gracefully if any remain.
        public Task<string> RecognizeOnceAsync(
            string languageTag,
            CancellationToken cancellationToken = default,
            Func<CancellationToken, Task> beforeRecognitionStartAsync = null,
            Func<CancellationToken, Task> afterRecognitionStartedAsync = null)
            => throw new NotSupportedException("Local ASR requires captured PCM. Use RecognizeAsync(CapturedAudio, ...).");

        public Task<bool> StopActiveRecognitionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public async Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                return;
            }

            await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Never dispose the native recognizer while a decode is still using it.
                await _decodeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var recognizer = _recognizer;
                    _recognizer = null;
                    if (recognizer != null)
                    {
                        // Do not pass the shutdown token to native disposal once we own both locks.
                        // Cancellation here could leave the native recognizer alive during process exit.
                        await Task.Run(recognizer.Dispose, CancellationToken.None).ConfigureAwait(false);
                    }

                    SetState(SpeechRecognitionServiceState.NotInitialized, null);
                }
                finally
                {
                    _decodeLock.Release();
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        private void SetState(SpeechRecognitionServiceState state, string detail)
        {
            EventHandler<SpeechRecognitionStateChangedEventArgs> handler;
            lock (_stateLock)
            {
                _state = state;
                _stateDetail = detail;
                handler = StateChanged;
            }

            handler?.Invoke(this, new SpeechRecognitionStateChangedEventArgs(state, detail));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                await ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _disposed = true;
                _lifecycleLock.Dispose();
                _decodeLock.Dispose();
            }
        }

        public void Dispose()
        {
            // Kept for non-async callers. All internal awaits use ConfigureAwait(false),
            // so this wrapper does not require the WinUI dispatcher to make progress.
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
