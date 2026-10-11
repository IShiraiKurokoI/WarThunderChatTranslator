#nullable enable

using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using Windows.Storage;

namespace WarThunderChatTranslator.Services
{
    internal sealed class GptSoVitsTtsProvider : ITtsProvider, IDisposable
    {
        private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(5);
        private readonly HttpClient _httpClient;
        private readonly GptSoVitsProfileStore _profiles;
        private readonly SemaphoreSlim _requestLock = new(1, 1);
        private string _loadedWeightsSignature = string.Empty;
        private bool _disposed;

        public GptSoVitsTtsProvider() : this(GptSoVitsProfileStore.Shared) { }

        // An injected HTTP client can exercise the real request/response logic against an in-process mock server.
        // An injected profile store avoids touching a user's persistent voice profiles during tests.
        internal GptSoVitsTtsProvider(GptSoVitsProfileStore profiles, HttpClient? httpClient = null)
        {
            _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
            _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                UseProxy = false
            })
            {
                Timeout = TimeSpan.FromSeconds(90)
            };
        }

        public string Id => ChatTtsConfig.ProviderGptSoVits;
        public string DisplayName => "GPT-SoVITS";

        public IReadOnlyList<TtsVoiceInfo> GetVoices()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _profiles.GetProfiles()
                .OrderBy(profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(profile => new TtsVoiceInfo(
                    profile.Id,
                    profile.Name,
                    FormatLanguage(profile.TextLanguage)))
                .ToArray();
        }

        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ConnectionTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("/openapi.json"));
                using var response = await _httpClient.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
                var connected = body.Contains("/tts", StringComparison.Ordinal);
                if (connected)
                {
                    // A manual connection check may follow a GPT-SoVITS restart.
                    // Force the next synthesis to re-apply any profile-specific weights.
                    _loadedWeightsSignature = string.Empty;
                }
                return connected;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                return false;
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

            var profile = _profiles.GetById(voiceId)
                ?? throw new InvalidOperationException("The selected GPT-SoVITS voice profile no longer exists.");
            if (string.IsNullOrWhiteSpace(profile.ReferenceAudioPath))
            {
                throw new InvalidOperationException("The selected GPT-SoVITS voice profile has no reference audio.");
            }

            var normalizedRate = Math.Clamp(speakingRate, 0.5, 2.0);
            var cacheIdentity = string.Join("|",
                profile.Id,
                profile.GptWeightsPath,
                profile.SoVitsWeightsPath,
                profile.ReferenceAudioPath,
                profile.PromptText,
                profile.PromptLanguage,
                profile.TextLanguage,
                ChatTtsConfig.GetGptSoVitsApiUrl());
            var cacheKey = TtsCacheKey.Compute(text.Trim(), Id, cacheIdentity, normalizedRate);
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
                await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // A second caller may have filled the cache while we waited for the request lock.
                    // Avoid synthesizing the same text twice and clean up this caller's temporary file.
                    if (await cacheFolder.TryGetItemAsync(fileName) is StorageFile filledDuringWait)
                    {
                        await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete);
                        return filledDuringWait;
                    }

                    await EnsureWeightsAsync(profile, cancellationToken).ConfigureAwait(false);

                    var payload = new
                    {
                        text = text.Trim(),
                        text_lang = profile.TextLanguage,
                        ref_audio_path = profile.ReferenceAudioPath,
                        prompt_text = profile.PromptText,
                        prompt_lang = profile.PromptLanguage,
                        text_split_method = "cut5",
                        batch_size = 1,
                        speed_factor = normalizedRate,
                        media_type = "wav",
                        streaming_mode = false,
                        parallel_infer = true
                    };

                    using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/tts"))
                    {
                        Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                    };
                    using var response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseContentRead,
                        cancellationToken).ConfigureAwait(false);
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(ParseApiError(bytes, response.ReasonPhrase));
                    }
                    if (bytes.Length < 44)
                    {
                        throw new InvalidOperationException("GPT-SoVITS returned empty audio.");
                    }

                    await FileIO.WriteBytesAsync(tempFile, bytes);

                    // Keep the request lock until the audio file is committed to the cache.
                    // Otherwise another caller can acquire it between synthesis and rename,
                    // miss the cache and issue a duplicate /tts request.
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
                    _requestLock.Release();
                }
            }
            catch (HttpRequestException ex)
            {
                _loadedWeightsSignature = string.Empty;
                try { await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                throw new InvalidOperationException("Cannot connect to the GPT-SoVITS API. Make sure its API service is running.", ex);
            }
            catch
            {
                try { await tempFile.DeleteAsync(StorageDeleteOption.PermanentDelete); } catch { }
                throw;
            }
        }

        public void InvalidateWeights() => _loadedWeightsSignature = string.Empty;

        private async Task EnsureWeightsAsync(GptSoVitsVoiceProfile profile, CancellationToken cancellationToken)
        {
            var signature = $"{ChatTtsConfig.GetGptSoVitsApiUrl()}|{profile.GptWeightsPath}|{profile.SoVitsWeightsPath}";
            if (string.Equals(signature, _loadedWeightsSignature, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(profile.GptWeightsPath))
            {
                await CallWeightEndpointAsync("/set_gpt_weights", "weights_path", profile.GptWeightsPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(profile.SoVitsWeightsPath))
            {
                await CallWeightEndpointAsync("/set_sovits_weights", "weights_path", profile.SoVitsWeightsPath, cancellationToken)
                    .ConfigureAwait(false);
            }

            _loadedWeightsSignature = signature;
        }

        private async Task CallWeightEndpointAsync(
            string endpoint,
            string parameter,
            string value,
            CancellationToken cancellationToken)
        {
            var uri = BuildUri($"{endpoint}?{parameter}={Uri.EscapeDataString(value)}");
            using var response = await _httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException(ParseApiError(bytes, response.ReasonPhrase));
        }

        private static string ParseApiError(byte[] bytes, string? reasonPhrase)
        {
            try
            {
                var text = Encoding.UTF8.GetString(bytes);
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;
                var message = root.TryGetProperty("message", out var messageElement)
                    ? messageElement.ToString()
                    : null;
                var exception = root.TryGetProperty("Exception", out var exceptionElement)
                    ? exceptionElement.ToString()
                    : null;
                if (!string.IsNullOrWhiteSpace(exception))
                {
                    return string.IsNullOrWhiteSpace(message) ? exception : $"{message}: {exception}";
                }
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message;
                }
            }
            catch
            {
                // Fall back to the HTTP status text below.
            }

            return string.IsNullOrWhiteSpace(reasonPhrase)
                ? "GPT-SoVITS request failed."
                : $"GPT-SoVITS request failed: {reasonPhrase}";
        }

        private static string FormatLanguage(string language) => language switch
        {
            "all_zh" => "中文",
            "en" => "English",
            "all_ja" => "日本語",
            "all_ko" => "한국어",
            "all_yue" => "粤语",
            "zh" => "中英混合",
            "ja" => "日英混合",
            "ko" => "韩英混合",
            "yue" => "粤英混合",
            "auto_yue" => "多语种 / 粤语",
            _ => "Auto"
        };

        private static Uri BuildUri(string relativePath)
        {
            var baseUrl = ChatTtsConfig.GetGptSoVitsApiUrl().TrimEnd('/');
            return new Uri(baseUrl + relativePath, UriKind.Absolute);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _httpClient.Dispose();
            _requestLock.Dispose();
        }
    }
}
