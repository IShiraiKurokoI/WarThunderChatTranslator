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
                var sameProcessGeneration = session.HasValue && session.Value == currentProcessGeneration;

                // Keep X-Game-Session for backward compatibility. Its value represents the game process generation, not an individual battle.
                context.Response.Headers["X-Game-Session"] = currentProcessGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Process-Generation"] = currentProcessGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Pid"] = currentPid.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Game-Last-Id"] = _gameChatService.LastGameChatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Web-Poll-Interval-Ms"] = ApplicationConfig.GetWebPollingIntervalMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers.CacheControl = "no-store";

                // Legacy clients without query parameters still receive the full array. The dashboard uses the process-generation token (sent through the legacy session parameter) plus lastId for incremental reads.
                var messages = sameProcessGeneration
                    ? _gameChatService.GetMessagesAfter(lastId.GetValueOrDefault())
                    : _gameChatService.GetCurrentMessages();

                return Results.Json(messages, JsonOptions);
            });

            app.MapGet("/styles.css", () => Results.Text(
                BuildDynamicCss(),
                "text/css; charset=utf-8",
                Encoding.UTF8));

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
            Logger.Info($"Kestrel HTTP 服务器已启动，正在监听 {_listenUrl}");
        }

        private static string BuildDynamicCss()
        {
            var fontFamily = ApplicationConfig.GetSettings("FontFamily") ?? "Segoe UI";
            var fontSize = ApplicationConfig.GetSettings("FontSize") ?? "14";
            var fontStyle = ApplicationConfig.GetSettings("FontStyle") ?? "Normal";
            var allyFontColor = ToRgba(ApplicationConfig.GetSettings("AllyFontColor") ?? "#FF5BC0DE");
            var enemyFontColor = ToRgba(ApplicationConfig.GetSettings("EnemyFontColor") ?? "#FFD9534F");
            var systemFontColor = ToRgba(ApplicationConfig.GetSettings("SystemFontColor") ?? "#FF856404");
            var bodyBackground = ApplicationConfig.GetSettings("BackgroundCSS") ?? "opacity: 0;";

            return $@"
                body {{
                    font-family: {fontFamily}, Arial, sans-serif;
                    font-weight: {fontStyle};
                    margin: 0;
                    padding: 20px;
                    {bodyBackground}
                }}
                h1 {{
                    text-align: center;
                    color: #333;
                }}
                #chat-container {{
                    max-width: 84vw;
                    margin: 20px auto;
                    background-color: #fff;
                    border-radius: 10px;
                    box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);
                    padding: 20px;
                    height: 70vh;
                    overflow-y: auto;
                }}
                .chat-message {{
                    display: flex;
                    align-items: center;
                    margin-bottom: 15px;
                    padding: 10px;
                    border-radius: 5px;
                    font-size: {fontSize}px;
                    line-height: 1.5;
                }}
                .chat-message img {{
                    width: 20px;
                    height: 20px;
                    margin-right: 10px;
                }}
                .chat-message.ally {{
                    background-color: #e5f7ff;
                    color: {allyFontColor};
                }}
                .chat-message.enemy {{
                    background-color: #ffe5e5;
                    color: {enemyFontColor};
                }}
                .chat-message.system {{
                    background-color: #fff3cd;
                    color: {systemFontColor};
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
