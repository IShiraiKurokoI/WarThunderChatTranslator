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
    /// Polls game chat independently from browser requests.
    /// Sends incremental requests to port 8111 at a fixed interval only while a War Thunder process is running.
    /// </summary>
    internal sealed partial class GameChatPollingService : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string[] GameProcessNames = ["aces", "aces-min-cpu"];
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ConcurrentDictionary<int, TranslationCacheEntry> _translationCache = new();
        private readonly ConcurrentDictionary<int, ChatMessage> _chatMessages = new();
        private readonly HttpClient _gameHttpClient;

        private int _currentGamePid;
        private long _currentGameStartTicks;
        private int _lastGameChatId;
        private long _processGeneration;
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

                    // Base the fixed interval on the start time of each cycle so slow requests never create overlapping polls.
                    // Reload the interval every cycle so changes take effect without restarting the application.
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
            return _chatMessages.Values
                .OrderBy(message => message.Id)
                .ToArray();
        }

        public IReadOnlyList<ChatMessage> GetMessagesAfter(int lastId)
        {
            return _chatMessages.Values
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
                Logger.Error(ex, "Unexpected error while polling game chat.");
            }
        }

        private async Task PollOnceAsync(CancellationToken cancellationToken)
        {
            var gameProcess = FindRunningGameProcess();
            if (gameProcess is null)
            {
                Interlocked.Exchange(ref _currentGamePid, 0);
                Interlocked.Exchange(ref _currentGameStartTicks, 0);

                if (_gameProcessWasVisible)
                {
                    Logger.Info("No aces.exe or aces-min-cpu.exe process detected. Game chat requests are paused.");
                    _gameProcessWasVisible = false;
                }
                return;
            }

            _gameProcessWasVisible = true;

            if (IsNewGameProcess(gameProcess.Value))
            {
                ResetForNewGameProcess(gameProcess.Value);
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

            // Revalidate the process after the HTTP request so a rapidly recycled PID cannot associate a new game's response with the previous process generation.
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

            if (!_gameEndpointHealthy)
            {
                _gameEndpointHealthy = true;
                Logger.Info("The game chat endpoint has recovered.");
            }

            if (incomingMessages.Count == 0)
            {
                return;
            }

            // Process only messages newer than the requested lastId so duplicate items returned by the game are not translated again.
            var incrementalMessages = incomingMessages
                .Where(message => message.Id > requestLastId)
                .GroupBy(message => message.Id)
                .Select(group => group.Last())
                .OrderBy(message => message.Id)
                .ToArray();

            if (incrementalMessages.Length == 0)
            {
                return;
            }

            await Task.WhenAll(incrementalMessages.Select(ProcessMessageAsync)).ConfigureAwait(false);

            foreach (var message in incrementalMessages)
            {
                _chatMessages[message.Id] = message;
            }

            // Track the highest ID received during the current game process lifetime and send it as lastId on the next poll.
            // A single process lifetime can contain multiple battles; PID/process generation is not a battle identifier.
            var maxId = incrementalMessages.Max(message => message.Id);
            if (maxId > requestLastId)
            {
                Interlocked.Exchange(ref _lastGameChatId, maxId);
            }
        }

        private async Task ProcessMessageAsync(ChatMessage message)
        {
            message.Msg = ColorTagRegex().Replace((message.Msg ?? string.Empty).Replace("\t", string.Empty), "$2");
            message.Mode = (message.Mode ?? string.Empty).Replace("\t", string.Empty);
            message.OriginalMessage = message.Msg;

            if (!_translationCache.TryGetValue(message.Id, out var cachedTranslation))
            {
                try
                {
                    var translationResult = await TranslationHelper.TranslateAsync(message.Msg).ConfigureAwait(false);
                    cachedTranslation = new TranslationCacheEntry(
                        translationResult.Translation,
                        translationResult.SourceLanguage?.ISO6391 ?? string.Empty);
                    _translationCache[message.Id] = cachedTranslation;
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
            _translationCache.Clear();
            _chatMessages.Clear();
            Interlocked.Exchange(ref _lastGameChatId, 0);
            Interlocked.Exchange(ref _currentGamePid, process.Pid);
            Interlocked.Exchange(ref _currentGameStartTicks, process.StartTimeUtc.Ticks);
            var processGeneration = Interlocked.Increment(ref _processGeneration);
            _gameEndpointHealthy = true;

            Logger.Info(
                $"Detected a new game process: {process.ProcessName}.exe, PID={process.Pid}, generation={processGeneration}. " +
                "Translation and chat caches were cleared, and lastId was reset to 0.");
        }

        private void LogEndpointFailureOnce(string reason)
        {
            if (_gameEndpointHealthy)
            {
                _gameEndpointHealthy = false;
                Logger.Warn($"The game process is running, but the port 8111 game chat endpoint is temporarily unavailable: {reason}");
            }
            else
            {
                Logger.Debug($"The game chat endpoint is still unavailable: {reason}");
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
            _gameHttpClient.Dispose();
        }

        [GeneratedRegex(@"<color(.*?)>(.*?)</color>", RegexOptions.CultureInvariant)]
        private static partial Regex ColorTagRegex();

        private readonly record struct GameProcessInfo(int Pid, string ProcessName, DateTime StartTimeUtc);

        private readonly record struct TranslationCacheEntry(string Translation, string SourceLanguageIsoCode);
    }
}
