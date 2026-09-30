using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Globalization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WarThunderChatTranslator.Configurations;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;
using WarThunderChatTranslator.Services;
using Windows.Graphics;
using Windows.UI;

namespace WarThunderChatTranslator
{
    public sealed partial class OverlayWindow : Window
    {
        internal const int DefaultWidth = 760;
        internal const int DefaultHeight = 320;
        internal const int MinimumWidth = 420;
        internal const int MinimumHeight = 180;

        private const int GwlStyle = -16;
        private const int GwlExStyle = -20;
        private const long WsPopup = 0x80000000L;
        private const long WsCaption = 0x00C00000L;
        private const long WsSysMenu = 0x00080000L;
        private const long WsThickFrame = 0x00040000L;
        private const long WsMinimizeBox = 0x00020000L;
        private const long WsMaximizeBox = 0x00010000L;
        private const long WsExTransparent = 0x00000020L;
        private const long WsExToolWindow = 0x00000080L;
        private const long WsExLayered = 0x00080000L;
        private const long WsExNoActivate = 0x08000000L;
        private const uint LwaAlpha = 0x00000002;
        private const int DefaultOpacityPercent = 90;
        private const int MinimumOpacityPercent = 35;
        private const int MaximumOpacityPercent = 100;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaBorderColor = 34;
        private const int DwmwcpDoNotRound = 1;
        private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoActivate = 0x0010;
        private const uint SwpFrameChanged = 0x0020;
        private static readonly IntPtr HwndTopMost = new(-1);

        private readonly Func<GameChatPollingService> _gameChatServiceAccessor;
        private readonly OverlayWindowManager _manager;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _foregroundTimer;
        private readonly DispatcherTimer _placementSaveTimer;
        private readonly AppWindow _appWindow;
        private readonly OverlappedPresenter _presenter;
        private readonly nint _hwnd;

        private bool _settingsLoaded;
        private bool _isClosed;
        private bool _adjustmentMode;
        private bool _isDragging;
        private bool _applyingPlacement;
        private bool _clickThroughApplied;
        private long _lastProcessGeneration = long.MinValue;
        private int _lastMessageId = int.MinValue;
        private long _lastStyleVersion = long.MinValue;
        private NativePoint _dragStartCursor;
        private PointInt32 _dragStartWindow;
        private uint _lastForegroundPid;
        private bool _lastForegroundWasGame;

        internal OverlayWindow(Func<GameChatPollingService> gameChatServiceAccessor, OverlayWindowManager manager)
        {
            InitializeComponent();
            _gameChatServiceAccessor = gameChatServiceAccessor;
            _manager = manager;

            Title = Localization.GetString("OverlayTitle");
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.SetIcon("favicon.ico");
            _presenter = _appWindow.Presenter as OverlappedPresenter;

            ConfigureWindow();
            LoadOverlayPreferences();
            ApplyStoredPlacement();
            ApplyVisualSettings();

            _placementSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _placementSaveTimer.Tick += PlacementSaveTimer_Tick;

            _appWindow.Changed += AppWindow_Changed;
            Closed += OverlayWindow_Closed;
            Localization.CultureChanged += Localization_CultureChanged;

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _refreshTimer.Tick += RefreshTimer_Tick;
            _refreshTimer.Start();

            _foregroundTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _foregroundTimer.Tick += ForegroundTimer_Tick;
            _foregroundTimer.Start();

            RefreshMessages(force: true);
            UpdateInputBehavior(force: true);
        }

        internal bool IsAlive => !_isClosed && IsWindow(_hwnd);

        internal bool IsVisible => IsAlive && _appWindow.IsVisible;

        internal void ShowOverlay()
        {
            if (!IsAlive)
            {
                return;
            }

            _appWindow.Show(false);
            ApplyNativeFrameStyle();
            EnsureTopMost();
            UpdateInputBehavior(force: true);
        }

        internal void HideOverlay()
        {
            if (!IsAlive)
            {
                return;
            }

            if (_placementSaveTimer.IsEnabled)
            {
                _placementSaveTimer.Stop();
                PersistCurrentPlacement();
            }

            _appWindow.Hide();
        }

