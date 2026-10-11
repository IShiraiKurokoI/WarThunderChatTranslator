#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WarThunderChatTranslator.Helpers;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace WarThunderChatTranslator.Services
{
    // The DeviceInformation IDs must come from the same Windows render selector used
    // by MediaPlayer.AudioDevice. NAudio endpoint IDs are not interchangeable here.
    internal static class TtsOutputDeviceService
    {
        internal sealed record OutputDevice(string Id, string Name);

        public static async Task<IReadOnlyList<OutputDevice>> GetAvailableDevicesAsync()
        {
            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            return devices
                .Where(device => device.IsEnabled)
                .Select(device => new OutputDevice(device.Id, device.Name))
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        public static async Task<DeviceInformation?> ResolveAsync(string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                // null asks MediaPlayer to use the Windows default output device.
                return null;
            }

            var devices = await DeviceInformation.FindAllAsync(MediaDevice.GetAudioRenderSelector());
            return devices.FirstOrDefault(device => device.IsEnabled &&
                string.Equals(device.Id, deviceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    Localization.GetString("ChatTtsOutputDeviceUnavailableError"));
        }
    }
}
