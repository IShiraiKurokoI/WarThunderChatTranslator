#nullable enable

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NLog;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    /// <summary>
    /// Windows App SDK notification registration for the packaged WinUI 3 application.
    /// A notification must never be able to abort startup, shutdown, or a background task.
    /// </summary>
    internal static class ApplicationNotifications
    {
        private const string TtsDownloadTag = "tts-model-download";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly object TtsDownloadSync = new();
        private static bool _registered;
        private static Action<string>? _onActivated;
        private static bool _ttsDownloadNotificationShown;
        private static int _ttsDownloadSequence;

        public static void Initialize(Action<string> onActivated)
        {
            if (_registered)
            {
                return;
            }

            var manager = AppNotificationManager.Default;
            _onActivated = onActivated;
            manager.NotificationInvoked += OnNotificationInvoked;
            try
            {
                manager.Register();
                _registered = true;
            }
            catch (Exception ex)
            {
                manager.NotificationInvoked -= OnNotificationInvoked;
                _onActivated = null;
                Logger.Warn(ex, "App notification registration failed; continuing without toast notifications.");
            }
        }

        public static void Show(string message, string? navigationTarget = null)
        {
            if (!_registered || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                var builder = new AppNotificationBuilder().AddText(message);
                if (!string.IsNullOrWhiteSpace(navigationTarget))
                {
                    builder.AddArgument("navigate", navigationTarget);
                }

                AppNotificationManager.Default.Show(builder.BuildNotification());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Windows declined the app notification.");
            }
        }

        public static void ShowTtsModelDownloadProgress(SherpaModelDownloadProgress? progress, bool installing)
        {
            if (!_registered)
            {
                return;
            }

            try
            {
                var data = CreateTtsDownloadProgressData(progress, installing);
                bool showNewNotification;
                lock (TtsDownloadSync)
                {
                    showNewNotification = !_ttsDownloadNotificationShown;
                    _ttsDownloadNotificationShown = true;
                }

                if (showNewNotification)
                {
                    var notification = new AppNotificationBuilder()
                        .AddArgument("navigate", "ChatTtsPage")
                        .AddText(Localization.GetString("ChatTtsDownloadToastTitle"))
                        .AddProgressBar(new AppNotificationProgressBar()
                            .BindTitle()
                            .BindValue()
                            .BindValueStringOverride()
                            .BindStatus())
                        .AddButton(new AppNotificationButton(Localization.GetString("CommonCancel"))
                            .AddArgument("action", "cancelTtsModelDownload"))
                        .BuildNotification();
                    notification.Tag = TtsDownloadTag;
                    notification.Progress = data;
                    AppNotificationManager.Default.Show(notification);
                    return;
                }

                _ = UpdateTtsDownloadProgressAsync(data);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Could not show TTS model download progress notification.");
            }
        }

        public static void CompleteTtsModelDownload()
        {
            _ = ReplaceTtsDownloadNotificationAsync(Localization.GetString("ChatTtsDownloadToastCompleted"));
        }

        public static void CancelledTtsModelDownload()
        {
            _ = ReplaceTtsDownloadNotificationAsync(Localization.GetString("ChatTtsDownloadToastCancelled"));
        }

        public static void FailedTtsModelDownload()
        {
            _ = ReplaceTtsDownloadNotificationAsync(Localization.GetString("ChatTtsDownloadToastFailed"));
        }

        private static AppNotificationProgressData CreateTtsDownloadProgressData(
            SherpaModelDownloadProgress? progress,
            bool installing)
        {
            var sequence = (uint)Math.Max(1, Interlocked.Increment(ref _ttsDownloadSequence));
            var percent = progress?.Percent;
            return new AppNotificationProgressData(sequence)
            {
                Title = SherpaTtsModelManager.RecommendedModelDisplayName,
                Value = installing ? 1.0 : Math.Clamp((percent ?? 0.0) / 100.0, 0.0, 1.0),
                ValueStringOverride = installing
                    ? Localization.GetString("ChatTtsDownloadToastInstalling")
                    : percent is double value
                        ? string.Format(CultureInfo.CurrentCulture, "{0:0}%", value)
                        : Localization.GetString("ChatTtsDownloadToastPreparing"),
                Status = installing
                    ? Localization.GetString("ChatTtsDownloadToastInstalling")
                    : Localization.GetString("ChatTtsDownloadToastStatus")
            };
        }

        private static async Task UpdateTtsDownloadProgressAsync(AppNotificationProgressData data)
        {
            try
            {
                await AppNotificationManager.Default.UpdateAsync(data, TtsDownloadTag);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Could not update TTS model download notification.");
            }
        }

        private static async Task ReplaceTtsDownloadNotificationAsync(string finalMessage)
        {
            if (!_registered)
            {
                return;
            }

            lock (TtsDownloadSync)
            {
                _ttsDownloadNotificationShown = false;
            }

            try
            {
                await AppNotificationManager.Default.RemoveByTagAsync(TtsDownloadTag);
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Could not remove TTS model download progress notification.");
            }

            Show(finalMessage, "ChatTtsPage");
        }

        private static void OnNotificationInvoked(
            AppNotificationManager sender,
            AppNotificationActivatedEventArgs args)
        {
            try
            {
                // Windows can invoke this callback off the UI thread.
                _onActivated?.Invoke(args.Argument ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not handle a notification click.");
            }
        }

        public static void Shutdown()
        {
            if (!_registered)
            {
                return;
            }

            _registered = false;
            try
            {
                var manager = AppNotificationManager.Default;
                manager.NotificationInvoked -= OnNotificationInvoked;
                _onActivated = null;
                manager.Unregister();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Could not unregister app notifications during shutdown.");
            }
        }
    }
}