        internal void EnsureTopMost()
        {
            if (!IsAlive)
            {
                return;
            }

            _presenter.IsAlwaysOnTop = true;
            SetWindowPos(_hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        internal void ApplyStoredPlacement()
        {
            if (!IsAlive)
            {
                return;
            }

            _applyingPlacement = true;
            try
            {
                var savedDevice = ApplicationConfig.GetSettings(ApplicationConfig.OverlayMonitorDeviceKey);
                var monitor = MonitorHelper.GetMonitorByDeviceName(savedDevice);

                var width = ReadInt(ApplicationConfig.OverlayWidthKey, DefaultWidth);
                var height = ReadInt(ApplicationConfig.OverlayHeightKey, DefaultHeight);
                var size = new SizeInt32(
                    Math.Clamp(width, MinimumWidth, Math.Max(MinimumWidth, monitor.WorkWidth)),
                    Math.Clamp(height, MinimumHeight, Math.Max(MinimumHeight, monitor.WorkHeight)));

                PointInt32 position;
                if (TryReadInt(ApplicationConfig.OverlayXKey, out var x) && TryReadInt(ApplicationConfig.OverlayYKey, out var y))
                {
                    position = MonitorHelper.ClampToWorkArea(monitor, new PointInt32(x, y), size);
                }
                else
                {
                    position = MonitorHelper.CenterInWorkArea(monitor, size);
                }

                _appWindow.Resize(size);
                _appWindow.Move(position);
                SavePlacement(position, size, monitor.DeviceName);
            }
            finally
            {
                _applyingPlacement = false;
            }
        }

        private void ConfigureWindow()
        {
            _presenter.IsAlwaysOnTop = true;
            _presenter.IsMaximizable = false;
            _presenter.IsMinimizable = false;
            _presenter.IsResizable = false;
            _presenter.SetBorderAndTitleBar(false, false);
            ApplyNativeFrameStyle();

            var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle));
            ApplyDwmFramePreferences();
            ApplyWindowOpacity(ReadOverlayOpacityPercent());
            SetWindowPos(_hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }

        private void LoadOverlayPreferences()
        {
            _settingsLoaded = false;
            var displayMode = ApplicationConfig.GetSettings(ApplicationConfig.OverlayDisplayModeKey) ?? "translation";
            DisplayModeComboBox.SelectedIndex = displayMode switch
            {
                "original" => 1,
                "translationWithOriginal" => 2,
                _ => 0
            };

            ShowIsoToggle.IsOn = string.Equals(
                ApplicationConfig.GetSettings(ApplicationConfig.OverlayShowSourceLanguageKey),
                "true",
                StringComparison.OrdinalIgnoreCase);

            var opacityPercent = ReadOverlayOpacityPercent();
            OpacitySlider.Value = opacityPercent;
            UpdateOpacityValueText(opacityPercent);
            _settingsLoaded = true;
        }

        private void RefreshTimer_Tick(object sender, object e)
        {
            RefreshMessages(force: false);
            var styleVersion = ApplicationConfig.GetDashboardStyleVersion();
            if (styleVersion != _lastStyleVersion)
            {
                ApplyVisualSettings();
                RefreshMessages(force: true);
            }
        }

        private void ForegroundTimer_Tick(object sender, object e)
        {
            UpdateInputBehavior(force: false);
        }

        private void RefreshMessages(bool force)
        {
            var service = _gameChatServiceAccessor?.Invoke();
            if (service is null)
            {
                if (force)
                {
                    RenderMessages(Array.Empty<ChatMessage>());
                }
                return;
            }

            var generation = service.ProcessGeneration;
            var lastId = service.LastGameChatId;
            if (!force && generation == _lastProcessGeneration && lastId == _lastMessageId)
            {
                return;
            }

            _lastProcessGeneration = generation;
            _lastMessageId = lastId;
            RenderMessages(service.GetCurrentMessages());
        }

        private void RenderMessages(System.Collections.Generic.IReadOnlyList<ChatMessage> messages)
        {
            MessagePanel.Children.Clear();

            if (messages.Count == 0)
            {
                MessagePanel.Children.Add(new TextBlock
                {
                    Text = Localization.GetString("DashboardEmptyState"),
                    Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
                    FontSize = 13,
                    Margin = new Thickness(4, 8, 4, 0)
                });
                return;
            }

            foreach (var message in messages)
            {
                MessagePanel.Children.Add(CreateMessageElement(message));
            }

            _ = DispatcherQueue.TryEnqueue(() => MessageScrollViewer.ChangeView(null, MessageScrollViewer.ScrollableHeight, null, true));
        }

        private FrameworkElement CreateMessageElement(ChatMessage message)
        {
            var text = new TextBlock
            {
                Text = FormatMessage(message),
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily(ApplicationConfig.GetSettings("FontFamily") ?? "Segoe UI"),
                FontSize = ReadDouble("FontSize", 14),
                FontWeight = GetFontWeight(ApplicationConfig.GetSettings("FontStyle")),
                Foreground = new SolidColorBrush(GetMessageColor(message)),
                Margin = new Thickness(2, 1, 2, 1)
            };

            return text;
        }

        private static string FormatMessage(ChatMessage message)
        {
            var original = string.IsNullOrEmpty(message.OriginalMessage) ? message.Msg ?? string.Empty : message.OriginalMessage;
            var translation = string.IsNullOrEmpty(message.TranslatedMessage) ? original : message.TranslatedMessage;
            var displayMode = ApplicationConfig.GetSettings(ApplicationConfig.OverlayDisplayModeKey) ?? "translation";
            var content = displayMode switch
            {
                "original" => original,
                "translationWithOriginal" => $"{translation} ({original})",
                _ => translation
            };

            var showIso = string.Equals(
                ApplicationConfig.GetSettings(ApplicationConfig.OverlayShowSourceLanguageKey),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (showIso && !string.IsNullOrWhiteSpace(message.SourceLanguageIsoCode))
            {
                content += $" [{message.SourceLanguageIsoCode}]";
            }

            var modePrefix = string.IsNullOrWhiteSpace(message.Mode) ? string.Empty : $"[{message.Mode}] ";
            var sender = string.IsNullOrWhiteSpace(message.Sender)
                ? Localization.GetString("DashboardSystemSender")
                : message.Sender.Trim();
            return $"{modePrefix}{sender}: {content}";
        }

        private static Color GetMessageColor(ChatMessage message)
        {
            var key = string.IsNullOrWhiteSpace(message.Sender)
                ? "SystemFontColor"
                : message.Enemy ? "EnemyFontColor" : "AllyFontColor";
            var fallback = key switch
            {
                "EnemyFontColor" => "#FFD9534F",
                "SystemFontColor" => "#FF856404",
                _ => "#FF5BC0DE"
            };

            return ParseColor(ApplicationConfig.GetSettings(key) ?? fallback, fallback);
        }

        private void ApplyVisualSettings()
        {
            _lastStyleVersion = ApplicationConfig.GetDashboardStyleVersion();

            var opacityPercent = ReadOverlayOpacityPercent();
            UpdateOpacityValueText(opacityPercent);
            ApplyWindowOpacity(opacityPercent);
        }

        private void DisplayModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_settingsLoaded || DisplayModeComboBox.SelectedItem is not ComboBoxItem item)
            {
                return;
            }

            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayDisplayModeKey, item.Tag?.ToString() ?? "translation");
            RefreshMessages(force: true);
        }

