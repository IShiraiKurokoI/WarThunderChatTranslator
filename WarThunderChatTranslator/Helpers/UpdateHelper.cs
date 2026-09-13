using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace WarThunderChatTranslator.Helpers
{
    internal static class UpdateHelper
    {
        private static readonly StoreContext StoreContext = StoreContext.GetDefault();

        public static async Task<IReadOnlyList<StorePackageUpdate>> GetAvailableUpdatesAsync()
        {
            return await StoreContext.GetAppAndOptionalStorePackageUpdatesAsync();
        }

        public static async Task InstallUpdatesAsync(IEnumerable<StorePackageUpdate> updates)
        {
            await StoreContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
        }
    }
}
