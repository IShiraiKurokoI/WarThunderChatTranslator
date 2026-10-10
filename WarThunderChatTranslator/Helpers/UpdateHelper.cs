using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace WarThunderChatTranslator.Helpers
{
    internal static class UpdateHelper
    {
        private static readonly StoreContext StoreContext = StoreContext.GetDefault();
        private static IntPtr _initializedOwnerWindow;

        public static async Task<IReadOnlyList<StorePackageUpdate>> GetAvailableUpdatesAsync()
        {
            return await StoreContext.GetAppAndOptionalStorePackageUpdatesAsync();
        }

        public static async Task InstallUpdatesAsync(IEnumerable<StorePackageUpdate> updates, IntPtr ownerWindowHandle)
        {
            if (ownerWindowHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("A valid owner window handle is required to install Microsoft Store updates.");
            }

            // StoreContext methods that display Microsoft Store UI must be associated
            // with an HWND in desktop/WinUI 3 applications. Without this, the Store
            // API fails with 0x80070578 (ERROR_INVALID_WINDOW_HANDLE).
            if (_initializedOwnerWindow != ownerWindowHandle)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(StoreContext, ownerWindowHandle);
                _initializedOwnerWindow = ownerWindowHandle;
            }

            // This API displays Store UI and therefore must be invoked from the UI thread.
            await StoreContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        }
    }
}