        private void ShowIsoToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_settingsLoaded)
            {
                return;
            }

            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayShowSourceLanguageKey, ShowIsoToggle.IsOn ? "true" : "false");
            RefreshMessages(force: true);
        }


        private void OpacitySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var opacityPercent = (int)Math.Round(e.NewValue);
            UpdateOpacityValueText(opacityPercent);

            if (!_settingsLoaded)
            {
                return;
            }

            ApplicationConfig.SaveSettings(
                ApplicationConfig.OverlayOpacityPercentKey,
                opacityPercent.ToString(CultureInfo.InvariantCulture));
            ApplyVisualSettings();
        }

        private void DragHandle_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var point = e.GetCurrentPoint(DragHandle);
            if (!point.Properties.IsLeftButtonPressed || !GetCursorPos(out _dragStartCursor))
            {
                return;
            }

            _isDragging = true;
            _dragStartWindow = _appWindow.Position;
            DragHandle.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void DragHandle_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDragging || !GetCursorPos(out var current))
            {
                return;
            }

            var position = new PointInt32(
                _dragStartWindow.X + current.X - _dragStartCursor.X,
                _dragStartWindow.Y + current.Y - _dragStartCursor.Y);
            _appWindow.Move(position);
            e.Handled = true;
        }

        private void DragHandle_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragHandle.ReleasePointerCapture(e.Pointer);
                _placementSaveTimer.Stop();
                PersistCurrentPlacement();
            }
            e.Handled = true;
        }

        private void DragHandle_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragHandle.ReleasePointerCapture(e.Pointer);
                _placementSaveTimer.Stop();
                PersistCurrentPlacement();
            }
        }

        private void DragHandle_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            _isDragging = false;
            _adjustmentMode = !_adjustmentMode;
            ApplyAdjustmentMode();
            e.Handled = true;
        }

        private void ApplyAdjustmentMode()
        {
            _presenter.IsResizable = _adjustmentMode;
            _presenter.SetBorderAndTitleBar(_adjustmentMode, false);
            ApplyNativeFrameStyle();
            DragHandle.Background = new SolidColorBrush(_adjustmentMode
                ? Color.FromArgb(110, 0, 120, 212)
                : Color.FromArgb(42, 255, 255, 255));

            ApplyDwmFramePreferences();
            UpdateInputBehavior(force: true);
            if (_adjustmentMode)
            {
                Activate();
            }
        }

        private void ApplyNativeFrameStyle()
        {
            if (!IsAlive)
            {
                return;
            }

            var style = GetWindowLongPtr(_hwnd, GwlStyle).ToInt64();
            if (_adjustmentMode)
            {
                style &= ~WsPopup;
                style |= WsThickFrame;
            }
            else
            {
                style &= ~(WsCaption | WsSysMenu | WsThickFrame | WsMinimizeBox | WsMaximizeBox);
                style |= WsPopup;
            }

            SetWindowLongPtr(_hwnd, GwlStyle, WindowLongToIntPtr(style));
            SetWindowPos(
                _hwnd,
                HwndTopMost,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        }

        private void UpdateInputBehavior(bool force)
        {
            var gameFocused = IsGameForeground();
            var shouldClickThrough = !_adjustmentMode && gameFocused;
            if (!force && shouldClickThrough == _clickThroughApplied)
            {
                return;
            }

            var exStyle = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
            exStyle |= WsExLayered | WsExToolWindow;

            if (shouldClickThrough)
            {
                exStyle |= WsExTransparent | WsExNoActivate;
            }
            else
            {
                exStyle &= ~WsExTransparent;
                if (_adjustmentMode)
                {
                    exStyle &= ~WsExNoActivate;
                }
                else
                {
                    exStyle |= WsExNoActivate;
                }
            }

            SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(exStyle));
            SetWindowPos(_hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
            _clickThroughApplied = shouldClickThrough;
        }

        private bool IsGameForeground()
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            GetWindowThreadProcessId(foreground, out var foregroundPid);
            if (foregroundPid == 0)
            {
                return false;
            }

            if (foregroundPid == _lastForegroundPid)
            {
                return _lastForegroundWasGame;
            }

            _lastForegroundPid = foregroundPid;
            _lastForegroundWasGame = false;
            try
            {
                using var process = Process.GetProcessById((int)foregroundPid);
                _lastForegroundWasGame = string.Equals(process.ProcessName, "aces", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(process.ProcessName, "aces-min-cpu", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }

            return _lastForegroundWasGame;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (_isClosed || _applyingPlacement)
            {
                return;
            }

            if (args.DidPositionChange || args.DidSizeChange)
            {
                _placementSaveTimer.Stop();
                _placementSaveTimer.Start();
            }
        }

        private void PlacementSaveTimer_Tick(object sender, object e)
        {
            _placementSaveTimer.Stop();
            PersistCurrentPlacement();
        }

        private void PersistCurrentPlacement()
        {
            if (!IsAlive)
            {
                return;
            }

            var monitor = MonitorHelper.GetMonitorForWindow(_hwnd);
            SavePlacement(_appWindow.Position, _appWindow.Size, monitor.DeviceName);
            _manager.NotifyPlacementChanged();
        }

        private static void SavePlacement(PointInt32 position, SizeInt32 size, string deviceName)
        {
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayMonitorDeviceKey, deviceName ?? string.Empty);
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayXKey, position.X.ToString(CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayYKey, position.Y.ToString(CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayWidthKey, size.Width.ToString(CultureInfo.InvariantCulture));
            ApplicationConfig.SaveSettings(ApplicationConfig.OverlayHeightKey, size.Height.ToString(CultureInfo.InvariantCulture));
        }

        private int ReadOverlayOpacityPercent()
        {
            var value = ReadInt(ApplicationConfig.OverlayOpacityPercentKey, DefaultOpacityPercent);
            return Math.Clamp(value, MinimumOpacityPercent, MaximumOpacityPercent);
        }

        private void UpdateOpacityValueText(int opacityPercent)
        {
            if (OpacityValueText is not null)
            {
                OpacityValueText.Text = $"{Math.Clamp(opacityPercent, MinimumOpacityPercent, MaximumOpacityPercent)}%";
            }
        }

        private void ApplyWindowOpacity(int opacityPercent)
        {
            if (!IsAlive)
            {
                return;
            }

            var clampedPercent = Math.Clamp(opacityPercent, MinimumOpacityPercent, MaximumOpacityPercent);
            var alpha = (byte)Math.Clamp((int)Math.Round(255.0 * clampedPercent / 100.0), 0, 255);
            SetLayeredWindowAttributes(_hwnd, 0, alpha, LwaAlpha);
        }

        private void ApplyDwmFramePreferences()
        {
            if (!IsAlive)
            {
                return;
            }

            var cornerPreference = DwmwcpDoNotRound;
            _ = DwmSetWindowAttribute(
                _hwnd,
                DwmwaWindowCornerPreference,
                ref cornerPreference,
                sizeof(int));

            var borderColor = DwmwaColorNone;
            _ = DwmSetWindowAttribute(
                _hwnd,
                DwmwaBorderColor,
                ref borderColor,
                sizeof(int));
        }

        private void Localization_CultureChanged()
        {
            Title = Localization.GetString("OverlayTitle");
            OverlayTitle.Text = Localization.GetString("OverlayTitle");
            RefreshMessages(force: true);
        }

        private void OverlayWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_isClosed)
            {
                return;
            }

            if (_placementSaveTimer?.IsEnabled == true && IsWindow(_hwnd))
            {
                PersistCurrentPlacement();
            }

            _isClosed = true;
            _refreshTimer?.Stop();
            _foregroundTimer?.Stop();
            _placementSaveTimer?.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
            _foregroundTimer.Tick -= ForegroundTimer_Tick;
            _placementSaveTimer.Tick -= PlacementSaveTimer_Tick;
            _appWindow.Changed -= AppWindow_Changed;
            Localization.CultureChanged -= Localization_CultureChanged;
            _manager.NotifyWindowClosed(this);
        }

        private static Windows.UI.Text.FontWeight GetFontWeight(string style)
        {
            ushort weight = style switch
            {
                "lighter" => (ushort)300,
                "bold" => (ushort)700,
                "bolder" => (ushort)800,
                _ => (ushort)400
            };

            return new Windows.UI.Text.FontWeight { Weight = weight };
        }

        private static Color ParseColor(string value, string fallback)
        {
            if (TryParseColor(value, out var color) || TryParseColor(fallback, out color))
            {
                return color;
            }

            return Colors.White;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var hex = value.Trim().TrimStart('#');
            try
            {
                if (hex.Length == 8)
                {
                    color = Color.FromArgb(
                        byte.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(hex[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    return true;
                }

                if (hex.Length == 6)
                {
                    color = Color.FromArgb(
                        255,
                        byte.Parse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    return true;
                }
            }
            catch (FormatException)
            {
            }

            return false;
        }

        private static int ReadInt(string key, int fallback)
        {
            return TryReadInt(key, out var value) ? value : fallback;
        }

        private static bool TryReadInt(string key, out int value)
        {
            return int.TryParse(ApplicationConfig.GetSettings(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static double ReadDouble(string key, double fallback)
        {
            return double.TryParse(ApplicationConfig.GetSettings(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : fallback;
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
        {
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, value) : SetWindowLong32(hWnd, nIndex, value);
        }


        private static IntPtr WindowLongToIntPtr(long value)
        {
            return IntPtr.Size == 8
                ? new IntPtr(value)
                : new IntPtr(unchecked((int)value));
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
    }
}
