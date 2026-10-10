#nullable enable

using NLog;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using Windows.Storage;

namespace WarThunderChatTranslator.Services
{
    internal enum SherpaTtsDownloadStatus
    {
        Idle,
        Downloading,
        Installing,
        Completed,
        Cancelled,
        Failed
    }

    internal sealed record SherpaTtsDownloadState(
        SherpaTtsDownloadStatus Status,
        SherpaModelDownloadProgress? Progress = null,
        string? ErrorMessage = null)
    {
        public bool IsBusy => Status is SherpaTtsDownloadStatus.Downloading or SherpaTtsDownloadStatus.Installing;
    }

    internal sealed class SherpaTtsModelManager
    {
        public const string RecommendedModelId = "kokoro-int8-multi-lang-v1_1";
        public const string RecommendedModelDisplayName = "Kokoro v1.1 · 103 voices";
        public const string RecommendedModelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/kokoro-int8-multi-lang-v1_1.tar.bz2";

        private static readonly string[] KokoroV11Speakers =
        [
            "af_maple", "af_sol", "bf_vale",
            "zf_001", "zf_002", "zf_003", "zf_004", "zf_005", "zf_006", "zf_007", "zf_008",
            "zf_017", "zf_018", "zf_019", "zf_021", "zf_022", "zf_023", "zf_024", "zf_026", "zf_027",
            "zf_028", "zf_032", "zf_036", "zf_038", "zf_039", "zf_040", "zf_042", "zf_043", "zf_044",
            "zf_046", "zf_047", "zf_048", "zf_049", "zf_051", "zf_059", "zf_060", "zf_067", "zf_070",
            "zf_071", "zf_072", "zf_073", "zf_074", "zf_075", "zf_076", "zf_077", "zf_078", "zf_079",
            "zf_083", "zf_084", "zf_085", "zf_086", "zf_087", "zf_088", "zf_090", "zf_092", "zf_093",
            "zf_094", "zf_099",
            "zm_009", "zm_010", "zm_011", "zm_012", "zm_013", "zm_014", "zm_015", "zm_016", "zm_020",
            "zm_025", "zm_029", "zm_030", "zm_031", "zm_033", "zm_034", "zm_035", "zm_037", "zm_041",
            "zm_045", "zm_050", "zm_052", "zm_053", "zm_054", "zm_055", "zm_056", "zm_057", "zm_058",
            "zm_061", "zm_062", "zm_063", "zm_064", "zm_065", "zm_066", "zm_068", "zm_069", "zm_080",
            "zm_081", "zm_082", "zm_089", "zm_091", "zm_095", "zm_096", "zm_097", "zm_098", "zm_100"
        ];

        private static readonly string[] KokoroV10Speakers =
        [
            "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica", "af_kore", "af_nicole", "af_nova",
            "af_river", "af_sarah", "af_sky", "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam",
            "am_michael", "am_onyx", "am_puck", "am_santa", "bf_alice", "bf_emma", "bf_isabella", "bf_lily",
            "bm_daniel", "bm_fable", "bm_george", "bm_lewis", "ef_dora", "em_alex", "ff_siwis", "hf_alpha",
            "hf_beta", "hm_omega", "hm_psi", "if_sara", "im_nicola", "jf_alpha", "jf_gongitsune", "jf_nezumi",
            "jf_tebukuro", "jm_kumo", "pf_dora", "pm_alex", "pm_santa", "zf_xiaobei", "zf_xiaoni", "zf_xiaoxiao",
            "zf_xiaoyi", "zm_yunjian", "zm_yunxi", "zm_yunxia", "zm_yunyang"
        ];

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly SemaphoreSlim _installLock = new(1, 1);
        private readonly object _downloadStateLock = new();
        private CancellationTokenSource? _downloadCancellation;
        private Task? _activeDownloadTask;
        private SherpaTtsDownloadState _downloadState = new(SherpaTtsDownloadStatus.Idle);
        private DateTime _lastDownloadUiUpdateUtc = DateTime.MinValue;
        private int _lastReportedPercent = -1;

        public static SherpaTtsModelManager Shared { get; } = new();

        public event Action<SherpaTtsDownloadState>? DownloadStateChanged;

        public SherpaTtsDownloadState DownloadState
        {
            get
            {
                lock (_downloadStateLock)
                {
                    return _downloadState;
                }
            }
        }

        public void StartRecommendedModelDownload()
        {
            lock (_downloadStateLock)
            {
                if (_activeDownloadTask is { IsCompleted: false })
                {
                    return;
                }

                _downloadCancellation?.Dispose();
                _downloadCancellation = new CancellationTokenSource();
                _lastDownloadUiUpdateUtc = DateTime.MinValue;
                _lastReportedPercent = -1;
                var cancellationToken = _downloadCancellation.Token;
                _activeDownloadTask = RunManagedRecommendedModelDownloadAsync(cancellationToken);
            }
        }

