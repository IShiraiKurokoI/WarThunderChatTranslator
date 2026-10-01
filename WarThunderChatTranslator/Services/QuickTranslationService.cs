using NLog;
using System;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    public sealed class QuickTranslationService : IDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SpeechRecognitionService _speechRecognitionService = new();
        private readonly object _stateLock = new();

        private QuickTranslationHotkey _activeBinding;
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

        public event EventHandler<SpeechRecognitionStateChangedEventArgs> SpeechStateChanged
        {
            add => _speechRecognitionService.StateChanged += value;
            remove => _speechRecognitionService.StateChanged -= value;
        }

        public Task WarmUpSpeechAsync(string languageTag, CancellationToken cancellationToken = default)
            => _speechRecognitionService.WarmUpAsync(languageTag, cancellationToken);

        public Task ReinitializeSpeechAsync(string languageTag, CancellationToken cancellationToken = default)
            => _speechRecognitionService.ReinitializeAsync(languageTag, cancellationToken);

        public Task ReleaseSpeechAsync(CancellationToken cancellationToken = default)
            => _speechRecognitionService.ReleaseAsync(cancellationToken);

        public async Task ExecuteAsync(QuickTranslationHotkey binding, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(binding);

            if (!QuickTranslationConfig.IsEnabled() || !binding.Enabled)
            {
                return;
            }

            bool shouldRequestStop = false;
            lock (_stateLock)
            {
                if (_activeBinding != null)
                {
                    if (_activeBinding.Id == binding.Id)
                    {
                        if (!_stopRequested)
                        {
                            _stopRequested = true;
                            shouldRequestStop = true;
                        }
                    }
                    else
                    {
                        _logger.Debug(
                            "Ignored quick translation hotkey {0} because {1} is currently recording.",
                            binding.Shortcut,
                            _activeBinding.Shortcut);
                    }
                }
                else
                {
                    _activeBinding = binding.Clone();
                    _stopRequested = false;
                    _recordingEndCuePlayed = false;
                }
            }

            if (shouldRequestStop)
            {
                _logger.Debug("Quick translation stop requested by hotkey {0}.", binding.Shortcut);

                // Audible stop feedback should be immediate even though Windows may need a short
                // moment to flush the last pending recognition result.
                _ = PlayRecordingEndCueOnceAsync(cancellationToken);
                _ = StopRecognitionSafeAsync(cancellationToken);
                return;
            }

            lock (_stateLock)
            {
                if (_activeBinding == null || _activeBinding.Id != binding.Id || _stopRequested)
                {
                    return;
                }
            }

            try
            {
                string recognizedText;
                var recognitionLanguage = ApplicationConfig.GetSettings(QuickTranslationConfig.RecognitionLanguageKey);

                try
                {
                    _speechRecognitionService.PrepareForRecognition();
                    _logger.Debug(
                        "Starting quick speech recognition with prewarmed recognizer. Shortcut={0}, Language={1}, State={2}",
                        binding.Shortcut,
                        string.IsNullOrWhiteSpace(recognitionLanguage) ? "system" : recognitionLanguage,
                        _speechRecognitionService.State);

                    recognizedText = await _speechRecognitionService.RecognizeOnceAsync(
                        recognitionLanguage,
                        cancellationToken,
                        PlayRecordingStartBeforeSessionAsync);

                    await PlayRecordingEndCueOnceAsync(cancellationToken);
                    _logger.Info("Quick speech recognition succeeded. Characters={0}", recognizedText.Length);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await PlayRecordingEndCueOnceAsync(cancellationToken);
                    _logger.Warn(ex, "Quick speech recognition failed.");
                    SuccessAudioService.PlayRecognitionFailure();
                    return;
                }

                try
                {
                    _logger.Debug(
                        "Starting quick translation. Characters={0}, Target={1}.",
                        recognizedText.Length,
                        binding.TargetLanguage);

                    var result = await TranslationHelper.TranslateAsync(recognizedText, binding.TargetLanguage);
                    if (string.IsNullOrWhiteSpace(result?.Translation))
                    {
                        throw new InvalidOperationException(Localization.GetString("QuickTranslationEmptyTranslationResult"));
                    }

                    await ClipboardService.SetTextAsync(result.Translation, cancellationToken);
                    _logger.Info(
                        "Quick translation copied to clipboard. Target={0}, Translator={1}",
                        binding.TargetLanguage,
                        result.Service);

                    try
                    {
                        await SuccessAudioService.PlaySuccessAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception audioEx)
                    {
                        _logger.Warn(
                            audioEx,
                            "Quick translation succeeded, but the configured success audio failed. Falling back to the system sound.");
                        SuccessAudioService.PlaySystemSuccess();
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Quick translation or clipboard operation failed. Target={0}", binding.TargetLanguage);
                    try
                    {
                        await SuccessAudioService.PlayTranslationFailureAsync(cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception audioEx)
                    {
                        _logger.Warn(audioEx, "Configured translation-failure audio failed. Falling back to the system sound.");
                        SuccessAudioService.PlayTranslationFailure();
                    }
                }
            }
            finally
            {
                lock (_stateLock)
                {
                    if (_activeBinding?.Id == binding.Id)
                    {
                        _activeBinding = null;
                        _stopRequested = false;
                        _recordingEndCuePlayed = false;
                    }
                }
            }
        }

        private async Task PlayRecordingStartBeforeSessionAsync(CancellationToken cancellationToken)
        {
            try
            {
                var timing = QuickTranslationConfig.GetRecordingStartTiming();
                if (string.Equals(
                        timing,
                        QuickTranslationConfig.RecordingStartTimingAfterPlayback,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Debug("Playing recording-start cue to completion before starting speech capture.");
                    await SuccessAudioService.PlayRecordingStartAndWaitAsync(cancellationToken);
                }
                else
                {
                    // Default game-friendly mode: begin playing the cue and immediately call
                    // SpeechContinuousRecognitionSession.StartAsync. The recognizer has already
                    // been compiled in the background, so these occur effectively together.
                    _logger.Debug("Playing recording-start cue and starting speech capture immediately.");
                    await SuccessAudioService.PlayRecordingStartAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception audioEx)
            {
                _logger.Warn(audioEx, "Recording-start audio failed. Speech recognition will continue.");
            }
        }

        private async Task StopRecognitionSafeAsync(CancellationToken cancellationToken)
        {
            try
            {
                var stopped = await _speechRecognitionService.StopActiveRecognitionAsync(cancellationToken);
                if (stopped)
                {
                    _logger.Debug("Active quick speech recognition stop request completed.");
                }
                else
                {
                    _logger.Warn("Active quick speech recognition required the stop fallback path.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal application shutdown.
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to stop the active quick speech recognition session.");
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception audioEx)
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
            _speechRecognitionService.Dispose();
        }
    }
}
