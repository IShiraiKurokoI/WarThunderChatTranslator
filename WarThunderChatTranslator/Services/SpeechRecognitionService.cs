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
    /// Local, non-streaming Paraformer ASR. The model is loaded once and kept alive;
    /// microphone capture is handled separately and inference only runs after recording stops.
    /// </summary>
    public sealed class SpeechRecognitionService : IDisposable
    {
        public const string ModelFolderName = "sherpa-onnx-paraformer-zh-small-2024-03-09";
        public const string ModelFileName = "model.int8.onnx";
        public const string TokensFileName = "tokens.txt";
        public const string ModelDisplayName = "Paraformer zh-en small INT8";
        public const string ModelSha256 = "3ef6c19369b912f7caf3cef8e545c5ccd1a33d9d7ec792a46668dc41c4b229ec";

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
                    // Reject placeholders/partial downloads without hashing 81.8 MB on every UI check.
                    return File.Exists(ModelPath)
                        && File.Exists(TokensPath)
                        && new FileInfo(ModelPath).Length > 70L * 1024 * 1024
                        && new FileInfo(TokensPath).Length > 50L * 1024;
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
        public string CurrentLanguageTag => "zh-en-local";
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

            await _lifecycleLock.WaitAsync(cancellationToken);
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
                    config.ModelConfig.Paraformer.Model = ModelPath;
                    config.ModelConfig.NumThreads = 1;
                    config.ModelConfig.Provider = "cpu";
                    config.ModelConfig.Debug = 0;
                    config.ModelConfig.ModelType = "paraformer";
                    config.DecodingMethod = "greedy_search";
                    return new OfflineRecognizer(config);
                }, cancellationToken);

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
            await ReleaseAsync(cancellationToken);
            await WarmUpAsync(cancellationToken);
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

            await WarmUpAsync(cancellationToken);
            await _decodeLock.WaitAsync(cancellationToken);
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
                }, cancellationToken);
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

            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                var recognizer = _recognizer;
                _recognizer = null;
                if (recognizer != null)
                {
                    await Task.Run(recognizer.Dispose, cancellationToken);
                }
                SetState(SpeechRecognitionServiceState.NotInitialized, null);
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try { ReleaseAsync().GetAwaiter().GetResult(); } catch { }
            _disposed = true;
            _lifecycleLock.Dispose();
            _decodeLock.Dispose();
        }
    }
}
