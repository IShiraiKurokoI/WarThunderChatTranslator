using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace WarThunderChatTranslator.Services
{
    public enum SpeechRecognitionServiceState
    {
        NotInitialized,
        WarmingUp,
        Ready,
        Recording,
        Stopping,
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

    public sealed class SpeechRecognitionService : IDisposable
    {
        private sealed class SessionStartException : Exception
        {
            public SessionStartException(Exception innerException)
                : base(innerException?.Message, innerException)
            {
            }
        }

        private static readonly TimeSpan AutoStopSilenceTimeout = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ManualStopTimeout = TimeSpan.FromSeconds(4);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _stateLock = new();
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
        private SpeechRecognizer _recognizer;
        private string _recognizerLanguageTag;
        private SpeechContinuousRecognitionSession _activeSession;
        private TaskCompletionSource<SpeechRecognitionResultStatus> _activeCompletion;
        private bool _stopInProgress;
        private bool _pendingStopRequested;
        private bool _disposed;
        private SpeechRecognitionServiceState _state = SpeechRecognitionServiceState.NotInitialized;
        private string _stateDetail;

        public event EventHandler<SpeechRecognitionStateChangedEventArgs> StateChanged;

        public SpeechRecognitionServiceState State
        {
            get
            {
                lock (_stateLock)
                {
                    return _state;
                }
            }
        }

        public string StateDetail
        {
            get
            {
                lock (_stateLock)
                {
                    return _stateDetail;
                }
            }
        }

        public bool IsRecognitionActive
        {
            get
            {
                lock (_stateLock)
                {
                    return _activeSession != null;
                }
            }
        }

        public void PrepareForRecognition()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_stateLock)
            {
                if (_activeSession == null)
                {
                    _pendingStopRequested = false;
                    _stopInProgress = false;
                }
            }
        }

        public async Task WarmUpAsync(string languageTag, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            var selectedLanguage = ResolveLanguage(languageTag);
            var effectiveLanguageTag = selectedLanguage?.LanguageTag ?? SpeechRecognizer.SystemSpeechLanguage?.LanguageTag ?? string.Empty;

            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_recognizer != null &&
                    string.Equals(_recognizerLanguageTag, effectiveLanguageTag, StringComparison.OrdinalIgnoreCase) &&
                    State is SpeechRecognitionServiceState.Ready or SpeechRecognitionServiceState.Recording or SpeechRecognitionServiceState.Stopping)
                {
                    return;
                }

                if (IsRecognitionActive)
                {
                    // Reconfiguration is intentionally deferred until the current recording finishes.
                    // The caller can await this method again after the active session completes.
                    return;
                }

                await BuildRecognizerCoreAsync(selectedLanguage, effectiveLanguageTag, cancellationToken);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task ReinitializeAsync(string languageTag, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            var selectedLanguage = ResolveLanguage(languageTag);
            var effectiveLanguageTag = selectedLanguage?.LanguageTag ?? SpeechRecognizer.SystemSpeechLanguage?.LanguageTag ?? string.Empty;

            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (IsRecognitionActive)
                {
                    return;
                }

                DisposePreparedRecognizer();
                await BuildRecognizerCoreAsync(selectedLanguage, effectiveLanguageTag, cancellationToken);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            if (IsRecognitionActive)
            {
                await StopActiveRecognitionAsync(cancellationToken);
            }

            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                SpeechContinuousRecognitionSession session;
                lock (_stateLock)
                {
                    session = _activeSession;
                }

                if (session != null)
                {
                    try
                    {
                        await session.CancelAsync();
                    }
                    catch
                    {
                        // Best effort. The active recognition task will clean up its handlers.
                    }
                }

                DisposePreparedRecognizer();
                SetState(SpeechRecognitionServiceState.NotInitialized, null);
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<string> RecognizeOnceAsync(
            string languageTag,
            CancellationToken cancellationToken = default,
            Func<CancellationToken, Task> beforeSessionStartAsync = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // In the normal path this is a no-op because the recognizer was already warmed up
            // when the feature was enabled or when the app started.
            await WarmUpAsync(languageTag, cancellationToken);

            var cueInvoked = false;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return await RecognizePreparedAsync(
                        cancellationToken,
                        !cueInvoked ? beforeSessionStartAsync : null,
                        () => cueInvoked = true);
                }
                catch (SessionStartException) when (attempt == 0 && !cancellationToken.IsCancellationRequested)
                {
                    // A long-lived recognizer can become invalid after audio-device changes,
                    // suspend/resume, or Windows speech-service restarts. Rebuild it once and retry.
                    await ReinitializeAsync(languageTag, cancellationToken);
                }
            }

            throw new InvalidOperationException(Localization.GetString("QuickTranslationSpeechUnavailable"));
        }

        private async Task<string> RecognizePreparedAsync(
            CancellationToken cancellationToken,
            Func<CancellationToken, Task> beforeSessionStartAsync,
            Action cueInvoked)
        {
            await _lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var recognizer = _recognizer;
                if (recognizer == null)
                {
                    throw new SessionStartException(new InvalidOperationException(
                        Localization.GetString("QuickTranslationSpeechUnavailable")));
                }

                var session = recognizer.ContinuousRecognitionSession;
                session.AutoStopSilenceTimeout = AutoStopSilenceTimeout;

                var results = new List<string>();
                var resultsLock = new object();
                var completedTcs = new TaskCompletionSource<SpeechRecognitionResultStatus>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                void OnResultGenerated(
                    SpeechContinuousRecognitionSession sender,
                    SpeechContinuousRecognitionResultGeneratedEventArgs args)
                {
                    var result = args.Result;
                    if (result?.Status != SpeechRecognitionResultStatus.Success || string.IsNullOrWhiteSpace(result.Text))
                    {
                        return;
                    }

                    lock (resultsLock)
                    {
                        results.Add(result.Text.Trim());
                    }
                }

                string latestHypothesis = null;

                void OnHypothesisGenerated(
                    SpeechRecognizer sender,
                    SpeechRecognitionHypothesisGeneratedEventArgs args)
                {
                    var text = args?.Hypothesis?.Text;
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    lock (resultsLock)
                    {
                        latestHypothesis = text.Trim();
                    }
                }

                void OnCompleted(
                    SpeechContinuousRecognitionSession sender,
                    SpeechContinuousRecognitionCompletedEventArgs args)
                {
                    _logger.Debug("Continuous speech recognition Completed event. Status={0}", args.Status);
                    completedTcs.TrySetResult(args.Status);
                }

                recognizer.HypothesisGenerated += OnHypothesisGenerated;
                session.ResultGenerated += OnResultGenerated;
                session.Completed += OnCompleted;
                var sessionStarted = false;

                try
                {
                    bool stopWasAlreadyRequested;
                    lock (_stateLock)
                    {
                        stopWasAlreadyRequested = _pendingStopRequested;
                    }

                    if (!stopWasAlreadyRequested && beforeSessionStartAsync != null)
                    {
                        cueInvoked?.Invoke();
                        await beforeSessionStartAsync(cancellationToken);
                    }

                    try
                    {
                        await session.StartAsync(SpeechContinuousRecognitionMode.Default);
                        sessionStarted = true;
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        SetState(SpeechRecognitionServiceState.Unavailable, ex.Message);
                        throw new SessionStartException(ex);
                    }

                    bool stopImmediately;
                    lock (_stateLock)
                    {
                        if (_disposed)
                        {
                            throw new ObjectDisposedException(nameof(SpeechRecognitionService));
                        }

                        _activeSession = session;
                        _activeCompletion = completedTcs;
                        stopImmediately = _pendingStopRequested;
                        _stopInProgress = stopImmediately;
                    }
                    SetState(
                        stopImmediately ? SpeechRecognitionServiceState.Stopping : SpeechRecognitionServiceState.Recording,
                        null);

                    if (stopImmediately)
                    {
                        try
                        {
                            var stoppedCleanly = await StopSessionWithTimeoutAsync(session, cancellationToken);
                            _logger.Debug("Queued speech stop applied immediately after StartAsync. Clean={0}", stoppedCleanly);

                            // StopAsync promises to flush pending recognition results to ResultGenerated.
                            // Some systems do not reliably raise Completed after a manual stop, so
                            // explicitly release the recognition waiter once the stop path completes.
                            completedTcs.TrySetResult(SpeechRecognitionResultStatus.Success);
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Queued speech stop failed after StartAsync.");
                            completedTcs.TrySetResult(SpeechRecognitionResultStatus.UserCanceled);
                        }
                        finally
                        {
                            lock (_stateLock)
                            {
                                if (ReferenceEquals(_activeSession, session))
                                {
                                    _stopInProgress = false;
                                }
                            }
                        }
                    }

                    using var cancellationRegistration = cancellationToken.Register(() =>
                    {
                        _ = CancelSessionSafeAsync(session);
                    });

                    var completedStatus = await completedTcs.Task;
                    cancellationToken.ThrowIfCancellationRequested();

                    string text;
                    lock (resultsLock)
                    {
                        text = string.Join(" ", results.Where(item => !string.IsNullOrWhiteSpace(item))).Trim();
                        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(latestHypothesis))
                        {
                            // A very short manual recording can be stopped before Windows promotes
                            // the current hypothesis to ResultGenerated. Prefer that hypothesis to
                            // dropping the user's whole utterance.
                            text = latestHypothesis;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }

                    if (completedStatus != SpeechRecognitionResultStatus.Success)
                    {
                        throw new InvalidOperationException(string.Format(
                            Localization.GetString("QuickTranslationSpeechRecognitionFailedFormat"),
                            completedStatus));
                    }

                    throw new InvalidOperationException(Localization.GetString("QuickTranslationSpeechNoText"));
                }
                finally
                {
                    lock (_stateLock)
                    {
                        if (ReferenceEquals(_activeSession, session))
                        {
                            _activeSession = null;
                            _activeCompletion = null;
                            _stopInProgress = false;
                            _pendingStopRequested = false;
                        }
                    }

                    recognizer.HypothesisGenerated -= OnHypothesisGenerated;
                    session.ResultGenerated -= OnResultGenerated;
                    session.Completed -= OnCompleted;

                    if (sessionStarted && !_disposed && _recognizer != null)
                    {
                        SetState(SpeechRecognitionServiceState.Ready, null);
                    }
                }
            }
            finally
            {
                _lifecycleLock.Release();
            }
        }

        public async Task<bool> StopActiveRecognitionAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            SpeechContinuousRecognitionSession session;
            TaskCompletionSource<SpeechRecognitionResultStatus> completion;
            SpeechRecognizer recognizer;
            lock (_stateLock)
            {
                _pendingStopRequested = true;
                session = _activeSession;
                completion = _activeCompletion;
                recognizer = _recognizer;
                if (session == null)
                {
                    // Warm-up/start may still be finishing. Keep this queued so the session is
                    // stopped immediately after StartAsync succeeds.
                    return true;
                }

                if (_stopInProgress)
                {
                    return true;
                }

                _stopInProgress = true;
            }

            // Update the UI immediately. The microphone is no longer considered an active
            // recording from the user's point of view once the stop hotkey was accepted.
            SetState(SpeechRecognitionServiceState.Stopping, null);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.Debug(
                    "Stopping active speech recognition. RecognizerState={0}, CompletionRegistered={1}",
                    recognizer?.State,
                    completion != null);

                // Microsoft's guidance is to stop only while the recognizer is not Idle. If it
                // already became Idle due to auto-stop, simply unblock the waiter below.
                var stoppedCleanly = true;
                if (recognizer?.State != SpeechRecognizerState.Idle)
                {
                    stoppedCleanly = await StopSessionWithTimeoutAsync(session, cancellationToken);
                }

                // StopAsync flushes pending results to ResultGenerated before it completes.
                // Explicitly signal completion as a compatibility fallback because on some
                // systems the Completed event is not observed after a manual hotkey stop.
                completion?.TrySetResult(SpeechRecognitionResultStatus.Success);
                _logger.Debug("Active speech recognition stop path completed. Clean={0}", stoppedCleanly);
                return stoppedCleanly;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Stopping active speech recognition failed. Falling back to cancellation.");

                // Never leave the UI and the first ExecuteAsync call stuck in Recording forever.
                // Cancel as a best-effort fallback, then release the waiter. The recognition task
                // can still use any final ResultGenerated text or the latest hypothesis captured
                // before cancellation.
                await CancelSessionSafeAsync(session);
                completion?.TrySetResult(SpeechRecognitionResultStatus.Success);
                return false;
            }
            finally
            {
                lock (_stateLock)
                {
                    if (ReferenceEquals(_activeSession, session))
                    {
                        _stopInProgress = false;
                    }
                }
            }
        }

        private async Task<bool> StopSessionWithTimeoutAsync(
            SpeechContinuousRecognitionSession session,
            CancellationToken cancellationToken)
        {
            // Convert the WinRT async action into an ordinary Task so a broken/slow speech
            // service cannot leave the quick-translation state machine stuck forever.
            async Task AwaitStopAsync()
            {
                await session.StopAsync();
            }

            var stopTask = AwaitStopAsync();
            var timeoutTask = Task.Delay(ManualStopTimeout, cancellationToken);
            var winner = await Task.WhenAny(stopTask, timeoutTask);

            if (winner == stopTask)
            {
                await stopTask;
                return true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            _logger.Warn(
                "Speech recognition StopAsync did not finish within {0:F1}s. Falling back to CancelAsync.",
                ManualStopTimeout.TotalSeconds);
            await CancelSessionSafeAsync(session);
            return false;
        }

        private async Task BuildRecognizerCoreAsync(
            Language selectedLanguage,
            string effectiveLanguageTag,
            CancellationToken cancellationToken)
        {
            SetState(SpeechRecognitionServiceState.WarmingUp, null);

            SpeechRecognizer newRecognizer = null;
            try
            {
                newRecognizer = selectedLanguage == null
                    ? new SpeechRecognizer()
                    : new SpeechRecognizer(selectedLanguage);

                newRecognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation,
                    "QuickTranslationDictation"));

                var compileOperation = newRecognizer.CompileConstraintsAsync();
                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    try { compileOperation.Cancel(); } catch { }
                });

                var compileResult = await compileOperation;
                cancellationToken.ThrowIfCancellationRequested();
                if (compileResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    throw new InvalidOperationException(string.Format(
                        Localization.GetString("QuickTranslationSpeechInitializationFailedFormat"),
                        compileResult.Status));
                }

                newRecognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = AutoStopSilenceTimeout;

                DisposePreparedRecognizer();
                _recognizer = newRecognizer;
                _recognizerLanguageTag = effectiveLanguageTag;
                newRecognizer = null;
                SetState(SpeechRecognitionServiceState.Ready, null);
            }
            catch (OperationCanceledException)
            {
                newRecognizer?.Dispose();
                if (!_disposed)
                {
                    SetState(SpeechRecognitionServiceState.NotInitialized, null);
                }
                throw;
            }
            catch (Exception ex)
            {
                newRecognizer?.Dispose();
                SetState(SpeechRecognitionServiceState.Unavailable, ex.Message);
                throw;
            }
        }

        private void DisposePreparedRecognizer()
        {
            var recognizer = _recognizer;
            _recognizer = null;
            _recognizerLanguageTag = null;
            recognizer?.Dispose();
        }

        private void SetState(SpeechRecognitionServiceState state, string detail)
        {
            EventHandler<SpeechRecognitionStateChangedEventArgs> handler;
            lock (_stateLock)
            {
                if (_state == state && string.Equals(_stateDetail, detail, StringComparison.Ordinal))
                {
                    return;
                }

                _state = state;
                _stateDetail = detail;
                handler = StateChanged;
            }

            handler?.Invoke(this, new SpeechRecognitionStateChangedEventArgs(state, detail));
        }

        private static async Task CancelSessionSafeAsync(SpeechContinuousRecognitionSession session)
        {
            try
            {
                await session.CancelAsync();
            }
            catch
            {
                // Best effort cancellation during application shutdown.
            }
        }

        private static Language ResolveLanguage(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
            {
                return SpeechRecognizer.SystemSpeechLanguage;
            }

            var supported = SpeechRecognizer.SupportedTopicLanguages.FirstOrDefault(language =>
                string.Equals(language.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase));
            return supported ?? SpeechRecognizer.SystemSpeechLanguage;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            SpeechContinuousRecognitionSession session;
            SpeechRecognizer recognizerToDispose = null;
            lock (_stateLock)
            {
                session = _activeSession;
                _activeSession = null;
                _activeCompletion?.TrySetResult(SpeechRecognitionResultStatus.UserCanceled);
                _activeCompletion = null;
                if (session == null)
                {
                    recognizerToDispose = _recognizer;
                    _recognizer = null;
                }
            }

            if (session != null)
            {
                _ = CancelSessionSafeAsync(session);
            }

            recognizerToDispose?.Dispose();
        }
    }
}
