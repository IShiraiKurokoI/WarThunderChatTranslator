using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    /// <summary>
    /// Hotkey-driven voice translation pipeline:
    /// WASAPI capture -> local SenseVoice ASR -> GTranslate -> clipboard -> cue.
    /// Recording and model loading are independent so microphone capture can start immediately.
    /// </summary>
    public sealed class QuickTranslationService : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SpeechRecognitionService _speechRecognitionService = new();
        private readonly AudioCaptureService _audioCaptureService = new();
        private readonly object _stateLock = new();

        private QuickTranslationHotkey _activeBinding;
        private TaskCompletionSource<CapturedAudio> _captureCompletion;
        private bool _stopRequested;
        private bool _recordingEndCuePlayed;
        private bool _disposed;

        public QuickTranslationService(SuccessAudioService successAudioService)
        {
            SuccessAudioService = successAudioService ?? throw new ArgumentNullException(nameof(successAudioService));
        }

        public SuccessAudioService SuccessAudioService { get; }
        public SpeechRecognitionServiceState SpeechState => _speechRecognitionService.State;
        public string SpeechStateDetail => _speechRecognitionService.StateDetail;
        public string SpeechLanguageTag => _speechRecognitionService.CurrentLanguageTag;

        public event EventHandler<SpeechRecognitionStateChangedEventArgs> SpeechStateChanged
        {
            add => _speechRecognitionService.StateChanged += value;
            remove => _speechRecognitionService.StateChanged -= value;
        }

        public Task WarmUpSpeechAsync(string ignoredLanguageTag, CancellationToken cancellationToken = default)
            => _speechRecognitionService.WarmUpAsync(cancellationToken);

        public Task ReinitializeSpeechAsync(string ignoredLanguageTag, CancellationToken cancellationToken = default)
            => _speechRecognitionService.ReinitializeAsync(cancellationToken);

        public async Task ReleaseSpeechAsync(CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<CapturedAudio> pendingCapture;
            lock (_stateLock)
            {
                pendingCapture = _captureCompletion;
                _activeBinding = null;
                _captureCompletion = null;
                _stopRequested = false;
                _recordingEndCuePlayed = false;
            }

            pendingCapture?.TrySetCanceled();
            await _audioCaptureService.AbortAsync();
            await _speechRecognitionService.ReleaseAsync(cancellationToken);
        }

        public async Task ExecuteAsync(QuickTranslationHotkey binding, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(binding);

            if (!QuickTranslationConfig.IsEnabled() || !binding.Enabled)
            {
                return;
            }

            bool isStopPress;
            TaskCompletionSource<CapturedAudio> captureCompletion;
            lock (_stateLock)
            {
                if (_activeBinding != null)
                {
                    if (_activeBinding.Id != binding.Id)
                    {
                        _logger.Debug(
                            "Ignored quick translation hotkey {0} because {1} is currently recording.",
                            binding.Shortcut,
                            _activeBinding.Shortcut);
                        return;
                    }

                    if (_stopRequested)
                    {
                        return;
                    }

                    _stopRequested = true;
                    isStopPress = true;
                    captureCompletion = _captureCompletion;
                }
                else
                {
                    _activeBinding = binding.Clone();
                    _stopRequested = false;
                    _recordingEndCuePlayed = false;
                    _captureCompletion = new TaskCompletionSource<CapturedAudio>(TaskCreationOptions.RunContinuationsAsynchronously);
                    captureCompletion = _captureCompletion;
                    isStopPress = false;
                }
            }

            if (isStopPress)
            {
                _logger.Debug("Quick translation recording stop requested by hotkey {0}.", binding.Shortcut);
                await StopCaptureAndPublishAsync(captureCompletion, cancellationToken);
                return;
            }

            try
            {
                // Do not await model loading here. It is normally preloaded at application start,
                // but if not, loading runs concurrently while the user is speaking.
                var modelWarmupTask = _speechRecognitionService.WarmUpAsync(cancellationToken);

                try
                {
                    await StartCaptureAsync(cancellationToken);

                    bool stopAlreadyRequested;
                    lock (_stateLock)
                    {
                        stopAlreadyRequested = _stopRequested;
                    }

                    if (stopAlreadyRequested)
                    {
                        await StopCaptureAndPublishAsync(captureCompletion, cancellationToken);
                    }

                    var audio = await captureCompletion.Task.WaitAsync(cancellationToken);
                    await PlayRecordingEndCueOnceAsync(cancellationToken);

                    await modelWarmupTask;
                    var recognizedText = await _speechRecognitionService.RecognizeAsync(audio, cancellationToken);
                    _logger.Info("Local quick speech recognition succeeded. Characters={0}", recognizedText.Length);

                    await TranslateAndCopyAsync(recognizedText, binding.TargetLanguage, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Quick speech capture/recognition failed.");
                    SuccessAudioService.PlayRecognitionFailure();
                }
            }
            finally
            {
                lock (_stateLock)
                {
                    if (_activeBinding?.Id == binding.Id)
                    {
                        _activeBinding = null;
                        _captureCompletion = null;
                        _stopRequested = false;
                        _recordingEndCuePlayed = false;
                    }
                }

                if (!_disposed)
                {
                    _speechRecognitionService.MarkReady();
                }
            }
        }

        private async Task StartCaptureAsync(CancellationToken cancellationToken)
        {
            var timing = QuickTranslationConfig.GetRecordingStartTiming();
            var deviceId = ApplicationConfig.GetSettings(QuickTranslationConfig.MicrophoneDeviceIdKey) ?? string.Empty;

            if (string.Equals(timing, QuickTranslationConfig.RecordingStartTimingAfterPlayback, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    _logger.Debug("Playing recording-start cue before local microphone capture.");
                    await SuccessAudioService.PlayRecordingStartAndWaitAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Warn(ex, "Recording-start audio failed; microphone capture will still start.");
                }

                await _audioCaptureService.StartAsync(deviceId, cancellationToken);
            }
            else
            {
                // Default game-friendly mode. Start the cue, then open capture immediately;
                // PlayRecordingStartAsync returns once playback has been started, not when it ends.
                try
                {
                    _logger.Debug("Starting recording-start cue and local microphone capture immediately.");
                    await SuccessAudioService.PlayRecordingStartAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.Warn(ex, "Recording-start audio failed; microphone capture will still start.");
                }

                await _audioCaptureService.StartAsync(deviceId, cancellationToken);
            }

            _speechRecognitionService.MarkRecording();
            _logger.Info("Quick translation microphone capture started. Device={0}",
                string.IsNullOrWhiteSpace(deviceId) ? "default" : deviceId);
        }

        private async Task StopCaptureAndPublishAsync(
            TaskCompletionSource<CapturedAudio> completion,
            CancellationToken cancellationToken)
        {
            if (completion == null || completion.Task.IsCompleted)
            {
                return;
            }

            // A very fast second press can arrive while the first invocation is still starting capture.
            if (!_audioCaptureService.IsRecording)
            {
                return;
            }

            try
            {
                var audio = await _audioCaptureService.StopAsync(cancellationToken);
                completion.TrySetResult(audio);
                _logger.Info("Quick translation microphone capture stopped. DurationMs={0:0}", audio.Duration.TotalMilliseconds);
                await PlayRecordingEndCueOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                _logger.Warn(ex, "Failed to stop quick translation microphone capture.");
            }
        }

        private async Task TranslateAndCopyAsync(string recognizedText, string targetLanguage, CancellationToken cancellationToken)
        {
            try
            {
                _logger.Debug(
                    "Starting quick translation. Characters={0}, Target={1}, Text={2}",
                    recognizedText.Length,
                    targetLanguage,
                    recognizedText);

                var result = await TranslationHelper.TranslateAsync(recognizedText, targetLanguage);
                if (string.IsNullOrWhiteSpace(result?.Translation))
                {
                    throw new InvalidOperationException(Localization.GetString("QuickTranslationEmptyTranslationResult"));
                }

                await ClipboardService.SetTextAsync(result.Translation, cancellationToken);
                _logger.Info(
                    "Quick translation copied to clipboard. Target={0}, Translator={1}",
                    targetLanguage,
                    result.Service);

                try
                {
                    await SuccessAudioService.PlaySuccessAsync(cancellationToken);
                }
                catch (Exception audioEx) when (audioEx is not OperationCanceledException)
                {
                    _logger.Warn(audioEx, "Configured success audio failed. Falling back to system sound.");
                    SuccessAudioService.PlaySystemSuccess();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Quick translation or clipboard operation failed. Target={0}", targetLanguage);
                try
                {
                    await SuccessAudioService.PlayTranslationFailureAsync(cancellationToken);
                }
                catch (Exception audioEx) when (audioEx is not OperationCanceledException)
                {
                    _logger.Warn(audioEx, "Configured translation-failure audio failed. Falling back to system sound.");
                    SuccessAudioService.PlayTranslationFailure();
                }
            }
        }

        private async Task PlayRecordingEndCueOnceAsync(CancellationToken cancellationToken)
        {
            lock (_stateLock)
            {
                if (_recordingEndCuePlayed)
                {
                    return;
                }

                _recordingEndCuePlayed = true;
            }

            try
            {
                await SuccessAudioService.PlayRecordingEndAsync(cancellationToken);
            }
            catch (Exception audioEx) when (audioEx is not OperationCanceledException)
            {
                _logger.Warn(audioEx, "Recording-end audio failed.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try { _audioCaptureService.Dispose(); } catch { }
            try { _speechRecognitionService.Dispose(); } catch { }
        }
    }
}