        public void CancelRecommendedModelDownload()
        {
            lock (_downloadStateLock)
            {
                _downloadCancellation?.Cancel();
            }
        }

        public static string RootDirectory
        {
            get
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var root = string.IsNullOrWhiteSpace(documents)
                    ? ApplicationData.Current.LocalFolder.Path
                    : Path.Combine(documents, "WTChatTranslator");
                var path = Path.Combine(root, "Models", "TTS");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public IReadOnlyList<SherpaTtsModelInfo> GetInstalledModels()
        {
            try
            {
                Directory.CreateDirectory(RootDirectory);
                return Directory.EnumerateDirectories(RootDirectory)
                    .Select(TryCreateModelInfo)
                    .Where(model => model is not null)
                    .Cast<SherpaTtsModelInfo>()
                    .OrderByDescending(model => model.IsRecommended)
                    .ThenBy(model => model.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not enumerate local Sherpa-ONNX TTS models.");
                return [];
            }
        }

        public SherpaTtsModelInfo? GetModel(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            return GetInstalledModels().FirstOrDefault(model =>
                string.Equals(model.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsRecommendedModelInstalled() => GetModel(RecommendedModelId) is not null;

        private async Task<SherpaTtsModelInfo> DownloadRecommendedModelCoreAsync(
            IProgress<SherpaModelDownloadProgress>? progress,
            Action? installing,
            CancellationToken cancellationToken)
        {
            await _installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = GetModel(RecommendedModelId);
                if (existing is not null)
                {
                    return existing;
                }

                Directory.CreateDirectory(RootDirectory);
                var archivePath = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, $"{RecommendedModelId}-{Guid.NewGuid():N}.tar.bz2");
                var stagingRoot = Path.Combine(RootDirectory, $".install-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingRoot);

                try
                {
                    using var httpClient = CreateDownloadHttpClient();
                    using (var response = await httpClient.GetAsync(
                               RecommendedModelUrl,
                               HttpCompletionOption.ResponseHeadersRead,
                               cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        var totalBytes = response.Content.Headers.ContentLength;
                        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                        await using var destination = new FileStream(
                            archivePath,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            bufferSize: 1024 * 128,
                            useAsync: true);

                        var buffer = new byte[1024 * 128];
                        long received = 0;
                        while (true)
                        {
                            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                            if (read <= 0)
                            {
                                break;
                            }

                            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                            received += read;
                            progress?.Report(new SherpaModelDownloadProgress(received, totalBytes));
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    installing?.Invoke();
                    await Task.Run(() => ExtractArchive(archivePath, stagingRoot, cancellationToken), cancellationToken).ConfigureAwait(false);

                    var extracted = Directory.EnumerateDirectories(stagingRoot)
                        .FirstOrDefault(path => string.Equals(Path.GetFileName(path), RecommendedModelId, StringComparison.OrdinalIgnoreCase))
                        ?? Directory.EnumerateDirectories(stagingRoot).FirstOrDefault()
                        ?? stagingRoot;

                    _ = TryCreateModelInfo(extracted)
                        ?? throw new InvalidDataException("The downloaded Kokoro package is incomplete or has an unsupported layout.");

                    var finalPath = Path.Combine(RootDirectory, RecommendedModelId);
                    if (Directory.Exists(finalPath))
                    {
                        Directory.Delete(finalPath, recursive: true);
                    }

                    if (string.Equals(extracted, stagingRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        Directory.Move(stagingRoot, finalPath);
                    }
                    else
                    {
                        Directory.Move(extracted, finalPath);
                        TryDeleteDirectory(stagingRoot);
                    }

                    return TryCreateModelInfo(finalPath)
                        ?? throw new InvalidDataException("The installed Kokoro package could not be validated.");
                }
                finally
                {
                    TryDeleteFile(archivePath);
                    TryDeleteDirectory(stagingRoot);
                }
            }
            finally
            {
                _installLock.Release();
            }
        }

        private async Task RunManagedRecommendedModelDownloadAsync(CancellationToken cancellationToken)
        {
            try
            {
                SetDownloadState(new SherpaTtsDownloadState(SherpaTtsDownloadStatus.Downloading));
                ApplicationNotifications.ShowTtsModelDownloadProgress(null, installing: false);

                var progress = new CallbackProgress<SherpaModelDownloadProgress>(ReportManagedDownloadProgress);
                var model = await DownloadRecommendedModelCoreAsync(
                    progress,
                    () =>
                    {
                        SetDownloadState(new SherpaTtsDownloadState(
                            SherpaTtsDownloadStatus.Installing,
                            DownloadState.Progress));
                        ApplicationNotifications.ShowTtsModelDownloadProgress(DownloadState.Progress, installing: true);
                    },
                    cancellationToken).ConfigureAwait(false);

                ApplicationConfig.SaveSettings(ChatTtsConfig.SherpaModelIdKey, model.Id);
                SetDownloadState(new SherpaTtsDownloadState(SherpaTtsDownloadStatus.Completed));
                ApplicationNotifications.CompleteTtsModelDownload();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetDownloadState(new SherpaTtsDownloadState(SherpaTtsDownloadStatus.Cancelled));
                ApplicationNotifications.CancelledTtsModelDownload();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Recommended local TTS model download failed.");
                SetDownloadState(new SherpaTtsDownloadState(
                    SherpaTtsDownloadStatus.Failed,
                    ErrorMessage: ex.Message));
                ApplicationNotifications.FailedTtsModelDownload();
            }
            finally
            {
                lock (_downloadStateLock)
                {
                    _activeDownloadTask = null;
                    _downloadCancellation?.Dispose();
                    _downloadCancellation = null;
                }
            }
        }

        private void ReportManagedDownloadProgress(SherpaModelDownloadProgress progress)
        {
            var percent = progress.Percent is double value ? (int)Math.Floor(value) : -1;
            var now = DateTime.UtcNow;
            if (percent == _lastReportedPercent && now - _lastDownloadUiUpdateUtc < TimeSpan.FromMilliseconds(300))
            {
                return;
            }

            _lastReportedPercent = percent;
            _lastDownloadUiUpdateUtc = now;
            SetDownloadState(new SherpaTtsDownloadState(SherpaTtsDownloadStatus.Downloading, progress));
            ApplicationNotifications.ShowTtsModelDownloadProgress(progress, installing: false);
        }

        private void SetDownloadState(SherpaTtsDownloadState state)
        {
            lock (_downloadStateLock)
            {
                _downloadState = state;
            }

            var handlers = DownloadStateChanged?.GetInvocationList();
            if (handlers is null)
            {
                return;
            }

            foreach (var handler in handlers.OfType<Action<SherpaTtsDownloadState>>())
            {
                try
                {
                    handler(state);
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "A local TTS download-state listener failed.");
                }
            }
        }

        private sealed class CallbackProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public CallbackProgress(Action<T> callback)
            {
                _callback = callback;
            }

            public void Report(T value) => _callback(value);
        }

        public async Task<SherpaTtsModelInfo> ImportModelAsync(
            string sourceDirectory,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException("The selected TTS model directory does not exist.");
            }

            var sourceInfo = TryCreateModelInfo(sourceDirectory)
                ?? throw new InvalidDataException("The selected folder is not a supported Kokoro model. Required: model.onnx/model.int8.onnx, voices.bin, tokens.txt and espeak-ng-data.");

            var sourceFullPath = Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var managedRoot = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (sourceFullPath.StartsWith(managedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                // The folder is already managed; do not create a duplicate copy of it.
                return sourceInfo;
            }

            if (managedRoot.StartsWith(sourceFullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected model folder contains the managed TTS model directory and cannot be imported safely.");
            }

            await _installLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var safeBaseName = SanitizeDirectoryName(Path.GetFileName(sourceDirectory));
                if (string.IsNullOrWhiteSpace(safeBaseName))
                {
                    safeBaseName = "kokoro-local";
                }

                var destination = Path.Combine(RootDirectory, safeBaseName);
                if (Directory.Exists(destination))
                {
                    var suffix = Guid.NewGuid().ToString("N")[..8];
                    destination = Path.Combine(
                        RootDirectory,
                        $"{safeBaseName}-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{suffix}");
                }

                try
                {
                    await Task.Run(() => CopyDirectory(sourceDirectory, destination, cancellationToken), cancellationToken).ConfigureAwait(false);
                    return TryCreateModelInfo(destination)
                        ?? throw new InvalidDataException("The imported model could not be validated after copying.");
                }
                catch
                {
                    TryDeleteDirectory(destination);
                    throw;
                }
            }
            finally
            {
                _installLock.Release();
            }
        }

        public void DeleteModel(string modelId)
        {
            var model = GetModel(modelId);
            if (model is null)
            {
                return;
            }
            var root = Path.GetFullPath(RootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(model.DirectoryPath);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to delete a model outside the managed TTS model directory.");
            }

            Directory.Delete(target, recursive: true);
        }

        private SherpaTtsModelInfo? TryCreateModelInfo(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    return null;
                }

                var modelFile = new[] { "model.int8.onnx", "model.onnx" }
                    .Select(name => Path.Combine(directory, name))
                    .FirstOrDefault(File.Exists);
                var voices = Path.Combine(directory, "voices.bin");
                var tokens = Path.Combine(directory, "tokens.txt");
                var dataDir = Path.Combine(directory, "espeak-ng-data");
                if (modelFile is null || !File.Exists(voices) || !File.Exists(tokens) || !Directory.Exists(dataDir))
                {
                    return null;
                }

                var id = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var isV11 = id.Contains("multi-lang-v1_1", StringComparison.OrdinalIgnoreCase);
                var isV10 = id.Contains("multi-lang-v1_0", StringComparison.OrdinalIgnoreCase);
                var speakerNames = isV11
                    ? KokoroV11Speakers
                    : isV10
                        ? KokoroV10Speakers
                        : Array.Empty<string>();

                var lexicons = new[] { "lexicon-us-en.txt", "lexicon-zh.txt" }
                    .Select(name => Path.Combine(directory, name))
                    .Where(File.Exists)
                    .ToArray();
                var ruleFsts = new[] { "phone-zh.fst", "date-zh.fst", "number-zh.fst" }
                    .Select(name => Path.Combine(directory, name))
                    .Where(File.Exists)
                    .ToArray();

                var displayName = string.Equals(id, RecommendedModelId, StringComparison.OrdinalIgnoreCase)
                    ? RecommendedModelDisplayName
                    : isV11
                        ? $"Kokoro v1.1 · {id}"
                        : isV10
                            ? $"Kokoro v1.0 · {id}"
                            : $"Kokoro · {id}";

                return new SherpaTtsModelInfo(
                    id,
                    displayName,
                    directory,
                    modelFile,
                    voices,
                    tokens,
                    dataDir,
                    string.Join(',', lexicons),
                    string.Join(',', ruleFsts),
                    speakerNames,
                    string.Equals(id, RecommendedModelId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not inspect Sherpa-ONNX TTS model folder {0}.", directory);
                return null;
            }
        }

        private HttpClient CreateDownloadHttpClient()
        {
            var mode = ApplicationConfig.GetSettings(ApplicationConfig.NetworkProxyModeKey);
            var handler = new HttpClientHandler();

            if (string.Equals(mode, "System", StringComparison.Ordinal))
            {
                handler.UseProxy = true;
            }
            else if (string.Equals(mode, "Custom", StringComparison.Ordinal))
            {
                try
                {
                    var proxyAddress = ApplicationConfig.GetSettings(ApplicationConfig.ProxyAddressKey);
                    if (string.IsNullOrWhiteSpace(proxyAddress))
                    {
                        throw new InvalidOperationException("The custom proxy address is empty.");
                    }

                    var proxy = new WebProxy(proxyAddress);
                    var account = ApplicationConfig.GetSettings(ApplicationConfig.ProxyAccountKey);
                    var password = ApplicationConfig.GetSettings(ApplicationConfig.ProxyPasswordKey);
                    if (!string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(password))
                    {
                        proxy.Credentials = new NetworkCredential(account, password);
                    }

                    handler.Proxy = proxy;
                    handler.UseProxy = true;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "The custom proxy is invalid for the TTS model download; falling back to the system proxy.");
                    handler.Proxy = null;
                    handler.UseProxy = true;
                }
            }
            else
            {
                handler.Proxy = null;
                handler.UseProxy = false;
            }

            return new HttpClient(handler, disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
        }

        private static void ExtractArchive(string archivePath, string destinationDirectory, CancellationToken cancellationToken)
        {
            // The official Kokoro package is a TAR archive wrapped in BZip2 (.tar.bz2).
            // SharpCompress 0.50+ intentionally does not unwrap compressed TAR files via
            // ArchiveFactory. ReaderFactory/TarReader is the supported API for Tar.BZip2.
            using var reader = ReaderFactory.OpenReader(archivePath);
            var destinationRoot = Path.GetFullPath(destinationDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var buffer = new byte[1024 * 128];

            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var key = reader.Entry.Key ?? string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                key = key.Replace('/', Path.DirectorySeparatorChar);
                key = key.Replace('\\', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(destinationDirectory, key));
                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The TTS model archive contains an invalid path.");
                }

                if (reader.Entry.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var entryStream = reader.OpenEntryStream();
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = entryStream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    output.Write(buffer, 0, read);
                }
            }
        }

        private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: false);
            }
        }

        private static string SanitizeDirectoryName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
        }
    }
}
