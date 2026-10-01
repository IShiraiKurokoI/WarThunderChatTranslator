using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WarThunderChatTranslator.Services
{
    public static class ClipboardService
    {
        private const uint GmemMoveable = 0x0002;
        private const uint CfUnicodeText = 13;

        public static async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TrySetText(text))
                {
                    return;
                }

                await Task.Delay(25, cancellationToken);
            }

            throw new InvalidOperationException("Unable to open the Windows clipboard after multiple attempts.");
        }

        private static bool TrySetText(string text)
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            IntPtr memory = IntPtr.Zero;
            try
            {
                if (!EmptyClipboard())
                {
                    return false;
                }

                var bytes = Encoding.Unicode.GetBytes(text + '\0');
                memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
                if (memory == IntPtr.Zero)
                {
                    return false;
                }

                var pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(memory);
                }

                if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
                {
                    return false;
                }

                // Ownership transfers to the system after SetClipboardData succeeds.
                memory = IntPtr.Zero;
                return true;
            }
            finally
            {
                if (memory != IntPtr.Zero)
                {
                    GlobalFree(memory);
                }
                CloseClipboard();
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr hMem);
    }
}
