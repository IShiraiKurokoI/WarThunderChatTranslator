using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;

namespace WarThunderChatTranslator.Pages
{
    public sealed partial class LocationPage : Page
    {
        private DispatcherTimer _placementSyncTimer;
        private OverlayWindowManager _overlayManager;
        private IReadOnlyList<MonitorHelper.MonitorDescriptor> _monitors = Array.Empty<MonitorHelper.MonitorDescriptor>();
        private bool _loading;
        private bool _isLoaded;

        public LocationPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                LoadValues();
                return;
            }

            _isLoaded = true;
            EnsurePlacementSyncTimer();
            _overlayManager = (Application.Current as App)?.OverlayManager;
            if (_overlayManager is not null)
            {
                _overlayManager.PlacementChanged += OverlayManager_PlacementChanged;
            }

            LoadValues();
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            _isLoaded = false;
            if (_placementSyncTimer is not null)
            {
                _placementSyncTimer.Stop();
                _placementSyncTimer.Tick -= PlacementSyncTimer_Tick;
                _placementSyncTimer = null;
            }

            if (_overlayManager is not null)
            {
                _overlayManager.PlacementChanged -= OverlayManager_PlacementChanged;
                _overlayManager = null;
            }
        }

        private void LoadValues()
        {
            _loading = true;
            try
            {
                RefreshMonitorItems();
                ApplyPlacementToControls(GetCurrentPlacement(), refreshMonitorsIfMissing: false);
            }
            finally
            {
                _loading = false;
            }
        }

        private void RefreshMonitorItems()
        {
            _monitors = MonitorHelper.GetMonitors();
            MonitorComboBox.Items.Clear();

            foreach (var monitor in _monitors)
            {
                MonitorComboBox.Items.Add(new ComboBoxItem
                {
                    Content = monitor.DisplayName,
                    Tag = monitor.DeviceName
                });
            }
        }

        private void ApplyPlacementToControls(
            (string DeviceName, int X, int Y, int Width, int Height) placement,
            bool refreshMonitorsIfMissing)
        {
            var previousLoading = _loading;
            _loading = true;
            try
            {
                var selectedIndex = FindMonitorIndex(placement.DeviceName);
                if (selectedIndex < 0 && refreshMonitorsIfMissing)
                {
                    RefreshMonitorItems();
                    selectedIndex = FindMonitorIndex(placement.DeviceName);
                }

                if (MonitorComboBox.Items.Count > 0)
                {
                    MonitorComboBox.SelectedIndex = selectedIndex >= 0
                        ? selectedIndex
                        : 0;
                }

                XBox.Value = placement.X;
                YBox.Value = placement.Y;
                WidthBox.Value = placement.Width;
                HeightBox.Value = placement.Height;
            }
            finally
            {
                _loading = previousLoading;
            }
        }

        private int FindMonitorIndex(string deviceName)
        {
            return _monitors
                .Select((monitor, index) => new { monitor, index })
                .FirstOrDefault(item => string.Equals(
                    item.monitor.DeviceName,
                    deviceName,
                    StringComparison.OrdinalIgnoreCase))?.index ?? -1;
        }

        private (string DeviceName, int X, int Y, int Width, int Height) GetCurrentPlacement()
        {
            return _overlayManager?.GetStoredPlacement() ?? GetStoredPlacementWithoutManager();
        }

        private void MonitorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || MonitorComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string deviceName)
            {
                return;
            }

            if (_overlayManager is not null)
            {
                _overlayManager.MoveToMonitor(deviceName);
                ApplyPlacementToControls(_overlayManager.GetStoredPlacement(), refreshMonitorsIfMissing: true);
                return;
            }

            var monitor = MonitorHelper.GetMonitorByDeviceName(deviceName);
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayMonitorDeviceKey, monitor.DeviceName);
            var size = new Windows.Graphics.SizeInt32((int)WidthBox.Value, (int)HeightBox.Value);
            var centered = MonitorHelper.CenterInWorkArea(monitor, size);
            XBox.Value = centered.X;
            YBox.Value = centered.Y;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (MonitorComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string deviceName)
            {
                return;
            }

            var x = (int)Math.Round(double.IsNaN(XBox.Value) ? 0 : XBox.Value);
            var y = (int)Math.Round(double.IsNaN(YBox.Value) ? 0 : YBox.Value);
            var width = (int)Math.Round(double.IsNaN(WidthBox.Value) ? OverlayWindow.DefaultWidth : WidthBox.Value);
            var height = (int)Math.Round(double.IsNaN(HeightBox.Value) ? OverlayWindow.DefaultHeight : HeightBox.Value);

            if (_overlayManager is not null)
            {
                _overlayManager.ApplyPlacement(deviceName, x, y, width, height);
                ApplyPlacementToControls(_overlayManager.GetStoredPlacement(), refreshMonitorsIfMissing: true);
                return;
            }

            var monitor = MonitorHelper.GetMonitorByDeviceName(deviceName);
            var size = new Windows.Graphics.SizeInt32(
                Math.Clamp(width, OverlayWindow.MinimumWidth, Math.Max(OverlayWindow.MinimumWidth, monitor.WorkWidth)),
                Math.Clamp(height, OverlayWindow.MinimumHeight, Math.Max(OverlayWindow.MinimumHeight, monitor.WorkHeight)));
            var position = MonitorHelper.ClampToWorkArea(monitor, new Windows.Graphics.PointInt32(x, y), size);
            SavePlacement(monitor.DeviceName, position.X, position.Y, size.Width, size.Height);
            ApplyPlacementToControls(GetStoredPlacementWithoutManager(), refreshMonitorsIfMissing: true);
        }

        private void CenterButton_Click(object sender, RoutedEventArgs e)
        {
            if (MonitorComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string deviceName)
            {
                return;
            }

            if (_overlayManager is not null)
            {
                _overlayManager.MoveToMonitor(deviceName);
                ApplyPlacementToControls(_overlayManager.GetStoredPlacement(), refreshMonitorsIfMissing: true);
                return;
            }

            var monitor = MonitorHelper.GetMonitorByDeviceName(deviceName);
            var width = (int)Math.Round(double.IsNaN(WidthBox.Value) ? OverlayWindow.DefaultWidth : WidthBox.Value);
            var height = (int)Math.Round(double.IsNaN(HeightBox.Value) ? OverlayWindow.DefaultHeight : HeightBox.Value);
            var size = new Windows.Graphics.SizeInt32(width, height);
            var centered = MonitorHelper.CenterInWorkArea(monitor, size);
            SavePlacement(monitor.DeviceName, centered.X, centered.Y, size.Width, size.Height);
            ApplyPlacementToControls(GetStoredPlacementWithoutManager(), refreshMonitorsIfMissing: true);
        }

        private void OverlayManager_PlacementChanged(object sender, EventArgs e)
        {
            if (!_isLoaded)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(QueuePlacementSync);
        }

        private void QueuePlacementSync()
        {
            if (!_isLoaded)
            {
                return;
            }

            EnsurePlacementSyncTimer();
            _placementSyncTimer.Stop();
            _placementSyncTimer.Start();
        }

        private void PlacementSyncTimer_Tick(object sender, object e)
        {
            _placementSyncTimer?.Stop();
            if (!_isLoaded)
            {
                return;
            }

            ApplyPlacementToControls(GetCurrentPlacement(), refreshMonitorsIfMissing: true);
        }

        private void EnsurePlacementSyncTimer()
        {
            if (_placementSyncTimer is not null)
            {
                return;
            }

            _placementSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _placementSyncTimer.Tick += PlacementSyncTimer_Tick;
        }

        private static (string DeviceName, int X, int Y, int Width, int Height) GetStoredPlacementWithoutManager()
        {
            var monitor = MonitorHelper.GetMonitorByDeviceName(ApplicationConfig.GetSettings(ApplicationConfig.OverlayMonitorDeviceKey));
            var width = ReadInt(ApplicationConfig.OverlayWidthKey, OverlayWindow.DefaultWidth);
            var height = ReadInt(ApplicationConfig.OverlayHeightKey, OverlayWindow.DefaultHeight);
            var size = new Windows.Graphics.SizeInt32(width, height);

            if (!TryReadInt(ApplicationConfig.OverlayXKey, out var x) || !TryReadInt(ApplicationConfig.OverlayYKey, out var y))
            {
                var centered = MonitorHelper.CenterInWorkArea(monitor, size);
                x = centered.X;
                y = centered.Y;
            }

            return (monitor.DeviceName, x, y, width, height);
        }

        private static void SavePlacement(string deviceName, int x, int y, int width, int height)
        {
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayMonitorDeviceKey, deviceName);
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayXKey, x.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayYKey, y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayWidthKey, width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayHeightKey, height.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
