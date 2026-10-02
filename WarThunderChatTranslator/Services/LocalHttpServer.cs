#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    /// <summary>
    /// Lightweight local HTTP server based on .NET 10 and ASP.NET Core Kestrel Minimal API.
    /// Browser clients read translated results from memory and no longer trigger requests to the game on port 8111.
    /// </summary>
    internal sealed class LocalHttpServer : IAsyncDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

        private readonly GameChatPollingService _gameChatService;
        private readonly string _listenUrl;
        private WebApplication? _webApplication;

        public LocalHttpServer(GameChatPollingService gameChatService, bool listenOnLan)
        {
            _gameChatService = gameChatService;
            _listenUrl = listenOnLan ? "http://0.0.0.0:8100" : "http://localhost:8100";
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_webApplication is not null)
            {
                return;
            }

            var options = new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(LocalHttpServer).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            };

            // CreateSlimBuilder enables only the ASP.NET Core features needed by the Minimal API, reducing host overhead.
            var builder = WebApplication.CreateSlimBuilder(options);
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(_listenUrl);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            });

            var app = builder.Build();

            app.MapGet("/gamechat", (HttpContext context, int? lastId, long? session, long? display) =>
            {
                var currentBattleGeneration = _gameChatService.BattleGeneration;
                var currentDisplayGeneration = _gameChatService.DisplayGeneration;
                var currentProcessGeneration = _gameChatService.ProcessGeneration;
                var currentPid = _gameChatService.CurrentGamePid;
                var currentStartTimeUtc = _gameChatService.CurrentGameStartTimeUtc;
                var sameBattleGeneration = session.HasValue && session.Value == currentBattleGeneration;
                var sameDisplayGeneration = display.HasValue && display.Value == currentDisplayGeneration;

                // X-Game-Session is the battle cursor generation used for incremental reads.
                // X-Game-Display-Generation changes whenever the visible history snapshot must be rebuilt.
                context.Response.Headers["X-Game-Session"] = currentBattleGeneration.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Battle-Generation"] = currentBattleGeneration.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Display-Generation"] = currentDisplayGeneration.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Process-Generation"] = currentProcessGeneration.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Battle-Running"] = _gameChatService.IsBattleRunning ? "true" : "false";
                context.Response.Headers["X-Game-Pid"] = currentPid.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Start-Time"] = currentStartTimeUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;
                context.Response.Headers["X-Game-Last-Id"] = _gameChatService.LastGameChatId.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Web-Poll-Interval-Ms"] = ApplicationConfig.GetWebPollingIntervalMilliseconds().ToString(CultureInfo.InvariantCulture);
                context.Response.Headers["X-Dashboard-Style-Version"] = ApplicationConfig.GetDashboardStyleVersion().ToString(CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";

                // A display-generation change means the client's visible history policy changed
                // (for example Logical -> No cleanup). Return a full visible snapshot so the
                // dashboard can rebuild and restore retained history immediately.
                var messages = !session.HasValue || !sameDisplayGeneration
                    ? _gameChatService.GetCurrentMessages()
                    : sameBattleGeneration
                        ? _gameChatService.GetCurrentBattleMessagesAfter(lastId.GetValueOrDefault())
                        : _gameChatService.GetCurrentBattleMessages();

                return Results.Json(messages, JsonOptions);
            });

            app.MapGet("/styles.css", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "dashboard.css");
                return File.Exists(filePath)
                    ? (IResult)Results.File(filePath, "text/css; charset=utf-8")
                    : Results.NotFound("404 Not Found - File is missing.");
            });

            app.MapGet("/user-styles.css", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var style = GetDashboardStyleSnapshot();
                return Results.Text(
                    BuildUserCss(style),
                    "text/css; charset=utf-8",
                    Encoding.UTF8);
            });

            app.MapGet("/api/dashboard-style", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                context.Response.Headers["Pragma"] = "no-cache";
                context.Response.Headers["Expires"] = "0";

                var style = GetDashboardStyleSnapshot(out var styleVersion);
                return Results.Json(new
                {
                    version = styleVersion,
                    fontFamily = style.FontFamily,
                    fontFamilyCss = CssString(style.FontFamily),
                    fontSize = style.FontSize,
                    fontWeight = style.FontWeight,
                    allyColor = style.AllyColor,
                    enemyColor = style.EnemyColor,
                    systemColor = style.SystemColor,
                    backgroundCss = style.BackgroundCss
                }, JsonOptions);
            });

            app.MapGet("/api/dashboard-ui", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(BuildDashboardLocalization(), JsonOptions);
            });

            app.MapGet("/dashboard", (HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var filePath = Path.Combine(AppContext.BaseDirectory, "Assets", "dashboard.html");
                return File.Exists(filePath)
                    ? (IResult)Results.File(filePath, "text/html; charset=utf-8")
                    : Results.NotFound("404 Not Found - File is missing.");
            });

            app.MapGet("/favicon.ico", () =>
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "favicon.ico");
                return File.Exists(filePath)
                    ? (IResult)Results.File(filePath, "image/x-icon")
                    : Results.NotFound("404 Not Found - File is missing.");
            });

            app.MapGet("/", () => Results.Redirect("/dashboard"));
            app.MapFallback(() => Results.NotFound("404 Not Found"));

            _webApplication = app;
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            Logger.Info($"Kestrel HTTP server started and is listening on {_listenUrl}.");
        }

        private static object BuildDashboardLocalization()
        {
            return new
            {
                language = Localization.CurrentLanguage,
                strings = new
                {
                    pageTitle = Localization.GetString("DashboardTitle"),
                    subtitle = Localization.GetString("DashboardSubtitle"),
                    displayMode = Localization.GetString("DashboardDisplayMode"),
                    displayTranslation = Localization.GetString("DashboardDisplayTranslation"),
                    displayOriginal = Localization.GetString("DashboardDisplayOriginal"),
                    displayTranslationAndOriginal = Localization.GetString("DashboardDisplayTranslationAndOriginal"),
                    showSourceLanguage = Localization.GetString("DashboardShowSourceLanguage"),
                    nightMode = Localization.GetString("DashboardNightMode"),
                    showChatBubbles = Localization.GetString("DashboardShowChatBubbles"),
                    systemSender = Localization.GetString("DashboardSystemSender"),
                    testAllySender = Localization.GetString("DashboardTestAllySender"),
                    testEnemySender = Localization.GetString("DashboardTestEnemySender"),
                    testAllyOriginal = Localization.GetString("DashboardTestAllyOriginal"),
                    testAllyTranslation = Localization.GetString("DashboardTestAllyTranslation"),
                    testEnemyOriginal = Localization.GetString("DashboardTestEnemyOriginal"),
                    testEnemyTranslation = Localization.GetString("DashboardTestEnemyTranslation"),
                    testSystemOriginal = Localization.GetString("DashboardTestSystemOriginal"),
                    testSystemTranslation = Localization.GetString("DashboardTestSystemTranslation"),
                    emptyState = Localization.GetString("DashboardEmptyState"),
                    controlsAriaLabel = Localization.GetString("DashboardControlsAriaLabel"),
                    chatAriaLabel = Localization.GetString("DashboardChatAriaLabel"),
                    gameRunning = Localization.GetString("DashboardGameRunning"),
                    gameStopped = Localization.GetString("DashboardGameStopped"),
                    gamePid = Localization.GetString("DashboardGamePid"),
                    gameStartTime = Localization.GetString("DashboardGameStartTime")
                }
            };
        }

        private sealed record DashboardStyleSnapshot(
            string FontFamily,
            string FontSize,
            string FontWeight,
            string AllyColor,
            string EnemyColor,
            string SystemColor,
            string BackgroundCss);

        private static DashboardStyleSnapshot GetDashboardStyleSnapshot()
        {
            return GetDashboardStyleSnapshot(out _);
        }

        private static DashboardStyleSnapshot GetDashboardStyleSnapshot(out long version)
        {
            System.Collections.Generic.IReadOnlyDictionary<string, string> settings;
            while (true)
            {
                var versionBefore = ApplicationConfig.GetDashboardStyleVersion();
                settings = ApplicationConfig.GetSettingsSnapshot(
                    ApplicationConfig.FontFamilyKey,
                    ApplicationConfig.FontSizeKey,
                    ApplicationConfig.FontStyleKey,
                    ApplicationConfig.AllyFontColorKey,
                    ApplicationConfig.EnemyFontColorKey,
                    ApplicationConfig.SystemFontColorKey,
                    ApplicationConfig.BackgroundCssKey);
                var versionAfter = ApplicationConfig.GetDashboardStyleVersion();

                if (versionBefore == versionAfter)
                {
                    version = versionAfter;
                    break;
                }
            }

            var fontFamily = GetSetting(settings, ApplicationConfig.FontFamilyKey, "Segoe UI").Trim();
            if (fontFamily.Length == 0)
            {
                fontFamily = "Segoe UI";
            }

            var fontSize = NormalizeFontSize(GetSetting(settings, ApplicationConfig.FontSizeKey, "14"));
            var fontWeight = NormalizeFontWeight(GetSetting(settings, ApplicationConfig.FontStyleKey, "normal"));
            var allyFontColor = ToRgba(GetSetting(settings, ApplicationConfig.AllyFontColorKey, "#FF5BC0DE"), "rgba(91, 192, 222, 1)");
            var enemyFontColor = ToRgba(GetSetting(settings, ApplicationConfig.EnemyFontColorKey, "#FFD9534F"), "rgba(217, 83, 79, 1)");
            var systemFontColor = ToRgba(GetSetting(settings, ApplicationConfig.SystemFontColorKey, "#FF856404"), "rgba(133, 100, 4, 1)");
            var bodyCss = GetSetting(settings, ApplicationConfig.BackgroundCssKey, "background-color: #f4f4f4;");

            return new DashboardStyleSnapshot(
                fontFamily,
                fontSize,
                fontWeight,
                allyFontColor,
                enemyFontColor,
                systemFontColor,
                bodyCss);
        }

        private static string BuildUserCss(DashboardStyleSnapshot style)
        {
            return $@"
                :root {{
                    --app-font-family: {CssString(style.FontFamily)};
                    --app-font-weight: {style.FontWeight};
                    --app-font-size: {style.FontSize}px;
                    --ally-color: {style.AllyColor};
                    --enemy-color: {style.EnemyColor};
                    --system-color: {style.SystemColor};
                }}
                body {{
                    {style.BackgroundCss}
                }}";
        }

        private static string GetSetting(System.Collections.Generic.IReadOnlyDictionary<string, string> settings, string key, string fallback)
        {
            return settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        private static string NormalizeFontSize(string rawValue)
        {
            if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                !double.TryParse(rawValue, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                value = 14;
            }

            value = Math.Clamp(value, 1, 200);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string NormalizeFontWeight(string rawValue)
        {
            return rawValue switch
            {
                "lighter" => "lighter",
                "bold" => "bold",
                "bolder" => "bolder",
                _ => "normal"
            };
        }

        private static string CssString(string value)
        {
            var escaped = value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);
            return $"\"{escaped}\"";
        }

        private static string ToRgba(string argbColor, string fallback)
        {
            if (string.IsNullOrWhiteSpace(argbColor))
            {
                return fallback;
            }

            var normalized = argbColor.Trim();
            if (!normalized.StartsWith('#') || normalized.Length != 9)
            {
                return fallback;
            }

            var argb = normalized[1..];
            if (!byte.TryParse(argb[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) ||
                !byte.TryParse(argb.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(argb.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(argb.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                return fallback;
            }

            var alpha = (a / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
            return $"rgba({r}, {g}, {b}, {alpha})";
        }

        public async ValueTask DisposeAsync()
        {
            if (_webApplication is null)
            {
                return;
            }

            try
            {
                await _webApplication.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                await _webApplication.DisposeAsync().ConfigureAwait(false);
                _webApplication = null;
            }
        }
    }
}
