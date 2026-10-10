using System;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NLog;

namespace WarThunderChatTranslator.Services
{
    /// <summary>
    /// Windows App SDK notification registration for the packaged WinUI 3 application.
    /// The package's localized VisualElements DisplayName supplies the OS toast header.
    /// A notification must never be able to abort startup or shutdown.
    /// </summary>
    internal static class ApplicationNotifications
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static bool _registered;
        private static Action<string> _onActivated;

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

        public static void Show(string message, string navigationTarget = null)
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
