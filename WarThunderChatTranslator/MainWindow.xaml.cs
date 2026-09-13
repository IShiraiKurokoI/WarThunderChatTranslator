// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Controls;
using WarThunderChatTranslator.Pages;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System;
using Windows.Graphics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WarThunderChatTranslator.Configurations;


// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WarThunderChatTranslator
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    /// 

    public sealed partial class MainWindow : Window
    {
        public string TitleText = "ս���������췭����";
        internal static MainWindow Instance { get; private set; }
        private OverlappedPresenter _presenter;
        private AppWindow _appWindow;
        private IntPtr _monitorHandle;
        private uint _dpi;

        private const int DesignWidth = 1400;
        private const int DesignHeight = 800;
        private const double BaselineWidth = 1920.0;
        private const double BaselineHeight = 1080.0;
        private const double BaselineDpi = 120.0;
        private const uint MonitorDefaultToNearest = 2;
        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "���������ý���";
            Instance = this;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            _appWindow.SetIcon("favicon.ico");
            ApplyDpiScale(hWnd, true);
            _appWindow.Changed += AppWindow_Changed;
            Closed += MainWindow_Closed;
            _presenter = _appWindow.Presenter as OverlappedPresenter;
            _presenter.IsAlwaysOnTop = false;
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidPositionChange)
            {
                ApplyDpiScale(WinRT.Interop.WindowNative.GetWindowHandle(this), false);
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            if (_appWindow != null)
            {
                _appWindow.Changed -= AppWindow_Changed;
            }
        }

        private void ApplyDpiScale(IntPtr windowHandle, bool force)
        {
            var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
            var dpi = GetDpiForWindow(windowHandle);
            if (!force && monitorHandle == _monitorHandle && dpi == _dpi)
            {
                return;
            }

            var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (monitorHandle == IntPtr.Zero || !GetMonitorInfo(monitorHandle, ref monitorInfo))
            {
                return;
            }

            var monitorWidth = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left;
            var monitorHeight = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;
            var resolutionScale = Math.Min(monitorWidth / BaselineWidth, monitorHeight / BaselineHeight);
            var dpiScale = dpi > 0 ? BaselineDpi / dpi : 1.0;
            var scale = resolutionScale * dpiScale;

            _appWindow.Resize(new SizeInt32(
                (int)Math.Round(DesignWidth * scale),
                (int)Math.Round(DesignHeight * scale)));
            _monitorHandle = monitorHandle;
            _dpi = dpi;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
