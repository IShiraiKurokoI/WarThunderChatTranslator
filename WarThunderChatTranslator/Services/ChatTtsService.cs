#nullable enable

using NLog;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Services.ContentFiltering;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace WarThunderChatTranslator.Services
{
    internal sealed class ChatTtsService : IAsyncDisposable
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly GameChatPollingService _gameChatService;
        private readonly TtsProviderManager _providerManager;
        private readonly Func<string, ITtsProvider> _resolveProvider;
        private readonly Func<StorageFile, double, CancellationToken, Task> _playAudio;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<string?, string?> _filterForTts;
        private readonly Channel<TtsJob> _queue;
        private readonly CancellationTokenSource _cts = new();
        private readonly SemaphoreSlim _playbackLock = new(1, 1);
        private readonly MediaPlayer _player = new();
        private readonly Task _workerTask;
        private long _latestSequence;
        private bool _disposed;

        public ChatTtsService(GameChatPollingService gameChatService, TtsProviderManager? providerManager = null)
            : this(gameChatService, providerManager, null, null, null, null) { }

        // Dependency seams allow deterministic queue/filter/playback tests on non-Windows CI.
        internal ChatTtsService(
            GameChatPollingService gameChatService,
            TtsProviderManager? providerManager,
            Func<string, ITtsProvider>? providerResolver,
            Func<StorageFile, double, CancellationToken, Task>? playback,
            Func<DateTimeOffset>? utcNow,
            Func<string?, string?>? filterForTts)
        {
            _gameChatService = gameChatService ?? throw new ArgumentNullException(nameof(gameChatService));
            _providerManager = providerManager ?? new TtsProviderManager();
            _resolveProvider = providerResolver ?? _providerManager.GetProvider;
            _playAudio = playback ?? PlayFileAsync;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
            _filterForTts = filterForTts ?? ContentFilterService.Shared.FilterForTts;

            // The queue itself is unbounded so its effective capacity can be changed at runtime.
            // Older items are discarded by sequence when they fall outside the latest configured window.
            _queue = Channel.CreateUnbounded<TtsJob>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

            _player.CommandManager.IsEnabled = false;
            _gameChatService.MessageProcessed += OnMessageProcessed;
            _workerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }

        public ITtsProvider Provider => _resolveProvider(ChatTtsConfig.GetProvider());

        public ITtsProvider GetProvider(string providerId) => _resolveProvider(providerId);

        public SherpaOnnxTtsProvider SherpaProvider => _providerManager.Sherpa;
        public GptSoVitsTtsProvider GptSoVitsProvider => _providerManager.GptSoVits;

        private void OnMessageProcessed(ChatMessage message)
        {
            if (_disposed || message is null || !message.TranslationSucceeded || !ChatTtsConfig.IsEnabled() || !ShouldSpeak(message))
            {
                return;
            }

            var text = _filterForTts(message.TranslatedMessage)?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var providerId = ChatTtsConfig.GetProvider();
            var job = new TtsJob(
                text,
                providerId,
                ChatTtsConfig.GetVoiceId(providerId),
                ChatTtsConfig.GetSpeakingRate(),
                ChatTtsConfig.GetVolume(),
                _utcNow(),
                Interlocked.Increment(ref _latestSequence));

            if (!_queue.Writer.TryWrite(job))
            {
                _logger.Debug("Chat TTS queue rejected a message.");
            }
        }

        private static bool ShouldSpeak(ChatMessage message)
        {
            if (string.IsNullOrWhiteSpace(message.Sender))
            {
                return ChatTtsConfig.ShouldSpeakSystem();
            }

            return message.Enemy
                ? ChatTtsConfig.ShouldSpeakEnemy()
                : ChatTtsConfig.ShouldSpeakAlly();
        }

        private async Task ProcessQueueAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var job in _queue.Reader.ReadAllAsync(cancellationToken))
                {
                    if (IsOutsideQueueWindow(job))
                    {
                        _logger.Debug("Dropped chat TTS item outside the latest {0}-message waiting window.", ChatTtsConfig.GetQueueCapacity());
                        continue;
                    }

                    if (IsExpired(job))
                    {
                        _logger.Debug("Dropped expired chat TTS item.");
                        continue;
                    }

                    try
                    {
                        var provider = _resolveProvider(job.ProviderId);
                        var audioFile = await provider.SynthesizeAsync(
                            job.Text,
                            job.VoiceId,
                            job.SpeakingRate,
                            cancellationToken).ConfigureAwait(false);

                        // Re-check after synthesis because newer messages may have arrived while the provider was working.
                        if (IsOutsideQueueWindow(job) || IsExpired(job))
                        {
                            continue;
                        }

                        await _playAudio(audioFile, job.Volume, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Could not speak a translated chat message using provider {0}.", job.ProviderId);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }


        private bool IsOutsideQueueWindow(TtsJob job)
        {
            var capacity = ChatTtsConfig.GetQueueCapacity();
            var latestSequence = Volatile.Read(ref _latestSequence);
            return latestSequence - job.Sequence >= capacity;
        }

        private bool IsExpired(TtsJob job)
        {
            var maxAge = ChatTtsConfig.GetMaxQueueAge();
            return maxAge.HasValue && _utcNow() - job.EnqueuedAt > maxAge.Value;
        }

        public async Task PreviewAsync(string? text = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var previewText = string.IsNullOrWhiteSpace(text)
                ? "Enemy on the capture point."
                : text.Trim();

            var providerId = ChatTtsConfig.GetProvider();
            var provider = _resolveProvider(providerId);
            var audioFile = await provider.SynthesizeAsync(
                previewText,
                ChatTtsConfig.GetVoiceId(providerId),
                ChatTtsConfig.GetSpeakingRate(),
                cancellationToken).ConfigureAwait(false);

            await _playAudio(audioFile, ChatTtsConfig.GetVolume(), cancellationToken).ConfigureAwait(false);
        }

        private async Task PlayFileAsync(StorageFile file, double volume, CancellationToken cancellationToken)
        {
            await _playbackLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();

                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                TypedEventHandler<MediaPlayer, object>? endedHandler = null;
                TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? failedHandler = null;

                endedHandler = (_, _) => completion.TrySetResult(true);
                failedHandler = (_, args) => completion.TrySetException(
                    new InvalidOperationException(args.ErrorMessage ?? "Media playback failed."));

                _player.MediaEnded += endedHandler;
                _player.MediaFailed += failedHandler;

                try
                {
                    _player.Pause();
                    // Apply routing immediately before each playback, so both queued chat
                    // and preview audio use the currently configured render device.
                    // Missing explicit devices fail instead of silently playing elsewhere.
                    var outputDevice = await TtsOutputDeviceService.ResolveAsync(
                        ChatTtsConfig.GetOutputDeviceId());
                    cancellationToken.ThrowIfCancellationRequested();
                    _player.AudioDevice = outputDevice;
                    _player.Volume = Math.Clamp(volume, 0.0, 1.0);
                    _player.Source = MediaSource.CreateFromStorageFile(file);
                    _player.Play();

                    using var registration = cancellationToken.Register(() =>
                    {
                        try { _player.Pause(); } catch { }
                        completion.TrySetCanceled(cancellationToken);
                    });

                    await completion.Task.ConfigureAwait(false);
                }
                finally
                {
                    _player.MediaEnded -= endedHandler;
                    _player.MediaFailed -= failedHandler;
                    _player.Source = null;
                }
            }
            finally
            {
                _playbackLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gameChatService.MessageProcessed -= OnMessageProcessed;
            _queue.Writer.TryComplete();
            _cts.Cancel();

            try { await _workerTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.Debug(ex, "Chat TTS worker did not stop cleanly."); }

            try { _player.Pause(); } catch { }
            _player.Dispose();
            _playbackLock.Dispose();
            _cts.Dispose();
            _providerManager.Dispose();
        }

        private sealed record TtsJob(
            string Text,
            string ProviderId,
            string VoiceId,
            double SpeakingRate,
            double Volume,
            DateTimeOffset EnqueuedAt,
            long Sequence);
    }
}
