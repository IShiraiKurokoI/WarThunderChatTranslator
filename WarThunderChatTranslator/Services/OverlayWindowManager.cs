using System;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Configurations;
using Windows.Graphics;

namespace WarThunderChatTranslator.Services
{
    internal sealed class OverlayWindowManager
    {
        private readonly Func<GameChatPollingService> _gameChatServiceAccessor;
        private OverlayWindow _window;

        public OverlayWindowManager(Func<GameChatPollingService> gameChatServiceAccessor)
        {
            _gameChatServiceAccessor = gameChatServiceAccessor;
        }

        public event EventHandler PlacementChanged;

        public bool IsVisible => GetLiveWindow()?.IsVisible == true;

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        public void Hide()
        {
            GetLiveWindow()?.HideOverlay();
        }

        public void Show()
        {
            var window = EnsureWindow();
            window.ShowOverlay();
        }

        public void ApplyStoredPlacement()
        {
            GetLiveWindow()?.ApplyStoredPlacement();
        }

        public void MoveToMonitor(string deviceName)
        {
            var monitor = MonitorHelper.GetMonitorByDeviceName(deviceName);
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayMonitorDeviceKey, monitor.DeviceName);

            var width = ReadInt(ApplicationConfig.OverlayWidthKey, OverlayWindow.DefaultWidth);
            var height = ReadInt(ApplicationConfig.OverlayHeightKey, OverlayWindow.DefaultHeight);
            var size = new SizeInt32(
                Math.Clamp(width, OverlayWindow.MinimumWidth, Math.Max(OverlayWindow.MinimumWidth, monitor.WorkWidth)),
                Math.Clamp(height, OverlayWindow.MinimumHeight, Math.Max(OverlayWindow.MinimumHeight, monitor.WorkHeight)));
            var centered = MonitorHelper.CenterInWorkArea(monitor, size);

            SavePlacement(centered, size, monitor.DeviceName);
            ApplyStoredPlacement();
            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyPlacement(string deviceName, int x, int y, int width, int height)
        {
            var monitor = MonitorHelper.GetMonitorByDeviceName(deviceName);
            var size = new SizeInt32(
                Math.Clamp(width, OverlayWindow.MinimumWidth, Math.Max(OverlayWindow.MinimumWidth, monitor.WorkWidth)),
                Math.Clamp(height, OverlayWindow.MinimumHeight, Math.Max(OverlayWindow.MinimumHeight, monitor.WorkHeight)));
            var position = MonitorHelper.ClampToWorkArea(monitor, new PointInt32(x, y), size);

            SavePlacement(position, size, monitor.DeviceName);
            ApplyStoredPlacement();
            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }

        public (string DeviceName, int X, int Y, int Width, int Height) GetStoredPlacement()
        {
            var monitor = MonitorHelper.GetMonitorByDeviceName(ApplicationConfig.GetSettings(ApplicationConfig.OverlayMonitorDeviceKey));
            var size = new SizeInt32(
                ReadInt(ApplicationConfig.OverlayWidthKey, OverlayWindow.DefaultWidth),
                ReadInt(ApplicationConfig.OverlayHeightKey, OverlayWindow.DefaultHeight));

            if (!TryReadInt(ApplicationConfig.OverlayXKey, out var x) || !TryReadInt(ApplicationConfig.OverlayYKey, out var y))
            {
                var centered = MonitorHelper.CenterInWorkArea(monitor, size);
                x = centered.X;
                y = centered.Y;
            }

            return (monitor.DeviceName, x, y, size.Width, size.Height);
        }

        public void Close()
        {
            var window = GetLiveWindow();
            if (window is null)
            {
                return;
            }

            _window = null;
            try
            {
                window.Close();
            }
            catch
            {
                if (window.IsAlive && _window is null)
                {
                    _window = window;
                }

                throw;
            }
        }

        internal void NotifyPlacementChanged()
        {
            PlacementChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void NotifyWindowClosed(OverlayWindow window)
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        }

        private OverlayWindow EnsureWindow()
        {
            var window = GetLiveWindow();
            if (window is not null)
            {
                return window;
            }

            window = new OverlayWindow(_gameChatServiceAccessor, this);
            _window = window;
            return window;
        }

        private OverlayWindow GetLiveWindow()
        {
            var window = _window;
            if (window is null)
            {
                return null;
            }

            if (window.IsAlive)
            {
                return window;
            }

            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }

            return null;
        }

        private static void SavePlacement(PointInt32 position, SizeInt32 size, string deviceName)
        {
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayMonitorDeviceKey, deviceName ?? string.Empty);
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayXKey, position.X.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayYKey, position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayWidthKey, size.Width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayHeightKey, size.Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static int ReadInt(string key, int fallback)
        {
            return TryReadInt(key, out var value) ? value : fallback;
        }

        private static bool TryReadInt(string key, out int value)
        {
            return int.TryParse(
                ApplicationConfig.GetSettings(key),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }
    }
}
