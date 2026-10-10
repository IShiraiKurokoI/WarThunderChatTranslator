#nullable enable

using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using Windows.Media.SpeechSynthesis;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WarThunderChatTranslator.Services
{
    internal sealed class WindowsTtsProvider : ITtsProvider, IDisposable
    {
        private static readonly TimeSpan GenerationTimeout = TimeSpan.FromSeconds(12);
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _cacheFinalizeLock = new(1, 1);
        private bool _disposed;

        public string Id => ChatTtsConfig.ProviderWindows;
        public string DisplayName => "Windows";

        public IReadOnlyList<TtsVoiceInfo> GetVoices()
        {
            return SpeechSynthesizer.AllVoices
                .OrderBy(voice => voice.Language, StringComparer.OrdinalIgnoreCase)
                .ThenBy(voice => voice.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(voice => new TtsVoiceInfo(voice.Id, voice.DisplayName, voice.Language))
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

            var normalizedRate = Math.Clamp(speakingRate, 0.5, 2.0);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var generationTask = Task.Run(
                () => SynthesizeCoreAsync(text.Trim(), voiceId, normalizedRate, linkedCts.Token),
                CancellationToken.None);

            var completedTask = await Task.WhenAny(
                generationTask,
                Task.Delay(GenerationTimeout, CancellationToken.None)).ConfigureAwait(false);

            if (completedTask != generationTask)
            {
                linkedCts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Windows TTS generation timed out after {GenerationTimeout.TotalSeconds:0} seconds.");
            }

            return await generationTask.ConfigureAwait(false);
        }

        private async Task<StorageFile> SynthesizeCoreAsync(
            string text,
            string voiceId,
            double speakingRate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var selectedVoice = SpeechSynthesizer.AllVoices
                .FirstOrDefault(voice => string.Equals(voice.Id, voiceId, StringComparison.Ordinal))
                ?? SpeechSynthesizer.DefaultVoice;

            if (selectedVoice is null)
            {
                throw new InvalidOperationException("No Windows speech synthesis voice is available.");
            }

            var cacheKey = TtsCacheKey.Compute(text, selectedVoice.Id, speakingRate);
            var fileName = cacheKey + ".wav";
            var root = await ApplicationData.Current.LocalFolder.CreateFolderAsync("ChatTts", CreationCollisionOption.OpenIfExists);
            var cacheFolder = await root.CreateFolderAsync("TtsCache", CreationCollisionOption.OpenIfExists);

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

            if (speechStream.Size == 0)
            {
                throw new InvalidOperationException("Windows TTS returned empty audio.");
            }

            var tempFile = await cacheFolder.CreateFileAsync(
                $"{cacheKey}.{Guid.NewGuid():N}.tmp",
                CreationCollisionOption.ReplaceExisting);

            try
            {
                using (var destination = await tempFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    destination.Size = 0;
                    using var input = speechStream.GetInputStreamAt(0);
                    using var output = destination.GetOutputStreamAt(0);
                    var copyOperation = RandomAccessStream.CopyAsync(input, output, speechStream.Size);
                    using var copyCancellation = cancellationToken.Register(() =>
                    {
                        try { copyOperation.Cancel(); } catch { }
                    });
                    await copyOperation;
                    cancellationToken.ThrowIfCancellationRequested();
                    await output.FlushAsync();
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

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _cacheFinalizeLock.Dispose();
        }
    }
}
