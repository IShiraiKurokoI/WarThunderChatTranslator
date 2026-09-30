#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
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

            app.MapGet("/gamechat", (HttpContext context, int? lastId, long? session) =>
            {
                var currentProcessGeneration = _gameChatService.ProcessGeneration;
                var currentPid = _gameChatService.CurrentGamePid;
                var currentStartTimeUtc = _gameChatService.CurrentGameStartTimeUtc;
                var sameProcessGeneration = session.HasValue && session.Value == currentProcessGeneration;

                // Keep X-Game-Session for backward compatibility. Its value represents the game process generation, not an individual battle.
                context.Response.Headers["X-Game-Session"] = currentProcessGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Process-Generation"] = currentProcessGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Pid"] = currentPid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Start-Time"] = currentStartTimeUtc?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                context.Response.Headers["X-Game-Last-Id"] = _gameChatService.LastGameChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Web-Poll-Interval-Ms"] = ApplicationConfig.GetWebPollingIntervalMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";

                // Legacy clients without query parameters still receive the full array. The dashboard uses the process-generation token (sent through the legacy session parameter) plus lastId for incremental reads.
                var messages = sameProcessGeneration
                    ? _gameChatService.GetMessagesAfter(lastId.GetValueOrDefault())
                    : _gameChatService.GetCurrentMessages();

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
                return Results.Text(
                    BuildUserCss(),
                    "text/css; charset=utf-8",
                    Encoding.UTF8);
            });

            app.MapGet("/api/dashboard-ui", () => Results.Json(
                BuildDashboardLocalization(),
                JsonOptions));

            app.MapGet("/dashboard", () =>
            {
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

        private static string BuildUserCss()
        {
            var fontFamily = ApplicationConfig.GetSettings("FontFamily") ?? "Segoe UI";
            var fontSize = ApplicationConfig.GetSettings("FontSize") ?? "14";
            var fontStyle = ApplicationConfig.GetSettings("FontStyle") ?? "normal";
            var allyFontColor = ToRgba(ApplicationConfig.GetSettings("AllyFontColor") ?? "#FF5BC0DE");
            var enemyFontColor = ToRgba(ApplicationConfig.GetSettings("EnemyFontColor") ?? "#FFD9534F");
            var systemFontColor = ToRgba(ApplicationConfig.GetSettings("SystemFontColor") ?? "#FF856404");
            var bodyCss = ApplicationConfig.GetSettings("BackgroundCSS") ?? "background-color: #f4f4f4;";

            return $@"
                :root {{
                    --app-font-family: {fontFamily};
                    --app-font-weight: {fontStyle};
                    --app-font-size: {fontSize}px;
                    --ally-color: {allyFontColor};
                    --enemy-color: {enemyFontColor};
                    --system-color: {systemFontColor};
                }}
                body {{
                    {bodyCss}
                }}";
        }

        private static string ToRgba(string argbColor)
        {
            if (!argbColor.StartsWith('#') || argbColor.Length != 9)
            {
                return argbColor;
            }

            var argb = argbColor[1..];
            var a = int.Parse(argb[..2], System.Globalization.NumberStyles.HexNumber) / 255.0;
            var r = int.Parse(argb.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            var g = int.Parse(argb.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            var b = int.Parse(argb.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
            return $"rgba({r}, {g}, {b}, {a:0.##})";
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
