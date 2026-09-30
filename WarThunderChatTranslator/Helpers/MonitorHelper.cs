using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace WarThunderChatTranslator.Helpers
{
    internal static class MonitorHelper
    {
        private const uint MonitorDefaultToNearest = 2;
        private const uint MonitorInfoPrimary = 1;

        internal sealed record MonitorDescriptor(
            nint Handle,
            string DeviceName,
            int Left,
            int Top,
            int Width,
            int Height,
            int WorkLeft,
            int WorkTop,
            int WorkWidth,
            int WorkHeight,
            bool IsPrimary)
        {
            public string DisplayName => $"{DeviceName} ({Width} × {Height})";
        }

        public static IReadOnlyList<MonitorDescriptor> GetMonitors()
        {
            var monitors = new List<MonitorDescriptor>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                var info = new MonitorInfoEx
                {
                    cbSize = Marshal.SizeOf<MonitorInfoEx>(),
                    szDevice = string.Empty
                };

                if (GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(ToDescriptor(monitor, info));
                }

                return true;
            }, IntPtr.Zero);

            return monitors
                .OrderByDescending(monitor => monitor.IsPrimary)
                .ThenBy(monitor => monitor.Left)
                .ThenBy(monitor => monitor.Top)
                .ToArray();
        }

        public static MonitorDescriptor GetCurrentMonitor()
        {
            if (GetCursorPos(out var cursor))
            {
                var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
                var descriptor = GetDescriptor(monitor);
                if (descriptor is not null)
                {
                    return descriptor;
                }
            }

            return GetMonitors().FirstOrDefault(monitor => monitor.IsPrimary)
                ?? GetMonitors().FirstOrDefault()
                ?? new MonitorDescriptor(IntPtr.Zero, "DISPLAY", 0, 0, 1920, 1080, 0, 0, 1920, 1040, true);
        }

        public static MonitorDescriptor GetMonitorForWindow(nint hwnd)
        {
            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            return GetDescriptor(monitor) ?? GetCurrentMonitor();
        }

        public static MonitorDescriptor GetMonitorByDeviceName(string deviceName)
        {
            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                var match = GetMonitors().FirstOrDefault(monitor =>
                    string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    return match;
                }
            }

            return GetCurrentMonitor();
        }

        public static PointInt32 CenterInWorkArea(MonitorDescriptor monitor, SizeInt32 size)
        {
            return new PointInt32(
                monitor.WorkLeft + Math.Max(0, (monitor.WorkWidth - size.Width) / 2),
                monitor.WorkTop + Math.Max(0, (monitor.WorkHeight - size.Height) / 2));
        }

        public static PointInt32 ClampToWorkArea(MonitorDescriptor monitor, PointInt32 position, SizeInt32 size)
        {
            var maxX = monitor.WorkLeft + Math.Max(0, monitor.WorkWidth - size.Width);
            var maxY = monitor.WorkTop + Math.Max(0, monitor.WorkHeight - size.Height);
            return new PointInt32(
                Math.Clamp(position.X, monitor.WorkLeft, maxX),
                Math.Clamp(position.Y, monitor.WorkTop, maxY));
        }

        private static MonitorDescriptor GetDescriptor(nint monitor)
        {
            if (monitor == IntPtr.Zero)
            {
                return null;
            }

            var info = new MonitorInfoEx
            {
                cbSize = Marshal.SizeOf<MonitorInfoEx>(),
                szDevice = string.Empty
            };

            return GetMonitorInfo(monitor, ref info) ? ToDescriptor(monitor, info) : null;
        }

        private static MonitorDescriptor ToDescriptor(nint handle, MonitorInfoEx info)
        {
            return new MonitorDescriptor(
                handle,
                info.szDevice ?? string.Empty,
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left,
                info.rcMonitor.Bottom - info.rcMonitor.Top,
                info.rcWork.Left,
                info.rcWork.Top,
                info.rcWork.Right - info.rcWork.Left,
                info.rcWork.Bottom - info.rcWork.Top,
                (info.dwFlags & MonitorInfoPrimary) != 0);
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MonitorInfoEx
        {
            public int cbSize;
            public Rect rcMonitor;
            public Rect rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }
    }
}
