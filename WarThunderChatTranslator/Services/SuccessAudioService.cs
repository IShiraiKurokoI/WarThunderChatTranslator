using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WarThunderChatTranslator.Services
{
    public sealed class SuccessAudioService : IDisposable
    {
        private const uint MbIconAsterisk = 0x00000040;
        private const uint MbIconExclamation = 0x00000030;
        private const uint MbIconHand = 0x00000010;
        private static readonly TimeSpan TtsGenerationTimeout = TimeSpan.FromSeconds(12);

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _playbackLock = new(1, 1);
        private readonly SemaphoreSlim _cacheFinalizeLock = new(1, 1);
        private readonly Dictionary<QuickTranslationAudioCue, MediaPlayer> _players = new();
        private bool _disposed;

        public SuccessAudioService()
        {
            foreach (QuickTranslationAudioCue cue in Enum.GetValues(typeof(QuickTranslationAudioCue)))
            {
                var player = new MediaPlayer();
                player.CommandManager.IsEnabled = false;
                _players[cue] = player;
            }
        }

        public static IReadOnlyList<VoiceInformation> GetVoices()
        {
            return SpeechSynthesizer.AllVoices
                .OrderBy(voice => voice.Language, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public Task PlaySuccessAsync(CancellationToken cancellationToken = default)
            => PlayCueAsync(QuickTranslationAudioCue.Success, waitForCompletion: false, cancellationToken);

        public Task PlayRecordingStartAsync(CancellationToken cancellationToken = default)
            => PlayCueAsync(QuickTranslationAudioCue.RecordingStart, waitForCompletion: false, cancellationToken);

        public Task PlayRecordingStartAndWaitAsync(CancellationToken cancellationToken = default)
            => PlayCueAsync(QuickTranslationAudioCue.RecordingStart, waitForCompletion: true, cancellationToken);

        public Task PlayRecordingEndAsync(CancellationToken cancellationToken = default)
            => PlayCueAsync(QuickTranslationAudioCue.RecordingEnd, waitForCompletion: false, cancellationToken);

        public Task PlayTranslationFailureAsync(CancellationToken cancellationToken = default)
            => PlayCueAsync(QuickTranslationAudioCue.TranslationFailure, waitForCompletion: false, cancellationToken);

        public Task PreviewAsync(QuickTranslationAudioCue cue, CancellationToken cancellationToken = default)
            => PlayCueAsync(cue, waitForCompletion: false, cancellationToken);

        public Task PreviewAsync(CancellationToken cancellationToken = default)
            => PreviewAsync(QuickTranslationAudioCue.Success, cancellationToken);

        private async Task PlayCueAsync(
            QuickTranslationAudioCue cue,
            bool waitForCompletion,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            var mode = ApplicationConfig.GetSettings(QuickTranslationConfig.GetSoundModeKey(cue))
                       ?? QuickTranslationConfig.SoundModeSystem;

            switch (mode)
            {
                case QuickTranslationConfig.SoundModeTts:
                {
                    var text = ApplicationConfig.GetSettings(QuickTranslationConfig.GetPromptTextKey(cue));
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        text = QuickTranslationConfig.GetDefaultPromptText(cue);
                    }

                    var voiceId = ApplicationConfig.GetSettings(QuickTranslationConfig.GetVoiceIdKey(cue));
                    var speakingRate = QuickTranslationConfig.GetSpeakingRate(cue);
                    var file = await GetOrCreateTtsAudioAsync(text, voiceId, speakingRate, cancellationToken);
                    await PlayFileAsync(file, cue, waitForCompletion, cancellationToken);
                    break;
                }
                case QuickTranslationConfig.SoundModeCustom:
                {
                    var customPath = ApplicationConfig.GetSettings(QuickTranslationConfig.GetCustomAudioPathKey(cue));
                    if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                    {
                        var file = await StorageFile.GetFileFromPathAsync(customPath);
                        await PlayFileAsync(file, cue, waitForCompletion, cancellationToken);
                    }
                    else
                    {
                        PlaySystemCue(cue);
                        if (waitForCompletion)
                        {
                            await Task.Delay(250, cancellationToken);
                        }
                    }
                    break;
                }
                default:
                    PlaySystemCue(cue);
                    if (waitForCompletion)
                    {
                        await Task.Delay(250, cancellationToken);
                    }
                    break;
            }
        }

        public void PlaySystemSuccess() => MessageBeep(MbIconAsterisk);

        public void PlayRecognitionFailure() => MessageBeep(MbIconExclamation);

        public void PlayTranslationFailure() => MessageBeep(MbIconHand);

        private static void PlaySystemCue(QuickTranslationAudioCue cue)
        {
            MessageBeep(cue switch
            {
                QuickTranslationAudioCue.RecordingEnd => MbIconExclamation,
                QuickTranslationAudioCue.TranslationFailure => MbIconHand,
                _ => MbIconAsterisk
            });
        }

        public Task<StorageFile> GetOrCreateTtsAudioAsync(string text, string voiceId, CancellationToken cancellationToken = default)
            => GetOrCreateTtsAudioAsync(text, voiceId, QuickTranslationConfig.DefaultSpeakingRate, cancellationToken);

        public async Task<StorageFile> GetOrCreateTtsAudioAsync(
            string text,
            string voiceId,
            double speakingRate,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(Localization.GetString("QuickTranslationTtsPromptEmpty"), nameof(text));
            }

            var trimmedText = text.Trim();
            var normalizedSpeakingRate = Math.Clamp(speakingRate, 0.5, 6.0);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var generationTask = Task.Run(
                () => GetOrCreateTtsAudioCoreAsync(trimmedText, voiceId, normalizedSpeakingRate, linkedCts.Token),
                CancellationToken.None);

            var timeoutTask = Task.Delay(TtsGenerationTimeout, CancellationToken.None);
            var completedTask = await Task.WhenAny(generationTask, timeoutTask);
            if (completedTask != generationTask)
            {
                linkedCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                _logger.Warn("Quick translation TTS generation timed out. VoiceId={0}", voiceId ?? "<default>");
                throw new TimeoutException(string.Format(Localization.GetString("QuickTranslationTtsGenerationTimeoutFormat"), TtsGenerationTimeout.TotalSeconds));
            }

            return await generationTask;
        }

        private async Task<StorageFile> GetOrCreateTtsAudioCoreAsync(
            string text,
            string voiceId,
            double speakingRate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selectedVoice = GetVoices().FirstOrDefault(voice => string.Equals(voice.Id, voiceId, StringComparison.Ordinal))
                ?? SpeechSynthesizer.DefaultVoice;
            var cacheKey = TtsCacheKey.Compute(text, selectedVoice.Id, speakingRate);
            var fileName = cacheKey + ".wav";
            var cacheFolder = await GetTtsCacheFolderAsync();
            if (await cacheFolder.TryGetItemAsync(fileName) is StorageFile cachedFile)
            {
                return cachedFile;
            }

            using var synthesizer = new SpeechSynthesizer { Voice = selectedVoice };
            synthesizer.Options.SpeakingRate = speakingRate;
            var synthesisOperation = synthesizer.SynthesizeTextToStreamAsync(text);
            using var synthesisCancellation = cancellationToken.Register(() =>
            {
                try { synthesisOperation.Cancel(); } catch { }
            });

            using var speechStream = await synthesisOperation;
            cancellationToken.ThrowIfCancellationRequested();

            var size = speechStream.Size;
            if (size == 0)
            {
                throw new InvalidOperationException(Localization.GetString("QuickTranslationTtsEmptyAudio"));
            }

            var tempName = $"{cacheKey}.{Guid.NewGuid():N}.tmp";
            var tempFile = await cacheFolder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting);

            try
            {
                using (var destination = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    destination.Size = 0;
                    using var input = speechStream.GetInputStreamAt(0);
                    using var output = destination.GetOutputStreamAt(0);
                    var copyOperation = RandomAccessStream.CopyAsync(input, output, size);
                    using var copyCancellation = cancellationToken.Register(() =>
                    {
                        try { copyOperation.Cancel(); } catch { }
                    });
                    await copyOperation;
                    cancellationToken.ThrowIfCancellationRequested();

                    var flushOperation = output.FlushAsync();
                    using var flushCancellation = cancellationToken.Register(() =>
                    {
                        try { flushOperation.Cancel(); } catch { }
                    });
                    await flushOperation;
                }

                await _cacheFinalizeLock.WaitAsync(cancellationToken);
                try
                {
                    if (await cacheFolder.TryGetItemAsync(fileName) is StorageFile raceWinner)
                    {
                        await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        return raceWinner;
                    }

                    await tempFile.RenameAsync(fileName, NameCollisionOption.ReplaceExisting);
                    var finalFile = await cacheFolder.GetFileAsync(fileName);
                    _logger.Info(
                        "Generated quick translation TTS cache {0} using voice {1}, rate {2:0.0}x. Size={3}",
                        fileName,
                        selectedVoice.DisplayName,
                        speakingRate,
                        size);
                    return finalFile;
                }
                finally
                {
                    _cacheFinalizeLock.Release();
                }
            }
            catch
            {
                try
                {
                    await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
                catch
                {
                    // Best-effort cleanup for a cancelled/failed generation.
                }

                throw;
            }
        }

        public async Task<string> ImportCustomAudioAsync(
            StorageFile sourceFile,
            QuickTranslationAudioCue cue = QuickTranslationAudioCue.Success,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(sourceFile);
            cancellationToken.ThrowIfCancellationRequested();

            var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("QuickTranslation", CreationCollisionOption.OpenIfExists);
            var audioRoot = await root.CreateFolderAsync("CustomAudio", CreationCollisionOption.OpenIfExists);
            var folder = await audioRoot.CreateFolderAsync(cue.ToString(), CreationCollisionOption.OpenIfExists);

            var existingFiles = await folder.GetFilesAsync();
            foreach (var existingFile in existingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(existingFile.Path, sourceFile.Path, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await existingFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Could not remove old quick translation custom audio file {0}.", existingFile.Path);
                    }
                }
            }

            if (string.Equals(sourceFile.Path, Path.Combine(folder.Path, sourceFile.Name), StringComparison.OrdinalIgnoreCase))
            {
                return sourceFile.Path;
            }

            var copied = await sourceFile.CopyAsync(folder, sourceFile.Name, NameCollisionOption.ReplaceExisting);
            return copied.Path;
        }

        public async Task ClearTtsCacheAsync()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _cacheFinalizeLock.WaitAsync();
            try
            {
                var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("QuickTranslation", CreationCollisionOption.OpenIfExists);
                if (await root.TryGetItemAsync("TtsCache") is StorageFolder folder)
                {
                    await folder.DeleteAsync(StorageDeleteOption.PermanentDelete);
                }
            }
            finally
            {
                _cacheFinalizeLock.Release();
            }
        }

        private async Task PlayFileAsync(
            StorageFile file,
            QuickTranslationAudioCue cue,
            bool waitForCompletion,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> completionTcs = null;
            MediaPlayer player = null;
            TypedEventHandler<MediaPlayer, object> endedHandler = null;
            TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs> failedHandler = null;

            await _playbackLock.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                cancellationToken.ThrowIfCancellationRequested();

                player = _players[cue];
                player.Pause();
                player.Volume = QuickTranslationConfig.GetSuccessVolume();

                if (waitForCompletion)
                {
                    completionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    endedHandler = (sender, args) => completionTcs.TrySetResult(true);
                    failedHandler = (sender, args) => completionTcs.TrySetResult(false);
                    player.MediaEnded += endedHandler;
                    player.MediaFailed += failedHandler;
                }

                player.Source = MediaSource.CreateFromStorageFile(file);
                player.Play();
            }
            finally
            {
                _playbackLock.Release();
            }

            if (!waitForCompletion || completionTcs == null || player == null)
            {
                return;
            }

            try
            {
                // Never allow a broken media backend or malformed custom file to stall speech
                // capture indefinitely. Fifteen seconds is intentionally generous for a cue.
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
                var completed = await Task.WhenAny(completionTcs.Task, timeoutTask);
                if (completed != completionTcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.Warn("Timed out waiting for quick-translation cue {0} to finish.", cue);
                }
            }
            finally
            {
                if (endedHandler != null)
                {
                    player.MediaEnded -= endedHandler;
                }

                if (failedHandler != null)
                {
                    player.MediaFailed -= failedHandler;
                }
            }
        }

        private static async Task<StorageFolder> GetTtsCacheFolderAsync()
        {
            var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("QuickTranslation", CreationCollisionOption.OpenIfExists);
            return await root.CreateFolderAsync("TtsCache", CreationCollisionOption.OpenIfExists);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var player in _players.Values)
            {
                player.Dispose();
            }
            _players.Clear();
            _playbackLock.Dispose();
            _cacheFinalizeLock.Dispose();
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MessageBeep(uint uType);
    }
}
