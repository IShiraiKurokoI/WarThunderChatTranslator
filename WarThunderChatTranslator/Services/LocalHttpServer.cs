#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Services.ContentFiltering;
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

        public const int PreferredPort = 8100;

        private readonly GameChatPollingService _gameChatService;
        private readonly bool _listenOnLan;
        private string _accessToken = GenerateAccessToken();
        private WebApplication? _webApplication;
        private int _boundPort;

        // The port is only published once Kestrel is accepting connections.
        public int Port => Volatile.Read(ref _boundPort);

        public string AccessToken => Volatile.Read(ref _accessToken);

        public Uri? DashboardUri
        {
            get
            {
                var port = Port;
                // Always open the local IPv4 loopback address, even when listening on all interfaces.
                return port > 0 ? new Uri($"http://127.0.0.1:{port}/dashboard") : null;
            }
        }

        public LocalHttpServer(GameChatPollingService gameChatService, bool listenOnLan)
        {
            _gameChatService = gameChatService;
            _listenOnLan = listenOnLan;
        }

        public void RegenerateAccessToken()
        {
            Volatile.Write(ref _accessToken, GenerateAccessToken());
            Logger.Info("Dashboard LAN access token was regenerated for the current application session.");
        }

        public IReadOnlyList<Uri> GetLanDashboardUris(bool includeAuthenticationToken)
        {
            var port = Port;
            if (port <= 0 || !_listenOnLan)
            {
                return Array.Empty<Uri>();
            }

            var token = AccessToken;
            return GetActiveLanIpv4Addresses()
                .Select(address =>
                {
                    var builder = new UriBuilder(Uri.UriSchemeHttp, address.ToString(), port, "/dashboard");
                    if (includeAuthenticationToken)
                    {
                        // Keep the secret in the URL fragment. Browsers do not send fragments in HTTP requests.
                        builder.Fragment = "token=" + Uri.EscapeDataString(token);
                    }

                    return builder.Uri;
                })
                .ToArray();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_webApplication is not null)
            {
                return;
            }

            try
            {
                await StartOnPortAsync(PreferredPort, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && IsAddressInUse(ex))
            {
                Logger.Warn(ex, "Dashboard port {0} is occupied; requesting an available port from the OS.", PreferredPort);
                // Port zero is allocated atomically by the socket bind (no free-port probe race).
                await StartOnPortAsync(0, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task StartOnPortAsync(int requestedPort, CancellationToken cancellationToken)
        {
            // Kestrel cannot bind localhost:0, so the dynamic-port fallback uses an IP address.
            // Keep the existing localhost/IPv4+IPv6 behavior for the preferred port.
            var listenUrl = _listenOnLan
                ? $"http://0.0.0.0:{requestedPort}"
                : requestedPort == 0 ? "http://127.0.0.1:0" : $"http://localhost:{requestedPort}";

            var options = new WebApplicationOptions
            {
                Args = [],
                ApplicationName = typeof(LocalHttpServer).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = Environments.Production
            };

            var builder = WebApplication.CreateSlimBuilder(options);
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls(listenUrl);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.AddServerHeader = false;
                kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            });

            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                if (!RequiresDashboardAuthorization(context))
                {
                    await next().ConfigureAwait(false);
                    return;
                }

                if (!IsAuthorized(context))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers["WWW-Authenticate"] = "Bearer";
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsync("Unauthorized", context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                await next().ConfigureAwait(false);
            });

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

                var displayMessages = messages.Select(ContentFilterService.Shared.CreateDisplayMessage).ToArray();
                return Results.Json(displayMessages, JsonOptions);
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

            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
                var port = GetBoundPort(app.Urls);
                _webApplication = app;
                Volatile.Write(ref _boundPort, port);
            }
            catch
            {
                // A failed bind leaves the host unusable. Dispose it before retrying.
                try { await app.DisposeAsync().ConfigureAwait(false); }
                catch (Exception disposeError) { Logger.Warn(disposeError, "Could not dispose a failed Dashboard host."); }
                throw;
            }

            Logger.Info(
                "Kestrel HTTP server started. Bound addresses: {0}; local dashboard: {1}.",
                string.Join(", ", app.Urls), DashboardUri);
        }

        private bool RequiresDashboardAuthorization(HttpContext context)
        {
            if (!ApplicationConfig.GetBooleanSetting(ApplicationConfig.DashboardLanAuthenticationEnabledKey))
            {
                return false;
            }

            var remoteAddress = context.Connection.RemoteIpAddress;
            if (IsLocalClientAddress(remoteAddress))
            {
                // Any request originating from this PC is exempt, including access through the PC's own LAN IP.
                return false;
            }

            var path = context.Request.Path.Value ?? string.Empty;
            return string.Equals(path, "/gamechat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, "/api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalClientAddress(IPAddress? remoteAddress)
        {
            if (remoteAddress is null)
            {
                return false;
            }

            var normalizedRemote = remoteAddress.IsIPv4MappedToIPv6
                ? remoteAddress.MapToIPv4()
                : remoteAddress;

            if (IPAddress.IsLoopback(normalizedRemote))
            {
                return true;
            }

            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                    .Select(unicast => unicast.Address.IsIPv4MappedToIPv6
                        ? unicast.Address.MapToIPv4()
                        : unicast.Address)
                    .Any(address => address.Equals(normalizedRemote));
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Could not enumerate local interface addresses while checking Dashboard authorization.");
                return false;
            }
        }

        private bool IsAuthorized(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suppliedToken = authorization[bearerPrefix.Length..].Trim();
            var expectedToken = AccessToken;
            if (suppliedToken.Length != expectedToken.Length)
            {
                return false;
            }

            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
            var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
            return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        }

        private static string GenerateAccessToken()
        {
            // 24 random bytes = 192 bits. Base64url without padding is exactly 32 characters.
            var bytes = RandomNumberGenerator.GetBytes(24);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static IReadOnlyList<IPAddress> GetActiveLanIpv4Addresses()
        {
            try
            {
                // Prefer the IPv4 address chosen by Windows for the current default route.
                // A UDP Connect selects a route/local endpoint without sending application data.
                var preferredOutboundAddress = TryGetPreferredOutboundIpv4Address();
                var internetAdapterId = TryGetInternetNetworkAdapterId();

                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                    .Where(adapter => adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                    .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                        .Where(unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        .Select(unicast => new
                        {
                            Address = unicast.Address,
                            IsInternetAdapter = IsAdapter(adapter, internetAdapterId),
                            HasGateway = HasIpv4Gateway(adapter),
                            HasTraffic = HasNetworkTraffic(adapter)
                        }))
                    .Where(item => !IPAddress.IsLoopback(item.Address))
                    .Where(item =>
                    {
                        var bytes = item.Address.GetAddressBytes();
                        return !(bytes[0] == 169 && bytes[1] == 254);
                    })
                    .GroupBy(item => item.Address)
                    .Select(group => group.First())
                    .OrderByDescending(item => item.IsInternetAdapter)
                    .ThenByDescending(item => preferredOutboundAddress is not null && item.Address.Equals(preferredOutboundAddress))
                    .ThenByDescending(item => item.HasGateway && item.HasTraffic)
                    .ThenByDescending(item => item.HasGateway)
                    .ThenByDescending(item => item.HasTraffic)
                    .ThenByDescending(item => IsPrivateIpv4Address(item.Address))
                    .ThenBy(item => item.Address.ToString(), StringComparer.Ordinal)
                    .Select(item => item.Address)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not enumerate LAN IPv4 addresses for Dashboard QR access.");
                return Array.Empty<IPAddress>();
            }
        }

        private static Guid? TryGetInternetNetworkAdapterId()
        {
            try
            {
                var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
                if (profile?.GetNetworkConnectivityLevel() != Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess)
                {
                    return null;
                }

                return profile.NetworkAdapter?.NetworkAdapterId;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAdapter(NetworkInterface adapter, Guid? expectedId)
        {
            return expectedId.HasValue
                && Guid.TryParse(adapter.Id, out var adapterId)
                && adapterId == expectedId.Value;
        }

        private static IPAddress? TryGetPreferredOutboundIpv4Address()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Connect(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 53));
                return (socket.LocalEndPoint as IPEndPoint)?.Address;
            }
            catch
            {
                return null;
            }
        }

        private static bool HasIpv4Gateway(NetworkInterface adapter)
        {
            try
            {
                return adapter.GetIPProperties().GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork
                    && !gateway.Address.Equals(IPAddress.Any)
                    && !gateway.Address.Equals(IPAddress.None));
            }
            catch
            {
                return false;
            }
        }

        private static bool HasNetworkTraffic(NetworkInterface adapter)
        {
            try
            {
                var statistics = adapter.GetIPv4Statistics();
                return statistics.BytesReceived > 0 || statistics.BytesSent > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsPrivateIpv4Address(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || bytes[0] == 192 && bytes[1] == 168
                || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
        }

        private static int GetBoundPort(IEnumerable<string> addresses)
        {
            foreach (var address in addresses)
            {
                if (Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttp && uri.Port > 0)
                {
                    return uri.Port;
                }
            }

            throw new InvalidOperationException("Kestrel started without reporting a valid bound HTTP port.");
        }

        private static bool IsAddressInUse(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is AddressInUseException ||
                    current is SocketException socket && socket.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    return true;
                }

                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (IsAddressInUse(inner))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
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
            var app = _webApplication;
            if (app is null)
            {
                return;
            }

            // Do not allow tray actions to reopen a port that is being shut down.
            _webApplication = null;
            Volatile.Write(ref _boundPort, 0);
            try
            {
                await app.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
