using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    /// <summary>
    /// Polls War Thunder map state and game chat independently from browser requests.
    /// Chat polling is active only while /map_info.json reports valid == true.
    /// </summary>
    internal sealed partial class GameChatPollingService : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] GameProcessNames = ["aces", "aces-min-cpu"];
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true
        };
        private const int MapUnavailableEndConfirmationCount = 3;

        private readonly ConcurrentDictionary<TranslationCacheKey, TranslationCacheEntry> _translationCache = new();
        private readonly ConcurrentQueue<TranslationCacheKey> _translationCacheOrder = new();
        private readonly ConcurrentDictionary<int, ChatMessage> _chatMessages = new();
        private readonly ConcurrentDictionary<int, long> _chatMessageGenerations = new();
        private readonly ConcurrentQueue<VisibleChatCacheKey> _chatMessageOrder = new();
        private readonly ConcurrentDictionary<int, ChatMessage> _currentBattleMessages = new();
        private readonly HttpClient _gameHttpClient;

        private int _currentGamePid;
        private long _currentGameStartTicks;
        private int _lastGameChatId;
        private long _processGeneration;
        private long _battleGeneration;
        private long _displayGeneration;
        private long _translationGeneration;
        private int _logicalDisplayCutoffId;
        private bool _logicalDisplayFilterActive;
        private bool _mapStateKnown;
        private bool _battleRunning;
        private int _consecutiveMapUnavailablePolls;
        private bool _gameEndpointHealthy = true;
        private bool _gameProcessWasVisible;
        private bool _disposed;

        public GameChatPollingService()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(1),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 2
            };

            _gameHttpClient = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri("http://127.0.0.1:8111/"),
                Timeout = TimeSpan.FromSeconds(3)
            };

            ApplicationConfig.BattleChatClearModeChanged += OnBattleChatClearModeChanged;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var initialInterval = ApplicationConfig.GetGamePollingInterval();
            Logger.Info($"Game chat background polling started with an interval of {initialInterval.TotalSeconds:0} seconds.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var cycleStarted = Stopwatch.GetTimestamp();
                    await PollOnceSafelyAsync(cancellationToken).ConfigureAwait(false);

                    var pollInterval = ApplicationConfig.GetGamePollingInterval();
                    var elapsed = Stopwatch.GetElapsedTime(cycleStarted);
                    var remainingDelay = pollInterval - elapsed;

                    if (remainingDelay > TimeSpan.Zero)
                    {
                        await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            finally
            {
                Logger.Info("Game chat background polling stopped.");
            }
        }

        public IReadOnlyList<ChatMessage> GetCurrentMessages()
        {
            var logicalFilterActive = Volatile.Read(ref _logicalDisplayFilterActive);
            var logicalCutoffId = Volatile.Read(ref _logicalDisplayCutoffId);

            return _chatMessages.Values
                .Where(message => !logicalFilterActive || message.Id > logicalCutoffId)
                .OrderBy(message => message.Id)
                .ToArray();
        }

        public IReadOnlyList<ChatMessage> GetMessagesAfter(int lastId)
        {
            var logicalFilterActive = Volatile.Read(ref _logicalDisplayFilterActive);
            var logicalCutoffId = Volatile.Read(ref _logicalDisplayCutoffId);
            var effectiveLastId = logicalFilterActive ? Math.Max(lastId, logicalCutoffId) : lastId;

            return _chatMessages.Values
                .Where(message => message.Id > effectiveLastId)
                .OrderBy(message => message.Id)
                .ToArray();
        }

        public IReadOnlyList<ChatMessage> GetCurrentBattleMessages()
        {
            return _currentBattleMessages.Values
                .OrderBy(message => message.Id)
                .ToArray();
        }

        public IReadOnlyList<ChatMessage> GetCurrentBattleMessagesAfter(int lastId)
        {
            return _currentBattleMessages.Values
                .Where(message => message.Id > lastId)
                .OrderBy(message => message.Id)
                .ToArray();
        }

        public int CurrentGamePid => Volatile.Read(ref _currentGamePid);

        public DateTime? CurrentGameStartTimeUtc
        {
            get
            {
                var ticks = Interlocked.Read(ref _currentGameStartTicks);
                return ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;
            }
        }

        public int LastGameChatId => Volatile.Read(ref _lastGameChatId);

        public long ProcessGeneration => Interlocked.Read(ref _processGeneration);

        /// <summary>
        /// Changes when a new battle starts (and when the game process is replaced). Incremental
        /// clients use it to reset their War Thunder gamechat cursor without assuming chat IDs span battles.
        /// </summary>
        public long BattleGeneration => Interlocked.Read(ref _battleGeneration);

        /// <summary>
        /// Changes only when the visible cache is actually cleared. This is intentionally
        /// separate from BattleGeneration so disabling automatic cleanup can retain history.
        /// </summary>
        public long DisplayGeneration => Interlocked.Read(ref _displayGeneration);

        public bool IsBattleRunning => Volatile.Read(ref _battleRunning);

        private async Task PollOnceSafelyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                LogEndpointFailureOnce(ex.Message);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogEndpointFailureOnce($"Request timed out: {ex.Message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unexpected error while polling War Thunder port 8111.");
            }
        }

        private async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            var gameProcess = FindRunningGameProcess();
            if (gameProcess is null)
            {
                HandleGameProcessMissing();
                return;
            }

            _gameProcessWasVisible = true;

            if (IsNewGameProcess(gameProcess.Value))
            {
                ResetForNewGameProcess(gameProcess.Value);
            }

            bool mapValid;
            try
            {
                mapValid = await GetMapValidAsync(cancellationToken).ConfigureAwait(false);
                _consecutiveMapUnavailablePolls = 0;
                MarkEndpointHealthy();
            }
            catch (HttpRequestException ex)
            {
                HandleMapUnavailable(ex.Message);
                return;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                HandleMapUnavailable($"Request timed out: {ex.Message}");
                return;
            }

            HandleMapState(mapValid);
            EnforcePhysicalCacheLimit();

            if (!mapValid)
            {
                return;
            }

            var requestLastId = Volatile.Read(ref _lastGameChatId);
            var requestUri = $"gamechat?lastId={requestLastId}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = await _gameHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var incomingMessages = await JsonSerializer.DeserializeAsync<List<ChatMessage>>(
                responseStream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];

            // Revalidate the process after the HTTP request so a rapidly recycled PID cannot
            // associate a response with the wrong process instance.
            var verifiedProcess = FindRunningGameProcess();
            if (verifiedProcess is null || !IsSameProcessInstance(gameProcess.Value, verifiedProcess.Value))
            {
                if (verifiedProcess is { } replacementProcess && IsNewGameProcess(replacementProcess))
                {
                    ResetForNewGameProcess(replacementProcess);
                }

                Logger.Debug("Discarded a game chat response because the War Thunder process changed during the poll cycle.");
                return;
            }

            MarkEndpointHealthy();

            if (incomingMessages.Count == 0)
            {
                EnforcePhysicalCacheLimit();
                return;
            }

            var incrementalMessages = incomingMessages
                .Where(message => message.Id > requestLastId)
                .GroupBy(message => message.Id)
                .Select(group => group.Last())
                .OrderBy(message => message.Id)
                .ToArray();

            if (incrementalMessages.Length == 0)
            {
                EnforcePhysicalCacheLimit();
                return;
            }

            var translationGeneration = Interlocked.Read(ref _translationGeneration);
            await Task.WhenAll(incrementalMessages.Select(message => ProcessMessageAsync(message, translationGeneration))).ConfigureAwait(false);

            foreach (var message in incrementalMessages)
            {
                _currentBattleMessages[message.Id] = message;
                _chatMessages[message.Id] = message;
                _chatMessageGenerations[message.Id] = translationGeneration;
                _chatMessageOrder.Enqueue(new VisibleChatCacheKey(translationGeneration, message.Id));
            }

            var maxId = incrementalMessages.Max(message => message.Id);
            if (maxId > requestLastId)
            {
                Interlocked.Exchange(ref _lastGameChatId, maxId);
            }

            EnforcePhysicalCacheLimit();
        }

        private async Task<bool> GetMapValidAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "map_info.json");
            using var response = await _gameHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var mapInfo = await JsonSerializer.DeserializeAsync<MapInfoResponse>(
                responseStream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            // The 8111 tactical map exposes valid=false when no map is loaded. This works
            // across air and ground modes, unlike /state and /indicators which are vehicle-specific.
            return mapInfo?.Valid == true;
        }

        private void HandleMapUnavailable(string reason)
        {
            LogEndpointFailureOnce(reason);

            if (!_mapStateKnown || !Volatile.Read(ref _battleRunning))
            {
                return;
            }

            _consecutiveMapUnavailablePolls++;
            if (_consecutiveMapUnavailablePolls < MapUnavailableEndConfirmationCount)
            {
                return;
            }

            // Some game states make port 8111 temporarily unavailable between the battle and hangar.
            // Require several consecutive failures before treating that as an end-of-battle transition.
            _consecutiveMapUnavailablePolls = 0;
            Volatile.Write(ref _battleRunning, false);
            EndBattle();
            Logger.Info($"Confirmed battle end after {MapUnavailableEndConfirmationCount} consecutive unavailable /map_info.json polls.");
        }

        private void HandleMapState(bool valid)
        {
            if (!_mapStateKnown)
            {
                _mapStateKnown = true;
                Volatile.Write(ref _battleRunning, valid);

                if (valid)
                {
                    BeginBattle();
                }

                return;
            }

            var wasValid = Volatile.Read(ref _battleRunning);
            if (wasValid == valid)
            {
                return;
            }

            Volatile.Write(ref _battleRunning, valid);
            if (valid)
            {
                BeginBattle();
            }
            else
            {
                EndBattle();
            }
        }

        private void BeginBattle()
        {
            _currentBattleMessages.Clear();
            Interlocked.Exchange(ref _lastGameChatId, 0);
            var battleGeneration = Interlocked.Increment(ref _battleGeneration);
            var translationGeneration = Interlocked.Increment(ref _translationGeneration);
            Logger.Info(
                $"Detected battle start from /map_info.json. Battle generation={battleGeneration}, " +
                $"translation generation={translationGeneration}, lastId reset to 0.");
        }

        private void EndBattle()
        {
            var clearMode = ApplicationConfig.GetBattleChatClearMode();

            if (string.Equals(clearMode, ApplicationConfig.BattleChatClearModeLogical, StringComparison.Ordinal))
            {
                var logicalCutoffId = _chatMessages.IsEmpty ? 0 : _chatMessages.Keys.Max();
                Interlocked.Exchange(ref _logicalDisplayCutoffId, logicalCutoffId);
                Volatile.Write(ref _logicalDisplayFilterActive, true);
                var displayGeneration = Interlocked.Increment(ref _displayGeneration);

                Logger.Info(
                    $"Detected battle end from /map_info.json. Logical cleanup hid retained history through chat ID {logicalCutoffId}; " +
                    $"display generation={displayGeneration}.");
            }
            else if (string.Equals(clearMode, ApplicationConfig.BattleChatClearModePhysical, StringComparison.Ordinal))
            {
                ClearRetainedChatHistory();
                ClearTranslationCache();
                Interlocked.Exchange(ref _logicalDisplayCutoffId, 0);
                Volatile.Write(ref _logicalDisplayFilterActive, false);
                var displayGeneration = Interlocked.Increment(ref _displayGeneration);

                Logger.Info(
                    $"Detected battle end from /map_info.json. Physical chat cache cleanup completed; " +
                    $"display generation={displayGeneration}.");
            }
            else
            {
                Logger.Info("Detected battle end from /map_info.json. Battle-end chat cleanup is disabled; retained history remains visible.");
            }

            _currentBattleMessages.Clear();
            // War Thunder chat IDs normally continue increasing across matches, but the endpoint
            // only returns records from the current match. Starting the next match at lastId=0
            // therefore safely requests that match's available chat from the beginning.
            Interlocked.Exchange(ref _lastGameChatId, 0);
        }

        private void OnBattleChatClearModeChanged(string oldMode, string newMode)
        {
            if (string.Equals(newMode, ApplicationConfig.BattleChatClearModePhysical, StringComparison.Ordinal))
            {
                ApplyImmediatePhysicalCleanup();
                return;
            }

            if (string.Equals(newMode, ApplicationConfig.BattleChatClearModeNone, StringComparison.Ordinal))
            {
                // Logical cleanup is only a visibility boundary. Returning to No cleanup must
                // immediately reveal any retained history that is still within the physical limit.
                if (!Volatile.Read(ref _logicalDisplayFilterActive))
                {
                    return;
                }

                Volatile.Write(ref _logicalDisplayFilterActive, false);
                var displayGeneration = Interlocked.Increment(ref _displayGeneration);
                Logger.Info(
                    $"Battle chat cleanup mode changed from {oldMode} to {newMode}. " +
                    $"Retained history is visible again; display generation={displayGeneration}.");
                return;
            }

            if (string.Equals(newMode, ApplicationConfig.BattleChatClearModeLogical, StringComparison.Ordinal))
            {
                // Selecting Logical changes what happens at the next battle end. Do not hide
                // already-visible records merely because the setting itself was changed.
                Logger.Info(
                    $"Battle chat cleanup mode changed from {oldMode} to {newMode}. " +
                    "Logical cleanup will take effect at the next battle end.");
            }
        }

        private void ApplyImmediatePhysicalCleanup()
        {
            var battleRunning = Volatile.Read(ref _battleRunning);

            if (battleRunning)
            {
                // When Physical cleanup is selected during a battle, delete only records from
                // earlier battles. Current-battle chat remains visible and translated.
                var currentGeneration = Interlocked.Read(ref _translationGeneration);

                foreach (var entry in _chatMessageGenerations.ToArray())
                {
                    if (entry.Value == currentGeneration)
                    {
                        continue;
                    }

                    if (_chatMessageGenerations.TryRemove(entry.Key, out _))
                    {
                        _chatMessages.TryRemove(entry.Key, out _);
                    }
                }

                foreach (var cacheKey in _translationCache.Keys.ToArray())
                {
                    if (cacheKey.BattleGeneration != currentGeneration)
                    {
                        _translationCache.TryRemove(cacheKey, out _);
                    }
                }

                // Physical cleanup also removes stale ordering metadata rather than leaving old
                // queue nodes behind until a future cache-limit eviction pass.
                var retainedChatOrder = new List<VisibleChatCacheKey>();
                while (_chatMessageOrder.TryDequeue(out var chatOrderKey))
                {
                    if (chatOrderKey.BattleGeneration == currentGeneration
                        && _chatMessageGenerations.ContainsKey(chatOrderKey.MessageId))
                    {
                        retainedChatOrder.Add(chatOrderKey);
                    }
                }

                foreach (var chatOrderKey in retainedChatOrder)
                {
                    _chatMessageOrder.Enqueue(chatOrderKey);
                }

                var retainedTranslationOrder = new List<TranslationCacheKey>();
                while (_translationCacheOrder.TryDequeue(out var translationOrderKey))
                {
                    if (translationOrderKey.BattleGeneration == currentGeneration
                        && _translationCache.ContainsKey(translationOrderKey))
                    {
                        retainedTranslationOrder.Add(translationOrderKey);
                    }
                }

                foreach (var translationOrderKey in retainedTranslationOrder)
                {
                    _translationCacheOrder.Enqueue(translationOrderKey);
                }
            }
            else
            {
                // Outside a battle there is no current session to preserve.
                ClearRetainedChatHistory();
                ClearTranslationCache();
                _currentBattleMessages.Clear();
            }

            Interlocked.Exchange(ref _logicalDisplayCutoffId, 0);
            Volatile.Write(ref _logicalDisplayFilterActive, false);
            var displayGeneration = Interlocked.Increment(ref _displayGeneration);

            Logger.Info(
                $"Physical chat cleanup selected. Immediate cleanup completed; " +
                $"current battle preserved={battleRunning}, display generation={displayGeneration}.");
        }

        private async Task ProcessMessageAsync(ChatMessage message, long translationGeneration)
        {
            message.Msg = ColorTagRegex().Replace((message.Msg ?? string.Empty).Replace("\t", string.Empty), "$2");
            message.Mode = (message.Mode ?? string.Empty).Replace("\t", string.Empty);
            message.OriginalMessage = message.Msg;

            var cacheKey = new TranslationCacheKey(translationGeneration, message.Id);
            if (!_translationCache.TryGetValue(cacheKey, out var cachedTranslation))
            {
                try
                {
                    var translationResult = await TranslationHelper.TranslateAsync(message.Msg).ConfigureAwait(false);
                    cachedTranslation = new TranslationCacheEntry(
                        translationResult.Translation,
                        translationResult.SourceLanguage?.ISO6391 ?? string.Empty);

                    if (_translationCache.TryAdd(cacheKey, cachedTranslation))
                    {
                        _translationCacheOrder.Enqueue(cacheKey);
                    }
                    else if (_translationCache.TryGetValue(cacheKey, out var existing))
                    {
                        cachedTranslation = existing;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Failed to translate chat message. ID={message.Id}.");
                    cachedTranslation = new TranslationCacheEntry("(翻译失败) " + message.Msg, string.Empty);
                }
            }

            message.TranslatedMessage = cachedTranslation.Translation;
            message.SourceLanguageIsoCode = cachedTranslation.SourceLanguageIsoCode;
            message.PrettyMessage = $"{message.Sender}: {cachedTranslation.Translation}";
        }

        private void EnforcePhysicalCacheLimit()
        {
            var limit = ApplicationConfig.GetPhysicalChatCacheLimit();

            while (_translationCache.Count > limit && _translationCacheOrder.TryDequeue(out var cacheKey))
            {
                _translationCache.TryRemove(cacheKey, out _);
            }

            if (_currentBattleMessages.Count > limit)
            {
                var currentIdsToRemove = _currentBattleMessages.Keys
                    .OrderBy(id => id)
                    .Take(Math.Max(0, _currentBattleMessages.Count - limit))
                    .ToArray();

                foreach (var id in currentIdsToRemove)
                {
                    _currentBattleMessages.TryRemove(id, out _);
                }
            }

            while (_chatMessages.Count > limit && _chatMessageOrder.TryDequeue(out var visibleKey))
            {
                if (_chatMessageGenerations.TryGetValue(visibleKey.MessageId, out var generation)
                    && generation == visibleKey.BattleGeneration)
                {
                    _chatMessageGenerations.TryRemove(visibleKey.MessageId, out _);
                    _chatMessages.TryRemove(visibleKey.MessageId, out _);
                }
            }
        }

        private void ClearRetainedChatHistory()
        {
            _chatMessages.Clear();
            _chatMessageGenerations.Clear();
            while (_chatMessageOrder.TryDequeue(out _))
            {
            }
        }

        private void ClearTranslationCache()
        {
            _translationCache.Clear();
            while (_translationCacheOrder.TryDequeue(out _))
            {
            }
        }

        private void HandleGameProcessMissing()
        {
            Interlocked.Exchange(ref _currentGamePid, 0);
            Interlocked.Exchange(ref _currentGameStartTicks, 0);

            if (_gameProcessWasVisible)
            {
                if (Volatile.Read(ref _battleRunning))
                {
                    Volatile.Write(ref _battleRunning, false);
                    EndBattle();
                }

                _mapStateKnown = false;
                _consecutiveMapUnavailablePolls = 0;
                Logger.Info("No aces.exe or aces-min-cpu.exe process detected. Port 8111 polling is paused.");
                _gameProcessWasVisible = false;
            }
        }

        private bool IsNewGameProcess(GameProcessInfo process)
        {
            return Volatile.Read(ref _currentGamePid) != process.Pid
                || Interlocked.Read(ref _currentGameStartTicks) != process.StartTimeUtc.Ticks;
        }

        private static bool IsSameProcessInstance(GameProcessInfo left, GameProcessInfo right)
        {
            return left.Pid == right.Pid
                && left.StartTimeUtc.Ticks == right.StartTimeUtc.Ticks
                && string.Equals(left.ProcessName, right.ProcessName, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetForNewGameProcess(GameProcessInfo process)
        {
            ClearTranslationCache();
            ClearRetainedChatHistory();
            _currentBattleMessages.Clear();
            Interlocked.Exchange(ref _lastGameChatId, 0);
            Interlocked.Exchange(ref _currentGamePid, process.Pid);
            Interlocked.Exchange(ref _currentGameStartTicks, process.StartTimeUtc.Ticks);
            var processGeneration = Interlocked.Increment(ref _processGeneration);
            var battleGeneration = Interlocked.Increment(ref _battleGeneration);
            var displayGeneration = Interlocked.Increment(ref _displayGeneration);
            Interlocked.Exchange(ref _translationGeneration, 0);
            Interlocked.Exchange(ref _logicalDisplayCutoffId, 0);
            Volatile.Write(ref _logicalDisplayFilterActive, false);
            _mapStateKnown = false;
            _consecutiveMapUnavailablePolls = 0;
            Volatile.Write(ref _battleRunning, false);
            _gameEndpointHealthy = true;

            Logger.Info(
                $"Detected a new game process: {process.ProcessName}.exe, PID={process.Pid}, process generation={processGeneration}, " +
                $"battle generation={battleGeneration}, display generation={displayGeneration}. All in-memory chat caches were reset.");
        }

        private void MarkEndpointHealthy()
        {
            if (!_gameEndpointHealthy)
            {
                _gameEndpointHealthy = true;
                Logger.Info("War Thunder port 8111 endpoints have recovered.");
            }
        }

        private void LogEndpointFailureOnce(string reason)
        {
            if (_gameEndpointHealthy)
            {
                _gameEndpointHealthy = false;
                Logger.Warn($"The game process is running, but a War Thunder port 8111 endpoint is temporarily unavailable: {reason}");
            }
            else
            {
                Logger.Debug($"War Thunder port 8111 is still unavailable: {reason}");
            }
        }

        private static GameProcessInfo? FindRunningGameProcess()
        {
            GameProcessInfo? newestProcess = null;

            foreach (var processName in GameProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.HasExited)
                        {
                            continue;
                        }

                        var info = new GameProcessInfo(
                            process.Id,
                            process.ProcessName,
                            process.StartTime.ToUniversalTime());

                        if (newestProcess is null
                            || info.StartTimeUtc > newestProcess.Value.StartTimeUtc
                            || (info.StartTimeUtc == newestProcess.Value.StartTimeUtc && info.Pid > newestProcess.Value.Pid))
                        {
                            newestProcess = info;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // The process may exit after enumeration; ignore it and continue.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // If the start time cannot be read, ignore this process and retry on the next poll.
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            return newestProcess;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ApplicationConfig.BattleChatClearModeChanged -= OnBattleChatClearModeChanged;
            _gameHttpClient.Dispose();
        }

        [GeneratedRegex(@"<color(.*?)>(.*?)</color>", RegexOptions.CultureInvariant)]
        private static partial Regex ColorTagRegex();

        private readonly record struct GameProcessInfo(int Pid, string ProcessName, DateTime StartTimeUtc);

        private readonly record struct TranslationCacheKey(long BattleGeneration, int MessageId);

        private readonly record struct VisibleChatCacheKey(long BattleGeneration, int MessageId);

        private readonly record struct TranslationCacheEntry(string Translation, string SourceLanguageIsoCode);

        private sealed class MapInfoResponse
        {
            public bool Valid { get; set; }
        }
    }
}
