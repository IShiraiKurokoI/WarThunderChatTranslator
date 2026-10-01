using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WarThunderChatTranslator.Entities;
using WarThunderChatTranslator.Helpers;

namespace WarThunderChatTranslator.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const uint WmHotkey = 0x0312;
        private const uint WmReload = 0x8001;
        private const uint WmStop = 0x8002;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly object _syncRoot = new();
        private readonly ManualResetEventSlim _started = new(false);
        private readonly Dictionary<int, QuickTranslationHotkey> _registeredBindings = new();
        private Thread _messageThread;
        private uint _threadId;
        private IReadOnlyList<QuickTranslationHotkey> _pendingBindings = Array.Empty<QuickTranslationHotkey>();
        private bool _disposed;

        public event Action<QuickTranslationHotkey> HotkeyPressed;
        public event Action<QuickTranslationHotkey, string> RegistrationFailed;

        public void Start()
        {
            ThrowIfDisposed();
            if (_messageThread != null)
            {
                return;
            }

            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "WTCT.GlobalHotkeys"
            };
            _messageThread.Start();
            _started.Wait();
        }

        public void UpdateBindings(IEnumerable<QuickTranslationHotkey> bindings)
        {
            ThrowIfDisposed();
            Start();

            lock (_syncRoot)
            {
                _pendingBindings = bindings?.Select(item => item.Clone()).ToArray() ?? Array.Empty<QuickTranslationHotkey>();
            }

            if (!PostThreadMessage(_threadId, WmReload, UIntPtr.Zero, IntPtr.Zero))
            {
                _logger.Warn("Failed to post global hotkey reload message. Win32Error={0}", Marshal.GetLastWin32Error());
            }
        }

        private void MessageLoop()
        {
            _threadId = GetCurrentThreadId();

            // Force creation of this thread's Win32 message queue before signalling Start().
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _started.Set();

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                switch (message.message)
                {
                    case WmHotkey:
                        HandleHotkey(unchecked((int)message.wParam.ToUInt64()));
                        break;
                    case WmReload:
                        ReloadBindings();
                        break;
                    case WmStop:
                        UnregisterAll();
                        return;
                }
            }

            UnregisterAll();
        }

        private void ReloadBindings()
        {
            IReadOnlyList<QuickTranslationHotkey> bindings;
            lock (_syncRoot)
            {
                bindings = _pendingBindings.Select(item => item.Clone()).ToArray();
            }

            UnregisterAll();

            foreach (var binding in bindings.Where(item => item.Enabled))
            {
                if (!HotkeyGesture.TryParse(binding.Shortcut, out var gesture, out var parseError))
                {
                    RegistrationFailed?.Invoke(binding.Clone(), parseError);
                    continue;
                }

                if (!RegisterHotKey(IntPtr.Zero, binding.Id, gesture.Modifiers, gesture.VirtualKey))
                {
                    var message = $"RegisterHotKey failed with Win32 error {Marshal.GetLastWin32Error()}.";
                    _logger.Warn("Unable to register quick translation hotkey {0}: {1}", binding.Shortcut, message);
                    RegistrationFailed?.Invoke(binding.Clone(), message);
                    continue;
                }

                var registered = binding.Clone();
                registered.Shortcut = gesture.NormalizedText;
                _registeredBindings[binding.Id] = registered;
                _logger.Info("Registered quick translation hotkey {0} -> {1}.", registered.Shortcut, registered.TargetLanguage);
            }
        }

        private void HandleHotkey(int id)
        {
            if (_registeredBindings.TryGetValue(id, out var binding))
            {
                try
                {
                    HotkeyPressed?.Invoke(binding.Clone());
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unhandled exception in global hotkey callback.");
                }
            }
        }

        private void UnregisterAll()
        {
            foreach (var id in _registeredBindings.Keys.ToArray())
            {
                UnregisterHotKey(IntPtr.Zero, id);
            }

            _registeredBindings.Clear();
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_messageThread != null && _threadId != 0)
            {
                PostThreadMessage(_threadId, WmStop, UIntPtr.Zero, IntPtr.Zero);
                _messageThread.Join(TimeSpan.FromSeconds(2));
            }

            _started.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public NativePoint pt;
            public uint lPrivate;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
